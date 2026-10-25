using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace Lamha.Tools.Parity;

/// <summary>
/// The line to parity.mjs: one JSON object per line on stdin and stdout. parity.mjs sends cases and network answers;
/// this side sends requests and results. Nothing else may write to stdout.
/// </summary>
public sealed class Bridge
{
    private readonly Lock _gate = new();
    private readonly StreamWriter _out = new(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonObject>> _waiting = new();
    private readonly Channel<JsonObject> _cases = Channel.CreateUnbounded<JsonObject>();
    private int _nextId;

    /// <summary>Starts reading stdin: answers go to the requests waiting for them, everything else to <see cref="Cases"/>.</summary>
    public void Start() => _ = Task.Run(async () =>
    {
        using var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        while (await input.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0 || JsonNode.Parse(line) is not JsonObject message)
            {
                continue;
            }

            if ((string?)message["op"] == "reply" && _waiting.TryRemove((int)message["rid"]!, out var waiter))
            {
                waiter.TrySetResult(message);
            }
            else
            {
                await _cases.Writer.WriteAsync(message).ConfigureAwait(false);
            }
        }

        _cases.Writer.Complete();
    });

    public ChannelReader<JsonObject> Cases => _cases.Reader;

    public void Send(JsonObject message)
    {
        var line = message.ToJsonString();
        lock (_gate)
        {
            _out.WriteLine(line);
        }
    }

    /// <summary>Sends <paramref name="request"/> and waits for parity.mjs's answer to it.</summary>
    public Task<JsonObject> Ask(JsonObject request)
    {
        var id = Interlocked.Increment(ref _nextId);
        var waiter = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiting[id] = waiter;
        request["op"] = "fetch";
        request["rid"] = id;
        Send(request);
        return waiter.Task;
    }
}

/// <summary>
/// One case's network: every request goes to parity.mjs, which answers it as it answered Lamha 2's (a recorded Google
/// answer, the fake AI, a failure, or nothing at all). <see cref="Pending"/> counts the answers on their way, so the
/// fake clock stands still while parity.mjs answers.
/// </summary>
public sealed class BridgeNet(Bridge bridge, string caseId) : HttpMessageHandler
{
    private int _pending;

    public int Pending => Volatile.Read(ref _pending);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var headers = new JsonObject();
        foreach (var (name, values) in request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>()))
        {
            headers[name.ToLowerInvariant()] = string.Join(", ", values);
        }

        JsonObject reply;
        Interlocked.Increment(ref _pending);
        try
        {
            reply = await bridge.Ask(new JsonObject
            {
                ["case"] = caseId,
                ["method"] = request.Method.Method,
                ["url"] = request.RequestUri!.ToString(),
                ["headers"] = headers,
                ["body"] = body,
            }).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _pending);
        }

        switch ((string?)reply["fail"])
        {
            case "offline":
                throw new HttpRequestException("NetworkError when attempting to fetch resource.");
            case "hang":
                // A network that answers nothing: the request waits for its timeout.
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                throw new InvalidOperationException();
        }

        return new HttpResponseMessage((HttpStatusCode)(int)reply["status"]!)
        {
            Content = new StringContent((string?)reply["body"] ?? "", Encoding.UTF8, "application/json"),
        };
    }
}
