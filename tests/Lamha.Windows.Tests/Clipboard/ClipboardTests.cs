using Lamha.Windows.Shell;
using ClipboardApi = Lamha.Windows.Clipboard.Clipboard;

namespace Lamha.Windows.Tests.Clipboard;

/// <summary>
/// These use the real clipboard, so they run one at a time and put back whatever was on it before.
/// </summary>
[Collection(nameof(ClipboardCollection))]
public sealed class ClipboardTests : IDisposable
{
    private readonly MessageWindow _owner = new();
    private readonly Lamha.Windows.Clipboard.ClipboardSnapshot _before = null!;

    public ClipboardTests()
    {
        // A sandboxed shell or a service account has no clipboard: say so instead of failing.
        if (!ClipboardApi.Open(_owner.Handle))
        {
            _owner.Dispose();
            Assert.Skip("The clipboard can't be opened here (a sandbox or a session without a desktop).");
        }

        global::Windows.Win32.PInvoke.CloseClipboard();
        _before = ClipboardApi.Save(_owner.Handle);
    }

    public void Dispose()
    {
        if (_before.IsComplete)
        {
            ClipboardApi.Restore(_owner.Handle, _before);
        }

        _owner.Dispose();
    }

    [Fact]
    public void TextRoundTrips()
    {
        Assert.True(ClipboardApi.WriteText(_owner.Handle, "لمحة · Lamha", keepOutOfHistory: false));
        Assert.Equal("لمحة · Lamha", ClipboardApi.ReadText(_owner.Handle));
    }

    [Fact]
    public void AWriteChangesTheSequenceNumber()
    {
        var before = ClipboardApi.SequenceNumber;
        ClipboardApi.WriteText(_owner.Handle, "bank", keepOutOfHistory: false);
        Assert.NotEqual(before, ClipboardApi.SequenceNumber);
    }

    [Fact]
    public void ASnapshotPutsTheUsersClipboardBack()
    {
        ClipboardApi.WriteText(_owner.Handle, "the user's text", keepOutOfHistory: false);
        var saved = ClipboardApi.Save(_owner.Handle);
        Assert.True(saved.IsComplete);

        ClipboardApi.WriteText(_owner.Handle, "Lamha's text", keepOutOfHistory: true);
        Assert.True(ClipboardApi.Restore(_owner.Handle, saved));
        Assert.Equal("the user's text", ClipboardApi.ReadText(_owner.Handle));
    }

    [Fact]
    public void LamhasOwnCopiesAreKeptOutOfClipboardHistory()
    {
        ClipboardApi.WriteText(_owner.Handle, "for a paste", keepOutOfHistory: true);
        var snapshot = ClipboardApi.Save(_owner.Handle);
        var history = ClipboardApi.Register("CanIncludeInClipboardHistory");
        var flag = Assert.Single(snapshot.Formats, f => f.Format == history);
        Assert.Equal(new byte[4], flag.Data);
    }

    [Fact]
    public void ACopyFromTheHistoryGoesBackWithItsFormattingAndStaysOutOfClipboardHistory()
    {
        Assert.True(ClipboardApi.WriteClip(_owner.Handle, "bold لمحة", "<html><body><b>bold</b> لمحة</body></html>"));
        Assert.Equal("bold لمحة", ClipboardApi.ReadText(_owner.Handle));
        Assert.Contains("<b>bold</b> لمحة", ClipboardApi.ReadHtml(_owner.Handle, 4096), StringComparison.Ordinal);
        var history = ClipboardApi.Register("CanIncludeInClipboardHistory");
        Assert.Single(ClipboardApi.Save(_owner.Handle).Formats, f => f.Format == history);
    }

    [Fact]
    public void ACopyAsPlainTextHasNoFormatting()
    {
        Assert.True(ClipboardApi.WriteClip(_owner.Handle, "plain", null));
        Assert.Equal("plain", ClipboardApi.ReadText(_owner.Handle));
        Assert.False(ClipboardApi.HasFormat("HTML Format"));
    }

    [Fact]
    public void TheUsersOwnCopiesStayInClipboardHistory()
    {
        ClipboardApi.WriteText(_owner.Handle, "Copy", keepOutOfHistory: false);
        var history = ClipboardApi.Register("CanIncludeInClipboardHistory");
        Assert.DoesNotContain(ClipboardApi.Save(_owner.Handle).Formats, f => f.Format == history);
    }
}

[CollectionDefinition(nameof(ClipboardCollection), DisableParallelization = true)]
public sealed class ClipboardCollection;
