using System.Text;

namespace LineEndingNormalizer.Tests;

/// <summary>
/// Direct coverage for ScanEngine.Scan: the single authoritative scan shared
/// by DetectFile, NormalizeFile, and (via whatIf) ValidateOnly.
/// </summary>
public sealed class ScanEngineTests
{
    static ScanEngineTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static readonly LineEnding[] AllTargets =
        [LineEnding.Crlf, LineEnding.Lf, LineEnding.Cr];

    private static Stream OpenUtf8(string content) =>
        new MemoryStream(Encoding.UTF8.GetBytes(content));

    // Repeated real-language text with Windows-1252-specific bytes (em dash,
    // i-diaeresis) so the heuristic detector reliably lands on windows-1252
    // even for a short trailing line-ending pattern under test.
    private const string Cp1252Sentence =
        "Le cafe etait delicieux \u2014 vraiment, c'etait na\u00EFve de penser autrement. ";

    private static readonly string Cp1252Carrier =
        string.Concat(Enumerable.Repeat(Cp1252Sentence, 8));

    private static Stream OpenCp1252(string suffix)
    {
        Encoding cp1252 = Encoding.GetEncoding(1252);
        return new MemoryStream(cp1252.GetBytes(Cp1252Carrier + suffix));
    }

    private static bool ExpectedRequiresConversion(
        bool sawCrLf, bool sawLf, bool sawCr, LineEnding target) =>
        (sawCrLf && target != LineEnding.Crlf) ||
        (sawLf && target != LineEnding.Lf) ||
        (sawCr && target != LineEnding.Cr);

    // ---- Test 1: line-ending matrix ----

    // LineEndingKind is internal; a public [Theory] method can't take it
    // directly as a parameter, so it's threaded through by name.
    public static IEnumerable<object[]> LineEndingMatrixCases()
    {
        yield return ["abc", false, false, false, "None"];
        yield return ["a\nb\nc\n", false, true, false, "Lf"];
        yield return ["a\rb\rc\r", false, false, true, "Cr"];
        yield return ["a\r\nb\r\nc\r\n", true, false, false, "Crlf"];
        yield return ["a\nb\r\nc\n", true, true, false, "Mixed"];
        yield return ["a\rb\nc\r", false, true, true, "Mixed"];
        yield return ["a\rb\r\nc\r", true, false, true, "Mixed"];
        yield return ["a\rb\nc\r\nd", true, true, true, "Mixed"];
        yield return ["line1\r\nline2\r", true, false, true, "Mixed"];
        yield return ["a\r\r\rb", false, false, true, "Cr"];
        yield return ["a\n\n\nb", false, true, false, "Lf"];
    }

    [Theory]
    [MemberData(nameof(LineEndingMatrixCases))]
    public void Utf8_LineEndingMatrix_ClassifiesAndDecidesConversion(
        string content, bool sawCrLf, bool sawLf, bool sawCr, string expectedKindName)
    {
        LineEndingKind expectedKind = Enum.Parse<LineEndingKind>(expectedKindName);

        foreach (LineEnding target in AllTargets)
        {
            using Stream stream = OpenUtf8(content);

            ScanEngine.ScanResult? scan =
                ScanEngine.Scan(stream, target);

            Assert.NotNull(scan);
            Assert.Equal(expectedKind, scan.Detection.LineEndingKind);
            Assert.Equal(
                ExpectedRequiresConversion(sawCrLf, sawLf, sawCr, target),
                scan.RequiresConversion);
        }
    }

    [Fact]
    public void Utf8_EmptyFile_EncodingNotDetected()
    {
        using Stream stream = OpenUtf8("");

        ScanEngine.ScanResult? scan =
            ScanEngine.Scan(stream, LineEnding.Crlf);

        Assert.Null(scan);
    }

    // Representative subset, not the full matrix: ScanBytes is one algorithm
    // regardless of which safe legacy encoding drives it, so this exercises
    // the byte path without needing every pattern to also survive heuristic
    // detection a second time under a different encoding.
    public static IEnumerable<object[]> LegacyRepresentativeCases()
    {
        yield return ["", false, false, false, "None"];
        yield return ["a\nb\nc\n", false, true, false, "Lf"];
        yield return ["a\rb\rc\r", false, false, true, "Cr"];
        yield return ["a\r\nb\r\nc\r\n", true, false, false, "Crlf"];
        yield return ["a\rb\nc\r\nd", true, true, true, "Mixed"];
        yield return ["line1\r\nline2\r", true, false, true, "Mixed"];
    }

    [Theory]
    [MemberData(nameof(LegacyRepresentativeCases))]
    public void Windows1252_LineEndingMatrix_ClassifiesAndDecidesConversion(
        string suffix, bool sawCrLf, bool sawLf, bool sawCr, string expectedKindName)
    {
        LineEndingKind expectedKind = Enum.Parse<LineEndingKind>(expectedKindName);

        foreach (LineEnding target in AllTargets)
        {
            using Stream stream = OpenCp1252(suffix);

            ScanEngine.ScanResult? scan =
                ScanEngine.Scan(stream, target);

            Assert.NotNull(scan);
            Assert.Equal(1252, scan.Detection.Encoding.CodePage);
            Assert.Equal(expectedKind, scan.Detection.LineEndingKind);
            Assert.Equal(
                ExpectedRequiresConversion(sawCrLf, sawLf, sawCr, target),
                scan.RequiresConversion);
        }
    }

    // ---- Test 2: target == null contract ----

    [Fact]
    public void Utf8_TargetNull_ClassifiesButNeverRequiresConversion()
    {
        // Non-ASCII byte so this genuinely detects as UTF-8, not US-ASCII.
        using Stream stream = OpenUtf8("caf" + "é" + "\rb\nc\r\nd");

        ScanEngine.ScanResult? scan =
            ScanEngine.Scan(stream, target: null);

        Assert.NotNull(scan);
        Assert.Equal(Encoding.UTF8.WebName, scan.Detection.Encoding.WebName);
        Assert.False(scan.Detection.HasBom);
        Assert.Equal(LineEndingKind.Mixed, scan.Detection.LineEndingKind);
        Assert.False(scan.RequiresConversion);
    }

    [Fact]
    public void Windows1252_TargetNull_ClassifiesButNeverRequiresConversion()
    {
        using Stream stream = OpenCp1252("a\rb\nc\r\nd");

        ScanEngine.ScanResult? scan =
            ScanEngine.Scan(stream, target: null);

        Assert.NotNull(scan);
        Assert.Equal(1252, scan.Detection.Encoding.CodePage);
        Assert.Equal(LineEndingKind.Mixed, scan.Detection.LineEndingKind);
        Assert.False(scan.RequiresConversion);
    }

    // ---- Test 3: NEL/LS/PS asymmetry ----

    [Theory]
    [InlineData('\u0085')]
    [InlineData('\u2028')]
    [InlineData('\u2029')]
    public void Utf8_UnicodeLineSeparatorOnly_ClassifiesNone_ButRequiresConversion(char separator)
    {
        var sb = new StringBuilder();

        for (int i = 0; i < 40; i++)
        {
            sb.Append("some readable text ").Append(i).Append(separator);
        }

        using Stream stream = OpenUtf8(sb.ToString());

        ScanEngine.ScanResult? scan =
            ScanEngine.Scan(stream, LineEnding.Crlf);

        Assert.NotNull(scan);
        Assert.Equal(LineEndingKind.None, scan.Detection.LineEndingKind);
        Assert.True(scan.RequiresConversion);
    }

    [Fact]
    public void Utf8_UnicodeLineSeparatorCombinedWithCrLf_ExcludedFromClassification_ButStillForcesConversion()
    {
        var sb = new StringBuilder();

        for (int i = 0; i < 40; i++)
        {
            sb.Append("some readable text ").Append(i)
              .Append(i % 2 == 0 ? "\r\n" : "\u2028");
        }

        using Stream stream = OpenUtf8(sb.ToString());

        // Target matches the only *classified* line ending (CRLF), so
        // without the separator this would be Unchanged - proving the
        // separator alone is what forces conversion here.
        ScanEngine.ScanResult? scan =
            ScanEngine.Scan(stream, LineEnding.Crlf);

        Assert.NotNull(scan);
        Assert.Equal(LineEndingKind.Crlf, scan.Detection.LineEndingKind);
        Assert.True(scan.RequiresConversion);
    }

    // ---- Test 4: buffer-boundary CR/LF ----

    [Fact]
    public void Utf8_CrlfSplitAcrossBoundary_ClassifiesCrlf()
    {
        // Byte 65535 is '\r', byte 65536 is '\n': the pair straddles the
        // 65536-byte read buffer exactly.
        string filler = new string('x', 65535);
        string content = filler + "\r\n" + "more content after the split";

        using Stream stream = OpenUtf8(content);

        ScanEngine.ScanResult? scan =
            ScanEngine.Scan(stream, LineEnding.Lf);

        Assert.NotNull(scan);
        Assert.Equal(LineEndingKind.Crlf, scan.Detection.LineEndingKind);
        Assert.True(scan.RequiresConversion);
    }

    [Fact]
    public void Utf8_CrAtBoundary_OrdinaryCharacterNext_ClassifiesCr()
    {
        string filler = new string('x', 65535);
        string content = filler + "\r" + "y"; // CR at 65535, ordinary char at 65536

        using Stream stream = OpenUtf8(content);

        ScanEngine.ScanResult? scan =
            ScanEngine.Scan(stream, LineEnding.Crlf);

        Assert.NotNull(scan);
        Assert.Equal(LineEndingKind.Cr, scan.Detection.LineEndingKind);
        Assert.True(scan.RequiresConversion);
    }

    [Fact]
    public void Utf8_StandaloneCrAtEndOfFile_ClassifiesCr()
    {
        using Stream stream = OpenUtf8("abc\r");

        ScanEngine.ScanResult? scan =
            ScanEngine.Scan(stream, LineEnding.Cr);

        Assert.NotNull(scan);
        Assert.Equal(LineEndingKind.Cr, scan.Detection.LineEndingKind);
        Assert.False(scan.RequiresConversion);
    }

    [Fact]
    public void Windows1252_CrlfSplitAcrossBoundary_ClassifiesCrlf()
    {
        Encoding cp1252 = Encoding.GetEncoding(1252);

        // Single-byte encoding: char count equals byte count, so padding to
        // an exact byte offset is direct.
        string filler = Cp1252Carrier + new string('x', 65535 - Cp1252Carrier.Length);
        string content = filler + "\r\n" + "more";

        Assert.Equal(65535, filler.Length);

        using Stream stream = new MemoryStream(cp1252.GetBytes(content));

        ScanEngine.ScanResult? scan =
            ScanEngine.Scan(stream, LineEnding.Lf);

        Assert.NotNull(scan);
        Assert.Equal(1252, scan.Detection.Encoding.CodePage);
        Assert.Equal(LineEndingKind.Crlf, scan.Detection.LineEndingKind);
        Assert.True(scan.RequiresConversion);
    }

    // ---- Test 5: UTF-8 decoder boundary ----

    [Fact]
    public void Utf8_MultiByteCharacterSplitAcrossBoundary_DetectsAndClassifiesCorrectly()
    {
        string lead = "cafe ";

        string filler = lead + new string(
            'x', 65536 - 1 - Encoding.UTF8.GetByteCount(lead));

        // Euro sign is 3 UTF-8 bytes; its first byte lands at the last byte
        // of the first read chunk, so the remaining two fall in the next.
        string content = filler + "\u20AC" + "\n";

        Assert.Equal(65536 - 1, Encoding.UTF8.GetByteCount(filler));

        using Stream stream = OpenUtf8(content);

        ScanEngine.ScanResult? scan =
            ScanEngine.Scan(stream, LineEnding.Crlf);

        Assert.NotNull(scan);
        Assert.Equal(LineEndingKind.Lf, scan.Detection.LineEndingKind);
        Assert.True(scan.RequiresConversion);
    }

    // ---- Test 6: malformed Unicode after an early mismatch (full-scan regression) ----

    [Fact]
    public void WhatIf_DetectsMalformedUnicode_EvenAfterEarlyLineEndingMismatch()
    {
        using var dir = new TempDirectory();

        // Early CRLF mismatches target LF immediately - the old early-exit
        // scanner would have stopped here and never reached the malformed
        // tail. Placed past TextEncoding.DefaultMaxSampleBytes so detection
        // (which only samples the start of the file) still succeeds.
        int splitOffset = TextEncoding.DefaultMaxSampleBytes + 200;

        var sb = new StringBuilder();
        sb.Append("first\r\n");

        for (int i = 0; sb.Length < splitOffset; i++)
        {
            sb.Append("line ").Append(i).Append('\n');
        }

        byte[] ascii = Encoding.ASCII.GetBytes(sb.ToString());
        Assert.True(ascii.Length > splitOffset);

        byte[] source = [.. ascii, 0xFF, 0xFE];

        string path = dir.WriteFile("malformed_after_mismatch.txt", source);

        Assert.Throws<DecoderFallbackException>(() =>
            NewLineNormalizer.NormalizeFile(path, LineEnding.Lf, whatIf: true));

        // WhatIf must never write, even when the scan itself throws.
        Assert.Equal(source, File.ReadAllBytes(path));
    }

    // ---- Test 7: cross-operation consistency ----

    // A short NEL/LS/PS-only sample doesn't carry enough text for the
    // heuristic detector to confidently commit to an encoding; pad with
    // repeated readable text, matching the pattern already proven to work
    // in Utf8_UnicodeLineSeparatorOnly_ClassifiesNone_ButRequiresConversion.
    private static string SeparatorOnlySample(char separator)
    {
        var sb = new StringBuilder();

        for (int i = 0; i < 40; i++)
        {
            sb.Append("some readable text ").Append(i).Append(separator);
        }

        return sb.ToString();
    }

    public static IEnumerable<object[]> CrossOperationCases()
    {
        yield return ["a\nb\n", "Lf"];
        yield return ["a\nb\n", "Crlf"];
        yield return ["a\r\nb\r\n", "Lf"];
        yield return ["a\r\nb\r\n", "Cr"];
        yield return ["a\rb\r", "Crlf"];
        yield return ["a\rb\nc\r\n", "Crlf"];
        yield return [SeparatorOnlySample('\u0085'), "Crlf"];
        yield return [SeparatorOnlySample('\u2028'), "Crlf"];
        yield return [SeparatorOnlySample('\u2029'), "Crlf"];
    }

    [Theory]
    [MemberData(nameof(CrossOperationCases))]
    public void ScanEngine_DetectFile_And_WhatIf_AgreeForSameFile(string content, string targetName)
    {
        LineEnding target = Enum.Parse<LineEnding>(targetName);

        using var dir = new TempDirectory();
        string path = dir.WriteFile("cross.txt", Encoding.UTF8.GetBytes(content));

        ScanEngine.ScanResult? scan;

        using (Stream stream = File.OpenRead(path))
        {
            scan = ScanEngine.Scan(stream, target);
        }

        DetectResult? detectResult = NewLineNormalizer.DetectFile(path);
        NormalizeResult normalizeResult = NewLineNormalizer.NormalizeFile(path, target, whatIf: true);

        Assert.NotNull(scan);
        Assert.NotNull(detectResult);

        Assert.Equal(scan.Detection.Encoding, detectResult.Encoding);
        Assert.Equal(scan.Detection.HasBom, detectResult.HasBom);
        Assert.Equal(scan.Detection.LineEndingKind, detectResult.LineEndingKind);

        NormalizeResult expected =
            scan.RequiresConversion ? NormalizeResult.Converted : NormalizeResult.Unchanged;

        Assert.Equal(expected, normalizeResult);
    }

    // ---- Test 8: DetectFile delegation ----

    [Fact]
    public void DetectFile_MatchesDirectScanEngineCall_ForSameFile()
    {
        using var dir = new TempDirectory();
        string path = dir.WriteFile("delegate.txt", Encoding.UTF8.GetBytes("a\rb\nc\r\nd"));

        DetectResult? viaDetectFile = NewLineNormalizer.DetectFile(path);

        ScanEngine.ScanResult? viaScan;
        using (Stream stream = File.OpenRead(path))
        {
            viaScan = ScanEngine.Scan(stream, target: null);
        }

        Assert.NotNull(viaDetectFile);
        Assert.NotNull(viaScan);
        Assert.Equal(viaScan.Detection.Encoding, viaDetectFile.Encoding);
        Assert.Equal(viaScan.Detection.HasBom, viaDetectFile.HasBom);
        Assert.Equal(viaScan.Detection.LineEndingKind, viaDetectFile.LineEndingKind);
    }

    // ---- Test 9: cancellation ----

    [Fact]
    public void Scan_PreCancelledToken_ThrowsImmediately()
    {
        using Stream stream = OpenUtf8("a\r\nb\r\n");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            ScanEngine.Scan(stream, LineEnding.Crlf, cts.Token));
    }

    [Fact]
    public void Scan_CancelledMidScan_ObservesCancellationBetweenBuffers()
    {
        // Several buffers' worth of content so there are multiple scan-loop
        // reads to cancel between, proving the check isn't only at entry.
        string content = string.Concat(Enumerable.Repeat(new string('x', 65536), 5)) + "\r\n";
        byte[] bytes = Encoding.UTF8.GetBytes(content);

        using var cts = new CancellationTokenSource();
        using var memory = new MemoryStream(bytes);

        // Lets the detection read and the first two scan-loop reads through,
        // then cancels - well before the 5-buffer file would finish anyway.
        using var wrapped = new CancelAfterReadsStream(memory, readsBeforeCancel: 3, cts);

        Assert.Throws<OperationCanceledException>(() =>
            ScanEngine.Scan(wrapped, LineEnding.Crlf, cts.Token));
    }

    /// <summary>
    /// Test-only wrapper that cancels a token after its Nth <see cref="Read"/>
    /// call, so cancellation mid-scan can be tested deterministically instead
    /// of racing a background thread against real I/O.
    /// </summary>
    private sealed class CancelAfterReadsStream : Stream
    {
        private readonly Stream _inner;
        private readonly int _readsBeforeCancel;
        private readonly CancellationTokenSource _cts;
        private int _reads;

        public CancelAfterReadsStream(Stream inner, int readsBeforeCancel, CancellationTokenSource cts)
        {
            _inner = inner;
            _readsBeforeCancel = readsBeforeCancel;
            _cts = cts;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = _inner.Read(buffer, offset, count);

            if (++_reads >= _readsBeforeCancel)
            {
                _cts.Cancel();
            }

            return read;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
