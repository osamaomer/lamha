using Lamha.Core.Ai;
using Lamha.Core.Settings;

namespace Lamha.Core.Translation;

/// <summary>What is being translated: it decides who may translate it.</summary>
public enum TranslationKind
{
    /// <summary>A word: Google's dictionary first, whatever the setting (its meanings are richer than a translation).</summary>
    Word,

    /// <summary>A sentence or a longer text.</summary>
    Text,

    /// <summary>A word's sentence, with the word marked.</summary>
    Context,

    /// <summary>Definitions.</summary>
    Gloss,
}

/// <summary>Who translates: Google or the AI.</summary>
public enum Engine
{
    Google,
    Ai,
}

/// <summary>Translated strings, and who translated them.</summary>
public sealed record Translated(IReadOnlyList<string> Texts, Engine Engine, string AiName = "", bool Rough = false);

/// <summary>Whether the PC has a network at all (Windows says so; Lamha 2's <c>navigator.onLine</c>).</summary>
public interface INetworkStatus
{
    bool IsOffline { get; }
}

/// <summary>
/// Every translation goes through here: Google, the AI, or Google with the AI when Google can't (Settings → Translation
/// service). Lamha 2's <c>trSettings</c>, <c>trPlan</c>, <c>tryEngines</c> and <c>translateBatch</c>. In 3.0 the
/// fallback is between Google and the AI only, as in 2.x (decision 0007).
/// </summary>
public sealed class TranslationService(SettingsService settings, AiService ai, GoogleTranslate google, AiTranslator aiTranslator, INetworkStatus network)
{
    public GoogleTranslate Google => google;

    public AiTranslator Ai => aiTranslator;

    /// <summary>The translator's AI, or null when it isn't set up (Google only).</summary>
    public AiConfig? TranslatorAi() => ai.TranslatorConfig();

    /// <summary>
    /// Which engines to try, in order. <paramref name="offline"/>: the dictionary is on "Local only", so only an AI on
    /// this PC or the local network may be used. <paramref name="forceAi"/>: the card's "Better translation", which asks
    /// the AI whatever the settings say.
    /// </summary>
    public IReadOnlyList<Engine> Plan(AiConfig? translatorAi, TranslationKind kind, bool offline = false, bool forceAi = false)
    {
        if (forceAi)
        {
            return translatorAi is null ? [] : [Engine.Ai];
        }

        var service = settings.Current.TrService;
        var useAi = translatorAi is not null && service != TranslateWith.Google && (!offline || translatorAi.IsLocal);
        var useGoogle = !offline && !network.IsOffline && !google.IsDown;
        Engine[] order = service == TranslateWith.Ai && kind != TranslationKind.Word ? [Engine.Ai, Engine.Google] : [Engine.Google, Engine.Ai];
        return [.. order.Where(e => e == Engine.Ai ? useAi : useGoogle)];
    }

    /// <summary>
    /// Tries each engine until one answers. An answer <paramref name="isRough"/> says has stray letters is kept only if
    /// nothing better comes; with no engine at all, <paramref name="emptyCode"/> is the error.
    /// </summary>
    public static async Task<T> TryEnginesAsync<T>(IReadOnlyList<Engine> plan, Func<Engine, Task<T>> run, Func<T, bool> isRough, string emptyCode = ErrorCodes.Network)
    {
        Exception? last = null;
        T? rough = default;
        var hasRough = false;
        foreach (var engine in plan)
        {
            try
            {
                var result = await run(engine).ConfigureAwait(false);
                if (isRough(result))
                {
                    if (!hasRough)
                    {
                        rough = result;
                        hasRough = true;
                    }

                    continue;
                }

                return result;
            }
            catch (LamhaException e)
            {
                last = e;
            }
        }

        if (hasRough)
        {
            return rough!;
        }

        throw last ?? new LamhaException(emptyCode);
    }

    /// <summary>Many strings (the same order back), translated by whoever the settings choose, and who did.</summary>
    public async Task<Translated> TranslateAsync(IReadOnlyList<string> texts, string target, string source = "auto", bool html = false, TranslationKind kind = TranslationKind.Text, bool offline = false, bool forceAi = false, CancellationToken cancel = default)
    {
        var translatorAi = TranslatorAi();
        return await TryEnginesAsync(
            Plan(translatorAi, kind, offline, forceAi),
            async engine =>
            {
                if (engine == Engine.Google)
                {
                    return new Translated(await google.TranslateAsync(texts, target, source, html, cancel).ConfigureAwait(false), Engine.Google);
                }

                var r = await aiTranslator.TranslateAsync(texts, target, source, html, translatorAi!, cancel).ConfigureAwait(false);
                return new Translated(r.Texts, Engine.Ai, translatorAi!.Name, r.Rough);
            },
            r => r.Rough,
            offline ? ErrorCodes.OfflineMode : ErrorCodes.Network).ConfigureAwait(false);
    }
}
