using System.Text.Json.Nodes;
using Lamha.Core.Abstractions;
using Lamha.Core.Ai;
using Lamha.Core.Caching;
using Lamha.Core.Diagnostics;
using Lamha.Core.Settings;

namespace Lamha.Core.Writing;

/// <summary>A proofreading correction.</summary>
public sealed record Issue(string Original, string Fix, string Category, string Why);

/// <summary>What a writing tool gives back: the text, or for proofreading the corrected text and its issues.</summary>
public sealed record WritingResult(string Text, IReadOnlyList<Issue> Issues);

/// <summary>Options from the card: Write new's kind and tone, Reply's intent, the summary's language, "try again".</summary>
public sealed record WritingOptions
{
    /// <summary>The summary's language, "ar" or "en"; "" follows the interface.</summary>
    public string Language { get; init; } = "";

    public string Tone { get; init; } = "";

    public string Intent { get; init; } = "";

    /// <summary>Write new: an email rather than a message.</summary>
    public bool Email { get; init; }

    /// <summary>"Try again": asks afresh instead of answering from the cache, and isn't counted in the journal twice.</summary>
    public bool Fresh { get; init; }
}

/// <summary>The writing tools on selected text: Lamha 2's <c>aiRun()</c>, its cache, and the mistake journal.</summary>
public sealed class WritingService(AiService ai, SettingsService settings, IMistakeStore journal, Localization localization, TimeProvider time, ErrorLog? errors = null)
{
    /// <summary>The longest text a tool takes (Lamha 2's <c>AI_MAX_TEXT</c>).</summary>
    public const int MaxText = 50_000;

    private const int JournalRecent = 200;
    private readonly Lru<string, WritingResult> _cache = new(60);
    private readonly SemaphoreSlim _journalGate = new(1, 1);

    /// <summary>Runs <paramref name="tool"/> on <paramref name="text"/>; a failure is noted for Report a problem ("ai proofread: ai_bad_key").</summary>
    public async Task<WritingResult> RunAsync(WritingTool tool, string text, WritingOptions? options = null, CancellationToken cancel = default)
    {
        try
        {
            return await RunCoreAsync(tool, text, options ?? new WritingOptions(), cancel).ConfigureAwait(false);
        }
        catch (LamhaException e)
        {
            errors?.Note("ai " + tool.ToString().ToLowerInvariant(), e);
            throw;
        }
    }

    private async Task<WritingResult> RunCoreAsync(WritingTool tool, string text, WritingOptions options, CancellationToken cancel)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            throw new LamhaException(ErrorCodes.Empty);
        }

        if (text.Length > MaxText)
        {
            throw new LamhaException(ErrorCodes.AiTooLong);
        }

        // Explanations (proofreading, explain) and the summary's default language follow the interface.
        var ui = localization.Language;
        var x = new PromptOptions(
            Language: options.Language is "ar" or "en" ? options.Language : ui,
            Ui: ui,
            Tone: options.Tone,
            Intent: options.Intent.Trim() is var intent && intent.Length > 2000 ? intent[..2000] : intent,
            Email: options.Email);

        var config = ai.Config();
        var s = settings.Current;
        var cacheKey = string.Join('\u0001', tool, config.Id, x.Language, x.Ui, x.Tone, x.Intent, x.Email, text);
        if (!options.Fresh && _cache.TryGet(cacheKey, out var hit))
        {
            return hit;
        }

        var keepJournal = tool == WritingTool.Proofread && s.SaveMistakes;
        if (keepJournal)
        {
            x = x with { Weak = WeakPoints() };
        }

        var request = new AiRequest($"{WritingPrompts.Task(tool, x)}\n\n<text>\n{text}\n</text>", WritingPrompts.Schema(tool), WritingPrompts.System, WritingPrompts.Effort(tool), text.Length);
        var answer = await ai.CompleteJsonAsync(config, request, cancel).ConfigureAwait(false);
        WritingResult result;
        if (tool == WritingTool.Proofread)
        {
            result = new WritingResult(Str(answer["corrected"]), Issues(answer["issues"]));
            if (keepJournal && !options.Fresh)
            {
                await RecordAsync(result.Issues).ConfigureAwait(false); // a retry of the same text isn't counted twice
            }
        }
        else
        {
            result = new WritingResult(Str(answer["text"]), []);
        }

        _cache.Set(cacheKey, result);
        return result;
    }

    /// <summary>
    /// The user's most frequent mistake kinds for the proofreading prompt: three or more times, the top three, never
    /// "other" (e.g. "articles (a/an/the), prepositions"). Lamha 2's <c>weakPoints()</c>.
    /// </summary>
    public string WeakPoints() => string.Join(", ", journal.Read().Counts
        .Where(c => c.Key != MistakeCategories.Other && MistakeCategories.IsKnown(c.Key) && c.Value >= 3)
        .OrderByDescending(c => c.Value)
        .Take(3)
        .Select(c => MistakeCategories.PromptName(c.Key)));

    /// <summary>The mistake journal as it is now.</summary>
    public MistakeJournal Journal() => journal.Read();

    /// <summary>
    /// The user's most frequent mistake kind and how often it was found, for the line under the Write page's box;
    /// never "other", and null when there's none yet (Lamha 2's <c>renderWeak()</c> in popup.js).
    /// </summary>
    public (string Category, int Count)? MostFrequent()
    {
        var top = journal.Read().Counts
            .Where(c => c.Key != MistakeCategories.Other && MistakeCategories.IsKnown(c.Key) && c.Value > 0)
            .OrderByDescending(c => c.Value)
            .FirstOrDefault();
        return top.Key is null ? null : (top.Key, top.Value);
    }

    /// <summary>
    /// Clear the journal (Lamha 2's, in its Settings): the statistics start again from now. Returns the journal as it
    /// was, for Undo.
    /// </summary>
    public MistakeJournal ClearJournal() => ReplaceJournal(MistakeJournal.Empty(time.GetUtcNow().ToUnixTimeMilliseconds()));

    /// <summary>Puts back a journal <see cref="ClearJournal"/> returned (Undo).</summary>
    public void RestoreJournal(MistakeJournal cleared) => ReplaceJournal(cleared);

    private MistakeJournal ReplaceJournal(MistakeJournal next)
    {
        _journalGate.Wait();
        try
        {
            var before = journal.Read();
            journal.Replace(next);
            return before;
        }
        finally
        {
            _journalGate.Release();
        }
    }

    /// <summary>The changes that are changes, each with a known category ("other" when the model made one up).</summary>
    private static List<Issue> Issues(JsonNode? node) => [.. (node as JsonArray ?? [])
        .OfType<JsonObject>()
        .Where(i => i["original"] is JsonValue o && o.TryGetValue<string>(out _) && i["fix"] is JsonValue f && f.TryGetValue<string>(out _))
        .Select(i => new Issue(Str(i["original"]), Str(i["fix"]), Str(i["category"]) is var c && MistakeCategories.IsKnown(c) ? c : MistakeCategories.Other, Str(i["why"])))
        .Where(i => i.Original != i.Fix)];

    /// <summary>Notes what proofreading found: newest check first, its mistakes in text order (Lamha 2's <c>recordMistakes</c>).</summary>
    private async Task RecordAsync(IReadOnlyList<Issue> issues)
    {
        await _journalGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var now = time.GetUtcNow().ToUnixTimeMilliseconds();
            journal.Record(
                [.. issues.Select(i => new MistakeEntry(i.Category, Cut(i.Original, 200), Cut(i.Fix, 200), Cut(i.Why, 400), now))],
                now,
                JournalRecent);
        }
        finally
        {
            _journalGate.Release();
        }
    }

    private static string Str(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    private static string Cut(string s, int max) => s.Length > max ? s[..max] : s;
}
