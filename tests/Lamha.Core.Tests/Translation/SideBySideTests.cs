using System.Text.Json.Nodes;
using Lamha.Core.Settings;
using Lamha.Core.Tests.Support;
using Lamha.Core.Translation;

namespace Lamha.Core.Tests.Translation;

/// <summary>Side by side: a paragraph and its translation, a sentence beside its translation.</summary>
public class SideBySideTests
{
    private static readonly LamhaSettings Online = new() { UiLang = "ar", DictSource = DictSource.Online, TranslateDefinitions = false };

    /// <summary>Google's answer for a text: its sentences as given, each { orig, trans }.</summary>
    private static FakeNet Google(params (string Orig, string Trans)[] sentences) => new(c => Nets.IsGoogleSingle(c)
        ? FakeNet.Json(200, new JsonObject
        {
            ["src"] = "en",
            ["sentences"] = new JsonArray([.. sentences.Select(s => (JsonNode)new JsonObject { ["orig"] = s.Orig, ["trans"] = s.Trans })]),
        })
        : FakeNet.Json(404, "{}"));

    [Fact]
    public async Task GooglesSentencesArePairedRowByRowWithTheParagraphsKept()
    {
        var net = Google(
            ("The committee was reluctant. ", "كانت اللجنة مترددة. "),
            ("It wanted more data.\n", "أرادت بيانات إضافية.\n"),
            ("The plan waited.", "انتظرت الخطة."));
        using var lamha = new TestLamha(net, Online);
        var result = await lamha.Lookup("The committee was reluctant. It wanted more data.\nThe plan waited.");

        Assert.Equal(3, result.Sentences.Count);
        Assert.Equal(
            [
                new SideRow("The committee was reluctant.", "كانت اللجنة مترددة.", false),
                new SideRow("It wanted more data.", "أرادت بيانات إضافية.", true),
                new SideRow("The plan waited.", "انتظرت الخطة.", false),
            ],
            SideBySide.Rows(result));
    }

    [Fact]
    public async Task OneSentenceIsntSideBySide()
    {
        using var lamha = new TestLamha(Google(("The committee was reluctant to approve the plan.", "كانت اللجنة مترددة في الموافقة على الخطة.")), Online);
        var result = await lamha.Lookup("The committee was reluctant to approve the plan.");
        Assert.Single(result.Sentences);
        Assert.Empty(SideBySide.Rows(result));
    }

    [Fact]
    public void AWordOrAnExplanationIsntSideBySide()
    {
        var pairs = new SentencePair[] { new("One. ", "واحد. "), new("Two.", "اثنان.") };
        Assert.Empty(SideBySide.Rows(new LookupResult { Query = "bank", IsWord = true, Translation = "مصرف", Sentences = pairs }));
        Assert.Empty(SideBySide.Rows(new LookupResult { Query = "One. Two.", Explain = true, Translation = "x", Sentences = pairs }));
        Assert.Empty(SideBySide.Rows(new LookupResult { Query = "One. Two.", Translation = " ", Sentences = pairs }));
    }

    [Fact]
    public void ALineBreakGoogleSendsOnItsOwnEndsTheParagraphBeforeIt()
    {
        var result = new LookupResult
        {
            Query = "One.\n\nTwo.",
            Translation = "واحد.\n\nاثنان.",
            Sentences = [new("One.", "واحد."), new("\n\n", "\n\n"), new("Two.", "اثنان.")],
        };
        Assert.Equal([new SideRow("One.", "واحد.", true), new SideRow("Two.", "اثنان.", false)], SideBySide.Rows(result));
    }

    [Fact]
    public void TheAisTranslationIsPairedLineByLineWhenTheLinesMatch()
    {
        var lines = new LookupResult { Query = "First line.\nSecond line.", Translation = "السطر الأول.\nالسطر الثاني." };
        Assert.Equal([new SideRow("First line.", "السطر الأول.", true), new SideRow("Second line.", "السطر الثاني.", false)], SideBySide.Rows(lines));

        // Two sentences on one line: one row, the text beside its whole translation.
        var whole = new LookupResult { Query = "One sentence. Another one.", Translation = "جملة. وأخرى." };
        Assert.Equal([new SideRow("One sentence. Another one.", "جملة. وأخرى.", false)], SideBySide.Rows(whole));

        // Lines that don't match: whole.
        var uneven = new LookupResult { Query = "One.\nTwo.\nThree.", Translation = "واحد واثنان.\nثلاثة." };
        Assert.Single(SideBySide.Rows(uneven));

        // One sentence from the AI: as always.
        Assert.Empty(SideBySide.Rows(new LookupResult { Query = "Just one sentence here", Translation = "جملة واحدة فقط" }));
    }

    [Theory]
    [InlineData("One.", 1)]
    [InlineData("One. Two!", 2)]
    [InlineData("Is it? Yes… it is.", 3)]
    [InlineData("هل هو؟ نعم.", 2)]
    [InlineData("Line one\nLine two", 2)]
    [InlineData("3.14 is pi", 1)]
    [InlineData("", 0)]
    public void SentencesAreCounted(string text, int count) => Assert.Equal(count, SideBySide.SentenceCount(text));
}
