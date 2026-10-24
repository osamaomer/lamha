using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Lamha.Core.Tests.Support;

/// <summary>Fake networks from Lamha 2's tests.</summary>
public static partial class Nets
{
    public static bool IsGoogle(Call c) => c.Url.Contains("translate_a", StringComparison.Ordinal);

    public static bool IsGoogleSingle(Call c) => c.Url.Contains("/translate_a/single", StringComparison.Ordinal);

    public static bool IsGoogleBatch(Call c) => c.Url.Contains("/translate_a/t", StringComparison.Ordinal);

    public static bool IsGemini(Call c) => c.Url.Contains("generativelanguage", StringComparison.Ordinal);

    public static bool IsOllama(Call c) => c.Url.EndsWith("/api/chat", StringComparison.Ordinal);

    /// <summary>Who answered each request: "google", "gemini", "ollama" or "other" (Lamha 2's <c>who</c>).</summary>
    public static IEnumerable<string> Who(IEnumerable<Call> calls) =>
        calls.Select(c => IsGoogle(c) ? "google" : IsGemini(c) ? "gemini" : IsOllama(c) ? "ollama" : "other");

    /// <summary>Google's answer for one word: <c>{ src: "en", sentences: [{ trans }] }</c> and anything more.</summary>
    public static HttpResponseMessage GoogleWord(string translation, string src = "en", string more = "") =>
        FakeNet.Json(200, $$"""{ "src": "{{src}}", "sentences": [ { "trans": {{JsonValue.Create(translation).ToJsonString()}}, "orig": "x" } ]{{(more.Length > 0 ? ", " + more : "")}} }""");

    /// <summary>Google's batch answer for one string.</summary>
    public static HttpResponseMessage GoogleBatch(string translation) => FakeNet.Json(200, new JsonArray(new JsonArray(translation, "en")));

    /// <summary>
    /// Google, faked: <c>single</c> translates its whole q split into line pieces ("AR&lt;…&gt;"), <c>t</c> translates
    /// each q (Lamha 2's <c>fakeGoogle</c>).
    /// </summary>
    public static HttpResponseMessage FakeGoogle(Call c)
    {
        if (IsGoogleSingle(c))
        {
            var q = c.Param("q")[0];
            var sentences = new JsonArray();
            foreach (var piece in LinePieces().Split(q).Where(p => p.Length > 0))
            {
                var trailing = TrailingBreaks().Match(piece).Value;
                sentences.Add((JsonNode)new JsonObject { ["trans"] = "AR<" + piece[..^trailing.Length] + ">" + trailing, ["orig"] = piece });
            }

            return FakeNet.Json(200, new JsonObject { ["src"] = "en", ["sentences"] = sentences });
        }

        if (IsGoogleBatch(c))
        {
            return FakeNet.Json(200, new JsonArray([.. c.Param("q").Select(x => (JsonNode)new JsonArray("AR<" + x + ">", "en"))]));
        }

        return FakeNet.Json(404, "{}");
    }

    /// <summary>
    /// A fake network for the translation service: Google works (<see cref="FakeGoogle"/>) or is rate-limited; Gemini and
    /// Ollama translate with <paramref name="aiReply"/>(texts, n) (n counts AI requests; null is Gemini's quota used up).
    /// Lamha 2's <c>trNet</c>.
    /// </summary>
    public static FakeNet Translation(bool googleUp = true, Func<IReadOnlyList<string>, int, IReadOnlyList<string>?>? aiReply = null)
    {
        aiReply ??= (texts, _) => [.. texts.Select(t => "AI<" + t + ">")];
        var aiCount = 0;
        return new FakeNet(c =>
        {
            if (IsGoogle(c))
            {
                return googleUp ? FakeGoogle(c) : FakeNet.Json(429, "{}");
            }

            if (IsGemini(c) || IsOllama(c))
            {
                var texts = TextsOf(c.Prompt);
                var translations = aiReply(texts, Interlocked.Increment(ref aiCount));
                var json = new JsonObject { ["translations"] = translations is null ? null : new JsonArray([.. translations.Select(t => (JsonNode)t)]) }.ToJsonString();
                return IsOllama(c) ? Replies.Ollama(json)
                    : translations is null ? FakeNet.Json(429, """{ "error": { "message": "quota" } }""")
                    : Replies.Gemini(json);
            }

            return FakeNet.Json(404, "{}");
        });
    }

    /// <summary>The strings an AI translation request asked for.</summary>
    public static IReadOnlyList<string> TextsOf(string prompt) =>
        [.. JsonNode.Parse(Texts().Match(prompt).Groups[1].Value)!.AsArray().Select(n => n!.GetValue<string>())];

    [GeneratedRegex(@"(?<=\n)(?!\n)")]
    private static partial Regex LinePieces();

    [GeneratedRegex(@"\n+$")]
    private static partial Regex TrailingBreaks();

    [GeneratedRegex(@"<texts>\n([\s\S]*)\n</texts>")]
    private static partial Regex Texts();
}
