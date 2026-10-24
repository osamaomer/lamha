using Lamha.Data.Database;
using Lamha.Data.Dictionary;

namespace Lamha.Data.Tests.Dictionary;

public sealed class DictionaryFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "lamha-dict-test-" + Guid.NewGuid().ToString("N"));

    public DictionaryFileTests()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "en"));
        Directory.CreateDirectory(Path.Combine(_folder, "forms"));
        Directory.CreateDirectory(Path.Combine(_folder, "ar"));
        File.WriteAllText(Path.Combine(_folder, "meta.json"), """{"entries":2}""");
        File.WriteAllText(Path.Combine(_folder, "en", "ba.json"), """
            {"bank":{"s":[["n","sloping land","they pulled the canoe up on the bank",[],["ضِفّة"]],["n","a financial institution","",["banking company"],[]]],"t":{"n":[["financial institution",["مصرف","بنك"]]]},"p":"bæŋk"},
             "back":{"s":[["r","in return","",[],[]]]}}
            """);
        File.WriteAllText(Path.Combine(_folder, "forms", "ba.json"), """{"banks":["bank"]}""");
        File.WriteAllText(Path.Combine(_folder, "ar", "0628.json"), """{"بنك":["bank","banker"]}""");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void TheBuilderKeepsEveryEntryInItsOrderAndTheReaderReadsThemBack()
    {
        var path = Path.Combine(_folder, "out", "dict.db");
        DictionaryBuilder.Build(_folder, path);
        using var dict = new DictionaryFile(path);

        var bank = dict.Entry("bank")!;
        Assert.Equal("bæŋk", bank.Ipa);
        Assert.Equal(["sloping land", "a financial institution"], bank.Senses.Select(s => s.Gloss));
        Assert.Equal(["ضِفّة"], bank.Senses[0].Arabic);
        Assert.Equal(["banking company"], bank.Senses[1].Synonyms);
        Assert.Equal(("n", "financial institution", "بنك"), (bank.Translations[0].Pos, bank.Translations[0].Senses[0].Hint, bank.Translations[0].Senses[0].Arabic[1]));
        Assert.Empty(dict.Entry("back")!.Translations);
        Assert.Null(dict.Entry("constructor"));
        Assert.Equal(["bank"], dict.Lemmas("banks"));
        Assert.Equal(["bank", "banker"], dict.English("بنك"));
        Assert.Equal(["bank", "back"], dict.Shard("ba")); // the file's order: most frequent first
        Assert.Equal("""{"entries":2}""", dict.Meta);
    }

    [Fact]
    public void AFileThatIsntADictionaryIsRefused()
    {
        var path = Path.Combine(_folder, "user.db");
        using (LamhaDatabase.Open(path))
        {
        }

        Assert.Throws<InvalidDataException>(() => new DictionaryFile(path));
    }
}
