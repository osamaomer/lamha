namespace Lamha.Core.Dictionary;

/// <summary>A word found in a language pack: its headword, and the form it was found as ("evler" → "ev").</summary>
public sealed record PackHit(string Word, string Form, DictionaryEntry Entry);

/// <summary>
/// The downloaded language packs (French–French, German–German, Spanish–Spanish, Turkish–Turkish): Lamha 2's
/// packs.js. Downloading them comes in phase 5; lookups ask them through this.
/// </summary>
public interface ILanguagePacks
{
    /// <summary>The installed packs' languages.</summary>
    IReadOnlyList<string> Languages { get; }

    /// <summary>Changes when packs are installed or removed: part of a lookup's cache key.</summary>
    string Key { get; }

    /// <summary>The word (any capitalisation, or one of its forms) in <paramref name="lang"/>'s pack, or null.</summary>
    PackHit? Find(string lang, string text);
}

/// <summary>No packs installed.</summary>
public sealed class NoLanguagePacks : ILanguagePacks
{
    public IReadOnlyList<string> Languages => [];

    public string Key => "";

    public PackHit? Find(string lang, string text) => null;
}
