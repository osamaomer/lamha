using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClipboardApi = Lamha.Windows.Clipboard.Clipboard;

namespace Lamha.Windows.Tests.Clipboard;

/// <summary>Windows' "HTML Format" as the clipboard panel writes it: the header's byte offsets must point at the HTML.</summary>
public sealed class HtmlFormatTests
{
    [Fact]
    public void ADocumentsBodyIsTheFragment()
    {
        var (document, fragment) = Read(ClipboardApi.CfHtml("<html><head><meta charset=\"utf-8\"></head><body><p>لمحة <b>Lamha</b></p></body></html>"));
        Assert.StartsWith("<html>", document, StringComparison.Ordinal);
        Assert.EndsWith("</html>", document, StringComparison.Ordinal);
        Assert.Equal("<p>لمحة <b>Lamha</b></p>", fragment);
    }

    [Fact]
    public void HtmlWithoutABodyIsWrappedInOne()
    {
        var (document, fragment) = Read(ClipboardApi.CfHtml("<i>مرحبًا</i> there"));
        Assert.Equal("<i>مرحبًا</i> there", fragment);
        Assert.Contains("<body>", document, StringComparison.Ordinal);
    }

    [Fact]
    public void ItEndsInAZero()
    {
        var bytes = ClipboardApi.CfHtml("<b>x</b>");
        Assert.Equal(0, bytes[^1]);
    }

    /// <summary>The document and the fragment the header's offsets give, as a program reading the clipboard finds them.</summary>
    private static (string Document, string Fragment) Read(byte[] bytes)
    {
        var header = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 200));
        int Offset(string key) => int.Parse(Regex.Match(header, key + @":(\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
        var (start, end) = (Offset("StartHTML"), Offset("EndHTML"));
        var (fragmentStart, fragmentEnd) = (Offset("StartFragment"), Offset("EndFragment"));
        Assert.Equal(bytes.Length - 1, end);
        return (Encoding.UTF8.GetString(bytes, start, end - start), Encoding.UTF8.GetString(bytes, fragmentStart, fragmentEnd - fragmentStart));
    }
}
