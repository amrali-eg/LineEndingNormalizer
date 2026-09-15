using System.Text;

namespace LineEndingNormalizer.Tests;

/// <summary>
/// Coverage for TextEncoding.IsSafeLegacyEncoding: the gate that decides
/// which legacy (non-Unicode) encodings are trusted for raw byte-level
/// CR/LF normalization instead of being reported as undetected.
/// </summary>
public sealed class TextEncodingTests
{
    static TextEncodingTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Theory]
    // Single-byte encodings: safe generically, via the runtime C0 check --
    // not because they're individually hardcoded.
    [InlineData(1252, true)]   // windows-1252
    [InlineData(28591, true)]  // iso-8859-1
    [InlineData(28597, true)]  // iso-8859-7 (Greek) -- what UtfUnknown
                                // actually returns for Greek text, not
                                // windows-1253; not individually named
                                // anywhere, covered only by the generic
                                // single-byte check.
    [InlineData(28598, true)]  // iso-8859-8 (Hebrew) -- likewise what
                                // UtfUnknown returns for Hebrew text.
    [InlineData(852, true)]    // ibm852
    [InlineData(20866, true)]  // koi8-r
    // Multi-byte encodings: only the explicitly pre-swept ones are safe.
    [InlineData(932, true)]    // shift_jis -- on the explicit allowlist
    [InlineData(51949, true)]  // euc-kr -- on the explicit allowlist
    [InlineData(950, true)]    // big5 -- on the explicit allowlist
    [InlineData(50220, false)] // iso-2022-jp -- multi-byte, stateful,
                                // NOT on the allowlist: never swept and
                                // verified, so must be rejected rather
                                // than assumed safe.
    public void IsSafeLegacyEncoding_MatchesVerifiedSet(int codepage, bool expectedSafe)
    {
        Encoding encoding = Encoding.GetEncoding(codepage);

        Assert.Equal(expectedSafe, TextEncoding.IsSafeLegacyEncoding(encoding));
    }

    [Fact]
    public void DetectFromBuffer_NeverReturnsAnUnsafeLegacyEncoding()
    {
        // Greek text is what originally exposed this: UtfUnknown reports
        // it as iso-8859-7, not windows-1253, and the safety gate must
        // accept that (via the generic single-byte check) rather than
        // reject it for not being individually named. Built from Unicode
        // code points (Greek lowercase alphabet, U+03B1-U+03C9) rather
        // than literal characters, matching LegacyEncodingCorpusTests.cs's
        // convention of keeping this repository's own source files ASCII.
        var sb = new StringBuilder();

        for (int i = 0; sb.Length < 900; i++)
        {
            char c = (char)(0x03B1 + i % (0x03C9 - 0x03B1 + 1));
            sb.Append(c);

            if (i % 6 == 5)
            {
                sb.Append(' ');
            }
        }

        Encoding iso88597 = Encoding.GetEncoding(28597);
        byte[] bytes = iso88597.GetBytes(sb.ToString());

        Encoding? detected = TextEncoding.DetectFromBuffer(bytes);

        Assert.NotNull(detected);
        Assert.True(TextEncoding.IsSafeLegacyEncoding(detected));
    }
}
