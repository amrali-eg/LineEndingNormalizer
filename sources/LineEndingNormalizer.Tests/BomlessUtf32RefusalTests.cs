using System.Text;

namespace LineEndingNormalizer.Tests;

/// <summary>
/// BOM-less UTF-32 is an estimate detection cannot establish, so it is refused.
/// </summary>
/// <remarks>
/// Ported from EncodingChecker's suite of the same name, which closed this as a
/// real historical defect. Two silent-corruption paths closed by one rule. A
/// BOM-less UTF-16 file with one character per line puts a C0 control in every
/// second code unit, so each four-byte group is an in-range unassigned scalar
/// and the file decodes as UTF-32 - converting it rewrites different text.
/// Separately, genuine BOM-less UTF-32 can be valid under both byte orders, and
/// detection prefers little-endian without saying so.
///
/// An opposite-order test cannot close the first: the UTF-16 file's bytes are
/// *not* valid under the opposite UTF-32 order, so the ambiguity check that
/// protects UTF-16 returns false. What cannot be proven is the codec, not just
/// its byte order.
///
/// The cost is that BOM-less UTF-32 no longer converts automatically even when
/// its content is unambiguous. That is deliberate: nothing in the bytes
/// distinguishes it from the UTF-16 file above. Unlike EncodingChecker, LEN has
/// no source-encoding override flag, so there is no documented way through this
/// refusal short of adding a byte-order mark.
/// </remarks>
public sealed class BomlessUtf32RefusalTests
{
    /// <summary>Finding 1: UTF-16 that also decodes as UTF-32 must not be converted.</summary>
    [Fact]
    public void Utf16ThatAlsoDecodesAsUtf32IsRefusedRatherThanRewritten()
    {
        using var dir = new TempDirectory();

        // Alternating LF and NUL characters, UTF-16LE, no BOM: every 4-byte
        // group is [0x0A, 0x00, 0x00, 0x00], a valid UTF-32LE scalar (U+0A),
        // so the file misdetects as BOM-less UTF-32LE.
        string text = string.Concat(Enumerable.Repeat("\n\0", 20));
        byte[] source = Encoding.Unicode.GetBytes(text);
        string path = dir.WriteFile("perline.txt", source);

        DetectResult? detected = NewLineNormalizer.DetectFile(path);
        Assert.NotNull(detected);
        Assert.Equal(12000, detected.Encoding.CodePage);
        Assert.False(detected.HasBom);

        var ex = Assert.Throws<ConversionRefusedException>(() =>
            NewLineNormalizer.NormalizeFile(
                path, LineEnding.Crlf, whatIf: false, backup: true));

        Assert.Equal(BomlessUnicodeSafety.UnprovableUtf32ReasonCode, ex.ReasonCode);
        Assert.Equal(source, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".bak"));
    }

    /// <summary>
    /// Finding 2: UTF-32 valid under both byte orders must not be converted.
    /// </summary>
    /// <remarks>
    /// Reaches the guard directly rather than through NormalizeFile: a file
    /// with no recognized separator never reaches the safety check at all
    /// (RequiresConversion is false, so NormalizeFile reports Unchanged
    /// without inspecting the codec further), and embedding a real separator
    /// in one order's 4-byte group always invalidates that same group under
    /// the opposite order - needing conversion and being valid both ways
    /// cannot coexist in one file.
    /// </remarks>
    [Fact]
    public void Utf32ValidUnderBothByteOrdersIsRefusedRatherThanGuessed()
    {
        // U+10200 repeated. Its big-endian bytes are 00 01 02 00; read back as
        // little-endian, those same bytes are U+20100 -- a different valid,
        // non-surrogate scalar, so both orders strictly decode the whole file.
        string text = string.Concat(Enumerable.Repeat(char.ConvertFromUtf32(0x10200), 20));
        byte[] source = new UTF32Encoding(bigEndian: true, byteOrderMark: false).GetBytes(text);

        using var stream = new MemoryStream(source);

        var detection = new DetectResult(
            UnicodeDetector.Utf32LittleEndianNoBom,
            HasBom: false,
            LineEndingKind.None);

        var ex = Assert.Throws<ConversionRefusedException>(() =>
            BomlessUnicodeSafety.EnsureSafeToNormalize(stream, detection));

        Assert.Equal(BomlessUnicodeSafety.UnprovableUtf32ReasonCode, ex.ReasonCode);
    }

    /// <summary>
    /// The cost, stated as a test: ordinary BOM-less UTF-32 is refused too.
    /// </summary>
    [Fact]
    public void OrdinaryBomlessUtf32IsRefusedAsWell()
    {
        using var dir = new TempDirectory();

        byte[] source = new UTF32Encoding(false, false).GetBytes("Hello world\n");
        string path = dir.WriteFile("plain.txt", source);
        byte[] before = File.ReadAllBytes(path);

        var ex = Assert.Throws<ConversionRefusedException>(() =>
            NewLineNormalizer.NormalizeFile(
                path, LineEnding.Crlf, whatIf: false, backup: true));

        Assert.Equal(BomlessUnicodeSafety.UnprovableUtf32ReasonCode, ex.ReasonCode);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    /// <summary>A BOM settles the codec, so UTF-32 with one still converts.</summary>
    [Fact]
    public void Utf32WithABomStillConverts()
    {
        using var dir = new TempDirectory();

        var utf32 = new UTF32Encoding(false, true);
        byte[] source = [.. utf32.GetPreamble(), .. utf32.GetBytes("Hello\n")];
        string path = dir.WriteFile("bom.txt", source);

        NormalizeResult result = NewLineNormalizer.NormalizeFile(
            path, LineEnding.Crlf, whatIf: false, backup: false);

        Assert.Equal(NormalizeResult.Converted, result);
    }

    /// <summary>
    /// Private-use characters must keep converting: icon fonts put them in
    /// ordinary text files, so a rule that rejected unassigned or
    /// private-use scalars would break real sources. This fix deliberately
    /// changes no scalar classification.
    /// </summary>
    [Fact]
    public void PrivateUseCharactersStillConvert()
    {
        // U+F0000 is plane 15 private use, well outside the BMP.
        string text = "icon     " + char.ConvertFromUtf32(0xF0000) + "\n";
        var utf16 = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);

        using var dir = new TempDirectory();
        string path = dir.WriteFile("icons.txt", [.. utf16.GetPreamble(), .. utf16.GetBytes(text)]);

        NormalizeResult result = NewLineNormalizer.NormalizeFile(
            path, LineEnding.Crlf, whatIf: false, backup: false);

        Assert.Equal(NormalizeResult.Converted, result);
    }

    /// <summary>Big-endian is refused exactly as little-endian is.</summary>
    [Fact]
    public void BigEndianBehavesTheSameWayAsLittleEndian()
    {
        using var dir = new TempDirectory();

        byte[] source = new UTF32Encoding(true, false).GetBytes("Hello world\n");
        string path = dir.WriteFile("be.txt", source);
        byte[] before = File.ReadAllBytes(path);

        var ex = Assert.Throws<ConversionRefusedException>(() =>
            NewLineNormalizer.NormalizeFile(
                path, LineEnding.Crlf, whatIf: false, backup: true));

        Assert.Equal(BomlessUnicodeSafety.UnprovableUtf32ReasonCode, ex.ReasonCode);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    /// <summary>A big-endian BOM settles the codec, so the file converts.</summary>
    [Fact]
    public void BigEndianWithABomStillConverts()
    {
        using var dir = new TempDirectory();

        var utf32 = new UTF32Encoding(true, true);
        byte[] source = [.. utf32.GetPreamble(), .. utf32.GetBytes("Hello\n")];
        string path = dir.WriteFile("bebom.txt", source);

        NormalizeResult result = NewLineNormalizer.NormalizeFile(
            path, LineEnding.Crlf, whatIf: false, backup: false);

        Assert.Equal(NormalizeResult.Converted, result);
    }
}
