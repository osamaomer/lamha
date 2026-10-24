namespace Lamha.Core.Net;

/// <summary>How a request failed.</summary>
public enum HttpFailureKind
{
    /// <summary>The server answered with an error status (<see cref="HttpFailure.Status"/>).</summary>
    Status,

    /// <summary>
    /// Nothing could be reached: no network, a name that doesn't resolve, a refused connection. Lamha 2 saw this as
    /// fetch's <c>TypeError</c>.
    /// </summary>
    Network,

    /// <summary>No answer in time (Lamha 2's <c>AbortError</c>).</summary>
    Timeout,

    /// <summary>An answer that isn't the JSON asked for.</summary>
    Invalid,
}

/// <summary>A request that failed, and how.</summary>
public sealed class HttpFailure : Exception
{
    public HttpFailure(HttpFailureKind kind, int status = 0, string body = "", Exception? inner = null)
        : base(kind == HttpFailureKind.Status ? "HTTP " + status : kind.ToString(), inner)
    {
        Kind = kind;
        Status = status;
        Body = body;
    }

    public HttpFailure()
        : this(HttpFailureKind.Network)
    {
    }

    public HttpFailure(string message)
        : this(HttpFailureKind.Network, 0, "", new InvalidOperationException(message))
    {
    }

    public HttpFailure(string message, Exception innerException)
        : this(HttpFailureKind.Network, 0, "", innerException)
    {
    }

    public HttpFailureKind Kind { get; }

    /// <summary>The HTTP status when <see cref="Kind"/> is <see cref="HttpFailureKind.Status"/>, else 0.</summary>
    public int Status { get; }

    /// <summary>The error answer's body, for the provider's own message. Never logged.</summary>
    public string Body { get; }
}
