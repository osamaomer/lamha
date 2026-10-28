using Lamha.App.Services;
using Lamha.App.ViewModels.Reader;
using Lamha.Core.Reading;
using Lamha.Core.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace Lamha.App.Controls;

/// <summary>
/// A block of a book on the reading page (the design system's book text: <c>LamhaBookTextStyle</c> and its headings): a
/// paragraph, a heading, a list item or a quotation as selectable rich text in the book's own direction; a picture,
/// decoded at the page's width; a line between scenes. It tells which character of the block's text a point is on, so
/// a click opens the card on that word.
/// </summary>
public sealed partial class BookBlockView : ContentControl
{
    public static readonly DependencyProperty BlockProperty =
        DependencyProperty.Register(nameof(Block), typeof(ReaderBlock), typeof(BookBlockView), new PropertyMetadata(null, OnBlockChanged));

    /// <summary>The widest a picture is decoded: the column's width at 200 %.</summary>
    private const int PictureWidth = 1240;

    // Each run of the block's text and where it starts in BookBlock.Text (the list marker isn't in it).
    private readonly Dictionary<Run, int> _starts = [];

    public BookBlockView()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    public ReaderBlock? Block
    {
        get => (ReaderBlock?)GetValue(BlockProperty);
        set => SetValue(BlockProperty, value);
    }

    /// <summary>The block's text, when it's text: for its selection.</summary>
    public RichTextBlock? Text => Content as RichTextBlock;

    /// <summary>The character of the block's text at <paramref name="point"/> (in this view's coordinates), or null.</summary>
    public int? CharacterAt(Point point)
    {
        if (Text is not { } text)
        {
            return null;
        }

        var at = TransformToVisual(text).TransformPoint(point);
        var pointer = text.GetPositionFromPoint(at);
        return pointer?.Parent is Run run && _starts.TryGetValue(run, out var start)
            ? start + Math.Max(0, pointer.Offset - run.ContentStart.Offset)
            : null;
    }

    private static void OnBlockChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((BookBlockView)d).Show(e.NewValue as ReaderBlock);

    private static Style StyleOf(string key) => (Style)Application.Current.Resources[key];

    private static double Space(string key) => (double)Application.Current.Resources[key];

    private void Show(ReaderBlock? item)
    {
        _starts.Clear();
        Content = null;
        if (item is null)
        {
            return;
        }

        var block = item.Block;
        Margin = new Thickness(0, 0, 0, block.Kind == BlockKind.Heading ? Space("LamhaSpace6") : Space("LamhaSpace4"));
        Content = block.Kind switch
        {
            BlockKind.Image => Picture(item),
            BlockKind.Rule => new Border
            {
                Height = 1,
                Width = Space("LamhaSpace10") * 3,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, Space("LamhaSpace4"), 0, Space("LamhaSpace4")),
                Background = ThemeLookup.Get(this, "LamhaLineBrush") as Brush,
            },
            _ => Words(block),
        };
    }

    private RichTextBlock Words(BookBlock block)
    {
        var text = new RichTextBlock
        {
            Style = StyleOf(block.Kind switch
            {
                BlockKind.Heading when block.Level <= 1 => "LamhaBookChapterTextStyle",
                BlockKind.Heading => "LamhaBookHeadingTextStyle",
                BlockKind.Preformatted => "LamhaBookFixedTextStyle",
                _ => "LamhaBookTextStyle",
            }),

            // The book's own direction, whatever the interface's: an Arabic paragraph reads right to left.
            FlowDirection = Scripts.IsArabicText(block.Text) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
        };

        var indent = (block.Kind == BlockKind.ListItem ? block.Level : 0) + block.Quote;
        if (indent > 0)
        {
            text.Margin = new Thickness(indent * Space("LamhaSpace6"), 0, 0, 0);
        }

        var paragraph = new Paragraph();
        if (block.Marker.Length > 0)
        {
            paragraph.Inlines.Add(new Run { Text = block.Marker + " " });
        }

        var at = 0;
        foreach (var piece in block.Runs)
        {
            var run = new Run { Text = piece.Text };
            _starts[run] = at;
            at += piece.Text.Length;
            if (piece.Style.HasFlag(RunStyle.Italic))
            {
                run.FontStyle = global::Windows.UI.Text.FontStyle.Italic;
            }

            if (piece.Style.HasFlag(RunStyle.Bold))
            {
                run.FontWeight = FontWeights.Bold;
            }

            if (piece.Style.HasFlag(RunStyle.Superscript))
            {
                Typography.SetVariants(run, FontVariants.Superscript);
            }
            else if (piece.Style.HasFlag(RunStyle.Subscript))
            {
                Typography.SetVariants(run, FontVariants.Subscript);
            }

            // A link to the web opens in the browser; a link inside the book waits for the notes' panel.
            if (piece.Link is { } link && Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                var hyperlink = new Hyperlink { NavigateUri = uri };
                hyperlink.Inlines.Add(run);
                paragraph.Inlines.Add(hyperlink);
            }
            else
            {
                paragraph.Inlines.Add(run);
            }
        }

        text.Blocks.Add(paragraph);
        return text;
    }

    private FrameworkElement Picture(ReaderBlock item)
    {
        // As wide as the column at most, and never wider than the picture itself (its pixels as the page's, as a browser does).
        var image = new Image { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
        var alt = item.Block.Text;
        if (alt.Length > 0)
        {
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(image, alt);
        }

        _ = LoadAsync(image, item);
        return image;
    }

    private static async Task LoadAsync(Image image, ReaderBlock item)
    {
        try
        {
            if (item.Block.ImagePath is not { } path || item.Pictures(path) is not { } stream)
            {
                return;
            }

            using var bytes = new MemoryStream();
            using (stream)
            {
                await stream.CopyToAsync(bytes).ConfigureAwait(true);
            }

            // Decoded at its own size, or at the column's when it's wider: a large picture never sits in memory whole.
            var bitmap = new BitmapImage();
            bytes.Position = 0;
            await bitmap.SetSourceAsync(bytes.AsRandomAccessStream());
            if (bitmap.PixelWidth > PictureWidth)
            {
                bitmap = new BitmapImage { DecodePixelWidth = PictureWidth };
                bytes.Position = 0;
                await bitmap.SetSourceAsync(bytes.AsRandomAccessStream());
            }

            if (bitmap.PixelWidth > 0)
            {
                image.MaxWidth = bitmap.PixelWidth;
            }

            image.Source = bitmap;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // An SVG or a damaged picture: the page goes on without it.
            AppLog.Info($"reader: a picture not shown ({ex.GetType().Name})");
        }
    }
}
