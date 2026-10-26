using System.Text.Json.Nodes;

namespace Lamha.Core.Import;

/// <summary>
/// What Lamha 2 kept on this PC, as read from its folder by Data's <c>Lamha2Folder</c>: its two storage areas as they
/// were on disk (desktop/storage.js), with the API keys decrypted, and its clipboard history decrypted. Everything in
/// it is untrusted: it's read field by field, each checked for its kind.
/// </summary>
/// <param name="Sync">storage-sync.json: the settings (background.js <c>DEFAULT_SETTINGS</c>).</param>
/// <param name="Local">
/// storage-local.json: this PC's settings, the deck, the history, the journal, and the keys (<c>aiKey</c>,
/// <c>geminiKey</c>) as plain strings, or left out when Windows couldn't decrypt them.
/// </param>
/// <param name="Clips">clipboard.json's copies (desktop/clipboard-store.js), decrypted.</param>
/// <param name="KeysLost">A key was there but couldn't be decrypted (the folder came from another Windows account).</param>
/// <param name="ClipsLost">Clipboard history was there but couldn't be decrypted.</param>
/// <param name="WikipediaFiles">Downloaded Wikipedias (wikipedia.json): they stay where they are.</param>
public sealed record Lamha2Data(JsonObject Sync, JsonObject Local, JsonArray Clips, bool KeysLost, bool ClipsLost, int WikipediaFiles);

/// <summary>What first run's Lamha 2 step lists before anything moves.</summary>
/// <param name="Cards">Review cards.</param>
/// <param name="History">Words in the history.</param>
/// <param name="Mistakes">Mistakes in the journal.</param>
/// <param name="Clips">Copies in clipboard history.</param>
public sealed record Lamha2Counts(int Cards, int History, int Mistakes, int Clips);

/// <summary>What Lamha 2 had that Lamha 3 doesn't, named after the move so nothing seems to vanish.</summary>
public enum Lamha2Dropped
{
    /// <summary>The Firefox extension and what only it did: per-site switches, the buttons on pages, page translation, the link to the app.</summary>
    Firefox,

    /// <summary>Downloaded Wikipedias, paused in 3.0; the files stay on disk for when it returns.</summary>
    OfflineWikipedia,
}

/// <summary>What moved.</summary>
/// <param name="Counts">What was there to move (the step's list).</param>
/// <param name="Keys">The providers whose API keys moved ("claude", "gemini").</param>
/// <param name="KeysLost">A key couldn't be read: it's asked for again.</param>
/// <param name="ClipsLost">Clipboard history couldn't be read.</param>
/// <param name="Dropped">What Lamha 3 doesn't have, of what this user used.</param>
public sealed record Lamha2Summary(Lamha2Counts Counts, IReadOnlyList<string> Keys, bool KeysLost, bool ClipsLost, IReadOnlyList<Lamha2Dropped> Dropped);
