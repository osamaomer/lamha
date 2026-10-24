namespace Lamha.Core.Abstractions;

/// <summary>
/// One copy in clipboard history as it's kept on disk. What was copied (text, formatting, label, cached results) is in
/// <see cref="Body"/>, encrypted by Windows for this user; only what sorting and the "programs" list need is plain.
/// </summary>
/// <param name="Id">Time-sortable: "c_" + the time + a counter + noise.</param>
/// <param name="TextKey">A keyed hash of the trimmed text, to find a copy made again without decrypting anything.</param>
/// <param name="Body">The encrypted <c>ClipBody</c>.</param>
/// <param name="SourceApp">The program it was copied from ("notepad.exe").</param>
/// <param name="Lang">"ar" | "en" | "mixed" | "other".</param>
/// <param name="CreatedAt">First copied.</param>
/// <param name="LastCopiedAt">Last copied (the same text copied again).</param>
/// <param name="LastUsedAt">Last pasted or copied from the history; 0 when never.</param>
/// <param name="CopyCount">Times it was copied.</param>
/// <param name="UseCount">Times it was used from the history.</param>
/// <param name="Pinned">Kept on top, and never trimmed or expired.</param>
public sealed record ClipRecord(
    string Id,
    byte[] TextKey,
    byte[] Body,
    string SourceApp,
    string Lang,
    long CreatedAt,
    long LastCopiedAt,
    long LastUsedAt,
    int CopyCount,
    int UseCount,
    bool Pinned)
{
    /// <summary>When it was last copied or used: what "recent" means in the list.</summary>
    public long Activity => Math.Max(LastCopiedAt, LastUsedAt);
}

/// <summary>Clipboard history on disk. Times are milliseconds since 1970 (UTC).</summary>
public interface IClipStore
{
    ClipRecord? Get(string id);

    ClipRecord? FindByKey(byte[] textKey);

    /// <summary>Every copy (for search, which needs their text).</summary>
    IReadOnlyList<ClipRecord> All();

    /// <summary>Adds or replaces the copy under its id.</summary>
    void Put(ClipRecord record);

    bool Delete(string id);

    int CountPinned();

    /// <summary>Deletes the oldest unpinned copies beyond the newest <paramref name="keep"/>; returns how many went.</summary>
    int TrimUnpinned(int keep);

    /// <summary>Deletes unpinned copies last copied or used before <paramref name="before"/>; returns how many went.</summary>
    int ExpireUnpinned(long before);

    /// <summary>Deletes every copy from <paramref name="app"/>, pinned ones too; returns how many went.</summary>
    int DeleteApp(string app);

    /// <summary>Deletes every copy, or every unpinned one; returns how many are left.</summary>
    int Clear(bool keepPinned);

    /// <summary>The programs copies came from, with how many each, most first.</summary>
    IReadOnlyList<(string App, int Count)> Apps();
}

/// <summary>Encryption only this Windows user can undo (DPAPI): clipboard history's contents.</summary>
public interface IDataProtector
{
    byte[] Protect(byte[] data);

    /// <summary>The data, or null when it can't be decrypted here (another Windows account, or damaged).</summary>
    byte[]? Unprotect(byte[] data);
}
