using System.Text.Json.Nodes;

namespace Lamha.Core.Tests.Support;

/// <summary>The AI providers' answers, as Lamha 2's tests faked them.</summary>
public static class Replies
{
    /// <summary>A proofread with a no-op change and a made-up category, both to be cleaned up.</summary>
    public const string Proof = """
        {
          "corrected": "I have been to the market yesterday.",
          "issues": [
            { "original": "I go", "fix": "I went", "category": "verb_tense", "why": "حدث في الماضي." },
            { "original": "market", "fix": "the market", "category": "articles", "why": "مكان معروف." },
            { "original": "x", "fix": "x", "category": "articles", "why": "no-op change must be dropped" },
            { "original": "a", "fix": "b", "category": "made_up", "why": "unknown category → other" }
          ]
        }
        """;

    public static HttpResponseMessage Claude(string json) => FakeNet.Json(200, new JsonObject
    {
        ["stop_reason"] = "end_turn",
        ["content"] = new JsonArray(
            new JsonObject { ["type"] = "thinking", ["thinking"] = "" },
            new JsonObject { ["type"] = "text", ["text"] = json }),
    });

    public static HttpResponseMessage Ollama(string json) => FakeNet.Json(200, new JsonObject
    {
        ["done"] = true,
        ["done_reason"] = "stop",
        ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = json },
    });

    public static HttpResponseMessage Gemini(string json, string finishReason = "STOP") => FakeNet.Json(200, new JsonObject
    {
        ["candidates"] = new JsonArray(new JsonObject
        {
            ["finishReason"] = finishReason,
            ["content"] = new JsonObject
            {
                ["role"] = "model",
                ["parts"] = new JsonArray(
                    new JsonObject { ["text"] = "thinking…", ["thought"] = true },
                    new JsonObject { ["text"] = json }),
            },
        }),
    });

    public static HttpResponseMessage OpenAi(string json, string finishReason = "stop") => FakeNet.Json(200, new JsonObject
    {
        ["choices"] = new JsonArray(new JsonObject
        {
            ["index"] = 0,
            ["finish_reason"] = finishReason,
            ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = json },
        }),
    });

    public static string Text(string text) => new JsonObject { ["text"] = text }.ToJsonString();
}
