using Lamha.Windows.Selection;
using Lamha.Windows.Shell;

namespace Lamha.Windows.Tests.Selection;

/// <summary>
/// The reader's safe failures: reading another program's text needs that program and a desktop, which is the user's
/// hand check (Notepad, Word, a browser).
/// </summary>
public sealed class SentenceReaderTests : IDisposable
{
    private readonly SentenceReader _reader = new();

    public void Dispose() => _reader.Dispose();

    [Fact]
    public async Task NoWindowOrNoTextReadsNothing()
    {
        Assert.Null(await _reader.AroundAsync(0, "bank", TimeSpan.FromSeconds(2)));
        Assert.Null(await _reader.AroundAsync(1234, "  \n ", TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task AWindowWithoutTheSelectionReadsNothing()
    {
        using var window = new MessageWindow();
        Assert.Null(await _reader.AroundAsync(window.Handle, "bank", TimeSpan.FromSeconds(5)));
        Assert.Null(await _reader.AroundAsync(window.Handle, "bank", TimeSpan.FromSeconds(5))); // and again, on the same worker
    }

    [Fact]
    public void BothSidesCompareTextWithWhitespaceCollapsed() => Assert.Equal("look up to", SentenceReader.Norm(" look\n up\t to "));
}
