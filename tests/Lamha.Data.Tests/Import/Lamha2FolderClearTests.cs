using System.Text.Json.Nodes;
using Lamha.Data.Import;

namespace Lamha.Data.Tests.Import;

/// <summary>Lamha 2's folder emptied 30 days after the move (decision 0019): its downloaded Wikipedias stay.</summary>
public sealed class Lamha2FolderClearTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lamha2-clear-" + Guid.NewGuid().ToString("N"));

    public Lamha2FolderClearTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private string At(params string[] parts) => Path.Combine([_dir, .. parts]);

    private void Write(string text, params string[] parts)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(At(parts))!);
        File.WriteAllText(At(parts), text);
    }

    private Lamha2Folder Folder() => new(_dir, _ => null);

    /// <summary>The folder as Lamha 2 leaves it: its storage, Electron's caches, the Firefox host's files.</summary>
    private void WriteLamha2()
    {
        Write("{}", "storage-local.json");
        Write("{}", "storage-sync.json");
        Write("{}", "clipboard.json");
        Write("{}", "Local State");
        Write("x", "Cache", "Cache_Data", "data_0");
        Write("x", "firefox", "host.ps1");
    }

    [Fact]
    public void EverythingGoesButTheWikipedias()
    {
        WriteLamha2();
        Write("zim", "wikipedia", "wikipedia_ar_top_mini.zim");
        Write("zim", "Other wikis", "wikipedia_en_top_mini.zim");
        Write("part", "Downloads now", "wikipedia_fr.zim.part");
        var files = new JsonArray(new JsonObject { ["file"] = At("wikipedia", "wikipedia_ar_top_mini.zim") }, new JsonObject { ["file"] = At("Other wikis", "wikipedia_en_top_mini.zim") });
        var downloads = new JsonArray(new JsonObject { ["part"] = At("Downloads now", "wikipedia_fr.zim.part") });
        Write(new JsonObject { ["folder"] = "", ["files"] = files, ["downloads"] = downloads }.ToJsonString(), "wikipedia.json");

        var gone = Folder().ClearKeepingWikipedia();

        Assert.Equal(6, gone);
        Assert.Equal(
            ["Downloads now", "Other wikis", "wikipedia", "wikipedia.json"],
            Directory.EnumerateFileSystemEntries(_dir).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.True(File.Exists(At("Other wikis", "wikipedia_en_top_mini.zim")));
    }

    [Fact]
    public void WikipediasInAFolderElsewhereKeepOnlyTheirList()
    {
        WriteLamha2();
        var elsewhere = Path.Combine(Path.GetTempPath(), "lamha2-wiki-" + Guid.NewGuid().ToString("N"));
        Write(new JsonObject { ["folder"] = elsewhere, ["files"] = new JsonArray(new JsonObject { ["file"] = Path.Combine(elsewhere, "a.zim") }) }.ToJsonString(), "wikipedia.json");

        Folder().ClearKeepingWikipedia();

        Assert.Equal(["wikipedia.json"], Directory.EnumerateFileSystemEntries(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public void WithNoWikipediaTheFolderGoesToo()
    {
        WriteLamha2();

        Folder().ClearKeepingWikipedia();

        Assert.False(Directory.Exists(_dir));
    }

    [Fact]
    public void AFolderThatIsntLamha2sIsLeftAlone()
    {
        Write("x", "notes.txt");
        Write("x", "Cache", "data_0");

        Assert.Equal(0, Folder().ClearKeepingWikipedia());
        Assert.True(File.Exists(At("notes.txt")));
    }
}
