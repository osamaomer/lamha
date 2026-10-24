using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;

namespace Lamha.Core.Tests.Support;

/// <summary>A request the fake network received.</summary>
public sealed class Call(string url, IReadOnlyDictionary<string, string> headers, string body)
{
    public string Url { get; } = url;

    public IReadOnlyDictionary<string, string> Headers { get; } = headers;

    public string Body { get; } = body;

    /// <summary>The body as JSON (the AI APIs), or null.</summary>
    public JsonNode? Json
    {
        get
        {
            try
            {
                return Body.Length > 0 ? JsonNode.Parse(Body) : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    /// <summary>A form field (Google's POST bodies) or, failing that, a query parameter.</summary>
    public IReadOnlyList<string> Param(string name)
    {
        var form = HttpUtility.ParseQueryString(Body.Contains('{', StringComparison.Ordinal) ? "" : Body).GetValues(name);
        if (form is { Length: > 0 })
        {
            return form;
        }

        var query = new Uri(Url).Query;
        return HttpUtility.ParseQueryString(query).GetValues(name) ?? [];
    }

    public string Header(string name) => Headers.TryGetValue(name.ToLowerInvariant(), out var v) ? v : "";

    /// <summary>The user message of an AI request, whichever provider it went to.</summary>
    public string Prompt => Json is { } j
        ? j["messages"]?.AsArray().LastOrDefault()?["content"]?.GetValue<string>()
            ?? j["contents"]?[0]?["parts"]?[0]?["text"]?.GetValue<string>() ?? ""
        : "";
}

/// <summary>
/// A network that records every request and answers with <c>reply(call, n)</c> (n counts from 1): Lamha 2's test
/// <c>recorder</c>. A reply can throw <see cref="HttpRequestException"/> (no network) or never come (<see cref="FakeNet.Never"/>).
/// </summary>
public sealed class FakeNet(Func<Call, int, CancellationToken, Task<HttpResponseMessage>> reply) : HttpMessageHandler
{
    private readonly Lock _gate = new();
    private readonly List<Call> _calls = [];

    public FakeNet(Func<Call, HttpResponseMessage> reply)
        : this((c, _, _) => Task.FromResult(reply(c)))
    {
    }

    public FakeNet(Func<Call, int, HttpResponseMessage> reply)
        : this((c, n, _) => Task.FromResult(reply(c, n)))
    {
    }

    public IReadOnlyList<Call> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    public static HttpResponseMessage Json(int status, string json) =>
        new((HttpStatusCode)status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Json(int status, JsonNode? json) => Json(status, json?.ToJsonString() ?? "null");

    /// <summary>An answer that never comes: the request waits until it's cancelled (its timeout).</summary>
    public static async Task<HttpResponseMessage> Never(CancellationToken cancel)
    {
        await Task.Delay(Timeout.Infinite, cancel).ConfigureAwait(false);
        throw new InvalidOperationException();
    }

    public static HttpRequestException Offline() => new("NetworkError when attempting to fetch resource.");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var headers = request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .ToDictionary(h => h.Key.ToLowerInvariant(), h => string.Join(", ", h.Value));
        var call = new Call(request.RequestUri!.ToString(), headers, body);
        int n;
        lock (_gate)
        {
            _calls.Add(call);
            n = _calls.Count;
        }

        return await reply(call, n, cancellationToken).ConfigureAwait(false);
    }
}
