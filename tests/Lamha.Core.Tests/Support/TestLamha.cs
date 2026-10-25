using Lamha.Core.Abstractions;
using Lamha.Core.Ai;
using Lamha.Core.Diagnostics;
using Lamha.Core.Dictionary;
using Lamha.Core.Net;
using Lamha.Core.Review;
using Lamha.Core.Settings;
using Lamha.Core.Translation;
using Lamha.Core.Wikipedia;
using Lamha.Core.Writing;
using Lamha.Data.Database;
using Microsoft.Extensions.Time.Testing;

namespace Lamha.Core.Tests.Support;

/// <summary>Keys kept in memory.</summary>
public sealed class FakeSecrets : ISecretStore
{
    private readonly Dictionary<string, string> _keys = [];

    public string? Get(string name) => _keys.TryGetValue(name, out var v) ? v : null;

    public void Set(string name, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            _keys.Remove(name);
        }
        else
        {
            _keys[name] = value;
        }
    }
}

/// <summary>The app's side of the report.</summary>
public sealed class TestHost : IHostInfo
{
    public string Host => "Windows app (test)";

    public IReadOnlyList<string> Lines => ["Memory 30 MB"];
}

/// <summary>The network's state, as the test sets it (Lamha 2's <c>navigator.onLine</c>).</summary>
public sealed class FakeNetworkStatus : INetworkStatus
{
    public bool IsOffline { get; set; }
}

/// <summary>
/// Lamha's brain as it runs, with an in-memory database, a fake network and a fake clock: Lamha 2's test
/// <c>makeEnv</c>. The interface is Arabic unless the settings say otherwise.
/// </summary>
public sealed class TestLamha : IDisposable
{
    /// <summary>Saturday 24 October 2026, 09:00 UTC.</summary>
    public static readonly DateTimeOffset Start = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    private readonly HttpClient _client;

    public TestLamha(FakeNet net, LamhaSettings? settings = null, IReadOnlyDictionary<string, string>? keys = null, TimeZoneInfo? zone = null, bool realDict = false, ILanguagePacks? packs = null, DateTimeOffset? start = null)
    {
        Net = net;
        Clock = new FakeTimeProvider(start ?? Start);
        Clock.SetLocalTimeZone(zone ?? TimeZoneInfo.Utc);
        Db = LamhaDatabase.InMemory();
        State = new SqliteStateStore(Db);
        History = new SqliteHistoryStore(Db);
        Deck = new SqliteDeckStore(Db);
        Mistakes = new SqliteMistakeStore(Db);
        Activity = new SqliteActivityStore(Db);
        Errors = new SqliteErrorLogStore(Db);
        Settings = new SettingsService(State);
        Settings.Save(settings ?? new LamhaSettings { UiLang = "ar" });
        foreach (var (name, key) in keys ?? new Dictionary<string, string>())
        {
            Secrets.Set(name, key);
        }

        _client = new HttpClient(net);
        Http = new Http(_client, Clock);
        Localization = new Localization(Settings, () => "ar-SA");
        Ai = new AiService(Settings, Secrets, Http);
        ErrorLog = new ErrorLog(Errors, Clock);
        Writing = new WritingService(Ai, Settings, Mistakes, Localization, Clock, ErrorLog);
        Dictionary = realDict ? new LocalDictionary(RealDictionary.Data) : null;
        Packs = packs ?? new NoLanguagePacks();
        Google = new GoogleTranslate(Http);
        Translation = new TranslationService(Settings, Ai, Google, new AiTranslator(Ai), Network);
        HistoryService = new HistoryService(History, Clock);
        Today = new TodayService(Activity, State, Deck, History, Settings, Clock, Dictionary);
        Review = new ReviewService(Deck, State, History, Settings, Today, Clock);
        Wikipedia = new WikipediaService(Http, Network, "3.0.0-test");
        Lookups = new LookupService(Settings, Localization, Translation, new WordExplainer(Ai, Packs), Packs, HistoryService, Review, Today, Wikipedia, Network, Clock, Dictionary, ErrorLog);
        Backup = new BackupService(Review, Deck, History, Mistakes, Activity, State, Today, Clock, "3.0.0-test");
        Diagnostics = new DiagnosticsReport(Settings, Localization, Secrets, Deck, History, Mistakes, Packs, ErrorLog, new TestHost(), "3.0.0-test");
    }

    public FakeNet Net { get; }

    public FakeTimeProvider Clock { get; }

    public LamhaDatabase Db { get; }

    public SqliteStateStore State { get; }

    public SqliteHistoryStore History { get; }

    public SqliteDeckStore Deck { get; }

    public SqliteMistakeStore Mistakes { get; }

    public SqliteActivityStore Activity { get; }

    public SqliteErrorLogStore Errors { get; }

    public FakeSecrets Secrets { get; } = new();

    public SettingsService Settings { get; }

    public Http Http { get; }

    public Localization Localization { get; }

    public AiService Ai { get; }

    public WritingService Writing { get; }

    public FakeNetworkStatus Network { get; } = new();

    public LocalDictionary? Dictionary { get; }

    public ILanguagePacks Packs { get; }

    public GoogleTranslate Google { get; }

    public TranslationService Translation { get; }

    public HistoryService HistoryService { get; }

    public TodayService Today { get; }

    public ReviewService Review { get; }

    public WikipediaService Wikipedia { get; }

    public LookupService Lookups { get; }

    public ErrorLog ErrorLog { get; }

    public BackupService Backup { get; }

    public DiagnosticsReport Diagnostics { get; }

    /// <summary>Now, in milliseconds since 1970, on the fake clock.</summary>
    public long Now => Clock.GetUtcNow().ToUnixTimeMilliseconds();

    /// <summary>A lookup, with the clock running: Lamha 2's <c>send({ type: "lookup", text, context })</c>.</summary>
    public Task<LookupResult> Lookup(string text, string? before = null, string? after = null, bool forceAi = false) =>
        Run(() => Lookups.LookupAsync(text, new LookupOptions { Context = before is null ? null : new WordContext(before, after ?? ""), ForceAi = forceAi }));

    /// <summary>A lookup's error code (with the clock running), or null when it answers.</summary>
    public Task<string?> LookupError(string text) => ErrorOf(() => Lookups.LookupAsync(text));

    /// <summary>Changes the settings.</summary>
    public void Set(Func<LamhaSettings, LamhaSettings> change) => Settings.Update(change);

    /// <summary>
    /// Waits for <paramref name="work"/> while moving the clock on in 100 ms steps, so timeouts and retry pauses pass
    /// without real waiting. Returns the result and how much fake time it took.
    /// </summary>
    public async Task<(T Result, TimeSpan Took)> Timed<T>(Func<Task<T>> work, int maxSeconds = 600)
    {
        var started = Clock.GetUtcNow();
        var task = work();
        for (var steps = 0; !task.IsCompleted && steps < maxSeconds * 10; steps++)
        {
            await Task.Delay(1, TestContext.Current.CancellationToken);
            if (!task.IsCompleted)
            {
                Clock.Advance(TimeSpan.FromMilliseconds(100));
            }
        }

        var result = await task;
        return (result, Clock.GetUtcNow() - started);
    }

    /// <summary><see cref="Timed{T}"/> for the result alone.</summary>
    public async Task<T> Run<T>(Func<Task<T>> work) => (await Timed(work)).Result;

    /// <summary>The error code <paramref name="work"/> fails with (running the clock), or null when it doesn't.</summary>
    public async Task<string?> ErrorOf<T>(Func<Task<T>> work)
    {
        try
        {
            await Run(work);
            return null;
        }
        catch (LamhaException e)
        {
            return e.Code;
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        Db.Dispose();
    }
}
