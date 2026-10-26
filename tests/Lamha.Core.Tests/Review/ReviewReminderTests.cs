using Lamha.Core.Review;
using Lamha.Core.Settings;
using Lamha.Core.Tests.Support;

namespace Lamha.Core.Tests.Review;

/// <summary>The tray's count and Lamha 2's review reminder (main.js <c>setBadge()</c>), and the deck telling it about changes.</summary>
public class ReviewReminderTests
{
    private static TestLamha Lamha(int newPerDay = 3)
    {
        var lamha = new TestLamha(new FakeNet(_ => FakeNet.Json(200, "{}")), new LamhaSettings { UiLang = "ar", CardsNewPerDay = newPerDay });
        lamha.State.Set("deck.imported", "true");
        return lamha;
    }

    private static ReviewReminder Reminder(TestLamha lamha) => new(lamha.Deck, lamha.State, lamha.Settings, lamha.Clock);

    private static void PutDue(TestLamha lamha, string word, long ago = 1000) =>
        lamha.Deck.Put(new Card { Word = word, Meaning = "م", Added = 1, Last = 1, Reps = 1, Interval = 1, Due = lamha.Now - ago });

    private static void PutNew(TestLamha lamha, string word, long added = 1) =>
        lamha.Deck.Put(new Card { Word = word, Meaning = "م", Added = added });

    [Fact]
    public async Task TheCountIsReviewsDueAndNewWordsUpToTheDaysLimit()
    {
        using var lamha = Lamha(newPerDay: 3);
        PutDue(lamha, "bank");
        PutDue(lamha, "river");
        lamha.Deck.Put(new Card { Word = "later", Meaning = "م", Added = 1, Last = 1, Reps = 1, Interval = 3, Due = lamha.Now + Scheduler.Day });
        for (var i = 0; i < 5; i++)
        {
            PutNew(lamha, "new" + i, i);
        }

        var reminder = Reminder(lamha);
        reminder.Recount(mayRemind: false);
        var queue = await lamha.Review.QueueAsync();
        Assert.Equal(5, reminder.Waiting); // 2 due + 3 of the 5 new
        Assert.Equal(queue.Due + queue.New, reminder.Waiting);

        // A new word answered counts toward the day's limit.
        await lamha.Review.GradeAsync("new4", Grade.Good);
        reminder.Recount(mayRemind: false);
        Assert.Equal(4, reminder.Waiting);
    }

    [Fact]
    public void ARemindersAtMostEvery4HoursWhenMoreWordsWaitAndOnlyWhenAllowed()
    {
        using var lamha = Lamha();
        var reminder = Reminder(lamha);
        Assert.False(reminder.Recount(mayRemind: true)); // nothing waiting

        PutDue(lamha, "bank");
        Assert.False(reminder.Recount(mayRemind: false)); // the window is in use, or Lamha is off
        Assert.Equal(1, reminder.Waiting);
        Assert.True(reminder.Recount(mayRemind: true)); // none since Lamha started

        PutDue(lamha, "river");
        Assert.False(reminder.Recount(mayRemind: true)); // more words, but within 4 hours

        lamha.Clock.Advance(TimeSpan.FromHours(4) + TimeSpan.FromMinutes(1));
        Assert.False(reminder.Recount(mayRemind: true)); // 4 hours on, but no more words than at the last count

        PutDue(lamha, "stone");
        Assert.True(reminder.Recount(mayRemind: true));
    }

    [Fact]
    public void ANotificationUsedCountsAsAReminder()
    {
        using var lamha = Lamha();
        PutDue(lamha, "bank");
        var reminder = Reminder(lamha);
        reminder.Reminded(); // a click on a notification started Lamha
        Assert.False(reminder.Recount(mayRemind: true));

        PutDue(lamha, "river");
        lamha.Clock.Advance(TimeSpan.FromHours(3));
        Assert.False(reminder.Recount(mayRemind: true)); // more words, but within 4 hours of it
        PutDue(lamha, "stone");
        lamha.Clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(1));
        Assert.True(reminder.Recount(mayRemind: true));
    }

    [Fact]
    public async Task TheFirstWordIsTheLongestWaitingDueOneElseTheNewestNew()
    {
        using var lamha = Lamha(newPerDay: 1);
        var reminder = Reminder(lamha);
        Assert.Null(reminder.First());

        PutNew(lamha, "old", added: 1);
        PutNew(lamha, "fresh", added: 2);
        Assert.Equal("fresh", reminder.First()?.Word);

        PutDue(lamha, "bank", ago: 1000);
        PutDue(lamha, "river", ago: 5000);
        Assert.Equal("river", reminder.First()?.Word);

        // Today's one new word is used up: only reviews remain.
        await lamha.Review.GradeAsync("fresh", Grade.Good);
        await lamha.Review.GradeAsync("river", Grade.Good);
        await lamha.Review.GradeAsync("bank", Grade.Good);
        Assert.Null(reminder.First());
        Assert.Equal("old", reminder.Find("old")?.Word);
    }

    [Fact]
    public async Task TheDeckSaysWhenItChanges()
    {
        using var lamha = Lamha();
        var changes = 0;
        lamha.Review.Changed += () => changes++;

        await lamha.Review.AddAsync(new CardDraft("bank", "ضفة"));
        Assert.Equal(1, changes);
        await lamha.Review.GradeAsync("bank", Grade.Good);
        Assert.Equal(2, changes);
        await lamha.Review.ToggleAsync(new CardDraft("river", "نهر"));
        await lamha.Review.RemoveAsync("river");
        Assert.Equal(4, changes);
        var backup = await lamha.Backup.ExportAsync();
        await lamha.Review.ClearAsync();
        Assert.Equal(5, changes);
        await lamha.Backup.ImportAsync(backup);
        Assert.Equal(6, changes);
        await lamha.Review.QueueAsync(); // reading doesn't count
        Assert.Equal(6, changes);
    }
}
