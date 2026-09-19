using System.Text;

namespace LineEndingNormalizer.Tests;

/// <summary>
/// Legacy files are rewritten as raw bytes in 64 KiB chunks, so a line ending can be cut in two
/// by a chunk boundary and the result must not depend on where the cut falls.
/// </summary>
/// <remarks>
/// A CR at the end of one chunk cannot be resolved until the next chunk shows whether an LF
/// follows: CR then LF is one line ending, CR then anything else is two things. Getting that
/// wrong changes the number of lines in a file without any error. The direct test feeds every
/// small input to <c>NormalizeBytes</c> under every possible chunk split and compares each
/// result with a plain reference; the end-to-end test puts a real CRLF across the real
/// 65536-byte read boundary. Both also cover the LF and CR target arms and text after the last
/// line ending, none of which the rest of the suite reached.
/// </remarks>
public sealed class LegacyLineEndingBoundaryTests
{
    private delegate int NormalizeBytesDelegate(
        ReadOnlySpan<byte> input,
        Span<byte> output,
        LineEnding target,
        ref bool pendingCr,
        bool isFinal);

    private static readonly NormalizeBytesDelegate NormalizeBytes =
        (NormalizeBytesDelegate)Delegate.CreateDelegate(
            typeof(NormalizeBytesDelegate),
            typeof(NewLineNormalizer).Assembly
                .GetType("LineEndingNormalizer.LosslessFileWriter")!
                .GetMethod(
                    "NormalizeBytes",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static)!);

    private static readonly LineEnding[] Targets =
        [LineEnding.Crlf, LineEnding.Lf, LineEnding.Cr];

    private static byte[] Replacement(LineEnding target) => target switch
    {
        LineEnding.Crlf => [0x0D, 0x0A],
        LineEnding.Lf => [0x0A],
        _ => [0x0D],
    };

    // CRLF, lone CR and lone LF are each one line ending; everything else is copied unchanged.
    private static byte[] Reference(byte[] input, LineEnding target)
    {
        var output = new List<byte>();
        byte[] replacement = Replacement(target);

        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] == 0x0D || input[i] == 0x0A)
            {
                if (input[i] == 0x0D && i + 1 < input.Length && input[i + 1] == 0x0A)
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

    // Every string of 'a', CR and LF up to the given length.
    private static IEnumerable<byte[]> AllInputs(int maxLength)
    {
        byte[] alphabet = [(byte)'a', 0x0D, 0x0A];
        var current = new List<byte[]> { Array.Empty<byte>() };

        for (int length = 0; length <= maxLength; length++)
        {
            foreach (byte[] item in current)
            {
                yield return item;
            }

            current = [.. current.SelectMany(prefix => alphabet.Select(b => (byte[])[.. prefix, b]))];
        }
    }

    [Fact]
    public void EveryChunkSplitOfEverySmallInputGivesTheSameResultAsAWholeRead()
    {
        int checkedCases = 0;

        foreach (LineEnding target in Targets)
        {
            foreach (byte[] input in AllInputs(6))
            {
                byte[] expected = Reference(input, target);

                // No cut, one cut at every position, and two cuts at every pair of positions.
                Assert.Equal(expected, RunInChunks(input, [], target));
                checkedCases++;

                for (int first = 0; first <= input.Length; first++)
                {
                    Assert.Equal(expected, RunInChunks(input, [first], target));
                    checkedCases++;

                    for (int second = first; second <= input.Length; second++)
                    {
                        Assert.Equal(expected, RunInChunks(input, [first, second], target));
                        checkedCases++;
                    }
                }
            }
        }

        // Guards against the loops silently doing nothing.
        Assert.True(checkedCases > 100_000, $"only {checkedCases} cases ran");
    }

    [Fact]
    public void ACrAtTheEndOfOneChunkFollowedByACrInTheNextIsTwoLineEndings()
    {
        // Not a pair: the first CR must be resolved as its own line ending when the second CR
        // arrives, rather than being merged with it or dropped.
        bool pendingCr = false;
        byte[] buffer = new byte[16];

        int first = NormalizeBytes([(byte)'a', 0x0D], buffer, LineEnding.Lf, ref pendingCr, isFinal: false);
        Assert.Equal([(byte)'a'], buffer[..first]);
        Assert.True(pendingCr);

        // The carried CR is resolved as a line ending; the new CR is now pending in its place.
        int second = NormalizeBytes([0x0D], buffer, LineEnding.Lf, ref pendingCr, isFinal: false);
        Assert.Equal([0x0A], buffer[..second]);
        Assert.True(pendingCr);

        int last = NormalizeBytes([(byte)'b'], buffer, LineEnding.Lf, ref pendingCr, isFinal: false);
        Assert.Equal([0x0A, (byte)'b'], buffer[..last]);
        Assert.False(pendingCr);

        Assert.Equal(0, NormalizeBytes([], buffer, LineEnding.Lf, ref pendingCr, isFinal: true));
        Assert.False(pendingCr);
    }

    // Second byte LF: CR then LF is one line ending. Second byte CR: two line endings.
    [Theory]
    [InlineData("Crlf", 0x0A)]
    [InlineData("Crlf", 0x0D)]
    [InlineData("Lf", 0x0A)]
    [InlineData("Lf", 0x0D)]
    [InlineData("Cr", 0x0A)]
    [InlineData("Cr", 0x0D)]
    public void ALegacyFileWithALineEndingAcrossTheRealReadBoundaryIsRewrittenExactly(
        string targetName, byte secondByte)
    {
        LineEnding target = Enum.Parse<LineEnding>(targetName);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding cp1252 = Encoding.GetEncoding(1252);

        // 65535 bytes of accented text, then CR as the last byte of the first 64 KiB read and
        // the second byte as the first byte of the next read, then text with no terminator.
        byte[] head = [.. Enumerable.Repeat((byte)0xE9, 65535)];
        byte[] tail = cp1252.GetBytes("end of file");
        byte[] source = [.. head, 0x0D, secondByte, .. tail];
        Assert.Equal(0x0D, source[65535]);
        Assert.Equal(secondByte, source[65536]);

        using var dir = new TempDirectory();
        string path = dir.WriteFile("boundary.txt", source);

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

        byte[] converted = File.ReadAllBytes(path);

        Assert.Equal(Reference(source, target), converted);

        // The accented bytes must come through untouched, and the unterminated tail intact.
        Assert.Equal(head, converted[..head.Length]);
        Assert.Equal(tail, converted[^tail.Length..]);
    }
}
