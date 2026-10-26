using System.IO.Compression;
using System.Text;
using Lamha.Data.Database;
using Lamha.Data.Dictionary;

namespace Lamha.Data.Tests.Dictionary;

public sealed class PackFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "lamha-pack-test-" + Guid.NewGuid().ToString("N"));

    public PackFileTests()
    {
        Directory.CreateDirectory(_folder);
        Lamha2Pack("tr.json.gz", """
            {"meta":{"lang":"tr","format":1,"version":"2026-09-28","words":2,"forms":2,"built":"2026-09-28T09:40:09+00:00","source":"Turkish Wiktionary via kaikki.org (wiktextract)","license":"CC BY-SA 4.0"},
             "shards":{"ev":{"w":{"ev":{"s":[["n","yaşanılan yer","Evin duvarları",[]],["n","aile","",["hane"]]],"p":"ˈev"}},"f":{"evler":"ev"}},
                       "ki":{"w":{"kitap":{"s":[["n","basılı yapraklar","",[]]]}},"f":{"kitaplar":"kitap"}},
                       "../x":{"w":{"evil":{"s":[]}}}}}
            """);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void TheBuilderKeepsLamha2sPackAndTheReaderReadsItBack()
    {
        var path = Path.Combine(_folder, "tr.db");
        Assert.Equal("tr", PackBuilder.Build(Path.Combine(_folder, "tr.json.gz"), path));
        using var pack = new PackFile(path, "tr", check: true);

        Assert.Equal(("tr", "2026-09-28", 2), (pack.Meta.Lang, pack.Meta.Version, pack.Meta.Words));
        var ev = pack.Entry("ev")!;
        Assert.Equal("ˈev", ev.Ipa);
        Assert.Equal([("n", "yaşanılan yer", "Evin duvarları"), ("n", "aile", "")], ev.Senses.Select(s => (s.Pos, s.Gloss, s.Example)));
        Assert.Equal(["hane"], ev.Senses[1].Synonyms);
        Assert.Empty(ev.Translations); // a pack has no Arabic
        Assert.Equal("ev", pack.Lemma("evler"));
        Assert.Null(pack.Lemma("ev"));
        Assert.Null(pack.Entry("Ev")); // as written: capitals are the lookup's to try
        Assert.Null(pack.Entry("evil")); // only shard keys, as packs.js read them
    }

    [Fact]
    public void AFileThatIsntAPackOrIsForAnotherLanguageIsRefused()
    {
        var path = Path.Combine(_folder, "tr.db");
        PackBuilder.Build(Path.Combine(_folder, "tr.json.gz"), path);
        Assert.Throws<InvalidDataException>(() => new PackFile(path, "fr"));

        var user = Path.Combine(_folder, "user.db");
        using (LamhaDatabase.Open(user))
        {
        }

        Assert.Throws<InvalidDataException>(() => new PackFile(user));
        File.WriteAllText(Path.Combine(_folder, "text.db"), "not a database");
        Assert.Throws<InvalidDataException>(() => new PackFile(Path.Combine(_folder, "text.db"), check: true));
        Lamha2Pack("v2.json.gz", """{"meta":{"lang":"tr","format":2},"shards":{}}""");
        Assert.Throws<InvalidDataException>(() => PackBuilder.Build(Path.Combine(_folder, "v2.json.gz"), Path.Combine(_folder, "v2.db")));
    }

    [Fact]
    public async Task TheFolderKeepsAPackOnlyOnceItsCheckedAndOnlyLanguageCodesBecomeFileNames()
    {
        var packs = new PackFolder(Path.Combine(_folder, "packs"));
        Assert.Empty(packs.Open()); // no folder yet

        var db = Path.Combine(_folder, "tr.db");
        PackBuilder.Build(Path.Combine(_folder, "tr.json.gz"), db);
        PackBuilder.Compress(db, db + ".gz");
        await using (var gzip = File.OpenRead(db + ".gz"))
        {
            await packs.ReceiveAsync("tr", gzip, TestContext.Current.CancellationToken);
        }

        Assert.Equal(["tr.db.part"], Directory.EnumerateFiles(Path.Combine(_folder, "packs")).Select(Path.GetFileName)); // received, not installed
        using (var pack = packs.Install("tr"))
        {
            Assert.Equal("ev", pack.Lemma("evler"));
        }

        var open = packs.Open();
        Assert.Equal(["tr"], open.Select(p => p.Meta.Lang));
        open[0].Dispose();

        await using (var gzip = File.OpenRead(db + ".gz"))
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => packs.ReceiveAsync("fr", gzip, TestContext.Current.CancellationToken)); // a Turkish pack under the French name
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => packs.ReceiveAsync("fr", new MemoryStream(Encoding.UTF8.GetBytes("not gzip")), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => packs.ReceiveAsync("../x", new MemoryStream(), TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentException>(() => packs.Remove("..\\tr"));
        Assert.Equal(["tr.db"], Directory.EnumerateFiles(Path.Combine(_folder, "packs")).Select(Path.GetFileName));

        packs.Remove("tr");
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_folder, "packs")));
    }

    private void Lamha2Pack(string name, string json)
    {
        using var gzip = new GZipStream(File.Create(Path.Combine(_folder, name)), CompressionLevel.Fastest);
        gzip.Write(Encoding.UTF8.GetBytes(json));
    }
}
