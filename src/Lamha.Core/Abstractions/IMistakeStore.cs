namespace Lamha.Core.Abstractions;

/// <summary>One mistake proofreading found (Lamha 2's <c>{ cat, original, fix, why, t }</c>).</summary>
public sealed record MistakeEntry(string Category, string Original, string Fix, string Why, long Time);

/// <summary>The mistake journal: how many texts were checked, mistakes by kind, and the latest ones, newest first.</summary>
public sealed record MistakeJournal(int Checks, IReadOnlyDictionary<string, int> Counts, IReadOnlyList<MistakeEntry> Recent, long Since)
{
    public static MistakeJournal Empty(long since) => new(0, new Dictionary<string, int>(), [], since);
}

/// <summary>Where the mistake journal is kept.</summary>
public interface IMistakeStore
{
    /// <summary>
    /// One proofread: counts the check and each mistake's kind, and puts its mistakes (in text order) before the older
    /// ones, keeping the latest <paramref name="keepRecent"/>.
    /// </summary>
    void Record(IReadOnlyList<MistakeEntry> mistakes, long now, int keepRecent);

    MistakeJournal Read();

    /// <summary>Replaces the whole journal (restoring a backup).</summary>
    void Replace(MistakeJournal journal);
}
