using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Lamha.Data.Import;

namespace Lamha.Data.Tests.Import;

/// <summary>Lamha 2's folder read as Electron left it: the keys and clipboard history encrypted by safeStorage.</summary>
public sealed class Lamha2FolderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lamha2-test-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    public Lamha2FolderTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>Windows' DPAPI, for the test: "sealed" is the bytes reversed.</summary>
    private static byte[]? Unprotect(byte[] data) => data.Length > 0 ? [.. data.Reverse()] : null;

    private void Write(string name, string text) => File.WriteAllText(Path.Combine(_dir, name), text);

    /// <summary>Local State as Electron writes it: "DPAPI" + the key sealed for this user.</summary>
    private void WriteLocalState(byte[] key) =>
        Write("Local State", new JsonObject { ["os_crypt"] = new JsonObject { ["encrypted_key"] = Convert.ToBase64String([.. "DPAPI"u8, .. key.Reverse()]) } }.ToJsonString());

    private string Sealed(string text) => Convert.ToBase64String(ElectronSafeStorage.Encrypt(_key, text));

    [Fact]
    public void TheKeysAndClipboardHistoryAreDecryptedAndNothingInTheFolderChanges()
    {
        WriteLocalState(_key);
        Write("storage-sync.json", """{ "theme": "dark", "uiLang": "en" }""");
        Write("storage-local.json", $$"""{ "geminiKey": { "$enc": "{{Sealed("AIza-مفتاح")}}" }, "aiKey": "sk-ant-plain", "lookupCount": 3 }""");
        Write("clipboard.json", $$"""{ "v": 1, "encrypted": true, "data": "{{Sealed("""[{ "id": "c_1", "text": "نص منسوخ" }]""")}}" }""");
        Write("wikipedia.json", """{ "folder": "x", "files": [ { "name": "a" }, { "name": "b" } ], "downloads": [] }""");
        var before = Directory.GetFiles(_dir).ToDictionary(f => f, File.ReadAllText);

        var folder = new Lamha2Folder(_dir, Unprotect);
        Assert.True(folder.Exists);
        var data = folder.Read()!;

        Assert.Equal("dark", data.Sync["theme"]!.GetValue<string>());
        Assert.Equal("AIza-مفتاح", data.Local["geminiKey"]!.GetValue<string>());
        Assert.Equal("sk-ant-plain", data.Local["aiKey"]!.GetValue<string>()); // saved by a version before keys were encrypted
        Assert.Equal("نص منسوخ", data.Clips[0]!["text"]!.GetValue<string>());
        Assert.Equal((false, false, 2), (data.KeysLost, data.ClipsLost, data.WikipediaFiles));
        Assert.Equal(before, Directory.GetFiles(_dir).ToDictionary(f => f, File.ReadAllText));
    }

    [Fact]
    public void FromAnotherWindowsAccountTheKeysAndCopiesAreLostAndTheRestIsRead()
    {
        WriteLocalState(RandomNumberGenerator.GetBytes(32)); // not the key the values were sealed with
        Write("storage-local.json", $$"""{ "geminiKey": { "$enc": "{{Sealed("AIza")}}" }, "lookupCount": 3 }""");
        Write("clipboard.json", $$"""{ "v": 1, "encrypted": true, "data": "{{Sealed("[]")}}" }""");

        var data = new Lamha2Folder(_dir, Unprotect).Read()!;
        Assert.Null(data.Local["geminiKey"]);
        Assert.Equal(3, data.Local["lookupCount"]!.GetValue<int>());
        Assert.Equal((true, true), (data.KeysLost, data.ClipsLost));
        Assert.Empty(data.Clips);
    }

    [Fact]
    public void WithoutLocalStateOrWithPlainCopiesOrDamagedFiles()
    {
        Write("storage-sync.json", "{ not json");
        Write("storage-local.json", """{ "aiKey": { "$enc": "%%%" } }""");
        Write("clipboard.json", """{ "v": 1, "encrypted": false, "clips": [ { "id": "c_1", "text": "plain" } ] }""");

        var data = new Lamha2Folder(_dir, Unprotect).Read()!;
        Assert.Empty(data.Sync);
        Assert.True(data.KeysLost);
        Assert.Equal("plain", data.Clips[0]!["text"]!.GetValue<string>());
        Assert.False(data.ClipsLost);
    }

    [Fact]
    public void AFolderWithoutLamha2sStorageIsNotLamha2s()
    {
        Write("wikipedia.json", "{}");
        var folder = new Lamha2Folder(_dir, Unprotect);
        Assert.False(folder.Exists);
        Assert.Null(folder.Read());
        Assert.False(new Lamha2Folder(Path.Combine(_dir, "missing"), Unprotect).Exists);
    }
}
