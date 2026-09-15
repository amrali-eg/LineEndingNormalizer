using System.Text;

namespace LineEndingNormalizer.Tests;

/// <summary>
/// LooksLikeText returned false on the first UnicodeCategory.PrivateUse scalar, so a
/// UTF-8 or UTF-16 file containing one icon-font glyph failed detection entirely and was
/// reported "Skipped (unknown encoding)" - its line endings were never normalized. The
/// behaviour was also position-dependent: only the first 500 scalars are examined, so the
/// same character further into the file was accepted.
///
/// Private-use scalars are now excluded from the printable ratio like control characters,
/// sharing one budget with them. A largely private-use buffer still fails, which is the
/// binary evidence the check exists to find.
///
/// Ported from the equivalent fix in EncodingChecker, which shares this detection stack.
/// </summary>
public sealed class PrivateUseAreaDetectionTests
{
    // Written as escapes, not literals. Private-use characters render as nothing and do
    // not reliably survive editing; a constant that silently became empty would leave
    // every test below asserting against ordinary text and passing for the wrong reason.
    private const string HomeIcon = "";     // Font Awesome "home"
    private const string ProfileIcon = "";
    private const string SaveIcon = "";
    private const string FirstPua = "";     // first private-use scalar

    private static string? Detect(string text, Encoding encoding) =>
        TextEncoding.DetectFromBuffer(encoding.GetBytes(text))?.WebName;

    private static string Repeat(string s, int times) =>
        string.Concat(Enumerable.Repeat(s, times));

    public static IEnumerable<object[]> Encodings() =>
    [
        [new UTF8Encoding(false), "utf-8"],
        [new UnicodeEncoding(false, false), "utf-16"],
    ];

    [Fact]
    public void TheFixtureConstantsAreActuallyPrivateUse()
    {
        // Guards the escapes above: if these ever became empty or ordinary characters,
        // the rest of this file would still pass while testing nothing.
        foreach (string s in new[] { HomeIcon, ProfileIcon, SaveIcon, FirstPua })
        {
            Rune rune = Assert.Single(s.EnumerateRunes());
            Assert.Equal(
                System.Globalization.UnicodeCategory.PrivateUse,
                Rune.GetUnicodeCategory(rune));
        }
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void IconFontMarkup_IsDetected(Encoding encoding, string expected)
    {
        string markup =
            "<nav class=\"menu\">\n" +
            $"  <i class=\"icon\">{HomeIcon}</i> Home\n" +
            $"  <i class=\"icon\">{ProfileIcon}</i> Profile\n" +
            $"  <i class=\"icon\">{SaveIcon}</i> Save\n" +
            "</nav>\n";

        Assert.Equal(expected, Detect(markup, encoding));
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void OnePrivateUseScalarInOrdinaryText_IsDetected(Encoding encoding, string expected)
    {
        string text =
            $"A perfectly ordinary sentence with one {FirstPua} private-use character in it.\n" +
            "Followed by more ordinary prose so the sample is comfortably text-like.\n";

        Assert.Equal(expected, Detect(text, encoding));
    }

    [Fact]
    public void PositionOfThePrivateUseScalar_NoLongerChangesTheOutcome()
    {
        string early = HomeIcon + Repeat("ordinary text ", 60);
        string late = Repeat("ordinary text ", 60) + HomeIcon;

        var utf8 = new UTF8Encoding(false);

        Assert.Equal("utf-8", Detect(early, utf8));
        Assert.Equal("utf-8", Detect(late, utf8));
        Assert.Equal(Detect(early, utf8), Detect(late, utf8));
    }

    [Fact]
    public void MostlyPrivateUseContent_IsStillRejected()
    {
        Assert.Null(TextEncoding.DetectFromBuffer(
            new UTF8Encoding(false).GetBytes(Repeat(FirstPua, 400))));
    }

    [Fact]
    public void PrivateUseUpToTheThreshold_IsAccepted()
    {
        // 50 private-use scalars in 500 leaves a printable ratio of exactly 0.90.
        string text = Repeat(FirstPua + Repeat("a", 9), 50);

        Assert.Equal(500, text.EnumerateRunes().Count());
        Assert.Equal("utf-8", Detect(text, new UTF8Encoding(false)));
    }

    [Fact]
    public void PrivateUsePastTheThreshold_IsRejected()
    {
        Assert.Null(TextEncoding.DetectFromBuffer(
            new UTF8Encoding(false).GetBytes(Repeat(FirstPua + "abc", 200))));
    }

    [Fact]
    public void PrivateUseText_IsNormalizedRatherThanSkipped()
    {
        // The practical consequence: such a file could not be normalized at all.
        using var dir = new TempDirectory();

        string content = $"<i class=\"icon\">{HomeIcon}</i> Home\nsecond line\n";
        string path = dir.WriteFile("icons.html", new UTF8Encoding(false).GetBytes(content));

        NormalizeResult result = NewLineNormalizer.NormalizeFile(
            path, LineEnding.Crlf, whatIf: false);

        Assert.Equal(NormalizeResult.Converted, result);
        Assert.Equal(
            content.Replace("\n", "\r\n"),
            new UTF8Encoding(false).GetString(File.ReadAllBytes(path)));
    }

    [Fact]
    public void BinaryLikeBuffers_AreStillRejected()
    {
        // Guards the trade: relaxing the private-use rule must not let binary through.
        var random = new Random(4242);

        byte[] randomBytes = new byte[4096];
        random.NextBytes(randomBytes);
        Assert.Null(TextEncoding.DetectFromBuffer(randomBytes));

        var puaHeavy = new StringBuilder();
        for (int i = 0; i < 600; i++)
            puaHeavy.Append((char)(0xE000 + (i % 0x1800)));

        Assert.Null(TextEncoding.DetectFromBuffer(
            new UnicodeEncoding(false, false).GetBytes(puaHeavy.ToString())));
    }
}
