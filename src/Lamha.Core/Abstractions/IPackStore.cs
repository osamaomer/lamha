using Lamha.Core.Dictionary;

namespace Lamha.Core.Abstractions;

/// <summary>What a language pack says about itself: Lamha 2's pack <c>meta</c> (tools/build_packs.py).</summary>
/// <param name="Lang">Its language ("fr").</param>
/// <param name="Version">When its words were taken from Wiktionary ("2026-09-28").</param>
/// <param name="Words">Its headwords.</param>
public sealed record PackMeta(string Lang, string Version, int Words);

/// <summary>An installed language pack's words, read from disk when they're looked up.</summary>
public interface IPackData : IDisposable
{
    PackMeta Meta { get; }

    /// <summary>The headword exactly as written ("Haus", "maison"), or null.</summary>
    DictionaryEntry? Entry(string word);

    /// <summary>The headword an inflected form belongs to ("evler" → "ev"), or null.</summary>
    string? Lemma(string form);
}

/// <summary>
/// Where downloaded language packs are kept: Lamha 2's desktop/pack-store.js (a folder of files). A pack is received
/// beside the installed ones and checked first, so a download cut short or damaged never replaces a pack that works.
/// </summary>
public interface IPackStore
{
    /// <summary>The installed packs, opened; a damaged one, or one in another format, isn't.</summary>
    IReadOnlyList<IPackData> Open();

    /// <summary>
    /// Writes a downloaded pack (gzip) for <paramref name="lang"/> and checks it, without installing it yet. Throws
    /// <see cref="InvalidDataException"/> when it isn't a pack for that language in the format Lamha reads.
    /// </summary>
    Task ReceiveAsync(string lang, Stream gzip, CancellationToken cancel = default);

    /// <summary>Installs the pack received for <paramref name="lang"/> in place of the one before (closed first).</summary>
    IPackData Install(string lang);

    /// <summary>Deletes <paramref name="lang"/>'s pack (closed first).</summary>
    void Remove(string lang);
}
