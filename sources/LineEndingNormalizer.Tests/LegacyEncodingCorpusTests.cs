using System.Text;

namespace LineEndingNormalizer.Tests;

/// <summary>
/// Exhaustive round-trip testing of the TextEncoding/UtfUnknown-assisted
/// legacy encoding detection path: for a wide range of legacy code pages,
/// build representative text, run it through the real end-to-end pipeline
/// (detection included, not bypassed), and verify that decoding the result
/// under WHATEVER encoding was actually detected -- not necessarily the one
/// the sample was originally written with -- reproduces the original text
/// exactly, with only line endings changed.
///
/// Samples are real language text. They were previously built by cycling
/// Unicode code point ranges, which kept this file's own bytes ASCII but
/// produced text resembling no language's letter-frequency distribution.
/// UtfUnknown is frequency-based, so ten of these tests had to be skipped:
/// the detector either declined to guess or guessed a different code page,
/// and the skip reasons recorded the hypothesis that the samples, not the
/// product, were at fault. Replacing them with real sentences resolved all
/// ten -- every code page below now detects and round-trips losslessly.
///
/// The reason the old approach avoided literals still stands: this tool
/// chain has mangled literal non-ASCII written through file-editing tools
/// before, most recently turning private-use characters into empty strings.
/// Ordinary script letters have proven durable where invisible code points
/// did not, but that is an observation rather than a guarantee, so
/// SamplesAreIntact below fails loudly if any sample is ever emptied,
/// stripped of its non-ASCII content, or made unrepresentable in its own
/// code page. Without it, mangling would silently turn these into ASCII
/// round-trip tests that pass while proving nothing.
/// </summary>
public sealed class LegacyEncodingCorpusTests
{
    static LegacyEncodingCorpusTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    #region Language samples

    private const string Russian =
        "Все счастливые семьи похожи друг на друга, каждая несчастливая семья " +
        "несчастлива по-своему. Всё смешалось в доме Облонских. Жена узнала, " +
        "что муж был в связи с бывшею в их доме француженкою гувернанткой.";

    private const string Western =
        "L'été dernier, à Paris, nous avons mangé des crêpes délicieuses près " +
        "de la Seine. Grüße aus München, wo die Straßen im Frühling schön " +
        "sind. El niño pequeño comió una manzana roja en el jardín.";

    private const string Turkish =
        "Türkçe cümlelerde ünlü uyumu çok önemlidir ve öğrenciler bunu erken " +
        "öğrenir. İstanbul'da güzel bir gün geçirdik, Boğaz'da çay içtik ve " +
        "şehrin tarihî yapılarını gezdik.";

    private const string CentralEuropean =
        "Zażółć gęślą jaźń, powiedział wesoły chłopiec na łące pełnej kwiatów. " +
        "Příliš žluťoučký kůň úpěl ďábelské ódy a všichni se divili tomu zvuku.";

    private const string Greek =
        "Η ελληνική γλώσσα έχει μακρά ιστορία και πλούσια γραμματολογική " +
        "παράδοση. Οι αρχαίοι φιλόσοφοι έγραψαν σημαντικά έργα που " +
        "μελετώνται ακόμη και σήμερα σε όλο τον κόσμο.";

    private const string Hebrew =
        "השפה העברית היא שפה שמית עתיקה המדוברת בישראל על ידי מיליוני אנשים. " +
        "היא נכתבת מימין לשמאל ויש לה אלפבית בן עשרים ושתיים אותיות.";

    private const string Arabic =
        "اللغة العربية من أكثر اللغات انتشارا في العالم ويتحدث بها مئات " +
        "الملايين من الناس في مختلف الدول. ولها تاريخ طويل وأدب غني جدا.";

    private const string Baltic =
        "Lietuvių kalba yra viena seniausių gyvųjų indoeuropiečių kalbų " +
        "pasaulyje ir ją vartoja apie tris milijonus žmonių. Latviešu valoda " +
        "arī pieder pie baltu valodu grupas.";

    private const string Thai =
        "ภาษาไทยเป็นภาษาราชการของประเทศไทยและมีระบบเสียงวรรณยุกต์ที่ซับซ้อน " +
        "คนไทยใช้ภาษาไทยในชีวิตประจำวันและในการเรียนการสอนทุกระดับ";

    private const string Japanese =
        "日本語は日本で話されている言語であり、漢字とひらがなとカタカナを使います。" +
        "東京の桜が咲く春の季節には、多くの人が公園に集まって花見を楽しみます。";

    private const string Korean =
        "한국어는 한반도에서 사용되는 언어이며 한글이라는 고유한 문자를 사용합니다. " +
        "세종대왕이 훈민정음을 창제한 이후 한글은 널리 쓰이게 되었습니다.";

    private const string ChineseSimplified =
        "汉语是世界上使用人数最多的语言之一，简体中文在中国大陆广泛使用。" +
        "许多学生从小就开始学习汉字，并且通过阅读来提高自己的语言能力。";

    private const string ChineseTraditional =
        "漢語是世界上使用人數最多的語言之一，繁體中文在台灣和香港廣泛使用。" +
        "許多學生從小就開始學習漢字，並且透過閱讀來提高自己的語言能力。";

    #endregion

    #region Helpers

    /// <summary>
    /// Repeats a sample with mixed line endings until it comfortably exceeds the
    /// ~512-byte size where the entropy-based binary-rejection heuristic reliably
    /// engages, so this corpus exercises the "confidently detected, real text"
    /// case rather than the small-sample edge case.
    /// </summary>
    private static string GrowSample(string sample)
    {
        var sb = new StringBuilder();
        var lineBreaks = new[] { "\n", "\r\n", "\r" };
        int i = 0;

        // All three line-ending styles appear, so normalization has real work to do.
        while (sb.Length < 900)
        {
            sb.Append(sample).Append(lineBreaks[i % lineBreaks.Length]);
            i++;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Independent reference normalizer (deliberately implemented
    /// separately from LosslessFileWriter.NormalizeChars) for computing
    /// the expected post-conversion text.
    /// </summary>
    private static string NormalizeReference(string text, string target)
    {
        var sb = new StringBuilder();
        int i = 0;

        while (i < text.Length)
        {
            char c = text[i];

            if (c == '\r')
            {
                sb.Append(target);
                i += (i + 1 < text.Length && text[i + 1] == '\n') ? 2 : 1;
            }
            else if (c == '\n')
            {
                sb.Append(target);
                i++;
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Runs one encoding through the real end-to-end pipeline (detection
    /// included) and verifies the decoded result -- decoded under
    /// whichever encoding TextEncoding actually detected, which this test
    /// does not assume matches <paramref name="writeEncoding"/> -- exactly
    /// reproduces the original text with only line endings changed.
    /// </summary>
    private static void AssertRoundTripsLosslessly(
        Encoding writeEncoding,
        string sampleName,
        string languageSample)
    {
        using var dir = new TempDirectory();

        string original = GrowSample(languageSample);
        byte[] source = writeEncoding.GetBytes(original);

        string path = dir.WriteFile(sampleName + ".txt", source);

        DetectResult? detected = NewLineNormalizer.DetectFile(path);

        Assert.True(
            detected != null,
            $"{sampleName}: encoding was not detected at all (expected some encoding, possibly not {writeEncoding.WebName}).");

        NormalizeResult result =
            NewLineNormalizer.NormalizeFile(path, LineEnding.Crlf, whatIf: false);

        Assert.True(
            result is NormalizeResult.Converted or NormalizeResult.Unchanged,
            $"{sampleName}: expected Converted or Unchanged, got {result}.");

        byte[] finalBytes = File.ReadAllBytes(path);

        // Decode using whatever was actually detected -- the crux of what
        // was asked: does round-tripping through a *detected*, possibly
        // different-from-original encoding still preserve every character?
        string decoded = detected!.Encoding.GetString(finalBytes);

        // Strip the detected encoding's own preamble bytes before decoding
        // content, matching how the pipeline itself treats the BOM.
        byte[] preamble = detected.Encoding.GetPreamble();
        if (preamble.Length > 0 && finalBytes.AsSpan(0, preamble.Length).SequenceEqual(preamble))
        {
            decoded = detected.Encoding.GetString(finalBytes, preamble.Length, finalBytes.Length - preamble.Length);
        }

        string expected = NormalizeReference(original, "\r\n");

        Assert.True(
            expected == decoded,
            $"{sampleName}: text content changed across the round trip " +
            $"(written as {writeEncoding.WebName}, detected as {detected.Encoding.WebName}). " +
            $"First difference at index {FirstDiff(expected, decoded)}.");
    }

    private static int FirstDiff(string a, string b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
        {
            if (a[i] != b[i])
            {
                return i;
            }
        }

        return n;
    }

    #endregion

    #region Fixture integrity

    public static IEnumerable<object[]> AllSamples() =>
    [
        ["windows-1251", Russian], ["koi8-r", Russian],
        ["windows-1252", Western], ["iso-8859-1", Western],
        ["windows-1254", Turkish], ["windows-1250", CentralEuropean],
        ["windows-1253", Greek], ["windows-1255", Hebrew],
        ["windows-1256", Arabic], ["windows-1257", Baltic],
        ["windows-874", Thai], ["shift_jis", Japanese],
        ["euc-kr", Korean], ["gb2312", ChineseSimplified],
        ["big5", ChineseTraditional],
    ];

    [Theory]
    [MemberData(nameof(AllSamples))]
    public void SamplesAreIntact(string charset, string sample)
    {
        // Guards against the tool chain silently mangling the literals above. Without
        // this, an emptied or ASCII-flattened sample would turn every test below into a
        // trivial ASCII round trip that passes while proving nothing.
        //
        // Thresholds sit just under the measured minimums across all fifteen samples
        // (shortest 63 chars, fewest 12 non-ASCII - CJK samples are short because the
        // script is dense, and Lithuanian carries few diacritics per sentence). They are
        // deliberately loose: the job is to catch a sample that lost its script content,
        // not to police how much of it each language happens to use.
        Assert.False(string.IsNullOrWhiteSpace(sample));
        Assert.True(sample.Length >= 50, $"{charset}: sample is suspiciously short.");

        int nonAscii = sample.Count(c => c > 0x7F);
        Assert.True(
            nonAscii >= 10,
            $"{charset}: only {nonAscii} non-ASCII characters; the script content looks lost.");

        // And it must survive its own code page exactly, or a failure below would be a
        // fixture artifact rather than a product result.
        Encoding strict = Encoding.GetEncoding(
            charset, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

        byte[] bytes = strict.GetBytes(sample);
        Assert.Equal(sample, strict.GetString(bytes));
    }

    #endregion

    // ---- Single-byte code pages ----

    [Fact]
    public void Windows1251_Cyrillic_RoundTrips()
    {
        AssertRoundTripsLosslessly(Encoding.GetEncoding(1251), "cp1251_cyrillic", Russian);
    }

    [Fact]
    public void KOI8R_Cyrillic_RoundTrips()
    {
        AssertRoundTripsLosslessly(Encoding.GetEncoding("koi8-r"), "koi8r_cyrillic", Russian);
    }

    [Fact]
    public void Windows1252_WesternEuropean_RoundTrips()
    {
        AssertRoundTripsLosslessly(Encoding.GetEncoding(1252), "cp1252_western", Western);
    }

    [Fact]
    public void Iso88591_Latin1_RoundTrips()
    {
        AssertRoundTripsLosslessly(Encoding.GetEncoding("iso-8859-1"), "iso88591_latin1", Western);
    }

    [Fact]
    public void Windows1253_Greek_RoundTrips()
    {
        AssertRoundTripsLosslessly(Encoding.GetEncoding(1253), "cp1253_greek", Greek);
    }

    [Fact]
    public void Windows1254_Turkish_RoundTrips()
    {
        AssertRoundTripsLosslessly(Encoding.GetEncoding(1254), "cp1254_turkish", Turkish);
    }

    [Fact]
    public void Windows1250_CentralEuropean_RoundTrips()
    {
        AssertRoundTripsLosslessly(Encoding.GetEncoding(1250), "cp1250_central_european", CentralEuropean);
    }

    [Fact]
    public void Windows1255_Hebrew_RoundTrips()
    {
        AssertRoundTripsLosslessly(Encoding.GetEncoding(1255), "cp1255_hebrew", Hebrew);
    }

    [Fact]
    public void Windows1256_Arabic_RoundTrips()
    {
        AssertRoundTripsLosslessly(Encoding.GetEncoding(1256), "cp1256_arabic", Arabic);
    }

    [Fact]
    public void Windows1257_Baltic_RoundTrips()
    {
        AssertRoundTripsLosslessly(Encoding.GetEncoding(1257), "cp1257_baltic", Baltic);
    }

    [Fact]
    public void Windows874_Thai_RoundTrips()
    {
        AssertRoundTripsLosslessly(Encoding.GetEncoding(874), "cp874_thai", Thai);
    }

    // ---- Multi-byte code pages ----

    [Fact]
    public void ShiftJis_Japanese_RoundTrips()
    {
        AssertRoundTripsLosslessly(Encoding.GetEncoding("shift_jis"), "shiftjis_japanese", Japanese);
    }

    [Fact]
    public void EucKr_Korean_RoundTrips()
    {
        AssertRoundTripsLosslessly(Encoding.GetEncoding("euc-kr"), "euckr_korean", Korean);
    }

    [Fact]
    public void Gbk_ChineseSimplified_RoundTrips()
    {
        AssertRoundTripsLosslessly(Encoding.GetEncoding("gb2312"), "gbk_chinese_simplified", ChineseSimplified);
    }

    [Fact]
    public void Big5_ChineseTraditional_RoundTrips()
    {
        AssertRoundTripsLosslessly(Encoding.GetEncoding("big5"), "big5_chinese_traditional", ChineseTraditional);
    }
}
