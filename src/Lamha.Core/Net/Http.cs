using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lamha.Core.Net;

/// <summary>
/// Lamha's requests: one <see cref="HttpClient"/> for everything, timeouts on the <see cref="TimeProvider"/> (so tests
/// control time), and failures sorted into <see cref="HttpFailure"/>.
/// </summary>
public sealed class Http(HttpClient client, TimeProvider time)
{
    public TimeProvider Time => time;

    /// <summary>
    /// Sends the request <paramref name="build"/> makes and reads the answer as JSON. A quick failure (a network blip, a
    /// busy server) is tried again up to <paramref name="retries"/> times, 0.4 s, then 0.8 s later; a timeout isn't: it
    /// would only double the wait. A 429 is never retried here, so the caller can move on. Lamha 2's <c>fetchJSON</c>.
    /// </summary>
    public async Task<JsonNode?> GetJsonAsync(Func<HttpRequestMessage> build, int retries, TimeSpan timeout, CancellationToken cancel = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var response = await SendAsync(build(), timeout, cancel).ConfigureAwait(false);
                var status = (int)response.StatusCode;
                if (!response.IsSuccessStatusCode)
                {
                    var body = await ReadBodyAsync(response, cancel).ConfigureAwait(false);
                    throw new HttpFailure(HttpFailureKind.Status, status, body);
                }

                return await ReadJsonAsync(response, cancel).ConfigureAwait(false);
            }
            catch (HttpFailure failure) when (attempt < retries && failure.Kind == HttpFailureKind.Network
                || attempt < retries && failure.Kind == HttpFailureKind.Status && failure.Status >= 500)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(400 * (1 << attempt)), time, cancel).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Sends a request with a timeout; network failures and timeouts become <see cref="HttpFailure"/>. With
    /// <see cref="HttpCompletionOption.ResponseHeadersRead"/> (a download read as it comes) the timeout is for the headers.
    /// </summary>
    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, TimeSpan timeout, CancellationToken cancel = default, HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        using var timer = new CancellationTokenSource(timeout, time);
        using var both = CancellationTokenSource.CreateLinkedTokenSource(timer.Token, cancel);
        try
        {
            return await client.SendAsync(request, completion, both.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timer.IsCancellationRequested && !cancel.IsCancellationRequested)
        {
            throw new HttpFailure(HttpFailureKind.Timeout);
        }
        catch (HttpRequestException e)
        {
            throw new HttpFailure(HttpFailureKind.Network, 0, "", e);
        }
        finally
        {
            request.Dispose();
        }
    }

    /// <summary>The answer's body as JSON; an answer that isn't JSON is <see cref="HttpFailureKind.Invalid"/>.</summary>
    public static async Task<JsonNode?> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancel = default)
    {
        try
        {
            var stream = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                return await JsonNode.ParseAsync(stream, cancellationToken: cancel).ConfigureAwait(false);
            }
        }
        catch (JsonException e)
        {
            throw new HttpFailure(HttpFailureKind.Invalid, (int)response.StatusCode, "", e);
        }
    }

    public static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancel = default)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return "";
        }
    }

    /// <summary>A POST with a JSON body.</summary>
    public static HttpRequestMessage PostJson(string url, JsonNode body, params ReadOnlySpan<(string Name, string Value)> headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        foreach (var (name, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        return request;
    }

    /// <summary>A POST with a form body (Google's translate endpoints).</summary>
    public static HttpRequestMessage PostForm(string url, IEnumerable<KeyValuePair<string, string>> form)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded") { CharSet = "UTF-8" };
        return request;
    }

    public static HttpRequestMessage Get(string url, params ReadOnlySpan<(string Name, string Value)> headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        foreach (var (name, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        return request;
    }

    /// <summary>The "error.message" (or "error" when it's a string) of an error answer's JSON body, or "".</summary>
    public static string ErrorMessage(string body)
    {
        try
        {
            var error = JsonNode.Parse(body)?["error"];
            return error is JsonValue v && v.TryGetValue<string>(out var s) ? s : error?["message"]?.GetValue<string>() ?? "";
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return "";
        }
    }

    /// <summary>A URL query string from name/value pairs, escaped.</summary>
    public static string Query(IEnumerable<KeyValuePair<string, string>> pairs) =>
        string.Join('&', pairs.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
}
