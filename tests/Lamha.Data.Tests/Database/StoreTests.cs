using Lamha.Core.Abstractions;
using Lamha.Core.Review;
using Lamha.Data.Database;

namespace Lamha.Data.Tests.Database;

public sealed class StoreTests : IDisposable
{
    private readonly LamhaDatabase _db = LamhaDatabase.InMemory();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void ANewDatabaseHasTheLatestSchema() => Assert.Equal(Migrations.Latest, _db.Version);

    [Fact]
    public void AFileDatabaseKeepsItsDataAndIsNotMigratedTwice()
    {
        var path = Path.Combine(Path.GetTempPath(), "lamha-test-" + Guid.NewGuid().ToString("N"), "lamha.db");
        try
        {
            using (var db = LamhaDatabase.Open(path))
            {
                new SqliteStateStore(db).Set("k", "v");
            }

            using (var db = LamhaDatabase.Open(path))
            {
                Assert.Equal("v", new SqliteStateStore(db).Get("k"));
                Assert.Equal(Migrations.Latest, db.Version);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void StateValuesAreSetReplacedAndRemoved()
    {
        var state = new SqliteStateStore(_db);
        Assert.Null(state.Get("a"));
        state.Set("a", "1");
        state.Set("a", "2");
        Assert.Equal("2", state.Get("a"));
        state.Set("a", null);
        Assert.Null(state.Get("a"));
    }

    [Fact]
    public void HistoryPutsAWordBackOnTopAndKeepsTheNewest()
    {
        var history = new SqliteHistoryStore(_db);
        Assert.True(history.Add(new HistoryEntry("bank", "مصرف", "en", 1), keep: 3));
        Assert.True(history.Add(new HistoryEntry("river", "نهر", "en", 1), keep: 3)); // the same millisecond: added order
        Assert.False(history.Add(new HistoryEntry("Bank", "ضفة", "en", 1), keep: 3), "the same word in another case");
        Assert.Equal(["Bank", "river"], history.Recent(10).Select(h => h.Word));
        history.Add(new HistoryEntry("a", "", "en", 2), keep: 3);
        history.Add(new HistoryEntry("b", "", "en", 3), keep: 3);
        Assert.Equal(["b", "a", "Bank"], history.Recent(10).Select(h => h.Word));
        Assert.True(history.Contains("BANK"));

        Assert.Equal(1, history.Merge([new HistoryEntry("a", "x", "en", 9), new HistoryEntry("old", "", "en", 0)], keep: 4));
        Assert.Equal(["b", "a", "Bank", "old"], history.Recent(10).Select(h => h.Word));
        history.Clear();
        Assert.Equal(0, history.Count());
    }

    [Fact]
    public void TheDeckAnswersReviewQueries()
    {
        var deck = new SqliteDeckStore(_db);
        deck.Put(new Card { Word = "New1", Meaning = "x", Added = 1 });
        deck.Put(new Card { Word = "new2", Meaning = "x", Added = 2 });
        deck.Put(new Card { Word = "due", Meaning = "x", Added = 0, Last = 50, Due = 90, Interval = 30 });
        deck.Put(new Card { Word = "later", Meaning = "x", Added = 0, Last = 50, Due = 500 });
        deck.Put(new Card { Word = "nothing", Added = 0, Last = 50, Due = 10 });

        Assert.Equal(["nothing", "due"], deck.Due(100, 10).Select(c => c.Word));
        Assert.Equal(["new2", "New1"], deck.NewestNew(10).Select(c => c.Word));
        Assert.Equal(new DeckCounts(Due: 2, New: 2, Total: 5, Learned: 1, NextDue: 500), deck.Counts(100));
        Assert.Equal("due", deck.FirstDue(1000, exceptKey: "x")?.Word);
        Assert.Equal("later", deck.FirstDue(1000, exceptKey: "due")?.Word);
        Assert.Equal("new1", deck.Get("new1")?.Key);

        var card = deck.Get("due")!;
        Assert.Equal((50L, 90L, 30), (card.Last!.Value, card.Due, card.Interval));
    }

    [Fact]
    public void RemovedWordsAreRememberedUntilPruned()
    {
        var deck = new SqliteDeckStore(_db);
        deck.Put(new Card { Word = "a", Meaning = "x" });
        deck.Put(new Card { Word = "b", Meaning = "x" });
        deck.Clear(when: 100);
        Assert.Equal(0, deck.Counts(0).Total);
        Assert.Equal(100, deck.RemovedAt("a"));
        deck.SetRemoved("c", 300);
        deck.PruneRemoved(before: 200);
        Assert.Equal(["c"], deck.AllRemoved().Keys);
        deck.ForgetRemoved("c");
        Assert.Null(deck.RemovedAt("c"));
    }

    [Fact]
    public void ABatchThatFailsWritesNothing()
    {
        var deck = new SqliteDeckStore(_db);
        Assert.Throws<InvalidOperationException>(() => deck.Batch<int>(() =>
        {
            deck.Put(new Card { Word = "a", Meaning = "x" });
            throw new InvalidOperationException();
        }));
        Assert.Null(deck.Get("a"));
    }

    [Fact]
    public void TheJournalKeepsTheNewestCheckFirstInTextOrder()
    {
        var journal = new SqliteMistakeStore(_db);
        journal.Record([new("articles", "a", "the", "", 1), new("spelling", "teh", "the", "", 1)], now: 1, keepRecent: 3);
        journal.Record([new("articles", "x", "y", "", 2), new("prepositions", "on", "in", "", 2)], now: 2, keepRecent: 3);
        var j = journal.Read();
        Assert.Equal(2, j.Checks);
        Assert.Equal(1, j.Since);
        Assert.Equal(2, j.Counts["articles"]);
        Assert.Equal(["x", "on", "a"], j.Recent.Select(m => m.Original));

        journal.Replace(new MistakeJournal(9, new Dictionary<string, int> { ["plurals"] = 4 }, [new("plurals", "p", "q", "", 5)], 7));
        Assert.Equal((9, 7L, "p"), (journal.Read().Checks, journal.Read().Since, journal.Read().Recent[0].Original));
        Assert.False(journal.Read().Counts.ContainsKey("articles"));
    }

    [Fact]
    public void ActivityCountsDaysAndForgetsOldOnes()
    {
        var activity = new SqliteActivityStore(_db);
        var day = new DateOnly(2026, 4, 24);
        activity.RaiseTo(day.AddDays(-90), 3);
        Assert.Equal(1, activity.Add(day, oldestKept: day.AddDays(-59)));
        Assert.Equal(2, activity.Add(day, oldestKept: day.AddDays(-59)));
        Assert.Equal([day], activity.All().Keys);
        activity.RaiseTo(day, 1);
        Assert.Equal(2, activity.Get(day));
        activity.RaiseTo(day, 5);
        Assert.Equal(5, activity.Get(day));
    }

    [Fact]
    public void TheErrorLogKeepsTheLatest()
    {
        var log = new SqliteErrorLogStore(_db);
        for (var i = 0; i < 5; i++)
        {
            log.Add(new ErrorLogEntry(i, "lookup", "network"), keep: 3);
        }

        Assert.Equal([4L, 3L, 2L], log.Recent().Select(e => e.Time));
    }
}
