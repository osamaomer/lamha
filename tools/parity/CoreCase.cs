using System.Text.Json.Nodes;
using Lamha.Core;
using Lamha.Core.Abstractions;
using Lamha.Core.Ai;
using Lamha.Core.Dictionary;
using Lamha.Core.Net;
using Lamha.Core.Review;
using Lamha.Core.Settings;
using Lamha.Core.Translation;
using Lamha.Core.Wikipedia;
using Lamha.Data.Database;
using Microsoft.Extensions.Time.Testing;

namespace Lamha.Tools.Parity;

/// <summary>Keys kept in memory.</summary>
internal sealed class MemorySecrets : ISecretStore
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

/// <summary>Windows' network status, as the case sets it (Lamha 2's <c>navigator.onLine</c>).</summary>
internal sealed class CaseNetworkStatus(bool offline) : INetworkStatus
{
    public bool IsOffline => offline;
}

/// <summary>
/// One case through Lamha 3's Core, composed as tests/Lamha.Core.Tests' TestLamha composes it (a fresh in-memory
/// database, the real dictionary, a fake clock) with every request answered by parity.mjs.
/// </summary>
internal static class CoreCase
{
    /// <summary>Saturday 24 October 2026, 09:00 UTC (TestLamha's start).</summary>
    private static readonly DateTimeOffset Start = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    public static async Task<JsonObject> RunAsync(Bridge bridge, LocalDictionary dictionary, JsonObject kase)
    {
        var id = (string)kase["id"]!;
        var s = (JsonObject)kase["settings"]!;
        var settingsNow = Settings(s);
        using var net = new BridgeNet(bridge, id);
        using var client = new HttpClient(net);
        var clock = new FakeTimeProvider(Start);
        clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        using var db = LamhaDatabase.InMemory();
        var state = new SqliteStateStore(db);
        var historyStore = new SqliteHistoryStore(db);
        var deck = new SqliteDeckStore(db);
        var activity = new SqliteActivityStore(db);
        var settings = new SettingsService(state);
        settings.Save(settingsNow);
        var secrets = new MemorySecrets();
        if (settingsNow.AiProvider == AiProviderKind.Gemini)
        {
            secrets.Set("gemini", "AIza-test");
        }

        var network = new CaseNetworkStatus((bool?)kase["offline"] ?? false);
        var http = new Http(client, clock);
        var localization = new Localization(settings, () => "ar-SA");
        var ai = new AiService(settings, secrets, http);
        var packs = new NoLanguagePacks();
        var translation = new TranslationService(settings, ai, new GoogleTranslate(http), new AiTranslator(ai), network);
        var history = new HistoryService(historyStore, clock);
        var today = new TodayService(activity, state, deck, historyStore, settings, clock, dictionary);
        var review = new ReviewService(deck, state, historyStore, settings, today, clock);
        var wikipedia = new WikipediaService(http, network, "3.0.0-parity");
        var lookups = new LookupService(settings, localization, translation, new WordExplainer(ai, packs), packs, history, review, today, wikipedia, network, clock, dictionary);

        var context = kase["context"] is JsonObject c ? new WordContext((string?)c["before"] ?? "", (string?)c["after"] ?? "") : null;
        var options = new LookupOptions { Context = context, ForceAi = (bool?)kase["forceAi"] ?? false };
        var answer = new JsonObject { ["op"] = "result", ["id"] = id };
        try
        {
            var task = lookups.LookupAsync((string)kase["text"]!, options);
            // The fake clock moves only while no answer is on its way from parity.mjs, so timeouts and waits pass without
            // real waiting while the network's own answers take no time, as Lamha 2's do.
            for (var steps = 0; !task.IsCompleted && steps < 6000; steps++)
            {
                await Task.WhenAny(task, Task.Delay(2)).ConfigureAwait(false);
                if (!task.IsCompleted && net.Pending == 0)
                {
                    clock.Advance(TimeSpan.FromMilliseconds(100));
                }
            }

            answer["data"] = ResultJson.Of(await task.ConfigureAwait(false));
        }
        catch (LamhaException e)
        {
            answer["error"] = e.Code;
        }
#pragma warning disable CA1031 // a crash is a difference to report, not a reason to stop the run
        catch (Exception e)
#pragma warning restore CA1031
        {
            answer["crash"] = e.GetType().Name + ": " + e.Message;
        }

        answer["history"] = new JsonArray([.. history.Recent(5).Select(h => (JsonNode)new JsonObject { ["q"] = h.Word, ["tr"] = h.Meaning, ["src"] = h.Language })]);
        return answer;
    }

    /// <summary>The case's settings, named as in Lamha 2 (parity.mjs gives Lamha 2 the same ones).</summary>
    private static LamhaSettings Settings(JsonObject s) => new()
    {
        UiLang = (string?)s["uiLang"] ?? "ar",
        TargetLang = (string?)s["targetLang"] ?? "ar",
        DictSource = (string?)s["dictSource"] switch
        {
            "offline" => DictSource.Offline,
            "online" => DictSource.Online,
            _ => DictSource.Local,
        },
        UseContext = (bool?)s["useContext"] ?? true,
        TranslateDefinitions = (bool?)s["translateDefinitions"] ?? true,
        EnDict = (bool?)s["enDict"] ?? false,
        ExplainLangs = [.. (s["explainLangs"] as JsonArray ?? []).Select(l => (string)l!)],
        ShowWikipedia = (bool?)s["showWikipedia"] ?? false,
        SaveHistory = (bool?)s["saveHistory"] ?? true,
        TrService = (string?)s["trService"] switch
        {
            "google" => TranslateWith.Google,
            "ai" => TranslateWith.Ai,
            _ => TranslateWith.Auto,
        },
        AiProvider = (string?)s["aiProvider"] switch
        {
            "gemini" => AiProviderKind.Gemini,
            "ollama" => AiProviderKind.Ollama,
            _ => AiProviderKind.Claude,
        },
        OllamaModel = (string?)s["ollamaModel"] ?? "",
    };
}
