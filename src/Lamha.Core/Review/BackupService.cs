using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lamha.Core.Abstractions;
using Lamha.Core.Writing;

namespace Lamha.Core.Review;

/// <summary>What a restore added.</summary>
public sealed record RestoreResult(int Cards, int History);

/// <summary>
/// Settings → save a copy and restore: the deck, the history, the mistake journal and the days' practice in one file,
/// in Lamha 2's format (<c>{ app: "lamha", kind: "backup", format: 1, … }</c>), so its copies restore here too. A
/// restore merges: nothing here is lost, the copy reviewed last keeps its schedule, and a removed word stays removed.
/// Everything in a file is untrusted: each card is rebuilt from the fields and kinds a card has.
/// </summary>
public sealed class BackupService(
    ReviewService review,
    IDeckStore deck,
    IHistoryStore history,
    IMistakeStore mistakes,
    IActivityStore activity,
    IStateStore state,
    TodayService today,
    TimeProvider time,
    string appVersion)
{
    /// <summary>Lamha 2 kept days as JavaScript's <c>toDateString()</c>: "Sat Oct 24 2026".</summary>
    private const string DayFormat = "ddd MMM dd yyyy";

    private static readonly JsonSerializerOptions Readable = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task<string> ExportAsync()
    {
        var (cards, removed, stats) = await review.Locked(() => (deck.All(), deck.AllRemoved(), review.Stats())).ConfigureAwait(false);
        var journal = mistakes.Read();
        var backup = new JsonObject
        {
            ["app"] = "lamha",
            ["kind"] = "backup",
            ["format"] = 1,
            ["version"] = appVersion,
            ["exportedAt"] = Days.NowMs(time),
            ["cards"] = new JsonObject(cards.Select(c => KeyValuePair.Create(c.Key, (JsonNode?)CardJson(c)))),
            ["cardsRemoved"] = new JsonObject(removed.Select(r => KeyValuePair.Create(r.Key, (JsonNode?)r.Value))),
            ["cardStats"] = new JsonObject
            {
                ["day"] = stats.Day?.ToString(DayFormat, CultureInfo.InvariantCulture),
                ["newSeen"] = stats.NewSeen,
                ["reviewDay"] = stats.ReviewDay?.ToString(DayFormat, CultureInfo.InvariantCulture),
                ["streak"] = stats.Streak,
            },
            ["history"] = new JsonArray([.. history.Recent(HistoryService.Keep).Select(h => (JsonNode)new JsonObject
            {
                ["q"] = h.Word, ["tr"] = h.Meaning, ["src"] = h.Language, ["t"] = h.Time,
            })]),
            ["mistakes"] = new JsonObject
            {
                ["checks"] = journal.Checks,
                ["counts"] = new JsonObject(journal.Counts.Select(c => KeyValuePair.Create(c.Key, (JsonNode?)c.Value))),
                ["recent"] = new JsonArray([.. journal.Recent.Select(m => (JsonNode)new JsonObject
                {
                    ["cat"] = m.Category, ["original"] = m.Original, ["fix"] = m.Fix, ["why"] = m.Why, ["t"] = m.Time,
                })]),
                ["since"] = journal.Since,
            },
            ["activity"] = new JsonObject(activity.All().Select(a => KeyValuePair.Create(a.Key.ToString(DayFormat, CultureInfo.InvariantCulture), (JsonNode?)a.Value))),
            ["lookupCount"] = today.LookupCount,
        };
        return backup.ToJsonString(Readable);
    }

    /// <summary>A saved copy merged with what's here. Throws "not_backup" for anything else.</summary>
    public async Task<RestoreResult> ImportAsync(string json)
    {
        JsonObject data;
        try
        {
            data = JsonNode.Parse(json) as JsonObject ?? throw new LamhaException(ErrorCodes.NotBackup);
        }
        catch (JsonException)
        {
            throw new LamhaException(ErrorCodes.NotBackup);
        }

        if (Str(data["app"]) != "lamha" || Num(data["format"]) != 1)
        {
            throw new LamhaException(ErrorCodes.NotBackup);
        }

        var cards = await review.Locked(() => deck.Batch(() => Merge(data["cards"] as JsonObject, data["cardsRemoved"] as JsonObject))).ConfigureAwait(false);
        review.OnChanged();

        // History: both lists, newest first, the last HistoryService.Keep words (Lamha 2's 100).
        var added = 0;
        if (data["history"] is JsonArray list)
        {
            var entries = list.OfType<JsonObject>()
                .Where(h => h["q"] is JsonValue q && q.TryGetValue<string>(out var word) && word.Length <= 100)
                .Select(h => new HistoryEntry(Str(h["q"]), Cut(Str(h["tr"]), 200), Cut(Str(h["src"]), 8), (long)(Num(h["t"]) ?? 0)))
                .ToList();
            added = history.Merge(entries, HistoryService.Keep);
        }

        // The journal and the days' practice: whichever copy has seen more (adding them up would count twice on a second restore).
        if (data["mistakes"] is JsonObject m && Num(m["checks"]) is { } checks && checks > mistakes.Read().Checks)
        {
            var counts = (m["counts"] as JsonObject ?? [])
                .Where(c => MistakeCategories.IsKnown(c.Key) && Num(c.Value) is >= 0)
                .ToDictionary(c => c.Key, c => (int)Num(c.Value)!.Value);
            var recent = (m["recent"] as JsonArray ?? []).OfType<JsonObject>()
                .Where(r => MistakeCategories.IsKnown(Str(r["cat"])))
                .Take(200)
                .Select(r => new MistakeEntry(Str(r["cat"]), Cut(Str(r["original"]), 200), Cut(Str(r["fix"]), 200), Cut(Str(r["why"]), 400), (long)(Num(r["t"]) ?? 0)))
                .ToList();
            mistakes.Replace(new MistakeJournal((int)checks, counts, recent, Num(m["since"]) is { } since ? (long)since : Days.NowMs(time)));
        }

        foreach (var (day, value) in data["activity"] as JsonObject ?? [])
        {
            if (DateOnly.TryParseExact(day, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && Num(value) is > 0 and var n)
            {
                activity.RaiseTo(date, (int)Math.Min(n, int.MaxValue));
            }
        }

        if (Num(data["lookupCount"]) is { } count && count > today.LookupCount)
        {
            state.Set("lookupCount", ((long)count).ToString(CultureInfo.InvariantCulture));
        }

        return new RestoreResult(cards, added);
    }

    /// <summary>
    /// Merges cards and removals from elsewhere into the deck; returns how many words changed (Lamha 2's
    /// <c>mergeCards</c>). A word in both keeps the schedule of the copy reviewed last, its text fields filled from
    /// either; a word removed after its last change stays removed.
    /// </summary>
    private int Merge(JsonObject? incoming, JsonObject? removedIn)
    {
        var changed = 0;
        var now = Days.NowMs(time);
        foreach (var (key, value) in removedIn ?? [])
        {
            if (key != Card.KeyOf(key) || key.Length > 100 || Num(value) is not { } at || !(at > 0 && at < now + Scheduler.Day))
            {
                continue;
            }

            var when = (long)at;
            if (!(deck.RemovedAt(key) >= when))
            {
                deck.SetRemoved(key, when);
            }

            if (deck.Get(key) is { } card && card.ChangedAt <= when)
            {
                deck.Delete(key);
                changed++;
            }
        }

        foreach (var (_, raw) in incoming ?? [])
        {
            if (Clean(raw as JsonObject) is not { } card)
            {
                continue;
            }

            var key = card.Key;
            if (deck.RemovedAt(key) >= card.ChangedAt)
            {
                continue; // removed after its last change
            }

            var old = deck.Get(key);
            if (old is null)
            {
                deck.Put(card);
                deck.ForgetRemoved(key);
                changed++;
                continue;
            }

            var schedule = (card.Last ?? 0) > (old.Last ?? 0) ? card : old; // the schedule of the copy reviewed last
            var merged = old with
            {
                Added = schedule.Added,
                Due = schedule.Due,
                Interval = schedule.Interval,
                Ease = schedule.Ease,
                Reps = schedule.Reps,
                Lapses = schedule.Lapses,
                Last = schedule.Last,
                Meaning = old.Meaning.Length > 0 ? old.Meaning : card.Meaning,
                Example = old.Example.Length > 0 ? old.Example : card.Example,
                Form = old.Form.Length > 0 ? old.Form : card.Form,
                Definition = old.Definition.Length > 0 ? old.Definition : card.Definition,
                EnglishFirst = old.EnglishFirst || card.EnglishFirst,
                Modified = Math.Max(old.Modified, card.Modified),
            };
            if (merged != old)
            {
                deck.Put(merged);
                changed++;
            }
        }

        return changed;
    }

    /// <summary>A card from outside, rebuilt with the fields and kinds a card has, or null (Lamha 2's <c>cleanCard</c>).</summary>
    internal static Card? Clean(JsonObject? raw)
    {
        if (raw?["q"] is not JsonValue qv || !qv.TryGetValue<string>(out var q) || q.Length > 100 || Card.KeyOf(q).Length == 0)
        {
            return null;
        }

        static long Whole(JsonNode? n) => Num(n) is { } v && v >= 0 ? (long)Math.Min(v, long.MaxValue / 2) : 0;
        var card = new Card
        {
            Word = q.Trim(),
            Meaning = Cut(Str(raw["tr"]), 200),
            Example = Cut(Str(raw["ex"]), 300),
            Form = Cut(Str(raw["form"]), 100),
            Definition = Cut(Str(raw["def"]), 300),
            Added = Whole(raw["added"]),
            Due = Whole(raw["due"]),
            Interval = (int)Math.Min(Math.Round(Num(raw["interval"]) is { } i && i >= 0 ? i : 0), int.MaxValue),
            Ease = Num(raw["ease"]) is { } ease ? Math.Clamp(ease, 1.3, 5) : 2.5,
            Reps = (int)Math.Min(Whole(raw["reps"]), int.MaxValue),
            Lapses = (int)Math.Min(Whole(raw["lapses"]), int.MaxValue),
            Modified = Whole(raw["mod"]),
            Last = Whole(raw["last"]) is > 0 and var last ? last : null,
            EnglishFirst = raw["en"] is JsonValue en && en.TryGetValue<bool>(out var b) && b,
        };
        return card.Meaning.Length > 0 || (card.EnglishFirst && card.Definition.Length > 0) ? card : null;
    }

    /// <summary>A card in Lamha 2's shape.</summary>
    private static JsonObject CardJson(Card c)
    {
        var o = new JsonObject
        {
            ["q"] = c.Word, ["tr"] = c.Meaning, ["ex"] = c.Example, ["form"] = c.Form, ["def"] = c.Definition,
            ["added"] = c.Added, ["due"] = c.Due, ["interval"] = c.Interval, ["ease"] = c.Ease, ["reps"] = c.Reps,
            ["lapses"] = c.Lapses, ["mod"] = c.Modified,
        };
        if (c.Last is { } last)
        {
            o["last"] = last;
        }

        if (c.EnglishFirst)
        {
            o["en"] = true;
        }

        return o;
    }

    private static string Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    /// <summary>A finite number, or null.</summary>
    private static double? Num(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) && double.IsFinite(d) ? d : null;

    private static string Cut(string s, int max) => s.Length > max ? s[..max] : s;
}
