namespace Lamha.Core.Review;

/// <summary>
/// A word in the review deck. Times are milliseconds since 1970 (UTC), as Lamha 2 kept them, so its decks and backups
/// carry over unchanged. A card is new until its first review (<see cref="Last"/> is null). Lamha 2's card fields:
/// q, tr, ex, form, def, en, added, due, interval, ease, reps, lapses, last, mod.
/// </summary>
public sealed record Card
{
    /// <summary>The word as it was looked up.</summary>
    public required string Word { get; init; }

    /// <summary>Its meaning in the translation language (Lamha 2's <c>tr</c>).</summary>
    public string Meaning { get; init; } = "";

    /// <summary>The sentence it was found in (<c>ex</c>).</summary>
    public string Example { get; init; } = "";

    /// <summary>The form the user selected, e.g. "mentioned" for "mention" (<c>form</c>).</summary>
    public string Form { get; init; } = "";

    /// <summary>The first definition (<c>def</c>).</summary>
    public string Definition { get; init; } = "";

    /// <summary>
    /// Review shows the English definition first and the meaning under it (<c>en</c>): a card from the English–English
    /// dictionary, which may have only the definition.
    /// </summary>
    public bool EnglishFirst { get; init; }

    public long Added { get; init; }

    public long Due { get; init; }

    /// <summary>Days until the next review after the last one.</summary>
    public int Interval { get; init; }

    public double Ease { get; init; } = 2.5;

    /// <summary>Successful reviews in a row.</summary>
    public int Reps { get; init; }

    /// <summary>Times it was forgotten.</summary>
    public int Lapses { get; init; }

    /// <summary>When it was last reviewed; null while it's new.</summary>
    public long? Last { get; init; }

    /// <summary>When it last changed: added, reviewed, or a field filled in (backups merge by it).</summary>
    public long Modified { get; init; }

    /// <summary>The deck's key for the word.</summary>
    public string Key => KeyOf(Word);

    public bool IsNew => Last is null;

    /// <summary>When the card last changed, for merging: added, reviewed, or a field filled in (Lamha 2's <c>cardTime</c>).</summary>
    public long ChangedAt => Math.Max(Modified, Math.Max(Last ?? 0, Added));

    /// <summary>A word's key in the deck: trimmed, in small letters (Lamha 2's <c>cardKey</c>).</summary>
    public static string KeyOf(string? word) => (word ?? "").Trim().ToLowerInvariant();
}
