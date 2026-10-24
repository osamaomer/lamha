using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lamha.Core.Caching;
using Lamha.Core.Net;
using Lamha.Core.Translation;

namespace Lamha.Core.Wikipedia;

/// <summary>A Wikipedia article's summary for the card.</summary>
/// <param name="Lang">The article's language.</param>
/// <param name="Title">The article's title.</param>
/// <param name="Extract">Its first paragraph, as plain text.</param>
/// <param name="Url">The article (https on Wikipedia only), or "".</param>
/// <param name="Thumb">Its picture (https on Wikimedia only), or "".</param>
public sealed record WikiSummary(string Lang, string Title, string Extract, string Url, string Thumb);

/// <summary>
/// The online Wikipedia summary on the card: the article in the translation language when there is one, else in
/// English. Lamha 2's <c>wikiSummary</c> without the downloaded copies, which wait for a later plan. A request on its
/// way is shared: the lookup starts it, and the card's own request finds it.
/// </summary>
public sealed partial class WikipediaService(Http http, INetworkStatus network, string appVersion)
{
    /// <summary>
    /// The summary is an extra under the answer: each request may take this long, and after a failure Wikipedia rests,
    /// as Google does. A network that answers nothing held the card's last part for up to 3 × 8 s on every lookup.
    /// </summary>
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan Rest = TimeSpan.FromSeconds(20);

    private readonly Lru<string, WikiSummary?> _cache = new(200);
    private readonly Dictionary<string, Task<WikiSummary?>> _pending = [];
    private readonly Lock _gate = new();
    private long _downUntil;

    /// <summary>
    /// Wikimedia asks every program to say who it is, and limits the ones that don't: Lamha 2's Windows app said only
    /// "node", and was told "too many requests" after a few lookups.
    /// </summary>
    public string UserAgent => $"Lamha/{appVersion} (https://github.com/osamaomer/lamha)";

    /// <summary>
    /// The summary for <paramref name="title"/>, preferring <paramref name="lang"/>, or null. <paramref name="offline"/>
    /// (the dictionary on "Local only") never goes online.
    /// </summary>
    public Task<WikiSummary?> SummaryAsync(string title, string lang = "ar", bool offline = false)
    {
        if (!Language().IsMatch(lang))
        {
            lang = "en"; // it becomes part of a hostname
        }

        if (offline || network.IsOffline)
        {
            return Task.FromResult<WikiSummary?>(null);
        }

        var key = title.ToLowerInvariant() + "|" + lang;
        lock (_gate)
        {
            if (_cache.TryGet(key, out var hit))
            {
                return Task.FromResult(hit);
            }

            if (_pending.TryGetValue(key, out var pending))
            {
                return pending;
            }

            var task = FindAsync(title, lang, key);
            _pending[key] = task;
            return task;
        }
    }

    /// <summary>
    /// Starts the card's Wikipedia part as soon as the lookup has its word: the card asks only once the whole lookup is
    /// done (the sentence's translation may take a while), and then finds the request already on its way.
    /// </summary>
    public void Prefetch(LookupResult result, bool showWikipedia, string targetLang, bool offline)
    {
        if (!showWikipedia || !result.IsWord || result.Source != "en" || result.Query.Length <= 2)
        {
            return;
        }

        _ = SummaryAsync(result.Query, result.Target == "en" ? "en" : targetLang, offline);
    }

    private async Task<WikiSummary?> FindAsync(string title, string lang, string key)
    {
        // Never finish before the caller has noted this request as pending.
        await Task.Yield();
        WikiSummary? result = null;
        var failed = false;
        try
        {
            if (Now < Interlocked.Read(ref _downUntil))
            {
                throw new HttpFailure(HttpFailureKind.Network); // it failed a moment ago: don't wait for it again
            }

            var query = Http.Query(
            [
                new("action", "query"), new("titles", title), new("prop", "langlinks"), new("lllang", lang),
                new("format", "json"), new("redirects", "1"), new("origin", "*"),
            ]);
            var links = await AskAsync($"https://en.wikipedia.org/w/api.php?{query}").ConfigureAwait(false);
            var page = (links?["query"]?["pages"] as JsonObject)?.FirstOrDefault().Value;
            if (page is JsonObject p && !p.ContainsKey("missing"))
            {
                var translated = Str(p["langlinks"]?[0]?["*"]);
                if (translated.Length > 0 && lang != "en")
                {
                    result = await ArticleAsync(lang, translated).ConfigureAwait(false);
                }

                result ??= await ArticleAsync("en", Str(p["title"])).ConfigureAwait(false);
            }
        }
        catch (HttpFailure failure)
        {
            result = null;
            failed = true;
            if (failure.Kind is HttpFailureKind.Network or HttpFailureKind.Timeout || failure.Status == 429)
            {
                var until = Now + (long)Rest.TotalMilliseconds;
                lock (_gate)
                {
                    _downUntil = Math.Max(_downUntil, until);
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                _pending.Remove(key);
            }
        }

        if (!failed)
        {
            _cache.Set(key, result);
        }

        return result;
    }

    /// <summary>An article's summary, or null when there's none; the network's failures go to the caller.</summary>
    private async Task<WikiSummary?> ArticleAsync(string lang, string title)
    {
        try
        {
            var s = await AskAsync($"https://{lang}.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(title.Replace(' ', '_'))}").ConfigureAwait(false);
            if (Str(s?["type"]) != "standard" || Str(s?["extract"]).Length == 0)
            {
                return null;
            }

            return new WikiSummary(lang, Str(s?["title"]), Str(s?["extract"]), HttpsOnly(Str(s?["content_urls"]?["desktop"]?["page"])), HttpsOnly(Str(s?["thumbnail"]?["source"])));
        }
        catch (HttpFailure failure) when (failure.Kind is HttpFailureKind.Status or HttpFailureKind.Invalid && failure.Status != 429)
        {
            return null; // this article, not the network
        }
    }

    private Task<JsonNode?> AskAsync(string url) =>
        http.GetJsonAsync(() => Http.Get(url, ("Api-User-Agent", UserAgent), ("User-Agent", UserAgent)), retries: 1, Wait);

    private long Now => http.Time.GetUtcNow().ToUnixTimeMilliseconds();

    private static string HttpsOnly(string url) => WikiUrl().IsMatch(url) ? url : "";

    private static string Str(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    [GeneratedRegex("^[a-z]{2,3}(-[a-z]{2,8})?$")]
    private static partial Regex Language();

    [GeneratedRegex(@"^https://[a-z0-9.-]+\.(wikipedia|wikimedia)\.org/", RegexOptions.IgnoreCase)]
    private static partial Regex WikiUrl();
}
