using System.Text;

namespace LineEndingNormalizer.Tests;

/// <summary>
/// Pins the strict-fallback contract at the point where it is easy to lose.
///
/// Assigning <see cref="Decoder.Fallback"/> after <see cref="Encoding.GetDecoder"/>
/// compiles, reads as correct, and does nothing for the encodings that come from
/// <see cref="CodePagesEncodingProvider"/>: those codecs capture their fallbacks from the
/// parent <see cref="Encoding"/> when they are created. The fallbacks have to be supplied
/// to <see cref="Encoding.GetEncoding(int, EncoderFallback, DecoderFallback)"/>, which is
/// what <see cref="TextEncoding.Strict"/> does.
///
/// LosslessFileWriter and ScanEngine are not exposed to this: both reach a decoder only
/// behind <see cref="TextEncoding.IsUnicodeEncoding"/>, and every code page on that
/// whitelist honours the assignment. TextValidation is the one place in this codebase that
/// is handed arbitrary legacy encodings, so it is the one that needs the rebuild - and the
/// whitelist's protective role is asserted here so that widening it cannot silently
/// reintroduce the defect elsewhere.
/// </summary>
public sealed class StrictFallbackEnforcementTests
{
    // EUC-JP bytes whose second character is a JIS X 0212 sequence introduced by SS3
    // (0x8F). Code page 51932 has no mapping for it, so a correctly strict decoder must
    // reject these bytes rather than substitute.
    private static readonly byte[] JisX0212Bytes =
        [0x8F, 0xB0, 0xDF, 0xB9, 0xA5, 0xA1, 0xA4, 0xC0, 0xA4, 0xB3, 0xA6, 0xA1, 0xAA];

    [Fact]
    public void AssigningDecoderFallbackAfterConstruction_DoesNotTakeEffect()
    {
        Encoding encoding = Encoding.GetEncoding("euc-jp");

        Decoder decoder = encoding.GetDecoder();
        decoder.Fallback = DecoderFallback.ExceptionFallback;

        char[] buffer = new char[encoding.GetMaxCharCount(JisX0212Bytes.Length)];

        // No exception: the assignment above was silently ignored. Asserted so a change
        // in platform behaviour is caught here rather than in the field.
        int written = decoder.GetChars(
            JisX0212Bytes, 0, JisX0212Bytes.Length, buffer, 0, flush: true);

        Assert.True(written > 0);
    }

    [Fact]
    public void TextEncodingStrict_MakesTheFallbackTakeEffect()
    {
        Encoding strict = TextEncoding.Strict(Encoding.GetEncoding("euc-jp"));

        Assert.Throws<DecoderFallbackException>(() => strict.GetString(JisX0212Bytes));
    }

    [Fact]
    public void TextEncodingStrict_RefusesAnEncodingThatCannotBeRebuiltStrictly()
    {
        // Returning the original encoding would reintroduce replacement fallback.
        Assert.Throws<NotSupportedException>(() => TextEncoding.Strict(new UnrebuildableEncoding()));
    }

    [Fact]
    public void TextValidation_RejectsBytesTheEncodingCannotRepresent()
    {
        // IsValidText independently validates UtfUnknown's answer, and the encodings it
        // is asked about are exactly the ones where the plain assignment does nothing.
        // Unfixed, the decode substitutes, the substituted characters still look like
        // text, and the gate confirms a codec that cannot read the file.
        Assert.False(
            TextValidation.IsValidText(Encoding.GetEncoding("euc-jp"), JisX0212Bytes));
    }

    [Fact]
    public void TextValidation_StillAcceptsContentTheEncodingCanRepresent()
    {
        Encoding eucJp = Encoding.GetEncoding("euc-jp");
        byte[] bytes = eucJp.GetBytes("こんにちは世界。日本語のテキストです。");

        Assert.True(TextValidation.IsValidText(eucJp, bytes));
    }

    [Theory]
    [InlineData(20127)]   // ASCII
    [InlineData(65001)]   // UTF-8
    [InlineData(1200)]    // UTF-16LE
    [InlineData(1201)]    // UTF-16BE
    [InlineData(12000)]   // UTF-32LE
    [InlineData(12001)]   // UTF-32BE
    public void EveryWhitelistedUnicodeCodePage_HonoursAssignedDecoderFallback(int codePage)
    {
        // Why LosslessFileWriter and ScanEngine are safe without the rebuild: every code
        // page IsUnicodeEncoding admits is a BCL encoding whose decoder does honour the
        // assignment. If this ever fails, those call sites need TextEncoding.Strict too.
        Encoding encoding = Encoding.GetEncoding(codePage);

        Assert.True(TextEncoding.IsUnicodeEncoding(encoding));

        Decoder decoder = encoding.GetDecoder();
        decoder.Fallback = DecoderFallback.ExceptionFallback;

        // 0xFF 0xFE 0xFD is not valid in any of these: an odd-length or malformed unit
        // sequence for the UTF-16/32 forms, and out of range for ASCII and UTF-8.
        byte[] invalid = [0xFF, 0xFE, 0xFD];
        char[] chars = new char[encoding.GetMaxCharCount(invalid.Length)];

        Assert.Throws<DecoderFallbackException>(
            () => decoder.GetChars(invalid, 0, invalid.Length, chars, 0, flush: true));
    }

    [Fact]
    public void IsUnicodeEncoding_RejectsLegacyCodePages()
    {
        // The guard's other half: legacy encodings must not reach the decoder paths that
        // rely on the assignment working.
        Assert.False(TextEncoding.IsUnicodeEncoding(Encoding.GetEncoding("euc-jp")));
        Assert.False(TextEncoding.IsUnicodeEncoding(Encoding.GetEncoding(1252)));
        Assert.False(TextEncoding.IsUnicodeEncoding(Encoding.GetEncoding("shift_jis")));
    }

    private sealed class UnrebuildableEncoding : Encoding
    {
        public override int CodePage => 65_000_002;

        public override int GetByteCount(char[] chars, int index, int count) => count;

        public override int GetBytes(
            char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex) => 0;

        public override int GetCharCount(byte[] bytes, int index, int count) => count;

        public override int GetChars(
            byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex) => 0;

        public override int GetMaxByteCount(int charCount) => charCount;

        public override int GetMaxCharCount(int byteCount) => byteCount;
    }
}
