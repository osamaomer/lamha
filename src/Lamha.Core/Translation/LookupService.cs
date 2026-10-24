using System.Text.RegularExpressions;
using Lamha.Core.Caching;
using Lamha.Core.Diagnostics;
using Lamha.Core.Dictionary;
using Lamha.Core.Review;
using Lamha.Core.Settings;
using Lamha.Core.Text;
using Lamha.Core.Wikipedia;

namespace Lamha.Core.Translation;

/// <summary>What the card asks for besides the text.</summary>
public sealed record LookupOptions
{
    /// <summary>The translation language for this lookup; "" is the setting's.</summary>
    public string Target { get; init; } = "";

    /// <summary>The sentence around a selected word, so the right meaning is chosen.</summary>
    public WordContext? Context { get; init; }

    /// <summary>The card's "Better translation": the AI, whatever the settings say.</summary>
    public bool ForceAi { get; init; }
}

/// <summary>
/// A lookup: the offline dictionary, Google, the AI, a language pack, picked by the settings with the others as
/// fallbacks, and the word's meaning in its sentence. Then the history, the review card and today's counts. Lamha 2's
/// <c>lookup()</c> and <c>remember()</c>, step for step.
/// </summary>
public sealed partial class LookupService(
    SettingsService settings,
    Localization localization,
    TranslationService translation,
    WordExplainer explainer,
    ILanguagePacks packs,
    HistoryService history,
    ReviewService review,
    TodayService today,
    WikipediaService wikipedia,
    INetworkStatus network,
    TimeProvider time,
    LocalDictionary? dictionary = null,
    ErrorLog? errors = null)
{
    /// <summary>Longer text is refused ("too_long") rather than cut.</summary>
    public const int MaxText = 30_000;

    /// <summary>How long a dictionary answer waits for the sentence's translation.</summary>
    private static readonly TimeSpan ContextWait = TimeSpan.FromMilliseconds(2500);

    /// <summary>Online first: how long Google has before the dictionary answers a word it knows.</summary>
    private static readonly TimeSpan OnlineWait = TimeSpan.FromMilliseconds(2500);

    private readonly Lru<string, LookupResult> _cache = new(300);

    /// <summary>Looks up <paramref name="rawText"/>; a failure is noted for Report a problem ("lookup: network").</summary>
    public async Task<LookupResult> LookupAsync(string rawText, LookupOptions? options = null, CancellationToken cancel = default)
    {
        try
        {
            return await LookupCoreAsync(rawText, options ?? new LookupOptions(), cancel).ConfigureAwait(false);
        }
        catch (LamhaException e)
        {
            errors?.Note("lookup", e);
            throw;
        }
    }

    private async Task<LookupResult> LookupCoreAsync(string rawText, LookupOptions options, CancellationToken cancel)
    {
        var s = settings.Current;
        var text = Pieces.Tidy(rawText); // paragraphs survive into the translation
        if (text.Length == 0)
        {
            throw new LamhaException(ErrorCodes.Empty);
        }

        if (text.Length > MaxText)
        {
            throw new LamhaException(ErrorCodes.TooLong);
        }

        var source = "auto";
        var target = options.Target.Length > 0 ? options.Target : s.TargetLang;
        if (s.ReverseForArabic && target == "ar" && Scripts.HasArabic(text) && !Scripts.HasLatinWord(text))
        {
            source = "ar";
            target = "en";
        }

        var word = TextKind.IsLookupCandidate(text);
        var mode = s.DictSource;
        var offline = mode == DictSource.Offline;
        var translatorAi = translation.TranslatorAi();
        var force = options.ForceAi;
        var englishWord = EnglishWord().IsMatch(text);
        // English–English: an English word, with the switch on or English as the translation language.
        var english = word && !force && englishWord && Explains("en", target, s);
        var context = s.UseContext && word && options.Context is { } c
            ? new WordContext(c.Before.Length > 300 ? c.Before[^300..] : c.Before, c.After.Length > 300 ? c.After[..300] : c.After)
            : null;
        var key = string.Join('|', text.ToLowerInvariant(), source, target, word, s.TranslateDefinitions, mode, localization.Language,
            context is null ? "" : context.Before + "¦" + context.After, $"{s.TrService}|{translatorAi?.Id}", force, english, string.Join(',', s.ExplainLangs), packs.Key);
        if (_cache.TryGet(key, out var cached))
        {
            wikipedia.Prefetch(cached, s.ShowWikipedia, s.TargetLang, offline);
            return await RememberAsync(cached, text, word, context, s).ConfigureAwait(false);
        }

        // The word's meaning in its sentence, asked at once: it doesn't depend on the dictionary's answer, so the two
        // requests go out together. A dictionary answer waits for it ContextWait from now, however long the lookup took.
        var contextAsk = context is not null && !english && !force ? ContextAsync(text, context, target, source, offline, cancel) : null;
        var contextInTime = contextAsk is null ? null : Within(contextAsk, ContextWait);

        // The offline dictionary covers English → Arabic and Arabic → English single words and short phrases.
        var enLocal = word && target == "ar" && englishWord;
        var arLocal = word && source == "ar" && target == "en" && text.Split(' ').Length <= 2;
        var hasLocal = (enLocal || arLocal) && dictionary is not null;
        LookupResult? Local() => !hasLocal ? null : enLocal ? dictionary!.LookupEnglishToArabic(text, context) : dictionary!.LookupArabic(text);

        var onlineLate = false;
        var result = english ? await EnglishAsync(text, context, s, target, cancel).ConfigureAwait(false) : null; // null: Google says it isn't English
        if (!english && hasLocal && mode != DictSource.Online && !force)
        {
            result = Local();
            if (result is { Translation.Length: 0 } && mode == DictSource.Local && context is null)
            {
                // The dictionary has definitions but no Arabic word: one small request for the main meaning.
                try
                {
                    var t = await translation.TranslateAsync([result.Query], target, "en", kind: TranslationKind.Word, cancel: cancel).ConfigureAwait(false);
                    result = result with { Translation = t.Texts.FirstOrDefault() ?? "" };
                }
                catch (LamhaException)
                {
                    // The definitions are still there.
                }
            }
        }

        // No network to ask (or "Local only"): an installed language pack may know the word.
        if (result is null && word && !force && (offline || network.IsOffline))
        {
            result = PackLookup(text, target, s);
        }

        if (result is null)
        {
            // Google or the AI (Settings → Translation service); on "Local only" just a local AI, if there is one.
            var plan = translation.Plan(translatorAi, word ? TranslationKind.Word : TranslationKind.Text, offline, force);
            if (plan.Count == 0 && offline)
            {
                throw new LamhaException(enLocal || arLocal ? ErrorCodes.NotFoundOffline : ErrorCodes.OfflineMode);
            }

            var ask = TranslationService.TryEnginesAsync(
                plan,
                engine => engine == Engine.Google
                    ? translation.Google.LookupAsync(text, source, target, word, localization.Language, s.TranslateDefinitions && target != "en" ? Glosses(target) : null, cancel)
                    : translation.Ai.LookupAsync(text, source, target, word, translatorAi!, cancel),
                r => r.Rough);

            // Online first, for a word the dictionary has: a network that answers nothing held the card for the whole
            // timeout. After a short wait the dictionary answers instead, and that answer isn't kept.
            if (mode == DictSource.Online && hasLocal && !force && !await Settles(ask, OnlineWait).ConfigureAwait(false))
            {
                _ = ask.ContinueWith(t => t.Exception, TaskScheduler.Default); // its failure, if any, is no one's
                result = Local();
                onlineLate = result is not null;
            }

            if (result is null)
            {
                try
                {
                    result = await ask.ConfigureAwait(false);
                }
                catch (LamhaException)
                {
                    // Network down or rate-limited: the offline dictionary, or a language pack.
                    result = !force ? Local() : null;
                    if (result is null && word && !force)
                    {
                        result = PackLookup(text, target, s);
                    }

                    if (result is null)
                    {
                        throw;
                    }
                }
            }
        }

        // A single word only the AI answered: the dictionary's answer wins when it has one; otherwise the AI's answer is
        // a guess, said so on the card and kept out of the history (it gave a made-up word a meaning).
        if (word && !force && result.From == ResultSource.Ai && result.IsWord)
        {
            result = Local() ?? result with { AiGuess = true };
        }

        // A word in a language the user reads in that language (or in the translation language itself): explained.
        if (word && !force && result.IsWord && !result.Explain && Explains(result.Source, target, s))
        {
            result = await explainer.ExplainAsync(result, text, context, translatorAi, offline, cancel).ConfigureAwait(false);
        }

        // The card's Wikipedia part: on its way while the sentence is translated.
        wikipedia.Prefetch(result, s.ShowWikipedia, s.TargetLang, offline);

        // The meaning of the word in this sentence. A dictionary answer waits for it a short while only: a network
        // that answers nothing mustn't hold the card; that answer isn't kept, so the next lookup asks again.
        var contextLate = false;
        if (contextAsk is not null && result.IsWord && !result.Explain)
        {
            var (settled, meaning) = result.From == ResultSource.Local ? await contextInTime!.ConfigureAwait(false) : (true, await contextAsk.ConfigureAwait(false));
            if (!settled)
            {
                contextLate = true;
            }
            else if (meaning is not null)
            {
                // The sentence's translation kept the word as it is (a name: "Gemini"): say so, don't call it the meaning.
                if (Scripts.LettersAndDigits(meaning.Word) == Scripts.LettersAndDigits(text))
                {
                    meaning = meaning with { Untranslated = true };
                }

                result = result with { Context = meaning };
                if (result.Translation.Length == 0 && !meaning.Untranslated)
                {
                    result = result with { Translation = meaning.Word };
                }
            }
        }

        if (!result.Rough && !contextLate && !onlineLate)
        {
            _cache.Set(key, result); // an AI answer with stray letters is asked again next time
        }

        return await RememberAsync(result, text, word, context, s).ConfigureAwait(false);
    }

    /// <summary>
    /// The card's switch for <paramref name="lang"/> words: explained in their own language, or translated. English
    /// keeps its own setting.
    /// </summary>
    public void SetExplain(string lang, bool on)
    {
        if (!LanguageCode().IsMatch(lang))
        {
            return;
        }

        settings.Update(s => lang == "en"
            ? s with { EnDict = on }
            : s with { ExplainLangs = [.. s.ExplainLangs.Where(l => l != lang), .. on ? new[] { lang } : []] });
    }

    /// <summary>The word's meaning inside its sentence as the Arabic view shows it, or "": at most 2.5 s, never an error.</summary>
    public async Task<string> ContextArabicAsync(string text, WordContext context, CancellationToken cancel = default)
    {
        var (settled, meaning) = await Within(ContextAsync(text, context, "ar", "en", false, cancel), ContextWait).ConfigureAwait(false);
        return settled && meaning is { Untranslated: false } m ? m.Word : "";
    }

    /// <summary>
    /// Explain <paramref name="lang"/> words in that language: always when it's the translation language (a translation
    /// would give the word back), else when the card's switch or Settings turned it on (Lamha 2's <c>explains</c>).
    /// </summary>
    private static bool Explains(string lang, string target, LamhaSettings s) =>
        lang.Length > 0 && (lang == target || (lang == "en" ? s.EnDict : s.ExplainLangs.Contains(lang)));

    /// <summary>
    /// What a lookup leaves behind, fresh or from the cache alike: the word at the top of the history, its review card,
    /// and the count toward milestones and today's goal (Lamha 2's <c>remember</c>).
    /// </summary>
    private async Task<LookupResult> RememberAsync(LookupResult result, string text, bool word, WordContext? context, LamhaSettings s)
    {
        result = AsWritten(result, text, context);
        var learnable = word && result.Translation.Length > 0 && !result.Translation.Equals(text, StringComparison.OrdinalIgnoreCase)
            && result.Context is not { Untranslated: true } // a name here: not a word to learn
            && !result.AiGuess; // the AI's guess at a word no dictionary has: maybe not a word at all
        if (!s.SaveHistory || !learnable)
        {
            return result;
        }

        var newWord = history.Add(result.Query, result.Explain && result.Meaning.Length > 0 ? result.Meaning : result.Translation, result.Source);
        // Automatic cards are a record of lookups too: "Keep a history" off means none (the bookmark still adds one).
        if (s.CardsAuto && result.Source == "en")
        {
            await review.AddAsync(CardFrom.Lookup(result, text, context)).ConfigureAwait(false);
        }

        // Milestones count words: one already in the recent history, looked up in another sentence, isn't counted again.
        if (!newWord)
        {
            return result;
        }

        var milestone = today.CountLookup();
        var goal = today.CountActivity(); // today's goal counts the same words
        return milestone > 0 || goal > 0 ? result with { Milestone = milestone, Goal = goal } : result;
    }

    /// <summary>
    /// The dictionaries keep words in small letters: a word with a capital in the middle of its sentence ("we flew to
    /// Paris") is a name, and keeps it on the card. Typed alone, or starting a sentence, a capital says nothing.
    /// </summary>
    private static LookupResult AsWritten(LookupResult result, string text, WordContext? context)
    {
        if (!result.IsWord || result.Query != text.ToLowerInvariant() || !Capitalised().IsMatch(text))
        {
            return result;
        }

        var before = context?.Before.Trim() ?? "";
        return before.Length == 0 || SentenceEnd().IsMatch(before) ? result : result with { Query = text };
    }

    /// <summary>
    /// An English word in the English–English dictionary: the offline dictionary first (it picks the definition that
    /// fits the sentence), Google's English definitions for what it lacks; null when Google says it isn't English.
    /// <see cref="LookupResult.Meaning"/> keeps the meaning in the translation language for the review card.
    /// </summary>
    private async Task<LookupResult?> EnglishAsync(string text, WordContext? context, LamhaSettings s, string target, CancellationToken cancel)
    {
        var mode = s.DictSource;
        LookupResult? Local(string contextArabic = "") => dictionary?.LookupEnglish(text, context, contextArabic) is { } r ? r with { Other = target } : null;
        if (mode != DictSource.Online)
        {
            // Online, the word's Arabic in its sentence (the Arabic view's answer) helps pick the same sense here.
            var contextArabic = context is not null && mode != DictSource.Offline && s.TargetLang == "ar" ? await ContextArabicAsync(text, context, cancel).ConfigureAwait(false) : "";
            if (Local(contextArabic) is { } found)
            {
                return found;
            }

            if (mode == DictSource.Offline)
            {
                throw new LamhaException(ErrorCodes.NotFoundOffline);
            }
        }

        var other = s.TargetLang != "en" ? s.TargetLang : "ar";
        LookupResult online;
        try
        {
            // Google's definitions are in the word's language: not translated here.
            online = await translation.Google.LookupAsync(text, "auto", other, true, localization.Language, null, cancel).ConfigureAwait(false);
        }
        catch (LamhaException)
        {
            if (mode == DictSource.Online && Local() is { } r)
            {
                return r; // offline or rate-limited: the dictionary after all
            }

            throw;
        }

        if (online.Source.Length > 0 && online.Source != "en")
        {
            return null; // Latin letters, but French, Spanish…: explained (or not) as that language
        }

        var entries = online.Definitions.SelectMany(d => d.Entries.Select(e => (d.Pos, Entry: e))).ToList();
        if (entries.Count == 0 && mode == DictSource.Online && Local() is { } fromDictionary)
        {
            return fromDictionary;
        }

        // Google groups its definitions by part of speech: lead with the one the sentence suggests.
        var baseForm = online.Definitions.FirstOrDefault()?.Base is { Length: > 0 } b ? b : text;
        var hint = LocalDictionary.PosHint(text.ToLowerInvariant(), baseForm.ToLowerInvariant(), context);
        var top = entries.Count == 0 ? default : hint.Length > 0 && entries.FindIndex(e => e.Pos.Code == hint) is >= 0 and var i ? entries[i] : entries[0];
        return online with
        {
            Source = "en",
            Explain = true,
            Target = "en",
            Other = target,
            Dict = [],
            Meaning = online.Translation,
            Translation = top.Entry?.Gloss ?? "",
            HeroExample = top.Entry?.Example ?? "",
            HeroPos = top.Entry is null ? default : top.Pos,
            ContextSense = top.Entry is not null && hint.Length > 0 && !ReferenceEquals(top.Entry, entries[0].Entry),
        };
    }

    /// <summary>
    /// Offline: the word in an installed pack, or null. Packs for languages the user wants explained come first; then
    /// any other, since an explanation beats an error when nothing can translate.
    /// </summary>
    private LookupResult? PackLookup(string text, string target, LamhaSettings s)
    {
        foreach (var lang in packs.Languages.OrderByDescending(l => Explains(l, target, s)))
        {
            if (packs.Find(lang, text) is { } hit)
            {
                return WordExplainer.FromPack(hit, text, lang, target);
            }
        }

        return null;
    }

    /// <summary>Translates "…before &lt;a i=0&gt;word&lt;/a&gt; after…" and pulls out the marked part, or null.</summary>
    private async Task<ContextMeaning?> ContextAsync(string text, WordContext context, string target, string source, bool offline, CancellationToken cancel)
    {
        try
        {
            var html = Html.Escape(context.Before) + "<a i=0>" + Html.Escape(text) + "</a>" + Html.Escape(context.After);
            var translated = await translation.TranslateAsync([html], target, source, html: true, TranslationKind.Context, offline, cancel: cancel).ConfigureAwait(false);
            var m = Marked().Match(translated.Texts.FirstOrDefault() ?? "");
            if (!m.Success)
            {
                return null;
            }

            static string Clean(string s) => Spaces().Replace(Html.DecodeEntities(Html.StripTags(s)), " ");
            var word = Clean(m.Groups[2].Value).Trim();
            return word.Length > 0 ? new ContextMeaning(word, Clean(m.Groups[1].Value), Clean(m.Groups[3].Value)) : null;
        }
        catch (LamhaException)
        {
            return null;
        }
    }

    /// <summary>Google's English definitions translated into the translation language (Arabic-first).</summary>
    private Func<IReadOnlyList<string>, string, Task<IReadOnlyList<string>>> Glosses(string target) => async (glosses, source) =>
        (await translation.TranslateAsync(glosses, target, source, kind: TranslationKind.Gloss).ConfigureAwait(false)).Texts;

    /// <summary><paramref name="task"/>'s result if it settles within <paramref name="wait"/>, else (false, default).</summary>
    private async Task<(bool Settled, T? Result)> Within<T>(Task<T> task, TimeSpan wait)
    {
        if (!await Settles(task, wait).ConfigureAwait(false))
        {
            return (false, default);
        }

        try
        {
            return (true, await task.ConfigureAwait(false));
        }
        catch (LamhaException)
        {
            return (true, default);
        }
    }

    /// <summary>Whether <paramref name="task"/> settles (answers or fails) within <paramref name="wait"/>.</summary>
    private async Task<bool> Settles(Task task, TimeSpan wait)
    {
        using var stop = new CancellationTokenSource();
        var timer = Task.Delay(wait, time, stop.Token);
        var first = await Task.WhenAny(task, timer).ConfigureAwait(false);
        await stop.CancelAsync().ConfigureAwait(false);
        return first == task;
    }

    [GeneratedRegex(@"^[A-Za-z][A-Za-z'’ -]*$")]
    private static partial Regex EnglishWord();

    [GeneratedRegex("^[a-z]{2,3}$")]
    private static partial Regex LanguageCode();

    [GeneratedRegex(@"^\p{Lu}\p{Ll}+$")]
    private static partial Regex Capitalised();

    [GeneratedRegex(@"[.!?…:][""'”’)\]]*$")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"^([\s\S]*?)<a i=""?0""?>([\s\S]*?)</a>([\s\S]*)$")]
    private static partial Regex Marked();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
