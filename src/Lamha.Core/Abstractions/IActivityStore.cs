namespace Lamha.Core.Abstractions;

/// <summary>Each day's practice: words new to the history plus review answers (Lamha 2's <c>activity</c>).</summary>
public interface IActivityStore
{
    /// <summary>Counts one for <paramref name="day"/>, forgets days before <paramref name="oldestKept"/>, and returns the day's count.</summary>
    int Add(DateOnly day, DateOnly oldestKept);

    int Get(DateOnly day);

    IReadOnlyDictionary<DateOnly, int> All();

    /// <summary>Raises <paramref name="day"/>'s count to <paramref name="count"/> if it's lower (restoring a backup).</summary>
    void RaiseTo(DateOnly day, int count);
}
