using Lamha.Core.Reading;
using Lamha.Data.Database;

namespace Lamha.Core.Tests.Reading;

public sealed class ReaderLookTests : IDisposable
{
    private readonly LamhaDatabase _db = LamhaDatabase.InMemory();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void TheDesignsLookIsTheDefault()
    {
        var look = new ReaderLookStore(new SqliteStateStore(_db)).Get();

        Assert.Equal(new ReaderLook("sitka", 18, 1.7, "normal", "lamha"), look);
        Assert.Equal(620, look.ColumnWidth);
    }

    [Fact]
    public void TheLookIsKeptForEveryBook()
    {
        var state = new SqliteStateStore(_db);
        new ReaderLookStore(state).Save(new ReaderLook("georgia", 22, 2.0, "wide", "paper"));

        var again = new ReaderLookStore(state).Get();

        Assert.Equal(new ReaderLook("georgia", 22, 2.0, "wide", "paper"), again);
        Assert.Equal(760, again.ColumnWidth);
    }

    [Fact]
    public void TheSizeStepsByTwoBetween14And28()
    {
        Assert.Equal(20, new ReaderLook().Resized(1).Size);
        Assert.Equal(16, new ReaderLook().Resized(-1).Size);
        Assert.Equal(28, new ReaderLook(Size: 28).Resized(1).Size);
        Assert.Equal(14, new ReaderLook(Size: 14).Resized(-1).Size);
    }

    [Fact]
    public void AnUnknownOrDamagedLookFallsBackToTheDefaults()
    {
        var state = new SqliteStateStore(_db);
        state.Set("reader.look", """{"font":"comic sans","size":99,"lineSpacing":3,"width":"huge","page":"pink"}""");
        Assert.Equal(new ReaderLook("sitka", 28, 1.7, "normal", "lamha"), new ReaderLookStore(state).Get());

        state.Set("reader.look", "not json");
        Assert.Equal(new ReaderLook(), new ReaderLookStore(state).Get());

        state.Set("reader.look", """{"size":17}""");
        Assert.Equal(16, new ReaderLookStore(state).Get().Size); // off the steps: the step below
    }
}
