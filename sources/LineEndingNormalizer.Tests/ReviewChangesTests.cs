using System.Text;

namespace LineEndingNormalizer.Tests;

/// <summary>
/// Coverage for the P0/P1/P2 review-and-commit changes: strict scanning,
/// metadata-before-replace safety, and -DetectOnly.
/// </summary>
public sealed class ReviewChangesTests
{
    // ---- P0.1: strict decoder in the prescan ----

    [Fact]
    public void AlreadyCorrectLineEndings_WithInvalidBytesLaterInFile_FailsRatherThanReportingUnchanged()
    {
        using var dir = new TempDirectory();

        // Every line ending already matches the target (CRLF), so under
        // the old lenient prescan this would have been silently reported
        // as "Unchanged" despite containing a byte sequence invalid for
        // its own detected encoding. The invalid bytes are placed just
        // past TextEncoding.DefaultMaxSampleBytes, derived from the real
        // constant rather than a hardcoded guess, so this test doesn't go
        // stale again if the sample size is ever retuned.
        int splitOffset = TextEncoding.DefaultMaxSampleBytes + 200;

        var sb = new StringBuilder();
        for (int i = 0; sb.Length < splitOffset; i++)
        {
            sb.Append("line ").Append(i).Append(" already correct\r\n");
        }

        byte[] ascii = Encoding.ASCII.GetBytes(sb.ToString());
        Assert.True(ascii.Length > splitOffset);

        byte[] source =
        [
            .. ascii.AsSpan(0, splitOffset),
            0xFF, 0xFE,
            .. ascii.AsSpan(splitOffset)
        ];

        string path = dir.WriteFile("looks_fine.txt", source);

        var ex = Assert.Throws<DecoderFallbackException>(() =>
            NewLineNormalizer.NormalizeFile(path, LineEnding.Crlf, whatIf: false));

        Assert.NotNull(ex);
        Assert.Equal(source, File.ReadAllBytes(path));
    }

    [Fact]
    public void StrictPrescan_StillReportsUnchanged_ForACleanFile()
    {
        using var dir = new TempDirectory();

        byte[] source = [.. "a\r\nb\r\nc\r\n"u8];
        string path = dir.WriteFile("clean.txt", source);

        NormalizeResult result =
            NewLineNormalizer.NormalizeFile(path, LineEnding.Crlf, whatIf: false);

        Assert.Equal(NormalizeResult.Unchanged, result);
        Assert.Equal(source, File.ReadAllBytes(path));
    }

    // ---- P0.2: metadata applied before atomic replace ----

    [Fact]
    public void MetadataAppliedToFinalFile_MatchesOriginal()
    {
        using var dir = new TempDirectory();

        string path = dir.WriteFile("f.txt", [.. "a\nb\n"u8]);

        var oldTime = new DateTime(2018, 3, 1, 12, 0, 0, DateTimeKind.Local);
        File.SetLastWriteTime(path, oldTime);
        File.SetCreationTime(path, oldTime);

        NewLineNormalizer.NormalizeFile(path, LineEnding.Crlf, whatIf: false);

        Assert.Equal(oldTime, File.GetLastWriteTime(path));
        Assert.Equal(oldTime, File.GetCreationTime(path));
    }

    [Fact]
    public void MetadataApplicationFailure_LeavesOriginalCompletelyUntouched_AndNoTempFileLeftover()
    {
        using var dir = new TempDirectory();

        byte[] source = [.. "a\nb\nc\n"u8];
        string path = dir.WriteFile("f.txt", source);

        // File.SetCreationTimeUtc rejects timestamps before the Windows
        // FILETIME epoch (1601-01-01 UTC); DateTime.MinValue reliably
        // triggers that failure, letting this test exercise the real
        // ApplyMetadata failure path without mocking anything. Length and
        // LastWriteTimeUtc are the file's real values so the new source-
        // revalidation check (added for source-mutation detection) passes
        // and execution actually reaches ApplyMetadata instead of failing
        // there first.
        var info = new FileInfo(path);

        var badMetadata = new FileMetadata(
            FileAttributes.Normal,
            info.Length,
            DateTime.MinValue,
            info.LastWriteTimeUtc,
            info.LastAccessTimeUtc);

        using (FileStream handle = File.OpenRead(path))
        {
            // No-BOM UTF-8, matching this file's actual (BOM-less) content --
            // Encoding.UTF8 carries a BOM preamble, which would now fail
            // the preamble-validation check before ever reaching
            // ApplyMetadata.
            Assert.ThrowsAny<Exception>(() =>
                LosslessFileWriter.ConvertFile(
                    handle,
                    path,
                    new UTF8Encoding(false),
                    LineEnding.Crlf,
                    badMetadata,
                    createBackup: false));
        }

        // The original must be byte-for-byte untouched: the replace must
        // never have happened.
        Assert.Equal(source, File.ReadAllBytes(path));

        // No leftover temp file from the failed attempt.
        Assert.Empty(Directory.GetFiles(dir.Path, "*.len.tmp"));
    }

    // ---- P1.3: shared source stream (detect -> scan -> conversion) ----

    [Fact]
    public void ConversionStillSucceeds_WithSourceHandleSharedAcrossPhases()
    {
        // Not a white-box test of handle reuse itself (an implementation
        // detail), but confirms the externally observable contract still
        // holds end-to-end now that NormalizeFile owns one stream across
        // detection, scanning, and the write pass instead of reopening.
        using var dir = new TempDirectory();

        byte[] source = [.. "café\nnaïve\r\nend\r"u8];
        string path = dir.WriteFile("shared_stream.txt", [0xEF, 0xBB, 0xBF, .. source]);

        NormalizeResult result =
            NewLineNormalizer.NormalizeFile(path, LineEnding.Crlf, whatIf: false);

        Assert.Equal(NormalizeResult.Converted, result);

        byte[] converted = File.ReadAllBytes(path);
        string decoded = Encoding.UTF8.GetString(converted, 3, converted.Length - 3);
        Assert.Equal("café\r\nnaïve\r\nend\r\n", decoded);
    }

    // ---- -Report double-scan elimination: NormalizeFile's out-DetectResult overload ----

    [Fact]
    public void NormalizeFile_OutDetectedOverload_ReturnsOriginalDetection()
    {
        using var dir = new TempDirectory();
        byte[] source = [0xEF, 0xBB, 0xBF, .. "a\nb\n"u8];
        string path = dir.WriteFile("f.txt", source);

        NormalizeResult result = NewLineNormalizer.NormalizeFile(
            path, LineEnding.Crlf, whatIf: false, backup: false, out DetectResult? detected);

        Assert.Equal(NormalizeResult.Converted, result);
        Assert.NotNull(detected);
        Assert.Equal("utf-8", detected.Encoding.WebName);
        Assert.True(detected.HasBom);
        Assert.Equal(LineEndingKind.Lf, detected.LineEndingKind); // the *original* file's kind, not the post-conversion CRLF
    }

    // ---- P2.7: -DetectOnly ----

    [Theory]
    [InlineData("a\r\nb\r\nc\r\n", "Crlf")]
    [InlineData("a\nb\nc\n", "Lf")]
    [InlineData("a\rb\rc\r", "Cr")]
    [InlineData("a\r\nb\nc\r", "Mixed")]
    [InlineData("no line breaks here", "None")]
    public void DetectFile_ClassifiesLineEndingKind(string content, string expectedKindName)
    {
        using var dir = new TempDirectory();

        string path = dir.WriteFile("f.txt", Encoding.UTF8.GetBytes(content));

        DetectResult? result = NewLineNormalizer.DetectFile(path);

        Assert.NotNull(result);
        Assert.Equal(Enum.Parse<LineEndingKind>(expectedKindName), result.LineEndingKind);
    }

    [Fact]
    public void DetectFile_DoesNotCountNelLsPs_TowardLineEndingKind()
    {
        using var dir = new TempDirectory();

        // -DetectOnly's reported categories are exactly CRLF/LF/CR/MIXED/
        // NONE; NEL/LS/PS are recognized as breaks by the converter but are
        // deliberately excluded from this classification. Enough
        // surrounding readable text is included so EncodingDetector's
        // printable-ratio heuristic still classifies the sample as UTF-8
        // (a handful of bare control/separator characters alone can fail
        // that heuristic on such a short sample).
        var sb = new StringBuilder();
        for (int i = 0; i < 40; i++)
        {
            sb.Append("some readable text ").Append(i);
            sb.Append(i % 3 == 0 ? (char)0x85 : i % 3 == 1 ? (char)0x2028 : (char)0x2029);
        }

        string content = sb.ToString();
        string path = dir.WriteFile("seps.txt", Encoding.UTF8.GetBytes(content));

        DetectResult? result = NewLineNormalizer.DetectFile(path);

        Assert.NotNull(result);
        Assert.Equal(LineEndingKind.None, result.LineEndingKind);
    }

    [Fact]
    public void DetectFile_ReportsEncodingAndBom()
    {
        using var dir = new TempDirectory();

        string path = dir.WriteFile("bom.txt", [0xEF, 0xBB, 0xBF, .. "x\r\n"u8]);

        DetectResult? result = NewLineNormalizer.DetectFile(path);

        Assert.NotNull(result);
        Assert.True(result.HasBom);
        Assert.Equal("UTF-8", Program.GetEncodingDisplayName(result.Encoding));
    }

    [Fact]
    public void DetectFile_ReturnsNull_ForUndetectableEncoding()
    {
        using var dir = new TempDirectory();

        var random = new Random(7);
        byte[] source = new byte[200];
        random.NextBytes(source);

        string path = dir.WriteFile("binary.bin", source);

        DetectResult? result = NewLineNormalizer.DetectFile(path);

        Assert.Null(result);
    }

    [Fact]
    public void DetectFile_FailsSafely_OnInvalidByteBeyondDetectionSample()
    {
        using var dir = new TempDirectory();

        // Placed just past TextEncoding.DefaultMaxSampleBytes, derived
        // from the real constant rather than a hardcoded guess.
        int splitOffset = TextEncoding.DefaultMaxSampleBytes + 200;

        var sb = new StringBuilder();
        for (int i = 0; sb.Length < splitOffset; i++)
        {
            sb.Append("line ").Append(i).Append(" needs no conversion\n");
        }

        byte[] ascii = Encoding.ASCII.GetBytes(sb.ToString());
        Assert.True(ascii.Length > splitOffset);

        byte[] source = [.. ascii.AsSpan(0, splitOffset), 0xFF, 0xFE, .. ascii.AsSpan(splitOffset)];

        string path = dir.WriteFile("late_invalid.txt", source);

        Assert.Throws<DecoderFallbackException>(() => NewLineNormalizer.DetectFile(path));

        // Detect-only: never writes, regardless of outcome.
        Assert.Equal(source, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("plain.txt", "plain.txt")]
    [InlineData("has,comma.txt", "\"has,comma.txt\"")]
    [InlineData("has\"quote.txt", "\"has\"\"quote.txt\"")]
    public void EscapeCsvField_QuotesOnlyWhenNeeded(string field, string expected)
    {
        Assert.Equal(expected, Program.EscapeCsvField(field));
    }

    // ---- LineEndingKind CSV display: an explicit contract independent of
    // whatever casing convention the enum itself happens to use ----

    [Theory]
    [InlineData("Crlf", "CRLF")]
    [InlineData("Lf", "LF")]
    [InlineData("Cr", "CR")]
    [InlineData("Mixed", "MIXED")]
    [InlineData("None", "NONE")]
    public void GetLineEndingKindDisplayName_MatchesDocumentedCsvFormat(string kindName, string expectedCsvValue)
    {
        // The -DetectOnly CSV format is a documented external contract
        // (LineEnding,Encoding,BOM,File with CRLF/LF/CR/MIXED/NONE values).
        // This must hold regardless of how LineEndingKind's own members
        // happen to be spelled internally.
        LineEndingKind kind = Enum.Parse<LineEndingKind>(kindName);

        Assert.Equal(expectedCsvValue, Program.GetLineEndingKindDisplayName(kind));
    }
}
