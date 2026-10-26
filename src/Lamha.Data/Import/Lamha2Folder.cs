using System.Text.Json;
using System.Text.Json.Nodes;
using Lamha.Core.Import;

namespace Lamha.Data.Import;

/// <summary>
/// Lamha 2's data folder, <c>%APPDATA%\Lamha</c> (Electron's userData), read without changing anything in it:
/// storage-sync.json and storage-local.json (desktop/storage.js), clipboard.json (desktop/clipboard-store.js) and
/// wikipedia.json (desktop/wiki-library.js). The API keys and clipboard history are decrypted with Electron's key.
/// </summary>
/// <param name="path">The folder.</param>
/// <param name="unprotect">Windows' DPAPI for this user, without entropy (Lamha.Windows' <c>Dpapi.UnprotectForUser</c>).</param>
public sealed class Lamha2Folder(string path, Func<byte[], byte[]?> unprotect)
{
    /// <summary>Lamha 2's API keys in storage.local (desktop/app-rules.js <c>SECRET_KEYS</c>).</summary>
    private static readonly string[] SecretKeys = ["aiKey", "geminiKey"];

    /// <summary>Where Lamha 2 kept its data for this Windows user.</summary>
    public static string DefaultPath { get; } = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lamha");

    public string Path { get; } = path;

    /// <summary>Whether Lamha 2 kept data here: either of its storage files.</summary>
    public bool Exists => File.Exists(In("storage-local.json")) || File.Exists(In("storage-sync.json"));

    /// <summary>What's there, or null when nothing is (or the folder can't be read).</summary>
    public Lamha2Data? Read()
    {
        if (!Exists)
        {
            return null;
        }

        var sync = Json(In("storage-sync.json")) as JsonObject ?? [];
        var local = Json(In("storage-local.json")) as JsonObject ?? [];
        var storage = new Lazy<ElectronSafeStorage?>(() => ElectronSafeStorage.Open(In("Local State"), unprotect));

        // The keys: { "$enc": base64 } when Windows could encrypt them, else plain text (desktop/storage.js).
        var keysLost = false;
        foreach (var name in SecretKeys)
        {
            if (local[name] is JsonObject sealedKey)
            {
                var plain = sealedKey["$enc"] is JsonValue v && v.TryGetValue<string>(out var enc) && Base64(enc) is { } bytes
                    ? storage.Value?.Decrypt(bytes)
                    : null;
                local[name] = plain;
                keysLost |= plain is null;
            }
        }

        // Clipboard history: { v: 1, encrypted: true, data: base64 } or { v: 1, encrypted: false, clips: [...] }.
        var clips = new JsonArray();
        var clipsLost = false;
        if (Json(In("clipboard.json")) is JsonObject head)
        {
            JsonNode? list = head["clips"];
            if (head["encrypted"] is JsonValue e && e.TryGetValue<bool>(out var encrypted) && encrypted)
            {
                var text = head["data"] is JsonValue d && d.TryGetValue<string>(out var data) && Base64(data) is { } bytes ? storage.Value?.Decrypt(bytes) : null;
                list = text is null ? null : Parse(text);
                clipsLost = list is null;
            }

            clips = list as JsonArray ?? clips;
        }

        var wikipedia = (Json(In("wikipedia.json")) as JsonObject)?["files"] as JsonArray;
        return new Lamha2Data(sync, local, clips, keysLost, clipsLost, wikipedia?.Count ?? 0);
    }

    /// <summary>
    /// The folder emptied, 30 days after its data moved and Lamha 2 was removed (decision 0019), but for the downloaded
    /// Wikipedias, kept for when offline Wikipedia returns (0005): wikipedia.json, the wikipedia folder, and any other
    /// folder in it that holds a file wikipedia.json names. Only a folder with Lamha 2's storage files is touched; what
    /// can't be deleted (a file in use) stays. How many entries went.
    /// </summary>
    public int ClearKeepingWikipedia()
    {
        if (!Exists)
        {
            return 0;
        }

        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "wikipedia.json", "wikipedia" };
        if (Json(In("wikipedia.json")) is JsonObject wiki)
        {
            // desktop/wiki-library.js: { folder, files: [{ file }], downloads: [{ part }] }, full paths
            var paths = new List<string?> { Text(wiki["folder"]) };
            paths.AddRange((wiki["files"] as JsonArray ?? []).Select(f => Text(f?["file"])));
            paths.AddRange((wiki["downloads"] as JsonArray ?? []).Select(d => Text(d?["part"])));
            foreach (var p in paths.OfType<string>().Where(System.IO.Path.IsPathFullyQualified))
            {
                var rel = System.IO.Path.GetRelativePath(Path, p);
                if (rel != "." && !rel.StartsWith("..", StringComparison.Ordinal) && !System.IO.Path.IsPathRooted(rel))
                {
                    keep.Add(rel.Split(System.IO.Path.DirectorySeparatorChar)[0]);
                }
            }
        }

        var gone = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(Path))
        {
            if (keep.Contains(System.IO.Path.GetFileName(entry)))
            {
                continue;
            }

            try
            {
                if (Directory.Exists(entry))
                {
                    Directory.Delete(entry, recursive: true);
                }
                else
                {
                    File.Delete(entry);
                }

                gone++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // in use, or not ours to delete: it stays
            }
        }

        if (!Directory.EnumerateFileSystemEntries(Path).Any())
        {
            Directory.Delete(Path); // no Wikipedia to keep: the folder goes too
        }

        return gone;
    }

    private static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;

    private string In(string name) => System.IO.Path.Combine(Path, name);

    /// <summary>A file's JSON, or null when it's missing or unreadable (Lamha 2 kept an unreadable store aside and started empty).</summary>
    private static JsonNode? Json(string file)
    {
        try
        {
            return File.Exists(file) ? Parse(File.ReadAllText(file)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static JsonNode? Parse(string json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static byte[]? Base64(string s)
    {
        try
        {
            return Convert.FromBase64String(s);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
