using System.Text.Json.Nodes;
using Lamha.Core.Caching;
using Lamha.Core.Net;
using Lamha.Core.Text;

namespace Lamha.Core.Translation;

/// <summary>
/// Google Translate's unofficial endpoints (Lamha 2's <c>googleJSON</c>, <c>googleBatch</c>, <c>onlineLookup</c>). Two
/// addresses are tried in order; one that's rate-limited goes on a growing cooldown so the user never notices. A
/// network that answers nothing pauses Google for a while, so the dictionary, a pack or a local AI answers at once.
/// </summary>
public sealed class GoogleTranslate(Http http)
{
    /// <summary>Google takes about 5,000 characters per request (Lamha 2's <c>LOOKUP_CHUNK</c>).</summary>
    public const int Chunk = 4500;

    /// <summary>
    /// A network that is connected but answers nothing (a hotel Wi-Fi, a captive portal): each request would wait for its
    /// timeout. Once Google can't be reached, the next requests fail at once for this long; the first success ends it.
    /// </summary>
    private static readonly TimeSpan NetPause = TimeSpan.FromSeconds(20);

    /// <summary>
    /// A quick failure this soon after Google answered is a blip, worth one more try. After longer, it's a network that's
    /// down, and retrying each address only held the dictionary's answer back.
    /// </summary>
    private static readonly TimeSpan BlipWindow = TimeSpan.FromSeconds(60);

    private readonly Endpoint[] _endpoints =
    [
        new("https://translate.googleapis.com", "gtx"),
        new("https://clients5.google.com", "dict-chrome-ex"),
    ];

    private readonly Lru<string, string> _batchCache = new(4000);
    private readonly Lock _gate = new();
    private long _downUntil;
    private long _okAt;

    private long Now => http.Time.GetUtcNow().ToUnixTimeMilliseconds();

    /// <summary>Google failed to answer a moment ago: don't wait for it again yet.</summary>
    public bool IsDown
    {
        get
        {
            lock (_gate)
            {
                return Now < _downUntil;
            }
        }
    }

    /// <summary>
    /// Many strings translated in one request each ~5,000 characters / 100 strings, in the same order; answers are
    /// cached. A malformed answer for one string gives the string back, uncached. <paramref name="html"/>: the strings
    /// carry markup, which comes back as markup.
    /// </summary>
    public async Task<IReadOnlyList<string>> TranslateAsync(IReadOnlyList<string> texts, string target, string source = "auto", bool html = false, CancellationToken cancel = default)
    {
        var format = html ? "html" : "text";
        string CacheKey(string t) => $"{format}|{source}|{target}|{t}";
        var result = new string[texts.Count];
        var todo = new List<int>();
        for (var i = 0; i < texts.Count; i++)
        {
            if (_batchCache.TryGet(CacheKey(texts[i]), out var hit))
            {
                result[i] = hit;
            }
            else
            {
                todo.Add(i);
            }
        }

        var chunks = Chunks.ByLength(todo, i => texts[i].Length, 5000, 100);
        await Task.WhenAll(chunks.Select(async indexes =>
        {
            var form = indexes.Select(i => KeyValuePair.Create("q", texts[i])).ToList();
            var data = await RequestAsync("t", [new("sl", source), new("tl", target), new("format", format)], form, cancel).ConfigureAwait(false);
            // One string may come back un-nested: ["text","en"] or "text".
            var items = data as JsonArray;
            if (indexes.Count == 1 && (data is JsonValue || items is [JsonValue, ..]))
            {
                items = [data is JsonArray a ? a[0]?.DeepClone() : data?.DeepClone()];
            }

            for (var k = 0; k < indexes.Count; k++)
            {
                var item = items is not null && k < items.Count ? items[k] : null;
                if (item is JsonArray nested)
                {
                    item = nested.FirstOrDefault();
                }

                var i = indexes[k];
                if (item is not JsonValue v || !v.TryGetValue<string>(out var translated))
                {
                    result[i] = texts[i]; // malformed: show the original, don't cache
                    continue;
                }

                result[i] = html ? translated : Html.DecodeEntities(translated); // markup is the caller's to parse
                _batchCache.Set(CacheKey(texts[i]), result[i]);
            }
        })).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Google's answer for a word (meanings, definitions, examples) or a text (its translation). Text too long for one
    /// request: the first piece tells the language, the rest go in a batch. Lamha 2's <c>onlineLookup</c> /
    /// <c>longLookup</c>. Definitions are translated by <paramref name="translateGlosses"/> when it's given.
    /// </summary>
    public async Task<LookupResult> LookupAsync(string text, string source, string target, bool word, string interfaceLanguage, Func<IReadOnlyList<string>, string, Task<IReadOnlyList<string>>>? translateGlosses = null, CancellationToken cancel = default)
    {
        if (!word && text.Length > Chunk)
        {
            var pieces = Pieces.SplitLong(text, Chunk);
            var first = await LookupAsync(pieces[0].Text, source, target, false, interfaceLanguage, null, cancel).ConfigureAwait(false);
            var rest = await TranslateAsync([.. pieces.Skip(1).Select(p => p.Text)], target, first.Source.Length > 0 ? first.Source : source, false, cancel).ConfigureAwait(false);
            var joined = string.Concat(new[] { first.Translation }.Concat(rest).Select((t, i) => t + pieces[i].Separator));
            // The first piece by its sentences, the rest a piece at a time (the batch doesn't split them).
            var pairs = first.Sentences.Concat(rest.Select((t, i) => new SentencePair(pieces[i + 1].Text + pieces[i + 1].Separator, t + pieces[i + 1].Separator)));
            return first with { Query = text, Translation = joined, Translit = "", SourceTranslit = "", Sentences = [.. pairs] };
        }

        // Labels (noun, verb…) come in the interface language.
        List<KeyValuePair<string, string>> query = [new("sl", source), new("tl", target), new("hl", interfaceLanguage), new("dj", "1"), new("ie", "UTF-8"), new("oe", "UTF-8")];
        foreach (var dt in word ? ["t", "bd", "md", "ss", "ex", "rm", "qca"] : new[] { "t", "rm" })
        {
            query.Add(new("dt", dt));
        }

        JsonNode? data;
        if (text.Length > 1500)
        {
            data = await RequestAsync("single", query, [new("q", text)], cancel).ConfigureAwait(false);
        }
        else
        {
            query.Add(new("q", text));
            data = await RequestAsync("single", query, null, cancel).ConfigureAwait(false);
        }

        var sentences = data?["sentences"] as JsonArray ?? [];
        var result = new LookupResult
        {
            Query = text,
            IsWord = word,
            Source = Str(data?["src"]) is { Length: > 0 } src ? src : source,
            Target = target,
            Translation = string.Concat(sentences.Select(s => s?["trans"] is JsonValue t && t.TryGetValue<string>(out var tr) ? tr : "")),
            Translit = sentences.Select(s => Str(s?["translit"])).FirstOrDefault(t => t.Length > 0) ?? "",
            SourceTranslit = sentences.Select(s => Str(s?["src_translit"])).FirstOrDefault(t => t.Length > 0) ?? "",
            Spell = Html.StripTags(Str(data?["spell"]?["spell_res"])),
            From = ResultSource.Online,
            Sentences = word ? [] : [.. sentences.Where(s => s?["orig"] is not null).Select(s => new SentencePair(Str(s?["orig"]), Str(s?["trans"])))],
        };
        if (!word)
        {
            return result;
        }

        var dict = (data?["dict"] as JsonArray ?? [])
            .Select(d => new DictGroup(
                PartOfSpeech.FromGoogle(Str(d?["pos"])),
                [.. (d?["entry"] as JsonArray ?? []).Select(e => new DictTerm
                {
                    Word = Str(e?["word"]),
                    Back = [.. (e?["reverse_translation"] as JsonArray ?? []).Take(4).Select(Str)],
                    Score = e?["score"] is JsonValue sc && sc.TryGetValue<double>(out var score) ? score : 0,
                })]))
            .Where(d => d.Terms.Count > 0)
            .ToList();

        var synonyms = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var set in data?["synsets"] as JsonArray ?? [])
        {
            foreach (var e in set?["entry"] as JsonArray ?? [])
            {
                if (Str(e?["definition_id"]) is { Length: > 0 } id)
                {
                    synonyms[id] = [.. (e?["synonym"] as JsonArray ?? []).Take(6).Select(Str)];
                }
            }
        }

        var definitions = (data?["definitions"] as JsonArray ?? [])
            .Select(d => new DefinitionGroup(
                PartOfSpeech.FromGoogle(Str(d?["pos"])),
                [.. (d?["entry"] as JsonArray ?? []).Take(5).Select(e => new Definition
                {
                    Gloss = Str(e?["gloss"]),
                    Example = Html.StripTags(Str(e?["example"])),
                    Synonyms = synonyms.TryGetValue(Str(e?["definition_id"]), out var syn) ? syn : [],
                })],
                Str(d?["base_form"])))
            .Where(d => d.Entries.Count > 0)
            .ToList();

        var examples = (data?["examples"]?["example"] as JsonArray ?? []).Take(4).Select(e => Html.StripTags(Html.DecodeEntities(Str(e?["text"])))).ToList();

        // Arabic-first: the English definitions translated too (a French word has French definitions).
        if (translateGlosses is not null && definitions.Count > 0)
        {
            var glosses = definitions.SelectMany(d => d.Entries).Take(14).Select(e => e.Gloss).ToList();
            try
            {
                var translated = await translateGlosses(glosses, result.Source is { Length: > 0 } s && s != "auto" ? s : "en").ConfigureAwait(false);
                var k = 0;
                definitions = [.. definitions.Select(d => d with
                {
                    Entries = [.. d.Entries.Select(e => k < translated.Count ? e with { GlossTranslated = translated[k++] } : e)],
                })];
            }
            catch (LamhaException)
            {
                // The definitions are still useful in English.
            }
            catch (HttpFailure)
            {
                // The definitions are still useful in English.
            }
        }

        return result with { Dict = dict, Definitions = definitions, Examples = examples };
    }

    /// <summary>
    /// The addresses of Google's voice reading <paramref name="text"/> in <paramref name="lang"/>, piece by piece (at
    /// most 3,000 characters). Throws "network" while Google can't be reached, so the app uses Windows' own voice.
    /// </summary>
    public IReadOnlyList<string> SpeechUrls(string text, string lang)
    {
        if (IsDown)
        {
            throw new LamhaException(ErrorCodes.Network);
        }

        var parts = Pieces.ForSpeech(text.Length > 3000 ? text[..3000] : text);
        var endpoint = Order()[0];
        return [.. parts.Select((p, i) => $"{endpoint.Base}/translate_tts?" + Http.Query(
        [
            new("ie", "UTF-8"), new("client", endpoint.Client), new("tl", lang), new("total", parts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("idx", i.ToString(System.Globalization.CultureInfo.InvariantCulture)), new("textlen", p.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)), new("q", p),
        ]))];
    }

    /// <summary>
    /// One piece of Google's voice (an address from <see cref="SpeechUrls"/>): its MP3. Throws <see cref="HttpFailure"/>
    /// when it can't be had within 8 s, as a word lookup waits, so Windows' voice can say it instead.
    /// </summary>
    public async Task<byte[]> SpeechPieceAsync(string url, CancellationToken cancel = default)
    {
        using var response = await http.SendAsync(Http.Get(url), TimeSpan.FromSeconds(8), cancel).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpFailure(HttpFailureKind.Status, (int)response.StatusCode);
        }

        return await response.Content.ReadAsByteArrayAsync(cancel).ConfigureAwait(false);
    }

    /// <summary>A GET (or a POST when <paramref name="form"/> is given) to a translate_a endpoint, failing over between addresses.</summary>
    private async Task<JsonNode?> RequestAsync(string path, List<KeyValuePair<string, string>> query, List<KeyValuePair<string, string>>? form, CancellationToken cancel)
    {
        int retries;
        lock (_gate)
        {
            if (Now < _downUntil)
            {
                throw new LamhaException(ErrorCodes.Network);
            }

            retries = Now - _okAt < (long)BlipWindow.TotalMilliseconds ? 1 : 0;
        }

        HttpFailure? last = null;
        foreach (var endpoint in Order())
        {
            var url = $"{endpoint.Base}/translate_a/{path}?client={endpoint.Client}&{Http.Query(query)}";
            try
            {
                // A word or a sentence: someone is waiting for the card.
                var data = await http.GetJsonAsync(
                    () => form is null ? Http.Get(url) : Http.PostForm(url, form),
                    retries,
                    TimeSpan.FromSeconds(path == "single" ? 8 : 12),
                    cancel).ConfigureAwait(false);
                lock (_gate)
                {
                    endpoint.Strikes = 0;
                    endpoint.Until = 0;
                    _downUntil = 0;
                    _okAt = Now;
                }

                return data;
            }
            catch (HttpFailure failure)
            {
                last = failure;
                if (failure.Status is 429 or 503)
                {
                    lock (_gate)
                    {
                        endpoint.Strikes = Math.Min(endpoint.Strikes + 1, 6);
                        endpoint.Until = Now + Math.Min(30 * 60_000L, 60_000L << (endpoint.Strikes - 1)); // 1, 2, 4 … 30 min
                    }
                }

                if (failure.Kind == HttpFailureKind.Timeout)
                {
                    break; // the network, not this address: the other would wait as long
                }
            }
        }

        if (last is { Status: 429 })
        {
            throw new LamhaException(ErrorCodes.RateLimited);
        }

        if (last is null || last.Kind is HttpFailureKind.Timeout or HttpFailureKind.Network)
        {
            lock (_gate)
            {
                _downUntil = Now + (long)NetPause.TotalMilliseconds;
            }

            throw new LamhaException(ErrorCodes.Network);
        }

        throw new LamhaException(ErrorCodes.GoogleError, last.Message);
    }

    /// <summary>The addresses: available ones first, then the one whose cooldown ends soonest.</summary>
    private Endpoint[] Order()
    {
        lock (_gate)
        {
            var now = Now;
            return [.. _endpoints.OrderBy(e => e.Until > now).ThenBy(e => e.Until > now ? e.Until : 0)];
        }
    }

    private static string Str(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    private sealed class Endpoint(string @base, string client)
    {
        public string Base { get; } = @base;

        public string Client { get; } = client;

        public long Until { get; set; }

        public int Strikes { get; set; }
    }
}

/// <summary>Groups of items small enough for one request.</summary>
public static class Chunks
{
    /// <summary>
    /// The indexes in groups of at most <paramref name="maxChars"/> characters and <paramref name="maxItems"/> items (an
    /// item longer than the limit goes alone). Lamha 2's <c>chunkIndexes</c>.
    /// </summary>
    public static List<List<int>> ByLength(IEnumerable<int> indexes, Func<int, int> lengthOf, int maxChars, int maxItems)
    {
        var chunks = new List<List<int>>();
        var current = new List<int>();
        var length = 0;
        foreach (var i in indexes)
        {
            if (current.Count > 0 && (length + lengthOf(i) > maxChars || current.Count >= maxItems))
            {
                chunks.Add(current);
                current = [];
                length = 0;
            }

            current.Add(i);
            length += lengthOf(i);
        }

        if (current.Count > 0)
        {
            chunks.Add(current);
        }

        return chunks;
    }
}
