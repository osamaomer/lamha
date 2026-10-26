using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Lamha.Core.Dictionary;
using Lamha.Core.Net;
using Lamha.Core.Settings;
using Lamha.Core.Tests.Support;
using Lamha.Core.Translation;
using Lamha.Data.Dictionary;

namespace Lamha.Core.Tests.Dictionary;

/// <summary>Downloadable language packs: Lamha 2's tests in test-writing.mjs and test-desktop.mjs, on the real pack files.</summary>
public sealed class LanguagePackTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "lamha-packs-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    /// <summary>
    /// A pack on the release, as .github/workflows/packs.yml makes it: Lamha 2's pack (build_packs.py's gzip'd
    /// <c>{ meta, shards }</c>) converted by <see cref="PackBuilder"/>, gzip'd.
    /// </summary>
    private byte[] PackFile(string lang, string version = "2026-09-28", JsonObject? shards = null)
    {
        shards ??= JsonNode.Parse("""
            {
              "ma": { "w": { "maison": { "s": [["n", "Bâtiment servant d'habitation.", "une maison de campagne", ["demeure", "logis"]], ["n", "Famille, lignée.", "", []], ["v", "(verbe fictif pour le test)", "", []]], "p": "mɛ.zɔ̃" } },
                      "f": { "maisons": "maison" } },
              "fa": { "w": { "famille": { "s": [["n", "Ensemble des parents.", "", []]] } }, "f": {} }
            }
            """)!.AsObject();
        var data = new JsonObject
        {
            ["meta"] = new JsonObject { ["lang"] = lang, ["format"] = 1, ["version"] = version, ["words"] = 2, ["forms"] = 1 },
            ["shards"] = shards,
        };
        var name = Path.Combine(_folder, "release", lang + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(name)!);
        using (var gzip = new GZipStream(File.Create(name + ".json.gz"), CompressionLevel.Fastest))
        {
            gzip.Write(Encoding.UTF8.GetBytes(data.ToJsonString()));
        }

        PackBuilder.Build(name + ".json.gz", name + ".db");
        PackBuilder.Compress(name + ".db", name + ".db.gz");
        return File.ReadAllBytes(name + ".db.gz");
    }

    /// <summary>The release serves <paramref name="files"/> ("fr.db.gz"); Google answers with <paramref name="google"/> (else 429).</summary>
    private static FakeNet PackNet(Dictionary<string, byte[]> files, Func<Call, HttpResponseMessage>? google = null) => new(PackReply(files, google));

    private static Func<Call, HttpResponseMessage> PackReply(Dictionary<string, byte[]> files, Func<Call, HttpResponseMessage>? google = null) => c =>
    {
        if (c.Url.StartsWith(LanguagePacks.Release, StringComparison.Ordinal))
        {
            return files.TryGetValue(c.Url[LanguagePacks.Release.Length..], out var file)
                ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(file) }
                : FakeNet.Json(404, "{}");
        }

        return google?.Invoke(c) ?? FakeNet.Json(429, "{}");
    };

    private string Packs => Path.Combine(_folder, "packs");

    [Fact]
    public async Task DownloadOneFindWordsAndTheirFormsWithNoInternetRemoveIt()
    {
        var net = PackNet(new() { ["fr.db.gz"] = PackFile("fr") });
        using var lamha = new TestLamha(net, new LamhaSettings { UiLang = "ar", TargetLang = "fr", DictSource = DictSource.Offline }, realDict: true, packFolder: Packs);
        var packs = lamha.PackService!;
        var before = packs.List().Single(p => p.Lang == "fr");
        Assert.Equal((false, (double?)null), (before.Installed, before.Progress));
        Assert.NotNull(await lamha.LookupError("maisons")); // nothing to answer with before

        await packs.InstallAsync("fr", TestContext.Current.CancellationToken);
        var after = packs.List().Single(p => p.Lang == "fr");
        Assert.Equal((true, 2, "2026-09-28"), (after.Installed, after.Words, after.Version));
        Assert.Equal(["fr"], packs.Languages);
        Assert.Equal("fr", packs.Key);

        var d = await lamha.Lookup("maisons");
        Assert.Equal((true, ResultSource.Local, "maison", "maisons", "Bâtiment servant d'habitation.", "une maison de campagne", "n", "mɛ.zɔ̃"),
            (d.Explain, d.From, d.Query, d.Inflected, d.Translation, d.HeroExample, d.HeroPos.Code, d.SourceTranslit));
        Assert.Equal([("n", 2), ("v", 1)], d.Definitions.Select(p => (p.Pos.Code, p.Entries.Count))); // grouped by part of speech
        Assert.Equal(["demeure", "logis"], d.Definitions[0].Entries[0].Synonyms);
        Assert.Equal("famille", (await lamha.Lookup("Famille")).Query); // any capitalisation

        packs.Dispose(); // Lamha closed: the next start finds the pack where it was kept
        using (var toArabic = new TestLamha(net, new LamhaSettings { UiLang = "ar", DictSource = DictSource.Offline }, realDict: true, packFolder: Packs))
        {
            Assert.Equal(ResultSource.Local, (await toArabic.Lookup("maison")).From); // offline, any installed pack beats an error
            toArabic.PackService!.Remove("fr");
            Assert.Empty(Directory.EnumerateFileSystemEntries(Packs)); // every part is gone
            Assert.Empty(toArabic.PackService.Languages);
            Assert.NotNull(await toArabic.LookupError("famille"));
        }

        Assert.All(net.Calls, c => Assert.StartsWith(LanguagePacks.Release, c.Url, StringComparison.Ordinal)); // only the download went online
    }

    [Fact]
    public async Task OnlineAPackExplainsAWordBeforeTheAiIsAskedGooglesTranslationStaysUnderIt()
    {
        var net = PackNet(new() { ["fr.db.gz"] = PackFile("fr") }, c => Nets.IsGoogleSingle(c) ? Nets.GoogleWord("منزل", "fr") : FakeNet.Json(429, "{}"));
        using var lamha = new TestLamha(net, new LamhaSettings { UiLang = "ar", ExplainLangs = ["fr"], DictSource = DictSource.Online, UseContext = false, AiProvider = AiProviderKind.Ollama, OllamaModel = "qwen3.5:4b" }, realDict: true, packFolder: Packs);
        await lamha.PackService!.InstallAsync("fr", TestContext.Current.CancellationToken);
        var d = await lamha.Lookup("maison");
        Assert.Equal((true, ResultSource.Local, "Bâtiment servant d'habitation.", "منزل", "ar"), (d.Explain, d.From, d.Translation, d.Meaning, d.Other));
        Assert.DoesNotContain(net.Calls, Nets.IsOllama); // no AI call
    }

    [Fact]
    public async Task APackThatIsntThereIsDamagedOrIsForAnotherLanguageIsRefusedAndNothingIsKept()
    {
        var net = PackNet(new()
        {
            ["fr.db.gz"] = PackFile("de"), // a German pack under the French name
            ["de.db.gz"] = Encoding.UTF8.GetBytes("not gzip at all"),
            ["tr.db.gz"] = Gzip("not a pack inside the gzip"),
        });
        using var lamha = new TestLamha(net, realDict: true, packFolder: Packs);
        var packs = lamha.PackService!;
        Assert.Equal("pack_bad", await ErrorOf(() => packs.InstallAsync("fr")));
        Assert.Equal("pack_bad", await ErrorOf(() => packs.InstallAsync("de")));
        Assert.Equal("pack_bad", await ErrorOf(() => packs.InstallAsync("tr")));
        Assert.Equal("pack_download", await ErrorOf(() => packs.InstallAsync("es"))); // not on the release
        Assert.Equal("pack_unknown", await ErrorOf(() => packs.InstallAsync("../x")));
        Assert.Equal("pack_unknown", await ErrorOf(() => packs.InstallAsync("ar")));
        Assert.Empty(Directory.Exists(Packs) ? Directory.EnumerateFileSystemEntries(Packs) : []);
        Assert.All(packs.List(), p => Assert.Equal((false, (double?)null), (p.Installed, p.Progress)));
    }

    [Fact]
    public async Task NoInternetIsADownloadErrorAndAFailedUpdateKeepsThePackThatWorks()
    {
        var online = true;
        var files = new Dictionary<string, byte[]> { ["fr.db.gz"] = PackFile("fr", "2026-09-28") };
        var net = new FakeNet(c => online ? PackReply(files)(c) : throw FakeNet.Offline());
        using var lamha = new TestLamha(net, realDict: true, packFolder: Packs);
        var packs = lamha.PackService!;
        await packs.InstallAsync("fr", TestContext.Current.CancellationToken);

        online = false;
        Assert.Equal("pack_download", await ErrorOf(() => packs.InstallAsync("fr")));
        files["fr.db.gz"] = Encoding.UTF8.GetBytes("damaged");
        online = true;
        Assert.Equal("pack_bad", await ErrorOf(() => packs.InstallAsync("fr")));
        Assert.Equal("maison", packs.Find("fr", "maisons")?.Word); // still the one that works
        Assert.Equal(["fr.db"], Directory.EnumerateFiles(Packs).Select(Path.GetFileName));

        files["fr.db.gz"] = PackFile("fr", "2026-11-01"); // a newer version takes its place
        await packs.InstallAsync("fr", TestContext.Current.CancellationToken);
        Assert.Equal("2026-11-01", packs.List().Single(p => p.Lang == "fr").Version);
        Assert.Equal("maison", packs.Find("fr", "maison")?.Word);
    }

    [Fact]
    public async Task ADownloadSaysHowFarItHasGotAndOneAtATime()
    {
        var file = PackFile("fr", shards: Shards(3000)); // big enough to come in several reads
        var release = new TaskCompletionSource();
        var net = new FakeNet(async (c, _, cancel) =>
        {
            await release.Task.WaitAsync(cancel);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(file) };
        });
        using var lamha = new TestLamha(net, packFolder: Packs);
        var packs = lamha.PackService!;
        var seen = new List<double?>();
        packs.Changed += () =>
        {
            lock (seen)
            {
                seen.Add(packs.List().Single(p => p.Lang == "fr").Progress);
            }
        };

        var install = packs.InstallAsync("fr", TestContext.Current.CancellationToken);
        Assert.Equal(0.0, packs.List().Single(p => p.Lang == "fr").Progress);
        Assert.Equal("pack_busy", await ErrorOf(() => packs.InstallAsync("fr")));
        release.SetResult();
        await install;

        Assert.Equal(0.0, seen[0]);
        Assert.Null(seen[^1]); // done
        var during = seen.Skip(1).SkipLast(1).Select(p => p!.Value).ToList();
        Assert.NotEmpty(during);
        Assert.Equal(during.Order(), during); // only forward
        Assert.All(during, p => Assert.InRange(p, 0, 0.95)); // the rest is installing it
        Assert.True(packs.List().Single(p => p.Lang == "fr").Installed);
    }

    [Fact]
    public void ADownloadThatNeverFinishedIsClearedAtTheNextStart()
    {
        Directory.CreateDirectory(Packs);
        File.WriteAllText(Path.Combine(Packs, "fr.db.part"), "half a pack");
        File.WriteAllText(Path.Combine(Packs, "de.db"), "not a pack"); // damaged: not used
        using var client = new HttpClient(PackNet([]));
        using var packs = new LanguagePacks(new PackFolder(Packs), new Http(client, TimeProvider.System));
        Assert.Empty(packs.Languages);
        Assert.False(File.Exists(Path.Combine(Packs, "fr.db.part")));
    }

    [Fact]
    public void FindLowercasesTheLanguagesOwnWayAndTriesPlurals()
    {
        var shards = JsonNode.Parse("""
            {
              "ki": { "w": { "kitap": { "s": [["n", "Ciltli veya ciltsiz olarak bir araya getirilmiş, basılı veya yazılı kâğıt yaprakların bütünü.", "", []]] } }, "f": { "kitaplar": "kitap" } },
              "ha": { "w": { "Haus": { "s": [["n", "Gebäude, das Menschen zum Wohnen dient.", "", []]] } }, "f": {} },
              "ca": { "w": { "casa": { "s": [["n", "Edificio para habitar.", "", []]] } }, "f": {} },
              "fl": { "w": { "flor": { "s": [["n", "Brote de muchas plantas.", "", []]] } }, "f": {} }
            }
            """)!.AsObject();
        Directory.CreateDirectory(Packs);
        foreach (var lang in new[] { "tr", "de", "es" })
        {
            File.WriteAllBytes(Path.Combine(Packs, lang + ".db"), Gunzip(PackFile(lang, shards: shards.DeepClone().AsObject())));
        }

        using var client = new HttpClient(PackNet([]));
        using var packs = new LanguagePacks(new PackFolder(Packs), new Http(client, TimeProvider.System));
        Assert.Equal(["de", "es", "tr"], packs.Languages);
        Assert.Equal("kitap", packs.Find("tr", "KİTAP")?.Word); // Turkish capitals (İ) lowercase the Turkish way
        Assert.Equal(("kitap", "kitaplar"), packs.Find("tr", "kitaplar") is { } f ? (f.Word, f.Form) : default);
        Assert.Equal("Haus", packs.Find("de", "haus")?.Word); // German nouns are capitalised
        Assert.Equal(("casa", "casas"), packs.Find("es", "casas") is { } p ? (p.Word, p.Form) : default);
        Assert.Equal("flor", packs.Find("es", "flores")?.Word);
        Assert.Null(packs.Find("es", "ses")); // too short to be a plural
        Assert.Null(packs.Find("fr", "maison")); // not installed
        Assert.Null(packs.Find("tr", new string('a', 41)));
        Assert.Null(packs.Find("tr", "  "));
    }

    [Fact]
    public async Task ARealPackFindsWordsAndTheirForms()
    {
        // The Turkish pack as the release has it: built with
        //   dotnet run --project tools/DictionaryBuilder -- pack <packs-v1>/tr.json.gz artifacts/packs
        var built = Path.Combine(RealDictionary.RepositoryRoot, "artifacts", "packs", "tr.db.gz");
        Assert.SkipUnless(File.Exists(built), "build it with: dotnet run --project tools/DictionaryBuilder -- pack tr.json.gz artifacts/packs");
        using var lamha = new TestLamha(PackNet(new() { ["tr.db.gz"] = await File.ReadAllBytesAsync(built, TestContext.Current.CancellationToken) }), packFolder: Packs);
        var packs = lamha.PackService!;
        await packs.InstallAsync("tr", TestContext.Current.CancellationToken);
        Assert.True(packs.List().Single(p => p.Lang == "tr").Words > 20000);
        var ev = packs.Find("tr", "evler")!;
        Assert.Equal(("ev", "evler"), (ev.Word, ev.Form));
        Assert.True(ev.Entry.Senses[0].Gloss.Length > 10); // a definition in Turkish
        Assert.Equal("kitap", packs.Find("tr", "KİTAP")?.Word);
    }

    private static async Task<string?> ErrorOf(Func<Task> work)
    {
        try
        {
            await work();
            return null;
        }
        catch (LamhaException e)
        {
            return e.Code;
        }
    }

    private static byte[] Gzip(string text)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
        {
            gzip.Write(Encoding.UTF8.GetBytes(text));
        }

        return output.ToArray();
    }

    private static byte[] Gunzip(byte[] data)
    {
        using var input = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>Shards with <paramref name="n"/> made-up words: a pack of some size.</summary>
    private static JsonObject Shards(int n)
    {
        var words = new JsonObject();
        for (var i = 0; i < n; i++)
        {
            words["mot" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)] = new JsonObject
            {
                ["s"] = new JsonArray(new JsonArray("n", Guid.NewGuid().ToString() + " " + Guid.NewGuid(), "", new JsonArray())),
            };
        }

        return new JsonObject { ["mo"] = new JsonObject { ["w"] = words, ["f"] = new JsonObject { ["maisons"] = "maison" } } };
    }
}
