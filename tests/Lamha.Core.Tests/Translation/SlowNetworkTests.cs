using Lamha.Core.Settings;
using Lamha.Core.Tests.Support;
using Lamha.Core.Translation;

namespace Lamha.Core.Tests.Translation;

/// <summary>
/// Lamha 2's test-writing.mjs on bad networks: one that answers nothing, one that fails at once, and blips. Time is the
/// fake clock's: Lamha's 8–12 s timeouts pass without real waiting.
/// </summary>
public class SlowNetworkTests
{
    private static readonly LamhaSettings Local = new() { UiLang = "ar", DictSource = DictSource.Local };
    private static readonly LamhaSettings Online = new() { UiLang = "ar", DictSource = DictSource.Online, TranslateDefinitions = false };

    /// <summary>Connected, but nothing ever answers (a Wi-Fi with no internet behind it).</summary>
    private static FakeNet Dead() => new((_, _, cancel) => FakeNet.Never(cancel));

    [Fact]
    public async Task ANetworkThatAnswersNothingTheDictionaryAnswersWithinSecondsAndTheNextLookupsDontWait()
    {
        var net = Dead();
        using var lamha = new TestLamha(net, Local, realDict: true);
        var (first, took) = await lamha.Timed(() => lamha.Lookups.LookupAsync("resilient", Context("Kids are very ", " after all.")));
        Assert.Equal(ResultSource.Local, first.From);
        Assert.True(took < TimeSpan.FromSeconds(4), $"the card waited {took} for the sentence");

        // The sentence's request gives up: Google counts as unreachable.
        await lamha.Timed(() => Task.Delay(TimeSpan.FromSeconds(13), lamha.Clock).ContinueWith(_ => true, TaskScheduler.Default));
        var sent = net.Calls.Count;
        var (second, took2) = await lamha.Timed(() => lamha.Lookups.LookupAsync("tenacious", Context("Kids are very ", " after all.")));
        Assert.Equal(ResultSource.Local, second.From);
        Assert.Equal("network", await lamha.LookupError("Kids are very resilient after all.")); // a sentence says there's no connection
        Assert.True(took2 < TimeSpan.FromMilliseconds(300), "at once");
        Assert.Equal(sent, net.Calls.Count); // nothing sent while Google is unreachable
    }

    [Fact]
    public async Task OnlineFirstOnANetworkThatAnswersNothingTheDictionaryAnswersAndThatAnswerIsntKept()
    {
        var up = false;
        var net = new FakeNet((c, _, cancel) => up
            ? Task.FromResult(Nets.IsGoogleSingle(c) ? Nets.GoogleWord("مرن") : FakeNet.Json(200, """[["الأطفال <a i=0>مرنون</a> جدًا.", "en"]]"""))
            : FakeNet.Never(cancel));
        using var lamha = new TestLamha(net, Online, realDict: true);
        var (r, took) = await lamha.Timed(() => lamha.Lookups.LookupAsync("resilient", Context("Kids are very ", " after all.")));
        Assert.Equal(ResultSource.Local, r.From);
        Assert.True(took < TimeSpan.FromSeconds(3.5), $"the card waited {took}");

        up = true; // back before the first request has given up: Google answers the same word now
        var again = await lamha.Run(() => lamha.Lookups.LookupAsync("resilient", Context("Kids are very ", " after all.")));
        Assert.Equal(ResultSource.Online, again.From); // the dictionary's stand-in answer wasn't cached
    }

    [Fact]
    public async Task ConnectedWithNoInternetBehindItNoWaitingOnRetries()
    {
        var net = new FakeNet(_ => throw FakeNet.Offline());
        using var lamha = new TestLamha(net, Online, realDict: true);
        var (r, took) = await lamha.Timed(() => lamha.Lookups.LookupAsync("resilient", Context("Kids are very ", " after all.")));
        Assert.Equal(ResultSource.Local, r.From);
        Assert.True(took < TimeSpan.FromMilliseconds(300), $"the card waited {took}");
        Assert.Equal(net.Calls.Count, net.Calls.Select(c => c.Url).Distinct().Count()); // no address asked twice
    }

    [Fact]
    public async Task JustAfterGoogleAnsweredAQuickFailureIsTriedAgain()
    {
        var failNext = 0;
        var net = new FakeNet(c =>
        {
            if (Interlocked.Decrement(ref failNext) >= 0)
            {
                throw FakeNet.Offline();
            }

            return Nets.IsGoogleSingle(c) ? Nets.GoogleWord("جملة") : FakeNet.Json(404, "{}");
        });
        using var lamha = new TestLamha(net);
        Assert.Equal("جملة", (await lamha.Lookup("One sentence here.")).Translation);
        failNext = 2; // both addresses fail once, a moment after Google answered
        Assert.Equal("جملة", (await lamha.Lookup("Another sentence here.")).Translation); // a blip is tried again
    }

    [Fact]
    public async Task OnlineFirstTheWordsSentenceIsTranslatedAlongsideTheLookupNotAfterIt()
    {
        var events = new List<string>();
        var net = new FakeNet(async (c, _, cancel) =>
        {
            var kind = Nets.IsGoogleSingle(c) ? "single" : Nets.IsGoogleBatch(c) ? "sentence" : "other";
            lock (events)
            {
                events.Add("start " + kind);
            }

            await Task.Delay(30, cancel);
            lock (events)
            {
                events.Add("end " + kind);
            }

            return kind == "sentence" ? FakeNet.Json(200, """[["الأطفال <a i=0>مرنون</a> جدًا.", "en"]]""") : Nets.GoogleWord("مرن");
        });
        using var lamha = new TestLamha(net, Online);
        var r = await lamha.Lookups.LookupAsync("resilient", Context("Kids are very ", " after all."), TestContext.Current.CancellationToken);
        Assert.Equal("مرنون", r.Context!.Word);
        Assert.True(events.IndexOf("start sentence") < events.IndexOf("end single"), string.Join(", ", events));
    }

    private static LookupOptions Context(string before, string after) => new() { Context = new Core.Dictionary.WordContext(before, after) };
}
