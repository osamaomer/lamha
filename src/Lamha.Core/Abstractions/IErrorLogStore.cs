namespace Lamha.Core.Abstractions;

/// <summary>A failure noted for Report a problem: when, where, and the error's code. Never the text involved.</summary>
public sealed record ErrorLogEntry(long Time, string Where, string Code);

/// <summary>The latest failures, newest first.</summary>
public interface IErrorLogStore
{
    void Add(ErrorLogEntry entry, int keep);

    IReadOnlyList<ErrorLogEntry> Recent();
}
