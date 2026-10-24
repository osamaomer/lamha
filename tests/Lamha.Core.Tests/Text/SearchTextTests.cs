using Lamha.Core.Text;

namespace Lamha.Core.Tests.Text;

/// <summary>Lamha 2's test-clipboard.mjs, sections "normalizeForSearch" and "detectLang".</summary>
public class SearchTextTests
{
    [Theory]
    [InlineData("مصرف", "مَصْرِف")]
    [InlineData("احمد", "أحمد")]
    [InlineData("احمد", "إحمد")]
    [InlineData("احمد", "آحمد")]
    [InlineData("مدرسه", "مدرسة")]
    [InlineData("مستشفي", "مستشفى")]
    [InlineData("كتاب", "كـتـاب")]
    [InlineData("2024", "٢٠٢٤")]
    [InlineData("inv", "Invoice_Q3")]
    public void AQueryFindsItsVariants(string query, string target) =>
        Assert.Contains(SearchText.Normalize(query), SearchText.Normalize(target), StringComparison.Ordinal);

    [Fact]
    public void HamzaCarriersMarksDigitsAndWhitespace()
    {
        Assert.Equal("مسوول", SearchText.Normalize("مسؤول"));
        Assert.Equal("قايمه", SearchText.Normalize("قائمة"));
        Assert.Equal("الله", SearchText.Normalize("ٱلله"));
        Assert.Equal("هذا", SearchText.Normalize("هٰذا"));
        Assert.Equal("1403", SearchText.Normalize("۱۴۰۳"));
        Assert.Equal("hello world", SearchText.Normalize("  Hello \n\t World  "));
        Assert.Equal("", SearchText.Normalize(null));
    }

    [Fact]
    public void TheDictionaryKeyKeepsArabicIndicDigits() => Assert.Equal("٢٠٢٤ كتاب", ArabicText.Normalize("٢٠٢٤ كِتاب"));

    [Fact]
    public void HighlightRangesMapBackToTheOriginalText()
    {
        Assert.Equal(["مَصْرِف"], Marked("حساب المَصْرِف الأهلي", "مصرف"));
        Assert.Equal(["كـتـاب"], Marked("كـتـاب جديد", "كتاب"));
        Assert.Equal(["أحمد", "مدرسة"], Marked("سيذهب أحمد إلى المدرسة", "احمد مدرسه"));
        Assert.Equal(["Inv", "٢٠٢٤"], Marked("Invoice_Q3 for ٢٠٢٤", "inv 2024"));
        Assert.Equal(["aaaa"], Marked("aaaa", "aa")); // overlapping matches merged
        Assert.Equal(["hello"], Marked("😀 hello", "hello")); // surrogate pairs
        Assert.Empty(SearchText.MatchRanges("anything", "  "));
    }

    [Theory]
    [InlineData("مرحبًا بكم في لمحة، هذا نص عربي خالص", "ar")]
    [InlineData("Please send me the invoice before Friday.", "en")]
    [InlineData("الاجتماع مع team يوم Monday الساعة", "mixed")]
    [InlineData("2024-09-26 😀 !!! ٣٤٥", "other")]
    [InlineData("x", "other")]
    [InlineData("سأرسل لك التقرير النهائي اليوم عبر البريد الإلكتروني إن شاء الله PDF", "ar")] // a few Latin letters stay "ar"
    [InlineData("The word مصرف means bank", "en")]
    public void TheLanguageOfACopy(string text, string language) => Assert.Equal(language, SearchText.DetectLanguage(text));

    private static string[] Marked(string text, string query) =>
        [.. SearchText.MatchRanges(text, query).Select(r => text[r.Start..r.End])];
}
