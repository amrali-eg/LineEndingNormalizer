using System.Text;

namespace LineEndingNormalizer.Tests;

/// <summary>
/// Coverage for whether the conversion pipeline (everything downstream of
/// EncodingDetector) already handles legacy single-byte code pages
/// correctly, given one directly. EncodingDetector itself has no
/// legacy-encoding detection yet -- these tests document that this is
/// genuinely the only gap, by driving the rest of the pipeline with
/// Windows-1252 as if detection had already produced it.
/// </summary>
public sealed class LegacyEncodingReadinessTests
{
    // Program.Main registers this; tests run in a separate process and
    // must register it themselves to resolve GetEncoding(1252) at all.
    static LegacyEncodingReadinessTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Fact]
    public void Windows1252_RoundTripsThroughFullPipeline_WithLineEndingsNormalized()
    {
        using var dir = new TempDirectory();

        Encoding cp1252 = Encoding.GetEncoding(1252);

        // café / naïve use accented Latin-1-range characters; € (0x80) and
        // — (0x97, em dash) are specifically in the Windows-1252 repertoire
        // where ISO-8859-1 has control characters -- exercising exactly
        // what makes Windows-1252 different from plain Latin-1.
        string original = "café — 5€\nnaïve\r\nend\r";
        byte[] source = cp1252.GetBytes(original);

        string path = dir.WriteFile("cp1252.txt", source);

        // Real Length/timestamps (not approximated) so the source-
        // revalidation check added for source-mutation detection doesn't
        // reject this as "changed" -- this test exercises the full
        // success path, not a deliberate-failure one.
        var info = new FileInfo(path);

        var metadata = new FileMetadata(
            info.Attributes,
            info.Length,
            info.CreationTimeUtc,
            info.LastWriteTimeUtc,
            info.LastAccessTimeUtc);

        using (FileStream handle = File.OpenRead(path))
        {
            LosslessFileWriter.ConvertFile(
                handle,
                path,
                cp1252,
                LineEnding.Crlf,
                metadata,
                createBackup: false);
        }

        byte[] converted = File.ReadAllBytes(path);
        string decoded = cp1252.GetString(converted);

        Assert.Equal("café — 5€\r\nnaïve\r\nend\r\n", decoded);

        // Bytes that aren't part of a line break must round-trip
        // byte-for-byte: decode(b, cp1252) -> encode(., cp1252) is an
        // identity function for every defined code point, so a detector
        // that guesses the "wrong" (but still valid) single-byte code page
        // cannot corrupt data -- it can only mislabel it.
        byte[] expected = cp1252.GetBytes("café — 5€\r\nnaïve\r\nend\r\n");
        Assert.Equal(expected, converted);
    }

    [Fact]
    public void Windows1252_HasNoUndefinedByteValues_UnlikeUnicodeEncodings()
    {
        // Verified empirically (not assumed): .NET's CodePagesEncodingProvider
        // maps every one of Windows-1252's five "officially unmapped"
        // positions (0x81, 0x8D, 0x8F, 0x90, 0x9D) to the matching C1
        // control character rather than leaving it undefined. The upshot:
        // *every* byte 0x00-0xFF decodes successfully as Windows-1252 --
        // there is no invalid byte sequence to strictly reject.
        //
        // This matters directly for the planned EncodingDetector extension:
        // unlike UTF-8/16/32, where a failed strict decode is the primary,
        // highly reliable rejection signal, that signal has *zero*
        // discriminating power for Windows-1252. Detection would have to
        // rely entirely on the printable-ratio/entropy heuristics already
        // used to confirm UTF-8/16/32 candidates -- there's no structural
        // validity check to fall back on for this encoding.
        Encoding cp1252 = Encoding.GetEncoding(1252);

        for (int b = 0; b <= 0xFF; b++)
        {
            Decoder decoder = cp1252.GetDecoder();
            decoder.Fallback = DecoderFallback.ExceptionFallback;

            byte[] source = [(byte)b];
            char[] chars = new char[8];

            int written = decoder.GetChars(source, 0, 1, chars, 0, flush: true);

            Assert.True(written > 0, $"Byte 0x{b:X2} unexpectedly failed to decode.");
        }
    }

    [Fact]
    public void Windows1252_BufferCapacityHelpers_BehaveSanely()
    {
        // LosslessFileWriter sizes its pooled buffers off
        // Encoding.GetMaxCharCount/GetMaxByteCount; confirms those aren't
        // implicitly assumed to be Unicode-only anywhere reachable.
        Encoding cp1252 = Encoding.GetEncoding(1252);

        Assert.True(cp1252.GetMaxCharCount(65536) > 0);
        Assert.True(cp1252.GetMaxByteCount(65536) > 0);
        Assert.Empty(cp1252.GetPreamble());
    }
}
