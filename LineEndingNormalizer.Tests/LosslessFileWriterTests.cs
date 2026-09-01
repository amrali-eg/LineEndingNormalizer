using System.ComponentModel;
using System.Text;

namespace LineEndingNormalizer.Tests;

/// <summary>
/// Regression coverage for the strict decode/normalize/encode/verify
/// pipeline in LosslessFileWriter, exercised through the public
/// NewLineNormalizer.NormalizeFile entry point. These codify scenarios
/// that were previously checked by hand with throwaway scripts.
/// </summary>
public sealed class LosslessFileWriterTests
{
    private const int BufferSize = 65536;

    [Fact]
    public void Utf8Bom_NormalizesLineEndings_PreservesBom()
    {
        using var dir = new TempDirectory();

        byte[] source =
        [
            0xEF, 0xBB, 0xBF,
            .. "line1\nline2\r\nline3\rline4"u8
        ];

        string path = dir.WriteFile("utf8bom.txt", source);

        NormalizeResult result =
            NewLineNormalizer.NormalizeFile(path, LineEnding.Crlf, whatIf: false);

        Assert.Equal(NormalizeResult.Converted, result);

        byte[] converted = File.ReadAllBytes(path);

        Assert.Equal((byte)0xEF, converted[0]);
        Assert.Equal((byte)0xBB, converted[1]);
        Assert.Equal((byte)0xBF, converted[2]);

        string text = Encoding.UTF8.GetString(converted, 3, converted.Length - 3);
        Assert.Equal("line1\r\nline2\r\nline3\r\nline4", text);
    }

    [Fact]
    public void Utf8WithTwoLeadingBoms_PreservesTheSecondAsText()
    {
        using var dir = new TempDirectory();

        byte[] source =
        [
            0xEF, 0xBB, 0xBF,
            0xEF, 0xBB, 0xBF,
            .. "line1\nline2"u8
        ];

        string path = dir.WriteFile("double-bom.txt", source);

        NormalizeResult result =
            NewLineNormalizer.NormalizeFile(
                path,
                LineEnding.Crlf,
                whatIf: false);

        Assert.Equal(NormalizeResult.Converted, result);

        byte[] expected =
        [
            0xEF, 0xBB, 0xBF,
            0xEF, 0xBB, 0xBF,
            .. "line1\r\nline2"u8
        ];

        Assert.Equal(expected, File.ReadAllBytes(path));

        DetectResult? detected = NewLineNormalizer.DetectFile(path);
        Assert.NotNull(detected);
        Assert.True(detected.HasBom);
    }

    [Fact]
    public void Utf16LeBom_NormalizesLineEndings_PreservesBomAndNonAsciiContent()
    {
        using var dir = new TempDirectory();

        string text = "café\nnaïve\r\n\U0001F600end\r";
        byte[] source = [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(text)];

        string path = dir.WriteFile("utf16bom.txt", source);

        NormalizeResult result =
            NewLineNormalizer.NormalizeFile(path, LineEnding.Crlf, whatIf: false);

        Assert.Equal(NormalizeResult.Converted, result);

        byte[] converted = File.ReadAllBytes(path);
        Assert.Equal((byte)0xFF, converted[0]);
        Assert.Equal((byte)0xFE, converted[1]);

        string decoded = Encoding.Unicode.GetString(converted, 2, converted.Length - 2);
        Assert.Equal("café\r\nnaïve\r\n\U0001F600end\r\n", decoded);
    }

    [Fact]
    public void BomLessUtf16LE_WhenOppositeByteOrderAlsoDecodes_IsRefusedWithoutWriting()
    {
        using var dir = new TempDirectory();

        // Repeated real Cyrillic text mixed with ASCII digits/punctuation/
        // spaces: dense enough non-Latin content that the BOM-less UTF-16
        // detector's primary NUL-density heuristic is inconclusive for both
        // byte orders (neither channel's NUL ratio clears the 0.5
        // threshold), forcing the chi-square channel-distribution fallback
        // in UnicodeDetector.CheckUtf16 to decide the byte order -- verified
        // empirically against this exact content: beChi ~= 5,943 vs
        // leChi ~= 55,830, both far above the 300 threshold, LE strictly wins.
        string phrase = "Привет мир 123 - как дела сегодня? Всё хорошо, спасибо! ";
        string secondLine = "вторая строка без нормального конца";
        string content = string.Concat(Enumerable.Repeat(phrase, 6)) + "\n" + secondLine + "\r";

        // Encoding.Unicode is UTF-16LE; GetBytes never prepends a BOM (only
        // GetPreamble()/a preamble-writing stream would), so this is
        // genuinely BOM-less input.
        byte[] source = Encoding.Unicode.GetBytes(content);
        Assert.False(source[0] == 0xFF && source[1] == 0xFE);

        string path = dir.WriteFile("cyrillic_no_bom.txt", source);

        var ex = Assert.Throws<ConversionRefusedException>(() =>
            NewLineNormalizer.NormalizeFile(
                path,
                LineEnding.Crlf,
                whatIf: false,
                backup: true));

        Assert.Equal(BomlessUnicodeSafety.AmbiguousReasonCode, ex.ReasonCode);
        Assert.Equal(source, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".bak"));
    }

    [Fact]
    public void BomLessUtf16BE_MisdetectedAsLittleEndian_IsRefusedWithoutTextLoss()
    {
        using var dir = new TempDirectory();

        // Under the real UTF-16BE interpretation this is U+4100, U+0A00,
        // U+4200. Under UTF-16LE the same bytes look like "A", LF, "B".
        // The old pipeline therefore inserted a false CR before every U+0A00.
        string authoritativeText =
            string.Concat(
                Enumerable.Repeat("\u4100\u0A00\u4200", 20));

        var utf16Be =
            new UnicodeEncoding(
                bigEndian: true,
                byteOrderMark: false,
                throwOnInvalidBytes: true);

        byte[] source = utf16Be.GetBytes(authoritativeText);
        string path = dir.WriteFile("ambiguous_utf16be.txt", source);

        DetectResult? detected = NewLineNormalizer.DetectFile(path);

        Assert.NotNull(detected);
        Assert.Equal(1200, detected.Encoding.CodePage);
        Assert.False(detected.HasBom);

        var ex = Assert.Throws<ConversionRefusedException>(() =>
            NewLineNormalizer.NormalizeFile(
                path,
                LineEnding.Crlf,
                whatIf: false,
                backup: true));

        Assert.Equal(BomlessUnicodeSafety.AmbiguousReasonCode, ex.ReasonCode);
        Assert.Equal(source, File.ReadAllBytes(path));
        Assert.Equal(authoritativeText, utf16Be.GetString(File.ReadAllBytes(path)));
        Assert.False(File.Exists(path + ".bak"));
    }

    [Fact]
    public void BomLessUtf16LE_WhenOppositeByteOrderIsInvalid_StillConverts()
    {
        using var dir = new TempDirectory();

        // U+00D8 encodes as D8 00 in UTF-16LE, which is an unpaired U+D800
        // surrogate in UTF-16BE. That makes the byte order structurally clear.
        string content = string.Concat(Enumerable.Repeat("Øline\n", 20));
        byte[] source = Encoding.Unicode.GetBytes(content);
        string path = dir.WriteFile("unambiguous_utf16le.txt", source);

        NormalizeResult result =
            NewLineNormalizer.NormalizeFile(
                path,
                LineEnding.Crlf,
                whatIf: false);

        Assert.Equal(NormalizeResult.Converted, result);
        Assert.Equal(
            content.Replace("\n", "\r\n", StringComparison.Ordinal),
            Encoding.Unicode.GetString(File.ReadAllBytes(path)));
    }

    [Fact]
    public void AlreadyNormalized_IsReportedUnchanged_AndFileIsByteIdentical()
    {
        using var dir = new TempDirectory();

        byte[] source = [.. "a\r\nb\r\nc\r\n"u8];
        string path = dir.WriteFile("already.txt", source);

        NormalizeResult result =
            NewLineNormalizer.NormalizeFile(path, LineEnding.Crlf, whatIf: false);

        Assert.Equal(NormalizeResult.Unchanged, result);
        Assert.Equal(source, File.ReadAllBytes(path));
    }

    [Fact]
    public void PreviewOnly_ReportsConverted_ButWritesNothing()
    {
        using var dir = new TempDirectory();

        byte[] source = [.. "a\nb\nc\n"u8];
        string path = dir.WriteFile("preview.txt", source);

        NormalizeResult result =
            NewLineNormalizer.NormalizeFile(path, LineEnding.Crlf, whatIf: true);

        Assert.Equal(NormalizeResult.Converted, result);
        Assert.Equal(source, File.ReadAllBytes(path));
    }

    [Fact]
    public void UndecodableEncoding_IsSkipped_AndFileIsUntouched()
    {
        using var dir = new TempDirectory();

        // Random bytes that won't decode as any supported encoding sample.
        var random = new Random(42);
        byte[] source = new byte[200];
        random.NextBytes(source);

        string path = dir.WriteFile("binary.txt", source);

        NormalizeResult result =
            NewLineNormalizer.NormalizeFile(path, LineEnding.Crlf, whatIf: false);

        Assert.Equal(NormalizeResult.EncodingNotDetected, result);
        Assert.Equal(source, File.ReadAllBytes(path));
    }

    [Fact]
    public void InvalidByteBeyondDetectionSample_FailsSafely_OriginalUnchangedNoTempLeftover()
    {
        using var dir = new TempDirectory();

        // The first TextEncoding.DefaultMaxSampleBytes+ bytes are plain
        // ASCII (detected as ASCII from the sample), then an invalid byte
        // appears well past the sample window and a line-ending change is
        // needed, forcing a full-file decode. The offset is derived from
        // the real constant, not a hardcoded guess, so this doesn't go
        // stale again if the sample size is ever retuned.
        int splitOffset = TextEncoding.DefaultMaxSampleBytes + 200;

        var sb = new StringBuilder();
        for (int i = 0; sb.Length < splitOffset; i++)
        {
            sb.Append("line ").Append(i).Append(" needs conversion\n");
        }

        byte[] ascii = Encoding.ASCII.GetBytes(sb.ToString());
        Assert.True(ascii.Length > splitOffset);

        byte[] source =
        [
            .. ascii.AsSpan(0, splitOffset),
            0xFF, 0xFE,
            .. ascii.AsSpan(splitOffset)
        ];

        string path = dir.WriteFile("late_invalid.txt", source);

        var ex = Assert.ThrowsAny<Exception>(() =>
            NewLineNormalizer.NormalizeFile(path, LineEnding.Crlf, whatIf: false));

        Assert.IsType<DecoderFallbackException>(ex);
        Assert.Equal(source, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.len.tmp"));
    }

    [Theory]
    [InlineData("Crlf")]
    [InlineData("Lf")]
    [InlineData("Cr")]
    public void CrlfPair_SplitAcrossChunkBoundary_NormalizesCorrectly(string targetName)
    {
        // LineEnding is internal; a public [Theory] method can't take it
        // directly as a parameter, so it's threaded through by name.
        LineEnding target = Enum.Parse<LineEnding>(targetName);

        using var dir = new TempDirectory();

        // Byte 65535 is '\r', byte 65536 is '\n': the pair straddles the
        // 65536-byte read buffer exactly.
        string filler = new string('x', 65535);
        string content = filler + "\r\n" + new string('y', 100) + "\n";
        byte[] source = Encoding.UTF8.GetBytes(content);


        string path = dir.WriteFile("boundary_crlf.txt", source);

        NewLineNormalizer.NormalizeFile(path, target, whatIf: false);

        string expected = NormalizeReference(content, target);
        string actual = Encoding.UTF8.GetString(File.ReadAllBytes(path));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void MultiByteUtf8Character_SplitAcrossChunkBoundary_DecodesCorrectly()
    {
        using var dir = new TempDirectory();

        // Non-ASCII lead content so the encoding is detected as UTF-8 from
        // the sample; the euro sign's 3 UTF-8 bytes straddle byte 65535/65536.
        string lead = "café ";
        string filler = lead + new string('x', BufferSize - 1 - Encoding.UTF8.GetByteCount(lead));
        string content = filler + "€" + "\rmore\n";
        byte[] source = Encoding.UTF8.GetBytes(content);

        // filler occupies bytes [0, BufferSize - 1); the euro sign's first
        // byte lands exactly at offset BufferSize - 1, the last byte of the
        // first read chunk, so its remaining two bytes fall in the next one.
        Assert.Equal(BufferSize - 1, Encoding.UTF8.GetByteCount(filler));

        string path = dir.WriteFile("boundary_utf8char.txt", source);

        NewLineNormalizer.NormalizeFile(path, LineEnding.Crlf, whatIf: false);

        string expected = NormalizeReference(content, LineEnding.Crlf);
        string actual = Encoding.UTF8.GetString(File.ReadAllBytes(path));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SurrogatePair_SplitAcrossChunkBoundary_DecodesCorrectly()
    {
        using var dir = new TempDirectory();

        string filler = new('x', 32767);
        string content = filler + "\U0001F600" + "\rend\n";
        byte[] utf16 = Encoding.Unicode.GetBytes(content);

        // filler (65534 bytes) + the high surrogate (2 bytes) exactly fill
        // the first 65536-byte read chunk, so the low surrogate's 2 bytes
        // fall entirely in the next one.
        Assert.Equal(BufferSize, filler.Length * 2 + 2);

        byte[] source = [0xFF, 0xFE, .. utf16];
        string path = dir.WriteFile("boundary_surrogate.txt", source);

        NewLineNormalizer.NormalizeFile(path, LineEnding.Crlf, whatIf: false);

        string expected = NormalizeReference(content, LineEnding.Crlf);
        byte[] converted = File.ReadAllBytes(path);
        string actual = Encoding.Unicode.GetString(converted, 2, converted.Length - 2);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void AdjacentLoneCarriageReturns_EachBecomeTheirOwnLineBreak()
    {
        using var dir = new TempDirectory();

        byte[] source = [.. "a\rb\rc\r"u8];
        string path = dir.WriteFile("double_cr.txt", source);

        NewLineNormalizer.NormalizeFile(path, LineEnding.Crlf, whatIf: false);

        Assert.Equal("a\r\nb\r\nc\r\n"u8.ToArray(), File.ReadAllBytes(path));
    }

    [Fact]
    public void AllUnicodeLineSeparators_NormalizeToTarget()
    {
        using var dir = new TempDirectory();

        char nel = (char)0x85;
        char ls = (char)0x2028;
        char ps = (char)0x2029;

        var sb = new StringBuilder();
        char[] seps = ['\n', '\r', nel, ls, ps];

        for (int i = 0; i < 200; i++)
        {
            sb.Append("line number ").Append(i)
              .Append(" has some readable english text in it")
              .Append(seps[i % seps.Length]);
        }

        string content = sb.ToString();
        byte[] source = Encoding.UTF8.GetBytes(content);
        string path = dir.WriteFile("all_separators.txt", source);

        NewLineNormalizer.NormalizeFile(path, LineEnding.Lf, whatIf: false);

        string expected = NormalizeReference(content, LineEnding.Lf);
        string actual = Encoding.UTF8.GetString(File.ReadAllBytes(path));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void LargeRandomizedMixedLineEndings_MatchesIndependentReferenceImplementation()
    {
        using var dir = new TempDirectory();

        var random = new Random(1234);
        char[] breakChoices = ['\n', '\r', (char)0x85, (char)0x2028, (char)0x2029];
        var sb = new StringBuilder();

        for (int i = 0; i < 20000; i++)
        {
            sb.Append("row ").Append(i).Append(" café wörld");

            int breaks = random.Next(1, 4);
            for (int b = 0; b < breaks; b++)
            {
                if (random.Next(4) == 0)
                {
                    sb.Append("\r\n");
                }
                else
                {
                    sb.Append(breakChoices[random.Next(breakChoices.Length)]);
                }
            }
        }

        string content = sb.ToString();
        byte[] source = Encoding.UTF8.GetBytes(content);
        string path = dir.WriteFile("fuzz.txt", source);

        NewLineNormalizer.NormalizeFile(path, LineEnding.Crlf, whatIf: false);

        string expected = NormalizeReference(content, LineEnding.Crlf);
        string actual = Encoding.UTF8.GetString(File.ReadAllBytes(path));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ReadOnlyAttributeAndTimestamps_ArePreservedAcrossConversion()
    {
        using var dir = new TempDirectory();

        byte[] source = [.. "x\ny\nz\n"u8];
        string path = dir.WriteFile("readonly.txt", source);

        var oldTime = new DateTime(2019, 6, 15, 8, 0, 0, DateTimeKind.Local);
        File.SetLastWriteTime(path, oldTime);
        File.SetCreationTime(path, oldTime);
        File.SetAttributes(path, FileAttributes.ReadOnly | FileAttributes.Archive);

        NewLineNormalizer.NormalizeFile(path, LineEnding.Crlf, whatIf: false);

        Assert.Equal("x\r\ny\r\nz\r\n"u8.ToArray(), File.ReadAllBytes(path));
        Assert.Equal(FileAttributes.ReadOnly | FileAttributes.Archive, File.GetAttributes(path));
        Assert.Equal(oldTime, File.GetLastWriteTime(path));

        // Clean up so TempDirectory's recursive delete can remove it.
        File.SetAttributes(path, FileAttributes.Archive);
    }

    [Fact]
    public void SourceChangedDuringConversion_IsDetected_OriginalUntouchedNoTempLeftover()
    {
        using var dir = new TempDirectory();

        byte[] source = [.. "a\nb\nc\n"u8];
        string path = dir.WriteFile("mutated.txt", source);

        var info = new FileInfo(path);

        // Metadata claims a Length one byte larger than the real file --
        // simulating the file having changed between when metadata was
        // captured and when ConvertFile revalidates it just before
        // replacement (the real file itself is genuinely untouched).
        var staleMetadata = new FileMetadata(
            info.Attributes,
            info.Length + 1,
            info.CreationTimeUtc,
            info.LastWriteTimeUtc,
            info.LastAccessTimeUtc);

        using (FileStream handle = File.OpenRead(path))
        {
            var ex = Assert.Throws<IOException>(() =>
                LosslessFileWriter.ConvertFile(
                    handle,
                    path,
                    Encoding.ASCII,
                    LineEnding.Crlf,
                    staleMetadata,
                    createBackup: false));

            Assert.Contains("changed", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(source, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.len.tmp"));
    }

    [Fact]
    public void ReadOnlyFile_WithBackupRequested_RoundTripsAndBackupIsCreated()
    {
        using var dir = new TempDirectory();

        byte[] source = [.. "a\nb\nc\n"u8];
        string path = dir.WriteFile("readonly_backup.txt", source);
        File.SetAttributes(path, FileAttributes.ReadOnly);

        try
        {
            NewLineNormalizer.NormalizeFile(path, LineEnding.Crlf, whatIf: false, backup: true);

            Assert.Equal("a\r\nb\r\nc\r\n"u8.ToArray(), File.ReadAllBytes(path));
            Assert.Equal(FileAttributes.ReadOnly, File.GetAttributes(path) & FileAttributes.ReadOnly);
            Assert.True(File.Exists(path + ".bak"));
            Assert.Equal(source, File.ReadAllBytes(path + ".bak"));
        }
        finally
        {
            // Clean up so TempDirectory's recursive delete can remove it.
            File.SetAttributes(path, FileAttributes.Archive);
        }
    }

    [Fact]
    public void BackupFailure_PreventsMainReplacement_OriginalUntouched()
    {
        using var dir = new TempDirectory();

        byte[] source = [.. "a\nb\nc\n"u8];
        string path = dir.WriteFile("backup_fail.txt", source);

        // Force the backup install step to fail: a directory already
        // occupies the ".bak" path, so installing the backup there can't
        // succeed.
        Directory.CreateDirectory(path + ".bak");

        Assert.ThrowsAny<Exception>(() =>
            NewLineNormalizer.NormalizeFile(path, LineEnding.Crlf, whatIf: false, backup: true));

        Assert.Equal(source, File.ReadAllBytes(path));

        // The backup temp file now shares the conversion temp suffix
        // (<file>.<guid>.bak.len.tmp), so this one glob covers both.
        Assert.Empty(Directory.GetFiles(dir.Path, "*.len.tmp"));
    }

    [Fact]
    public void ReparsePointAttribute_IsRejected_WithoutNeedingARealSymlink()
    {
        using var dir = new TempDirectory();

        byte[] source = [.. "a\nb\n"u8];
        string path = dir.WriteFile("fake_link.txt", source);

        // LosslessFileWriter.ConvertFile takes the caller's already-captured
        // FileMetadata, so the reparse-point check can be exercised directly
        // with a fabricated Attributes value -- no admin rights or real
        // symlink needed. It's checked before `source` is used, so a plain
        // open handle is enough to satisfy the parameter.
        using (var handle = File.OpenRead(path))
        {
            var info = new FileInfo(path);

            // Attributes is deliberately fabricated as ReparsePoint (the
            // real file isn't one); Length/timestamps are real so this
            // exercises only the reparse-point rejection, not the
            // unrelated source-revalidation check.
            var metadata = new FileMetadata(
                FileAttributes.ReparsePoint,
                info.Length,
                info.CreationTimeUtc,
                info.LastWriteTimeUtc,
                info.LastAccessTimeUtc);

            var ex = Assert.Throws<NotSupportedException>(() =>
                LosslessFileWriter.ConvertFile(
                    handle,
                    path,
                    Encoding.UTF8,
                    LineEnding.Crlf,
                    metadata,
                    createBackup: false));

            Assert.Contains("reparse point", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(source, File.ReadAllBytes(path));
    }

    [Fact]
    public void ReadOnlySource_WithReplacementBlocked_PreservesRealFailure_AndCleansUpReadOnlyTemp()
    {
        using var dir = new TempDirectory();

        byte[] source = [.. "a\nb\n"u8];
        string path = dir.WriteFile("readonly_blocked.txt", source);
        File.SetAttributes(path, FileAttributes.ReadOnly);

        try
        {
            // Same blocking technique as ReplaceFileFailure_WithApiAvailable_...
            // below: a Read/FileShare.Read handle is compatible with
            // NormalizeFile's own source open but blocks the rename/replace
            // step itself. Combined with a ReadOnly source, ApplyMetadata
            // copies ReadOnly onto the temp file before AtomicReplace fails,
            // exercising the cleanup path this test protects: deleting that
            // ReadOnly temp file must not replace the real failure with an
            // UnauthorizedAccessException from the delete itself.
            using FileStream blockingHandle =
                new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

            var ex = Assert.ThrowsAny<Exception>(() =>
                NewLineNormalizer.NormalizeFile(path, LineEnding.Crlf, whatIf: false));

            Assert.IsType<Win32Exception>(ex);

            Assert.Equal(source, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(dir.Path, "*.len.tmp"));
        }
        finally
        {
            // Clean up so TempDirectory's recursive delete can remove it.
            File.SetAttributes(path, FileAttributes.Archive);
        }
    }

    [Fact]
    public void ReplaceFileFailure_WithApiAvailable_ThrowsWin32Exception_NotSilentFallback()
    {
        using var dir = new TempDirectory();

        byte[] source = [.. "a\nb\n"u8];
        string path = dir.WriteFile("locked_dest.txt", source);

        // A second handle sharing only Read (no Delete) on the
        // destination: compatible with NormalizeFile's own source handle
        // (also FileShare.Read, so detection/scanning/writing the temp
        // file all proceed normally), but blocks the rename/replace step
        // specifically -- the same reason the pipeline disposes its own
        // source handle before calling AtomicReplace. This deterministically
        // reproduces "the ReplaceFile API is present but the call itself
        // fails" (a sharing violation), as opposed to "the API doesn't
        // exist on this system", which is the distinction this test exists
        // to verify: the former must now surface the real Win32 failure
        // rather than being silently retried via File.Move.
        using FileStream blockingHandle =
            new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        var ex = Assert.ThrowsAny<Exception>(() =>
            NewLineNormalizer.NormalizeFile(path, LineEnding.Crlf, whatIf: false));

        Assert.IsType<Win32Exception>(ex);
    }

    [Fact]
    public void SourceStream_IsDisposed_EvenWhenRejectedBeforeConversionBegins()
    {
        using var dir = new TempDirectory();

        byte[] source = [.. "a\nb\n"u8];
        string path = dir.WriteFile("fake_link2.txt", source);

        var info = new FileInfo(path);

        // Same fabricated-ReparsePoint technique as the test above, but
        // this one confirms ConvertFile disposes the transferred stream
        // even on a path that throws before the write/verify pipeline
        // (and the try/finally guarding it) would otherwise begin --
        // ownership transfers as soon as a non-null stream is accepted,
        // not only once the pipeline itself starts. Deliberately not
        // wrapped in `using`: ConvertFile, not this test, is responsible
        // for disposing it.
        var metadata = new FileMetadata(
            FileAttributes.ReparsePoint,
            info.Length,
            info.CreationTimeUtc,
            info.LastWriteTimeUtc,
            info.LastAccessTimeUtc);

        FileStream handle = File.OpenRead(path);

        Assert.Throws<NotSupportedException>(() =>
            LosslessFileWriter.ConvertFile(
                handle,
                path,
                Encoding.UTF8,
                LineEnding.Crlf,
                metadata,
                createBackup: false));

        Assert.Throws<ObjectDisposedException>(() => handle.ReadByte());
    }

    /// <summary>
    /// Independent reference normalization, deliberately implemented
    /// differently (char-by-char, no bulk-copy fast path) from
    /// LosslessFileWriter.NormalizeChars, so it can't share a bug with it.
    /// </summary>
    private static string NormalizeReference(string text, LineEnding target)
    {
        string replacement = target switch
        {
            LineEnding.Crlf => "\r\n",
            LineEnding.Lf => "\n",
            LineEnding.Cr => "\r",
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };

        var sb = new StringBuilder();
        int i = 0;

        while (i < text.Length)
        {
            char c = text[i];

            if (c == '\r')
            {
                sb.Append(replacement);

                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i += 2;
                }
                else
                {
                    i += 1;
                }
            }
            else if (c is '\n' or (char)0x85 or (char)0x2028 or (char)0x2029)
            {
                sb.Append(replacement);
                i += 1;
            }
            else
            {
                sb.Append(c);
                i += 1;
            }
        }

        return sb.ToString();
    }
}
