using System.Net;
using System.Text.RegularExpressions;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace Lamha.App.Controls;

/// <summary>
/// Lamha 2's strings with their little HTML, word for word, on a TextBlock: <c>&lt;b&gt;</c> bold, <c>&lt;a href&gt;</c>
/// a link that opens the browser, <c>&lt;code&gt;</c> a command on a line of its own, left to right, <c>&lt;br&gt;</c> a
/// new line. Anything else is dropped. Set <c>controls:Markup.Text</c> instead of Text.
/// </summary>
public static partial class Markup
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached("Text", typeof(string), typeof(Markup), new PropertyMetadata("", (d, e) =>
        {
            var block = (TextBlock)d;
            if (e.OldValue is null or "")
            {
                // A link's colour is the theme's: drawn again when the theme changes (Settings → Appearance).
                block.ActualThemeChanged += (_, _) => Fill(block, GetText(block));
            }

            Fill(block, (string)e.NewValue);
        }));

    private static readonly FontFamily Code = new("Consolas");

    public static string GetText(TextBlock block) => (string)block.GetValue(TextProperty);

    public static void SetText(TextBlock block, string value) => block.SetValue(TextProperty, value);

    private static void Fill(TextBlock block, string? html)
    {
        block.Inlines.Clear();
        var bold = false;
        Hyperlink? link = null;
        var code = false;
        foreach (Match part in Parts().Matches(html ?? ""))
        {
            if (part.Groups["tag"].Success)
            {
                var closing = part.Groups["close"].Length > 0;
                switch (part.Groups["tag"].Value.ToLowerInvariant())
                {
                    case "b":
                        bold = !closing;
                        break;
                    case "a" when !closing && Uri.TryCreate(WebUtility.HtmlDecode(part.Groups["href"].Value), UriKind.Absolute, out var uri)
                                              && uri.Scheme == Uri.UriSchemeHttps:
                        // The design's .tip a: Lamha's accent, semibold, no underline.
                        link = new Hyperlink { NavigateUri = uri, UnderlineStyle = UnderlineStyle.None, FontWeight = FontWeights.SemiBold };
                        if (ThemeLookup.Get(block, "LamhaAccentBrush") is Brush accent)
                        {
                            link.Foreground = accent;
                        }

                        block.Inlines.Add(link);
                        break;
                    case "a":
                        link = null;
                        break;
                    case "code":
                        code = !closing;
                        block.Inlines.Add(new LineBreak());
                        break;
                    case "br":
                        block.Inlines.Add(new LineBreak());
                        break;
                }

                continue;
            }

            // A command reads left to right in Arabic too: a left-to-right mark keeps its punctuation in place.
            var run = new Run { Text = WebUtility.HtmlDecode(part.Value) + (code ? "‎" : "") };
            if (bold)
            {
                run.FontWeight = FontWeights.SemiBold;
            }

            if (code)
            {
                run.FontFamily = Code;
            }

            (link?.Inlines ?? block.Inlines).Add(run);
        }
    }

    [GeneratedRegex(@"<(?<close>/?)(?<tag>\w+)(?:[^>]*?href=""(?<href>[^""]*)"")?[^>]*>|[^<]+")]
    private static partial Regex Parts();
}
