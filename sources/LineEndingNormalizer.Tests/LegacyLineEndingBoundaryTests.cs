using System.IO.Hashing;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LineEndingNormalizer.Tests;

/// <summary>
/// Legacy files are rewritten as raw bytes in 64 KiB chunks, so a line ending can be cut in two
/// by a chunk boundary and the result must not depend on where the cut falls.
/// </summary>
/// <remarks>
/// A CR at the end of one chunk cannot be resolved until the next chunk shows what follows it:
/// CR then LF is one line ending, CR then any other byte is a line ending followed by that byte,
/// and a CR still pending at the end of the input is a line ending of its own. Getting that wrong
/// changes the number of lines in a file without any error.
/// <para>
/// The direct tests feed every input of up to six bytes over {a, 0x85, CR, LF} to
/// <c>NormalizeBytes</c> under no cut, every single cut and every pair of cuts (two cuts at one
/// position leave an empty chunk) and compare each result with a plain reference. The end-to-end
/// tests put CR+LF and CR+CR across the real read-buffer boundary and one byte either side of it,
/// across three chunks, at the end of a file, and fill the output buffer to its worst case. The
/// byte-level path also records its own hashes, so the last tests check the hash it records for
/// the output and that verification rejects damaged output.
/// </para>
/// <para>
/// 0x85 is NEL in a Unicode file but an ordinary byte in a legacy one, so it must pass through
/// untouched, as must the other control bytes 0x0B and 0x0C.
/// </para>
/// </remarks>
public sealed class LegacyLineEndingBoundaryTests
{
    static LegacyLineEndingBoundaryTests()
    {
        // Program.Main registers this; tests run in a separate process and must register it
        // themselves to resolve code page 1252 at all.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private const byte Cr = 0x0D;
    private const byte Lf = 0x0A;

    private delegate int NormalizeBytesDelegate(
        ReadOnlySpan<byte> input,
        Span<byte> output,
        LineEnding target,
        ref bool pendingCr,
        bool isFinal);

    // The members under test are private, so they are found by reflection. Resolving lazily and
    // throwing a plain exception keeps a rename from surfacing as an opaque type-initializer
    // failure that names neither the member nor the cause.
    private static Type FindWriterType() =>
        typeof(NewLineNormalizer).Assembly.GetType("LineEndingNormalizer.LosslessFileWriter")
        ?? throw new InvalidOperationException(
            "LosslessFileWriter was not found; update these tests if it was renamed or moved.");

    private static MethodInfo FindWriterMethod(string name) =>
        FindWriterType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            $"LosslessFileWriter.{name} was not found; update these tests if it was renamed.");

    private static readonly Lazy<NormalizeBytesDelegate> NormalizeBytesLazy = new(() =>
    {
        try
        {
            return (NormalizeBytesDelegate)Delegate.CreateDelegate(
                typeof(NormalizeBytesDelegate), FindWriterMethod("NormalizeBytes"));
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                "LosslessFileWriter.NormalizeBytes no longer has the signature these tests call; " +
                "update the delegate in this file.",
                ex);
        }
    });

    private static NormalizeBytesDelegate NormalizeBytes => NormalizeBytesLazy.Value;

    private static int BufferSize =>
        (int)(FindWriterType()
                  .GetField("BufferSize", BindingFlags.NonPublic | BindingFlags.Static)
                  ?.GetRawConstantValue()
              ?? throw new InvalidOperationException(
                  "LosslessFileWriter.BufferSize was not found; update these tests if it was renamed."));

    private static readonly LineEnding[] Targets =
        [LineEnding.Crlf, LineEnding.Lf, LineEnding.Cr];

    public static TheoryData<string> TargetNames => ["Crlf", "Lf", "Cr"];

    private static LineEnding Parse(string targetName) => Enum.Parse<LineEnding>(targetName);

    private static byte[] Replacement(LineEnding target) => target switch
    {
        LineEnding.Crlf => [Cr, Lf],
        LineEnding.Lf => [Lf],
        _ => [Cr],
    };

    // CRLF, lone CR and lone LF are each one line ending; everything else is copied unchanged.
    private static byte[] Reference(byte[] input, LineEnding target)
    {
        var output = new List<byte>();
        byte[] replacement = Replacement(target);

        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] == Cr || input[i] == Lf)
            {
                if (input[i] == Cr && i + 1 < input.Length && input[i + 1] == Lf)
                {
                    i++;
                }

                output.AddRange(replacement);
            }
            else
            {
                output.Add(input[i]);
            }
        }

        return [.. output];
    }

    // Bytes that must never be treated as line endings on the byte-level path, none of them CR
    // or LF: an accented letter, 0x85 (NEL as U+0085 in Unicode, an ellipsis in Windows-1252),
    // vertical tab, form feed and an ASCII letter.
    private static byte[] Filler(int length)
    {
        byte[] pattern = [0xE9, 0x85, 0x0B, 0x0C, (byte)'a'];
        return [.. Enumerable.Range(0, length).Select(i => pattern[i % pattern.Length])];
    }

    private static byte[] RunInChunks(byte[] input, int[] cuts, LineEnding target)
    {
        byte[] buffer = new byte[input.Length * 2 + 2];
        var result = new List<byte>();
        bool pendingCr = false;
        int start = 0;

        foreach (int end in cuts.Append(input.Length))
        {
            int written = NormalizeBytes(
                input.AsSpan(start, end - start), buffer, target, ref pendingCr, isFinal: false);
            result.AddRange(buffer.AsSpan(0, written).ToArray());
            start = end;
        }

        int final = NormalizeBytes([], buffer, target, ref pendingCr, isFinal: true);
        result.AddRange(buffer.AsSpan(0, final).ToArray());

        return [.. result];
    }

    // A failure here would otherwise show two byte arrays and nothing about which of the
    // hundreds of thousands of inputs and cuts produced them.
    private static void AssertSameBytes(byte[] expected, byte[] actual, LineEnding target, byte[] input, int[] cuts)
    {
        if (!actual.AsSpan().SequenceEqual(expected))
        {
            Assert.Fail(
                $"target {target}, input {Convert.ToHexString(input)}, cuts [{string.Join(',', cuts)}]: " +
                $"expected {Convert.ToHexString(expected)} but got {Convert.ToHexString(actual)}");
        }
    }

    // Every string over the alphabet of each length up to the maximum, shortest first.
    private static IEnumerable<byte[]> AllInputs(int maxLength)
    {
        byte[] alphabet = [(byte)'a', 0x85, Cr, Lf];
        List<byte[]> current = [[]];

        for (int length = 0; length <= maxLength; length++)
        {
            foreach (byte[] item in current)
            {
                yield return item;
            }

            if (length < maxLength)
            {
                current = [.. current.SelectMany(prefix => alphabet.Select(b => (byte[])[.. prefix, b]))];
            }
        }
    }

    // The runs per input (no cut, every single cut, every pair of cuts) times the inputs of each
    // length from 0 to the maximum, times the targets.
    private static long ExpectedCases(int maxLength, int alphabetSize, int targets)
    {
        long total = 0;

        for (int n = 0; n <= maxLength; n++)
        {
            long runs = 1 + (n + 1) + (long)(n + 1) * (n + 2) / 2;
            total += (long)Math.Pow(alphabetSize, n) * runs;
        }

        return total * targets;
    }

    [Fact]
    public void EveryCutOfEverySmallInputGivesTheSameResultAsAWholeRead()
    {
        const int MaxLength = 6;
        long checkedCases = 0;

        foreach (LineEnding target in Targets)
        {
            foreach (byte[] input in AllInputs(MaxLength))
            {
                byte[] expected = Reference(input, target);

                // No cut, one cut at every position, and two cuts at every pair of positions.
                AssertSameBytes(expected, RunInChunks(input, [], target), target, input, []);
                checkedCases++;

                for (int first = 0; first <= input.Length; first++)
                {
                    AssertSameBytes(expected, RunInChunks(input, [first], target), target, input, [first]);
                    checkedCases++;

                    for (int second = first; second <= input.Length; second++)
                    {
                        int[] cuts = [first, second];
                        AssertSameBytes(expected, RunInChunks(input, cuts, target), target, input, cuts);
                        checkedCases++;
                    }
                }
            }
        }

        // Guards against the loops silently doing nothing or the input generator changing shape.
        Assert.Equal(ExpectedCases(MaxLength, alphabetSize: 4, targets: Targets.Length), checkedCases);
    }

    [Fact]
    public void TheReferenceModelAgreesWithARegularExpressionReplace()
    {
        // The reference follows the same rule as the code, so a shared misunderstanding would
        // pass both. A regular expression states the rule differently: an alternation that tries
        // CRLF first. Latin-1 maps every byte to one character, so bytes survive the round trip.
        foreach (LineEnding target in Targets)
        {
            string replacement = Encoding.Latin1.GetString(Replacement(target));

            foreach (byte[] input in AllInputs(6))
            {
                byte[] viaRegex = Encoding.Latin1.GetBytes(
                    Regex.Replace(Encoding.Latin1.GetString(input), @"\r\n|\r|\n", replacement));

                Assert.True(
                    Reference(input, target).AsSpan().SequenceEqual(viaRegex),
                    $"target {target}, input {Convert.ToHexString(input)}");
            }
        }
    }

    [Fact]
    public void ACrAtTheEndOfOneChunkFollowedByACrInTheNextIsTwoLineEndings()
    {
        // Not a pair: the first CR must be resolved as its own line ending when the second CR
        // arrives, rather than being merged with it or dropped.
        bool pendingCr = false;
        byte[] buffer = new byte[16];

        int first = NormalizeBytes([(byte)'a', Cr], buffer, LineEnding.Lf, ref pendingCr, isFinal: false);
        Assert.Equal([(byte)'a'], buffer[..first]);
        Assert.True(pendingCr);

        // The carried CR is resolved as a line ending; the new CR is now pending in its place.
        int second = NormalizeBytes([Cr], buffer, LineEnding.Lf, ref pendingCr, isFinal: false);
        Assert.Equal([Lf], buffer[..second]);
        Assert.True(pendingCr);

        int last = NormalizeBytes([(byte)'b'], buffer, LineEnding.Lf, ref pendingCr, isFinal: false);
        Assert.Equal([Lf, (byte)'b'], buffer[..last]);
        Assert.False(pendingCr);

        Assert.Equal(0, NormalizeBytes([], buffer, LineEnding.Lf, ref pendingCr, isFinal: true));
        Assert.False(pendingCr);
    }

    // Converts through the same entry point the tool uses, as Windows-1252 (a legacy code page),
    // and returns the bytes left in the file.
    private static byte[] ConvertLegacy(byte[] source, LineEnding target)
    {
        Encoding cp1252 = Encoding.GetEncoding(1252);

        using var dir = new TempDirectory();
        string path = dir.WriteFile("legacy.txt", source);

        var info = new FileInfo(path);
        var metadata = new FileMetadata(
            info.Attributes,
            info.Length,
            info.CreationTimeUtc,
            info.LastWriteTimeUtc,
            info.LastAccessTimeUtc);

        using (FileStream handle = File.OpenRead(path))
        {
            LosslessFileWriter.ConvertFile(handle, path, cp1252, target, metadata, createBackup: false);
        }

        return File.ReadAllBytes(path);
    }

    private static void AssertConvertsExactly(byte[] source, LineEnding target)
    {
        byte[] converted = ConvertLegacy(source, target);

        // Compare lengths first so a mismatch on a file of hundreds of kilobytes reports a
        // readable number instead of two enormous arrays.
        byte[] expected = Reference(source, target);
        Assert.True(
            expected.Length == converted.Length,
            $"target {target}: expected {expected.Length} bytes but the file holds {converted.Length}");
        Assert.True(
            expected.AsSpan().SequenceEqual(converted),
            $"target {target}: same length ({expected.Length}) but different bytes");
    }

    [Theory]
    [MemberData(nameof(TargetNames))]
    public void AnEmptyFileStaysEmpty(string targetName)
    {
        // The read loop never runs, so only the final flush and the hash of nothing are involved.
        AssertConvertsExactly([], Parse(targetName));
    }

    // The pair starts one byte before, at, and one byte after the last byte of the first read.
    public static IEnumerable<object[]> BoundaryCases()
    {
        foreach (string target in new[] { "Crlf", "Lf", "Cr" })
        {
            foreach (int offset in new[] { -1, 0, 1 })
            {
                foreach (byte second in new[] { Lf, Cr })
                {
                    yield return [target, offset, second];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(BoundaryCases))]
    public void ALineEndingAtTheReadBoundaryOrBesideItIsRewrittenExactly(
        string targetName, int offset, byte secondByte)
    {
        // The CR is the last byte of the first read when the offset is 0, so CR+LF is split
        // across two reads and CR+CR must stay two line endings. At -1 the pair sits inside the
        // first read, and at +1 inside the second.
        int crIndex = BufferSize - 1 + offset;
        byte[] source = [.. Filler(crIndex), Cr, secondByte, .. Filler(11)];
        Assert.Equal(Cr, source[crIndex]);
        Assert.Equal(secondByte, source[crIndex + 1]);

        AssertConvertsExactly(source, Parse(targetName));
    }

    [Theory]
    [MemberData(nameof(TargetNames))]
    public void AFileOfThreeReadsWithLineEndingsAtBothBoundariesIsRewrittenExactly(string targetName)
    {
        int size = BufferSize;

        var source = new List<byte>();
        source.AddRange(Filler(size - 1));
        source.AddRange([Cr, Lf]);                              // CR+LF across the first boundary
        source.AddRange(Filler(2 * size - 1 - source.Count));
        source.AddRange([Cr, Cr]);                              // CR+CR across the second boundary
        source.AddRange(Filler(3 * size - 1 - source.Count));
        source.Add(Cr);                                         // the last byte of an exact multiple

        Assert.Equal(3 * size, source.Count);

        AssertConvertsExactly([.. source], Parse(targetName));
    }

    public static IEnumerable<object[]> FilesEndingInCr()
    {
        foreach (string target in new[] { "Crlf", "Lf", "Cr" })
        {
            foreach (string kind in new[] { "short", "one read", "only a CR" })
            {
                yield return [target, kind];
            }
        }
    }

    [Theory]
    [MemberData(nameof(FilesEndingInCr))]
    public void AFileThatEndsInACrHasThatCrRewritten(string targetName, string kind)
    {
        // A trailing CR stays pending until the end of the input and is then flushed on its own,
        // so it goes through a different write and hash step from every other line ending.
        byte[] source = kind switch
        {
            "short" => [.. Filler(10), Cr],
            "one read" => [.. Filler(BufferSize - 1), Cr],
            _ => [Cr],
        };

        AssertConvertsExactly(source, Parse(targetName));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AllCrFilesDoubleInSizeExactly(int fullReads)
    {
        // Every CR becomes two bytes, so a read of CRs doubles in size. This checks the result
        // stays exact at that size. It cannot see an output buffer that is only slightly too
        // small: the pool rounds the request up to a larger array, so only a capacity of exactly
        // one read fails here.
        byte[] source = [.. Enumerable.Repeat(Cr, fullReads * BufferSize + 1)];

        AssertConvertsExactly(source, LineEnding.Crlf);
    }

    // Invokes a private static method and rethrows what it threw, not the reflection wrapper.
    private static object? InvokeWriter(string name, params object[] arguments)
    {
        try
        {
            return FindWriterMethod(name).Invoke(null, arguments);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    // Runs the byte-level writer directly so the hashes it records can be checked.
    private static (byte[] VerificationHash, byte[] SourceSha256) WriteBytes(
        string sourcePath, LineEnding target, string tempPath)
    {
        using FileStream source = File.OpenRead(sourcePath);

        object result = InvokeWriter(
            "WriteConvertedFileBytes", source, target, tempPath, CancellationToken.None)!;

        Type type = result.GetType();

        return (
            (byte[])type.GetProperty("VerificationHash")!.GetValue(result)!,
            (byte[])type.GetProperty("SourceSha256")!.GetValue(result)!);
    }

    private static void VerifyBytes(string tempPath, byte[] expectedHash) =>
        InvokeWriter("VerifyConvertedFileBytes", tempPath, expectedHash, CancellationToken.None);

    [Theory]
    [MemberData(nameof(TargetNames))]
    public void TheHashesRecordedWhileWritingDescribeTheBytesActuallyWritten(string targetName)
    {
        LineEnding target = Parse(targetName);

        // More than one read, a line ending in the middle, and a trailing CR that is flushed
        // separately after the read loop ends.
        byte[] source = [.. Filler(BufferSize + 10), Cr, Lf, .. Filler(5), Cr];

        using var dir = new TempDirectory();
        string sourcePath = dir.WriteFile("source.txt", source);
        string tempPath = dir.CombinePath("output.tmp");

        var (verificationHash, sourceSha256) = WriteBytes(sourcePath, target, tempPath);

        byte[] expected = Reference(source, target);
        Assert.Equal(expected, File.ReadAllBytes(tempPath));
        Assert.Equal(XxHash3.Hash(expected), verificationHash);
        Assert.Equal(SHA256.HashData(source), sourceSha256);

        // Verification accepts output that matches its recorded hash.
        VerifyBytes(tempPath, verificationHash);
    }

    [Fact]
    public void VerificationRejectsOutputThatNoLongerMatchesItsRecordedHash()
    {
        byte[] source = [.. Filler(100), Cr, Lf, .. Filler(100)];

        using var dir = new TempDirectory();
        string sourcePath = dir.WriteFile("source.txt", source);
        string tempPath = dir.CombinePath("output.tmp");

        var (verificationHash, _) = WriteBytes(sourcePath, LineEnding.Crlf, tempPath);
        byte[] written = File.ReadAllBytes(tempPath);

        // The control: untouched output verifies.
        VerifyBytes(tempPath, verificationHash);

        // One byte changed, same length.
        byte[] changed = [.. written];
        changed[0] ^= 0x01;
        File.WriteAllBytes(tempPath, changed);
        Assert.Throws<InvalidDataException>(() => VerifyBytes(tempPath, verificationHash));

        // Truncated by one byte.
        File.WriteAllBytes(tempPath, written[..^1]);
        Assert.Throws<InvalidDataException>(() => VerifyBytes(tempPath, verificationHash));
    }
}
