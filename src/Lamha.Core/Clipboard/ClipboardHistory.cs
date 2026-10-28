using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lamha.Core.Abstractions;
using Lamha.Core.Settings;
using Lamha.Core.Text;

namespace Lamha.Core.Clipboard;

/// <summary>A copy the clipboard listener recorded.</summary>
public sealed record CapturedClip(string Text, string? Html, string SourceApp, long CapturedAt);

/// <summary>Results of Lamha's tools on a copy, kept with it (encrypted) so a second look costs nothing.</summary>
public sealed record ClipCache
{
    /// <summary>The translation, per target language.</summary>
    public IReadOnlyDictionary<string, string> Translation { get; init; } = new Dictionary<string, string>();

    /// <summary>"Write it in English".</summary>
    public string? English { get; init; }

    /// <summary>The summary, per summary language ("ar", "en").</summary>
    public IReadOnlyDictionary<string, string> Summary { get; init; } = new Dictionary<string, string>();

    /// <summary>"Proofread": the corrected text and what was wrong (Lamha 2's <c>cache.proofread</c>).</summary>
    public ClipProofread? Proofread { get; init; }
}

/// <summary>A copy proofread: the corrected text and each issue.</summary>
public sealed record ClipProofread(string Corrected, IReadOnlyList<Writing.Issue> Issues);

/// <summary>A copy in clipboard history, whole.</summary>
public sealed record Clip
{
    public required string Id { get; init; }

    public required string Text { get; init; }

    /// <summary>The copy's formatting, when it had some and it was small enough.</summary>
    public string? Html { get; init; }

    /// <summary>The user's name for it (at most <see cref="ClipboardHistory.LabelMax"/> characters).</summary>
    public string Label { get; init; } = "";

    public required string SourceApp { get; init; }

    /// <summary>"ar" | "en" | "mixed" | "other".</summary>
    public required string Lang { get; init; }

    public long CreatedAt { get; init; }

    public long LastCopiedAt { get; init; }

    public long LastUsedAt { get; init; }

    public int CopyCount { get; init; } = 1;

    public int UseCount { get; init; }

    public bool Pinned { get; init; }

    public ClipCache Cache { get; init; } = new();
}

/// <summary>
/// A copy's text changed by <see cref="ClipboardHistory.Replace"/>: the copy as it was, the text it has now, and the
/// copy of that text merged into it (deleted, for <see cref="ClipboardHistory.UndoReplace"/> to bring back), if any.
/// </summary>
public sealed record ClipReplaced(Clip Before, string Text, string? MergedId);

/// <summary>
/// A row of the history list: the first 300 characters, and the line under them: an English copy's translation, or an
/// Arabic copy's English ("Write it in English").
/// </summary>
public sealed record ClipPreview(
    string Id,
    string Text,
    int Length,
    bool HasHtml,
    string Label,
    bool Pinned,
    string SourceApp,
    string Lang,
    long CreatedAt,
    long LastCopiedAt,
    long LastUsedAt,
    int CopyCount,
    int UseCount,
    string Translation);

/// <summary>A page of the list, and how many copies match in all.</summary>
public sealed record ClipPage(IReadOnlyList<ClipPreview> Items, int Total);

/// <summary>
/// Clipboard history: what the user copied, with duplicates merged, kept to a size and an age, and searched with
/// Arabic spelling variants made equal. Lamha 2's desktop/clipboard-store.js, on SQLite instead of one JSON file:
/// copies are read from disk only while the history is being looked at (<see cref="Unload"/> drops them), and each
/// copy's contents are encrypted by Windows for this user.
/// </summary>
public sealed class ClipboardHistory(IClipStore store, IDataProtector protector, IStateStore state, SettingsService settings, TimeProvider clock)
{
    public const int MaxPinned = 300;
    public const int LabelMax = 60;

    /// <summary>The not-found error code (a copy that's gone, or an undo that's too late).</summary>
    public const string NotFound = "not_found";

    /// <summary>More than <see cref="MaxPinned"/> pinned copies.</summary>
    public const string PinLimit = "pin_limit";

    private const int PreviewChars = 300;
    private const int IndexChars = 5000;
    private const string KeyName = "clipKey";

    /// <summary>A deleted copy can be brought back this long (the interface offers 5 s); memory only.</summary>
    private static readonly TimeSpan UndoTime = TimeSpan.FromSeconds(30);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, (ClipRecord Record, long At)> _trash = [];
    private Dictionary<string, Entry>? _index;
    private byte[]? _hashKey;
    private long _lastMs;
    private int _sameMs;

    /// <summary>Raised after anything in the history changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Records a copy, or moves the same text copied again to the top. Returns its id.</summary>
    public string Ingest(CapturedClip capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        string id;
        lock (_gate)
        {
            var key = TextKey(capture.Text);
            var known = store.FindByKey(key);
            Clip clip;
            if (known is not null && Open(known) is { } old)
            {
                clip = old with
                {
                    LastCopiedAt = capture.CapturedAt,
                    CopyCount = old.CopyCount + 1,
                    SourceApp = capture.SourceApp.Length > 0 ? capture.SourceApp : old.SourceApp,
                    Html = capture.Html,
                };
                Save(clip, key);
            }
            else
            {
                if (known is not null)
                {
                    store.Delete(known.Id); // the same text, written where it can't be read any more
                }

                clip = new Clip
                {
                    Id = NewId(),
                    Text = capture.Text,
                    Html = capture.Html,
                    SourceApp = capture.SourceApp.Length > 0 ? capture.SourceApp : "unknown",
                    Lang = SearchText.DetectLanguage(capture.Text),
                    CreatedAt = capture.CapturedAt,
                    LastCopiedAt = capture.CapturedAt,
                };
                Save(clip, key);
                Trim();
            }

            id = clip.Id;
        }

        OnChanged();
        return id;
    }

    /// <summary>
    /// Copies from elsewhere (Lamha 2's clipboard history, moved over by the importer), kept with their times, label and
    /// pin. A text already here keeps the copy that's here; pins stop at <see cref="MaxPinned"/>, and the history is
    /// trimmed to the setting afterwards. Returns how many were added and kept.
    /// </summary>
    public int Import(IEnumerable<Clip> clips)
    {
        ArgumentNullException.ThrowIfNull(clips);
        var added = 0;
        lock (_gate)
        {
            var pinned = store.CountPinned();
            foreach (var clip in clips)
            {
                var key = TextKey(clip.Text);
                if (clip.Text.Trim().Length == 0 || store.FindByKey(key) is not null)
                {
                    continue;
                }

                var pin = clip.Pinned && pinned < MaxPinned;
                pinned += pin ? 1 : 0;
                Save(
                    clip with
                    {
                        Id = IsId(clip.Id) && store.Get(clip.Id) is null ? clip.Id : NewId(),
                        Label = clip.Label.Length > LabelMax ? clip.Label[..LabelMax] : clip.Label,
                        SourceApp = clip.SourceApp.Length > 0 ? clip.SourceApp : "unknown",
                        Lang = SearchText.DetectLanguage(clip.Text),
                        Pinned = pin,
                    },
                    key);
                added++;
            }

            added = Math.Max(0, added - Trim());
        }

        if (added > 0)
        {
            OnChanged();
        }

        return added;
    }

    /// <summary>
    /// A page of the list: pinned copies always first, then the most recent, or for a query the best match first
    /// (every word must match; the whole query, word starts and the label rank higher). <paramref name="targetLang"/>
    /// picks the translation shown under English copies.
    /// </summary>
    public ClipPage List(string query = "", bool pinnedOnly = false, int limit = 100, int offset = 0, string targetLang = "ar")
    {
        lock (_gate)
        {
            var pool = Index().Values.Where(e => !pinnedOnly || e.Clip.Pinned);
            var q = SearchText.Normalize(query);
            List<Clip> ordered;
            if (q.Length == 0)
            {
                ordered = [.. pool.Select(e => e.Clip).Order(ListOrder.Instance)];
            }
            else
            {
                var tokens = q.Split(' ');
                ordered = [.. pool
                    .Select(e => (e.Clip, Score: Score(e, q, tokens)))
                    .Where(x => x.Score >= 0)
                    .OrderByDescending(x => x.Clip.Pinned)
                    .ThenByDescending(x => x.Score)
                    .ThenBy(x => x.Clip, RecencyOrder.Instance)
                    .Select(x => x.Clip)];
            }

            var items = ordered.Skip(offset).Take(limit).Select(c => Preview(c, targetLang)).ToList();
            return new ClipPage(items, ordered.Count);
        }
    }

    /// <summary>The whole copy, or null.</summary>
    public Clip? Get(string id)
    {
        lock (_gate)
        {
            return Find(id);
        }
    }

    public void SetPinned(string id, bool pinned)
    {
        lock (_gate)
        {
            var clip = Need(id);
            if (clip.Pinned == pinned)
            {
                return;
            }

            if (pinned && store.CountPinned() >= MaxPinned)
            {
                throw new LamhaException(PinLimit);
            }

            Save(clip with { Pinned = pinned });
        }

        OnChanged();
    }

    /// <summary>Names a copy: whitespace collapsed, at most 60 characters. Returns the label as kept.</summary>
    public string SetLabel(string id, string? label)
    {
        string kept;
        lock (_gate)
        {
            var clip = Need(id);
            kept = string.Join(' ', (label ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            kept = kept[..Math.Min(kept.Length, LabelMax)];
            Save(clip with { Label = kept });
        }

        OnChanged();
        return kept;
    }

    /// <summary>Deletes a copy; <see cref="Restore"/> can bring it back for a short while. False when it wasn't there.</summary>
    public bool Remove(string id)
    {
        lock (_gate)
        {
            var record = store.Get(id);
            if (record is null)
            {
                return false;
            }

            Discard(record);
        }

        OnChanged();
        return true;
    }

    /// <summary>
    /// Undoes <see cref="Remove"/>, once. When the same text was copied again meanwhile, that copy stays and its id is
    /// returned.
    /// </summary>
    public string Restore(string id)
    {
        string restored;
        lock (_gate)
        {
            restored = Undiscard(id) ?? throw new LamhaException(NotFound);
        }

        OnChanged();
        return restored;
    }

    /// <summary>
    /// The copy's text becomes <paramref name="text"/>, in its place (Replace on Proofread's corrections or "Write it in
    /// English"): its pin, name, program and times stay, its formatting goes (it was the old text's), and
    /// <paramref name="cache"/> is what's kept of the tools' results. Another copy of the new text is merged into it,
    /// its pin and name kept. <see cref="UndoReplace"/> puts it all back.
    /// </summary>
    public ClipReplaced Replace(string id, string text, ClipCache cache)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(cache);
        ClipReplaced replaced;
        lock (_gate)
        {
            var clip = Need(id);
            var key = TextKey(text);
            var merged = store.FindByKey(key) is { } other && other.Id != id ? other : null;
            var label = clip.Label.Length > 0 || merged is null ? clip.Label : Open(merged)?.Label ?? "";
            if (merged is not null)
            {
                Discard(merged);
            }

            Save(
                clip with
                {
                    Text = text,
                    Html = null,
                    Lang = SearchText.DetectLanguage(text),
                    Label = label,
                    Pinned = clip.Pinned || merged?.Pinned == true,
                    Cache = cache,
                },
                key);
            replaced = new ClipReplaced(clip, text, merged?.Id);
        }

        OnChanged();
        return replaced;
    }

    /// <summary>
    /// Undoes <see cref="Replace"/>: the copy as it was, and the copy merged into it back. False when that's no longer
    /// possible: the copy was deleted, or its old text was copied again meanwhile.
    /// </summary>
    public bool UndoReplace(ClipReplaced replaced)
    {
        ArgumentNullException.ThrowIfNull(replaced);
        lock (_gate)
        {
            var before = replaced.Before;
            var key = TextKey(before.Text);
            if (Find(before.Id) is null || (store.FindByKey(key) is { } again && again.Id != before.Id))
            {
                return false;
            }

            Save(before, key);
            if (replaced.MergedId is { } merged)
            {
                Undiscard(merged);
            }
        }

        OnChanged();
        return true;
    }

    /// <summary>Deletes every copy, or every unpinned one. Returns how many are left.</summary>
    public int Clear(bool keepPinned = false)
    {
        int left;
        lock (_gate)
        {
            left = store.Clear(keepPinned);
            _trash.Clear();
            _index = null;
        }

        OnChanged();
        return left;
    }

    /// <summary>
    /// Deletes unpinned copies not copied or used for longer than the setting (0 days: kept for ever). Returns how
    /// many went.
    /// </summary>
    public int Expire()
    {
        var days = settings.Current.ClipboardExpiryDays;
        if (days <= 0)
        {
            return 0;
        }

        int gone;
        lock (_gate)
        {
            gone = store.ExpireUnpinned(Now - (long)TimeSpan.FromDays(days).TotalMilliseconds);
            if (gone > 0)
            {
                _index = null;
            }
        }

        if (gone > 0)
        {
            OnChanged();
        }

        return gone;
    }

    /// <summary>Keeps the unpinned copies to the setting's number (after it changed). Returns how many went.</summary>
    public int ApplyLimit()
    {
        int gone;
        lock (_gate)
        {
            gone = Trim();
        }

        if (gone > 0)
        {
            OnChanged();
        }

        return gone;
    }

    /// <summary>The programs seen in the history, most copies first.</summary>
    public IReadOnlyList<(string App, int Count)> Apps()
    {
        lock (_gate)
        {
            return store.Apps();
        }
    }

    /// <summary>Deletes every copy from <paramref name="app"/>, pinned ones too (the user asked to forget it).</summary>
    public int RemoveApp(string app)
    {
        int gone;
        lock (_gate)
        {
            gone = store.DeleteApp(app);
            if (gone > 0)
            {
                _index = null;
            }
        }

        if (gone > 0)
        {
            OnChanged();
        }

        return gone;
    }

    /// <summary>A copy was pasted or copied again from the history: it moves up.</summary>
    public void MarkUsed(string id)
    {
        lock (_gate)
        {
            var clip = Need(id);
            Save(clip with { LastUsedAt = Now, UseCount = clip.UseCount + 1 });
        }

        OnChanged();
    }

    /// <summary>Keeps the translation into <paramref name="targetLang"/> with the copy.</summary>
    public void SetTranslation(string id, string targetLang, string translation) => UpdateCache(id, cache =>
        cache with { Translation = new Dictionary<string, string>(cache.Translation) { [targetLang] = translation } });

    /// <summary>Keeps a tool's result with the copy (Lamha 2's <c>setCache</c>); a copy deleted meanwhile is left alone.</summary>
    public void UpdateCache(string id, Func<ClipCache, ClipCache> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (_gate)
        {
            if (Find(id) is not { } clip)
            {
                return;
            }

            Save(clip with { Cache = change(clip.Cache) });
        }

        OnChanged();
    }

    /// <summary>
    /// Forgets the decrypted copies kept for searching. The history window calls it when it closes, so nothing of
    /// what the user copied stays in memory while Lamha sits in the tray.
    /// </summary>
    public void Unload()
    {
        lock (_gate)
        {
            _index = null;
        }
    }

    /// <summary>Whether decrypted copies are in memory now (for tests).</summary>
    internal bool IsLoaded => _index is not null;

    private long Now => clock.GetUtcNow().ToUnixTimeMilliseconds();

    /// <summary>Deletes a copy, kept a while for <see cref="Undiscard"/>.</summary>
    private void Discard(ClipRecord record)
    {
        store.Delete(record.Id);
        _index?.Remove(record.Id);
        var now = Now;
        foreach (var old in _trash.Where(t => now - t.Value.At > UndoTime.TotalMilliseconds).Select(t => t.Key).ToList())
        {
            _trash.Remove(old);
        }

        _trash[record.Id] = (record, now);
    }

    /// <summary>A discarded copy back, once; the id of the copy of its text that's there (another, copied again meanwhile), or null when it's too late.</summary>
    private string? Undiscard(string id)
    {
        if (!_trash.Remove(id, out var t) || Now - t.At > UndoTime.TotalMilliseconds)
        {
            return null;
        }

        if (store.FindByKey(t.Record.TextKey) is { } again)
        {
            return again.Id;
        }

        store.Put(t.Record);
        if (_index is not null && Open(t.Record) is { } clip)
        {
            _index[id] = new Entry(clip);
        }

        return id;
    }

    /// <summary>Unpinned copies beyond the setting go, the least recent first.</summary>
    private int Trim()
    {
        var gone = store.TrimUnpinned(Math.Max(1, settings.Current.ClipboardMaxItems));
        if (gone > 0)
        {
            _index = null;
        }

        return gone;
    }

    /// <summary>Time-sortable unique ids: c_ + milliseconds (base 36, 9 places) + a counter within the millisecond + noise.</summary>
    private string NewId()
    {
        var now = Now;
        _sameMs = now == _lastMs ? _sameMs + 1 : 0;
        _lastMs = now;
        return "c_" + Base36(now, 9) + Base36(_sameMs, 3) + Base36(RandomNumberGenerator.GetInt32(36 * 36 * 36), 3);
    }

    /// <summary>An id as <see cref="NewId"/> makes them (Lamha 2's too): "c_" and 15 small letters or digits.</summary>
    private static bool IsId(string id) =>
        id.Length == 17 && id.StartsWith("c_", StringComparison.Ordinal) && !id.AsSpan(2).ContainsAnyExcept("0123456789abcdefghijklmnopqrstuvwxyz");

    private static string Base36(long value, int width)
    {
        const string Digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        Span<char> buffer = stackalloc char[16];
        var i = buffer.Length;
        do
        {
            buffer[--i] = Digits[(int)(value % 36)];
            value /= 36;
        }
        while (value > 0);

        return new string(buffer[i..]).PadLeft(width, '0');
    }

    /// <summary>The key a copy is found by again: an HMAC of its trimmed text, with a key only this user can read.</summary>
    private byte[] TextKey(string text) => HMACSHA256.HashData(HashKey(), Encoding.UTF8.GetBytes(text.Trim()));

    private byte[] HashKey()
    {
        if (_hashKey is not null)
        {
            return _hashKey;
        }

        if (state.Get(KeyName) is { } saved && protector.Unprotect(Convert.FromBase64String(saved)) is { Length: 32 } key)
        {
            return _hashKey = key;
        }

        key = RandomNumberGenerator.GetBytes(32);
        state.Set(KeyName, Convert.ToBase64String(protector.Protect(key)));
        return _hashKey = key;
    }

    private Dictionary<string, Entry> Index()
    {
        if (_index is not null)
        {
            return _index;
        }

        var index = new Dictionary<string, Entry>();
        foreach (var record in store.All())
        {
            // A copy this Windows account can't decrypt is left alone: it can't be shown, and it isn't ours to delete.
            if (Open(record) is { } clip)
            {
                index[clip.Id] = new Entry(clip);
            }
        }

        return _index = index;
    }

    private Clip? Find(string id)
    {
        if (_index is not null)
        {
            return _index.TryGetValue(id, out var e) ? e.Clip : null;
        }

        return store.Get(id) is { } record ? Open(record) : null;
    }

    private Clip Need(string id) => Find(id) ?? throw new LamhaException(NotFound);

    private Clip? Open(ClipRecord record)
    {
        if (protector.Unprotect(record.Body) is not { } bytes)
        {
            return null;
        }

        ClipBody? body;
        try
        {
            body = JsonSerializer.Deserialize(bytes, ClipJson.Default.ClipBody);
        }
        catch (JsonException)
        {
            return null;
        }

        return body is null ? null : new Clip
        {
            Id = record.Id,
            Text = body.Text,
            Html = body.Html,
            Label = body.Label,
            Cache = body.Cache ?? new ClipCache(),
            SourceApp = record.SourceApp,
            Lang = record.Lang,
            CreatedAt = record.CreatedAt,
            LastCopiedAt = record.LastCopiedAt,
            LastUsedAt = record.LastUsedAt,
            CopyCount = record.CopyCount,
            UseCount = record.UseCount,
            Pinned = record.Pinned,
        };
    }

    private void Save(Clip clip, byte[]? textKey = null)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new ClipBody(clip.Text, clip.Html, clip.Label, clip.Cache), ClipJson.Default.ClipBody);
        store.Put(new ClipRecord(
            clip.Id, textKey ?? TextKey(clip.Text), protector.Protect(body), clip.SourceApp, clip.Lang,
            clip.CreatedAt, clip.LastCopiedAt, clip.LastUsedAt, clip.CopyCount, clip.UseCount, clip.Pinned));
        if (_index is not null)
        {
            _index[clip.Id] = new Entry(clip);
        }
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>-1 when a word is missing; otherwise +100 for the whole query, +10 per word starting a word, +30 in the label.</summary>
    private static int Score(Entry e, string q, string[] tokens)
    {
        foreach (var t in tokens)
        {
            if (!e.All.Contains(t, StringComparison.Ordinal))
            {
                return -1;
            }
        }

        var s = e.All.Contains(q, StringComparison.Ordinal) ? 100 : 0;
        foreach (var t in tokens)
        {
            for (var i = e.All.IndexOf(t, StringComparison.Ordinal); i != -1; i = e.All.IndexOf(t, i + 1, StringComparison.Ordinal))
            {
                if (IsWordStart(e.All, i))
                {
                    s += 10;
                    break;
                }
            }
        }

        if (e.Label.Length > 0 && tokens.All(t => e.Label.Contains(t, StringComparison.Ordinal)))
        {
            s += 30;
        }

        return s;
    }

    private static bool IsWordStart(string s, int i) =>
        i == 0 || char.GetUnicodeCategory(s[i - 1]) is not (
            UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or UnicodeCategory.DecimalDigitNumber
            or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber);

    private static ClipPreview Preview(Clip c, string targetLang) => new(
        c.Id, c.Text[..Math.Min(c.Text.Length, PreviewChars)], c.Text.Length, c.Html is not null, c.Label, c.Pinned,
        c.SourceApp, c.Lang, c.CreatedAt, c.LastCopiedAt, c.LastUsedAt, c.CopyCount, c.UseCount,
        c.Lang == "en" && c.Cache.Translation.TryGetValue(targetLang, out var tr) ? tr[..Math.Min(tr.Length, 200)]
        : c.Lang is "ar" or "mixed" && c.Cache.English is { } en ? en[..Math.Min(en.Length, 200)]
        : "");

    /// <summary>
    /// A copy with its normalized text for searching. The label, the text and the program are joined by newlines,
    /// which normalized words never contain, so a match can't straddle two of them.
    /// </summary>
    private sealed class Entry(Clip clip)
    {
        public Clip Clip { get; } = clip;

        public string Label { get; } = SearchText.Normalize(clip.Label);

        public string All { get; } = SearchText.Normalize(clip.Label) + "\n"
            + SearchText.Normalize(clip.Text[..Math.Min(clip.Text.Length, IndexChars)]) + "\n"
            + SearchText.Normalize(clip.SourceApp);
    }

    /// <summary>The most recent first; the same moment, the newer id first.</summary>
    private sealed class RecencyOrder : IComparer<Clip>
    {
        public static readonly RecencyOrder Instance = new();

        public int Compare(Clip? a, Clip? b)
        {
            ArgumentNullException.ThrowIfNull(a);
            ArgumentNullException.ThrowIfNull(b);
            var byTime = Math.Max(b.LastCopiedAt, b.LastUsedAt).CompareTo(Math.Max(a.LastCopiedAt, a.LastUsedAt));
            return byTime != 0 ? byTime : string.CompareOrdinal(b.Id, a.Id);
        }
    }

    /// <summary>Pinned first, then the most recent.</summary>
    private sealed class ListOrder : IComparer<Clip>
    {
        public static readonly ListOrder Instance = new();

        public int Compare(Clip? a, Clip? b)
        {
            ArgumentNullException.ThrowIfNull(a);
            ArgumentNullException.ThrowIfNull(b);
            return a.Pinned != b.Pinned ? (a.Pinned ? -1 : 1) : RecencyOrder.Instance.Compare(a, b);
        }
    }
}

/// <summary>What's encrypted of a copy.</summary>
internal sealed record ClipBody(string Text, string? Html, string Label, ClipCache? Cache);

/// <summary>Clipboard history's JSON, without reflection (NativeAOT).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ClipBody))]
internal sealed partial class ClipJson : JsonSerializerContext;
