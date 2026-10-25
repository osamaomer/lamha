using Lamha.Core.Settings;
using Lamha.Core.Tests.Support;
using Lamha.Core.Translation;

namespace Lamha.Core.Tests.Wikipedia;

/// <summary>Lamha 2's test-writing.mjs: the Wikipedia summary on the card (the online part; downloaded copies wait).</summary>
public class WikipediaTests
{
    private static HttpResponseMessage Wiki(Call c) =>
        c.Url.Contains("en.wikipedia.org/w/api.php", StringComparison.Ordinal)
            ? FakeNet.Json(200, """{ "query": { "pages": { "1": { "title": "Paris", "langlinks": [ { "*": "باريس" } ] } } } }""")
            : c.Url.Contains("ar.wikipedia.org/api/rest_v1/page/summary/", StringComparison.Ordinal)
                ? FakeNet.Json(200, """{ "type": "standard", "title": "باريس", "extract": "عاصمة فرنسا.", "content_urls": { "desktop": { "page": "https://ar.wikipedia.org/wiki/x" } }, "thumbnail": { "source": "javascript:alert(1)" } }""")
                : FakeNet.Json(404, "{}");

    [Fact]
    public async Task WikipediaStartsWithTheLookupSaysItsLamhaAndTheCardsRequestFindsItOnItsWay()
    {
        var events = new List<string>();
        var userAgents = new List<string>();
        var net = new FakeNet(async (c, _, cancel) =>
        {
            if (c.Url.Contains("wikipedia.org", StringComparison.Ordinal))
            {
                lock (events)
                {
                    events.Add("wiki");
                    userAgents.Add(c.Header("api-user-agent"));
                }

                return Wiki(c);
            }

            if (Nets.IsGoogleBatch(c))
            {
                lock (events)
                {
                    events.Add("sentence asked");
                }

                await Task.Delay(200, cancel); // the word's sentence: slow, as on a busy network
                lock (events)
                {
                    events.Add("sentence translated");
                }

                return FakeNet.Json(200, """[["أعيش في <a i=0>باريس</a> الآن", "en"]]""");
            }

            return Nets.GoogleWord("باريس");
        });
        using var lamha = new TestLamha(net, new LamhaSettings { UiLang = "ar", DictSource = DictSource.Online, TranslateDefinitions = false });
        await lamha.Lookups.LookupAsync("Paris", new LookupOptions { Context = new Core.Dictionary.WordContext("I live in ", " now") }, TestContext.Current.CancellationToken);
        Assert.True(events.IndexOf("wiki") >= 0 && events.IndexOf("wiki") < events.IndexOf("sentence translated"), string.Join(", ", events));
        Assert.Matches(@"^Lamha/\S* \(https://github\.com/osamaomer/lamha\)$", userAgents[0]); // Wikimedia limits programs that don't say who they are
        var asked = events.Count(e => e == "wiki");
        var w = await lamha.Wikipedia.SummaryAsync("Paris", "ar"); // what the card asks once the answer is shown
        Assert.Equal("عاصمة فرنسا.", w!.Extract);
        Assert.Equal("", w.Thumb); // https on Wikimedia only
        Assert.Equal(asked, events.Count(e => e == "wiki")); // no second request
    }

    [Fact]
    public async Task ANetworkThatAnswersNothingWikipediaGivesUpAfter4SecondsThenRests()
    {
        var net = new FakeNet((_, _, cancel) => FakeNet.Never(cancel));
        using var lamha = new TestLamha(net);
        var (first, took) = await lamha.Timed(() => lamha.Wikipedia.SummaryAsync("Paris", "ar"));
        Assert.Null(first);
        Assert.True(took < TimeSpan.FromSeconds(5), $"it waited {took}");
        var sent = net.Calls.Count;
        var (second, took2) = await lamha.Timed(() => lamha.Wikipedia.SummaryAsync("London", "ar"));
        Assert.Null(second);
        Assert.Equal(sent, net.Calls.Count); // not asked again during the pause
        Assert.True(took2 < TimeSpan.FromMilliseconds(100), "at once");

        lamha.Clock.Advance(TimeSpan.FromSeconds(21)); // the pause is over
        await lamha.Timed(() => lamha.Wikipedia.SummaryAsync("Rome", "ar"));
        Assert.True(net.Calls.Count > sent);
    }

    [Fact]
    public async Task OfflineOnlyNeverAsksWikipediaAndABadLanguageBecomesEnglish()
    {
        var net = new FakeNet(Wiki);
        using var lamha = new TestLamha(net);
        Assert.Null(await lamha.Wikipedia.SummaryAsync("Paris", "ar", offline: true));
        Assert.Empty(net.Calls);
        await lamha.Wikipedia.SummaryAsync("Paris", "evil.example.com/");
        Assert.Contains("lllang=en", net.Calls[0].Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWordWithoutAnArticleInTheLanguageGetsTheEnglishOne()
    {
        var net = new FakeNet(c => c.Url.Contains("/w/api.php", StringComparison.Ordinal)
            ? FakeNet.Json(200, """{ "query": { "pages": { "7": { "title": "Photosynthesis" } } } }""")
            : c.Url.Contains("en.wikipedia.org/api/rest_v1/page/summary/Photosynthesis", StringComparison.Ordinal)
                ? FakeNet.Json(200, """{ "type": "standard", "title": "Photosynthesis", "extract": "How plants make food." }""")
                : FakeNet.Json(404, "{}"));
        using var lamha = new TestLamha(net);
        var r = await lamha.Wikipedia.SummaryAsync("Photosynthesis", "ar");
        Assert.Equal(("en", "How plants make food."), (r!.Lang, r.Extract));
    }
}
