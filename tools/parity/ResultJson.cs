using System.Text.Json.Nodes;
using Lamha.Core.Translation;

namespace Lamha.Tools.Parity;

/// <summary>
/// A lookup result in Lamha 2's shape and names (background.js's result object), so parity.mjs compares field for field.
/// A part of speech goes as <c>{ code, label }</c>: parity.mjs names a code as Lamha 2 would have.
/// </summary>
internal static class ResultJson
{
    public static JsonObject Of(LookupResult r) => new()
    {
        ["query"] = r.Query,
        ["inflected"] = r.Inflected,
        ["formOf"] = r.FormOf,
        ["type"] = r.IsWord ? "word" : "text",
        ["src"] = r.Source,
        ["tl"] = r.Target,
        ["translation"] = r.Translation,
        ["translit"] = r.Translit,
        ["srcTranslit"] = r.SourceTranslit,
        ["spell"] = r.Spell,
        ["bestGloss"] = r.BestGloss,
        ["contextSense"] = r.ContextSense,
        ["dict"] = new JsonArray([.. r.Dict.Select(g => (JsonNode)new JsonObject
        {
            ["pos"] = Pos(g.Pos),
            ["terms"] = new JsonArray([.. g.Terms.Select(t => (JsonNode)new JsonObject
            {
                ["word"] = t.Word,
                ["back"] = Strings(t.Back),
                ["score"] = t.Score,
                ["hint"] = t.Hint,
                ["best"] = t.Best,
            })]),
        })]),
        ["definitions"] = new JsonArray([.. r.Definitions.Select(d => (JsonNode)new JsonObject
        {
            ["pos"] = Pos(d.Pos),
            ["base"] = d.Base,
            ["entries"] = new JsonArray([.. d.Entries.Select(e => (JsonNode)new JsonObject
            {
                ["gloss"] = e.Gloss,
                ["example"] = e.Example,
                ["synonyms"] = Strings(e.Synonyms),
                ["glossTr"] = e.GlossTranslated,
                ["ar"] = Strings(e.Arabic),
                ["best"] = e.Best,
            })]),
        })]),
        ["examples"] = Strings(r.Examples),
        ["source"] = r.From switch
        {
            ResultSource.Local => "local",
            ResultSource.Ai => "ai",
            _ => "online",
        },
        ["ai"] = r.AiName,
        ["rough"] = r.Rough,
        ["aiGuess"] = r.AiGuess,
        ["mode"] = r.Explain ? "explain" : "",
        ["other"] = r.Other,
        ["heroExample"] = r.HeroExample,
        ["heroPos"] = Pos(r.HeroPos),
        ["ar"] = r.Meaning,
        ["root"] = r.Root,
        ["plural"] = r.Plural,
        ["explainMissing"] = r.ExplainMissing,
        ["milestone"] = r.Milestone,
        ["goal"] = r.Goal,
        ["context"] = r.Context is { } c
            ? new JsonObject { ["word"] = c.Word, ["pre"] = c.Before, ["post"] = c.After, ["untranslated"] = c.Untranslated }
            : null,
    };

    private static JsonObject Pos(PartOfSpeech pos) => new() { ["code"] = pos.Code, ["label"] = pos.Label };

    private static JsonArray Strings(IEnumerable<string> list) => new([.. list.Select(s => (JsonNode)s)]);
}
