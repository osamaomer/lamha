namespace Lamha.Core.Abstractions;

/// <summary>
/// Small named values kept on this PC: the settings (as JSON), counters, the word of the day. Each value is read and
/// written whole, so only small things belong here; anything that grows has its own store.
/// </summary>
public interface IStateStore
{
    /// <summary>The value under <paramref name="key"/>, or null.</summary>
    string? Get(string key);

    /// <summary>Keeps <paramref name="value"/> under <paramref name="key"/>; null removes it.</summary>
    void Set(string key, string? value);
}
