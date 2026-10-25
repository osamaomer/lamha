using System.Text.RegularExpressions;
using Lamha.Core.Abstractions;

namespace Lamha.Core.Diagnostics;

/// <summary>
/// What went wrong lately, for Report a problem: the last 30 failures, when, where (lookup, ai…) and the error's code.
/// Never the text involved: a message that isn't one of Lamha's codes is cut short with anything in quotes taken out
/// (Lamha 2's <c>noteError</c> / <c>errorCode</c>).
/// </summary>
public sealed partial class ErrorLog(IErrorLogStore store, TimeProvider time)
{
    public const int Keep = 30;

    public void Note(string where, Exception error) => store.Add(new ErrorLogEntry(time.GetUtcNow().ToUnixTimeMilliseconds(), where, CodeOf(error)), Keep);

    public IReadOnlyList<ErrorLogEntry> Recent() => store.Recent();

    /// <summary>An error's code: Lamha's own, or the exception's type. Never a message: it may quote the user's text.</summary>
    public static string CodeOf(Exception error) => error is LamhaException e ? Code(e.Code) : error.GetType().Name;

    /// <summary>"ai_error:&lt;the provider's words&gt;" → "ai_error"; anything quoted taken out; at most 80 characters.</summary>
    public static string Code(string message)
    {
        var s = Quoted().Replace(message, "…");
        if (CodePrefix().IsMatch(s))
        {
            s = s.Split(':')[0];
        }

        return s.Length > 80 ? s[..80] : s;
    }

    [GeneratedRegex("\"[^\"]*\"|'[^']*'|“[^”]*”")]
    private static partial Regex Quoted();

    [GeneratedRegex(@"^[\w.-]+(:|$)")]
    private static partial Regex CodePrefix();
}
