using Lamha.App.Services;
using Lamha.App.ViewModels.Reader;
using Lamha.Core.Reading;
using Lamha.Core.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
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

    private static readonly (string Color, string Highlight, string Dot, string Name)[] Colors =
    [
        ("yellow", "LamhaHighlightYellowBrush", "LamhaHighlightYellowDotBrush", "Reader_Yellow"),
        ("green", "LamhaHighlightGreenBrush", "LamhaHighlightGreenDotBrush", "Reader_Green"),
        ("blue", "LamhaHighlightBlueBrush", "LamhaHighlightBlueDotBrush", "Reader_Blue"),
        ("pink", "LamhaHighlightPinkBrush", "LamhaHighlightPinkDotBrush", "Reader_Pink"),
    ];

    private RichTextBlock? _text;
    private ReaderBlock? _watched;

    // The list marker's length ("•" and its space) before the block's own text, in the rich text's characters.
    private int _offset;

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
    public RichTextBlock? Text => _text;

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

    // A recycled view follows its new block's changes (a highlight, a translation) and no longer the old one's.
    private static void OnBlockChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (BookBlockView)d;
        if (view._watched is { } old)
        {
            old.Changed -= view.Refresh;
        }

        view._watched = e.NewValue as ReaderBlock;
        if (view._watched is { } block)
        {
            block.Changed += view.Refresh;
        }

        view.Show(view._watched);
    }

    // A pointer in the rich text as a character of the block's text, or null (the list marker, a paragraph's edge).
    private int? IndexOf(TextPointer? pointer) =>
        pointer?.Parent is Run run && _starts.TryGetValue(run, out var start) ? start + Math.Max(0, pointer.Offset - run.ContentStart.Offset) : null;

    // What's selected, as characters of the block's text, and its words.
    private (int Start, int End, string Text)? Selected()
    {
        if (_text is not { } text || text.SelectedText.Length == 0 || IndexOf(text.SelectionStart) is not { } a || IndexOf(text.SelectionEnd) is not { } b)
        {
            return null;
        }

        var (start, end) = (Math.Min(a, b), Math.Max(a, b));
        return end > start ? (start, end, text.SelectedText.Trim()) : null;
    }

    private static Style StyleOf(string key) => (Style)Application.Current.Resources[key];

    private static double Space(string key) => (double)Application.Current.Resources[key];

    /// <summary>Draws the block again: Aa changed the look.</summary>
    public void Refresh() => Show(Block);

    private void Show(ReaderBlock? item)
    {
        _starts.Clear();
        _text = null;
        _offset = 0;
        Content = null;
        if (item is null)
        {
            return;
        }

        var block = item.Block;
        var look = item.Look();
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
                Background = look.Line ?? ThemeLookup.Get(this, "LamhaLineBrush") as Brush,
            },
            _ => Words(item, look),
        };
    }

    // The block's text, its highlights and the selection's toolbar; and its translation under it when there's one.
    private FrameworkElement Words(ReaderBlock item, BookLook look)
    {
        var text = _text = TextOf(item.Block, look);
        foreach (var (color, highlight, _, _) in Colors)
        {
            var ranges = item.Marks.Where(m => m.Color == color && m.End <= item.Block.Text.Length).ToList();
            if (ranges.Count == 0)
            {
                continue;
            }

            var highlighter = new TextHighlighter { Background = ThemeLookup.Get(this, highlight) as Brush };
            foreach (var mark in ranges)
            {
                highlighter.Ranges.Add(new TextRange { StartIndex = _offset + mark.Start, Length = mark.End - mark.Start });
            }

            text.TextHighlighters.Add(highlighter);
        }

        var notes = item.Marks.Where(m => m.Note.Length > 0).Select(m => m.Note).ToList();
        if (notes.Count > 0)
        {
            ToolTipService.SetToolTip(text, string.Join("\n", notes));
        }

        // Windows shows a text's selection toolbar for touch and pen only: for the mouse, it opens when the button is let go
        // over a selection, at the pointer, without taking the focus (the selection stays).
        var bar = Toolbar(item);
        text.SelectionFlyout = bar;
        text.AddHandler(PointerReleasedEvent, new PointerEventHandler((_, e) =>
        {
            if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse)
            {
                var at = e.GetCurrentPoint(text).Position;
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (Selected() is not null)
                    {
                        bar.ShowAt(text, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                        {
                            Position = at,
                            ShowMode = Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowMode.Transient,
                            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Top,
                        });
                    }
                });
            }
        }), handledEventsToo: true);
        if (item.Translation == TranslationState.None)
        {
            return text;
        }

        var stack = new StackPanel { Spacing = Space("LamhaSpace3") };
        stack.Children.Add(text);
        stack.Children.Add(TranslationBox(item, look));
        return stack;
    }

    // The selection toolbar (docs/design/reader.html): the four highlight colours, then Note, Translate, Copy, and
    // Remove the highlight when the selection has one. Windows' own text toolbar, so it opens as a selection ends.
    private CommandBarFlyout Toolbar(ReaderBlock item)
    {
        var bar = new CommandBarFlyout { AlwaysExpanded = true };
        foreach (var (color, _, dot, name) in Colors)
        {
            var button = new AppBarButton
            {
                LabelPosition = CommandBarLabelPosition.Collapsed,
                Label = Localization.Format("Reader_HighlightIn", Localization.Get(name)),
                Icon = new PathIcon
                {
                    Data = new EllipseGeometry { Center = new Point(8, 8), RadiusX = 7, RadiusY = 7 },
                    Foreground = (Brush)Application.Current.Resources[dot],
                },
            };
            ToolTipService.SetToolTip(button, button.Label);
            button.Click += (_, _) => Do(bar, item, SelectionAction.Highlight, color);
            bar.PrimaryCommands.Add(button);
        }

        bar.PrimaryCommands.Add(new AppBarSeparator());
        bar.PrimaryCommands.Add(Command(bar, item, Glyph.Note, "Reader_Note", SelectionAction.Note));
        bar.PrimaryCommands.Add(Command(bar, item, Glyph.Translate, "Reader_Translate", SelectionAction.Translate));
        bar.PrimaryCommands.Add(Command(bar, item, Glyph.Copy, "Common_Copy", SelectionAction.Copy));
        var remove = Command(bar, item, Glyph.Erase, "Reader_RemoveHighlight", SelectionAction.RemoveHighlight);
        bar.PrimaryCommands.Add(remove);
        bar.Opening += (_, _) => remove.Visibility = Selected() is { } s && item.Marks.Any(m => m.Start < s.End && m.End > s.Start)
            ? Visibility.Visible
            : Visibility.Collapsed;
        return bar;
    }

    private AppBarButton Command(CommandBarFlyout bar, ReaderBlock item, Glyph glyph, string label, SelectionAction action)
    {
        var button = new AppBarButton
        {
            LabelPosition = CommandBarLabelPosition.Collapsed,
            Label = Localization.Get(label),
            Icon = new FontIcon { Glyph = FluentIcon.Code(glyph) },
        };
        ToolTipService.SetToolTip(button, button.Label);
        button.Click += (_, _) => Do(bar, item, action, "");
        return button;
    }

    private void Do(CommandBarFlyout bar, ReaderBlock item, SelectionAction action, string color)
    {
        var selected = Selected();
        bar.Hide();
        if (selected is not { } s)
        {
            return;
        }

        if (action == SelectionAction.Note)
        {
            AskNote(item, s);
            return;
        }

        _text?.Select(_text.ContentStart, _text.ContentStart);
        item.Act(new SelectionRequest(action, s.Start, s.End, s.Text, color));
    }

    // The note's box under the selection (the design's: a field, Cancel and Save); saved, it's a yellow highlight with it.
    private void AskNote(ReaderBlock item, (int Start, int End, string Text) selected)
    {
        if (_text is not { } text)
        {
            return;
        }

        var box = new TextBox
        {
            Style = (Style)Application.Current.Resources["LamhaFieldStyle"],
            PlaceholderText = Localization.Get("Reader_NotePlaceholder"),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = Space("LamhaSpace10") * 2,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, Localization.Get("Reader_Note"));
        var save = new Button { Style = (Style)Application.Current.Resources["LamhaButtonStyle"], Content = Localization.Get("Reader_Save") };
        var cancel = new Button { Style = (Style)Application.Current.Resources["LamhaQuietButtonStyle"], Content = Localization.Get("Common_Cancel") };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = Space("LamhaSpace2") };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        var panel = new StackPanel { Width = Space("LamhaSpace10") * 7, Spacing = Space("LamhaSpace3") };
        panel.Children.Add(box);
        panel.Children.Add(buttons);
        var flyout = new Flyout { Content = panel, Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom };
        cancel.Click += (_, _) => flyout.Hide();
        save.Click += (_, _) =>
        {
            flyout.Hide();
            if (box.Text.Trim().Length > 0)
            {
                item.Act(new SelectionRequest(SelectionAction.Note, selected.Start, selected.End, selected.Text, "yellow", box.Text.Trim()));
            }
        };
        flyout.Opened += (_, _) => box.Focus(FocusState.Programmatic);
        flyout.ShowAt(text);
    }

    // A paragraph's translation under it (the design's box): "Translation", Copy and Close, then the text, or a line
    // while it's on its way, or why it couldn't be.
    private Border TranslationBox(ReaderBlock item, BookLook look)
    {
        var copy = IconButton(Glyph.Copy, "Common_Copy");
        copy.Click += (_, _) => item.Act(new SelectionRequest(SelectionAction.Copy, 0, 0, item.TranslationText));
        copy.Visibility = item.Translation == TranslationState.Shown ? Visibility.Visible : Visibility.Collapsed;
        var close = IconButton(Glyph.Close, "Common_Close");
        close.Click += (_, _) => item.Act(new SelectionRequest(SelectionAction.Translate, 0, 0, ""));

        var head = new Grid { ColumnSpacing = Space("LamhaSpace2") };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new FluentIcon { Glyph = Glyph.Translate, Size = IconSize.Small, VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { Style = StyleOf("LamhaCaptionTextStyle"), Text = Localization.Get("Reader_Translation"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(title, 1);
        Grid.SetColumn(copy, 2);
        Grid.SetColumn(close, 3);
        head.Children.Add(icon);
        head.Children.Add(title);
        head.Children.Add(copy);
        head.Children.Add(close);

        var body = new StackPanel { Spacing = Space("LamhaSpace1") };
        body.Children.Add(head);
        if (item.Translation == TranslationState.Waiting)
        {
            body.Children.Add(new Skeleton { Height = Space("LamhaSpace3"), Margin = new Thickness(0, Space("LamhaSpace2"), Space("LamhaSpace10") * 2, Space("LamhaSpace2")) });
        }
        else
        {
            var words = new TextBlock
            {
                Style = StyleOf(item.Translation == TranslationState.Failed ? "LamhaSecondaryTextStyle" : "LamhaReadingTextStyle"),
                Text = item.TranslationText,
                IsTextSelectionEnabled = true,
                FlowDirection = Scripts.IsArabicText(item.TranslationText) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            };
            if (item.Translation == TranslationState.Shown)
            {
                words.FontSize = look.TextSize;
                words.LineHeight = Math.Round(look.TextSize * 1.8);
            }

            if (look.Ink is { } ink)
            {
                words.Foreground = ink;
                title.Foreground = icon.Foreground = look.Muted;
            }

            body.Children.Add(words);
        }

        return new Border
        {
            Child = body,
            Background = look.Fill ?? ThemeLookup.Get(this, "LamhaChipBrush") as Brush,
            CornerRadius = (CornerRadius)Application.Current.Resources["LamhaRadiusControl"],
            Padding = new Thickness(Space("LamhaSpace4"), Space("LamhaSpace2"), Space("LamhaSpace2"), Space("LamhaSpace4")),
        };
    }

    private static Button IconButton(Glyph glyph, string label)
    {
        var button = new Button { Style = StyleOf("LamhaIconButtonStyle"), Content = new FluentIcon { Glyph = glyph } };
        ToolTipService.SetToolTip(button, Localization.Get(label));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, Localization.Get(label));
        return button;
    }

    private RichTextBlock TextOf(BookBlock block, BookLook look)
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
            var marker = block.Marker + " ";
            paragraph.Inlines.Add(new Run { Text = marker });
            _offset = marker.Length;
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

        // Aa: the look's face, size, spacing and the page's ink, over the style's.
        var (size, heading) = block.Kind switch
        {
            BlockKind.Heading when block.Level <= 1 => (look.ChapterSize, true),
            BlockKind.Heading => (look.HeadingSize, true),
            BlockKind.Preformatted => (look.FixedSize, false),
            _ => (look.TextSize, false),
        };
        if (block.Kind != BlockKind.Preformatted)
        {
            text.FontFamily = look.Font;
        }

        text.FontSize = size;
        text.LineHeight = look.LineHeight(size, heading);
        if (look.Ink is { } ink)
        {
            text.Foreground = ink;
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
