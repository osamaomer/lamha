using Lamha.App.Controls;
using Lamha.App.Services;
using Lamha.App.ViewModels.Reader;
using Lamha.Core.Dictionary;
using Lamha.Windows.Shell;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Lamha.App.Windows;

/// <summary>
/// A book's window (<see cref="ReaderViewModel"/>), in its own process (decision 0022). It opens at once with "Opening the
/// book…", then shows the book where the reader was, or why it couldn't open. A click on a word opens the card on it,
/// with its sentence; a drag selects as usual. The place is kept a moment after scrolling stops, and when it closes.
/// </summary>
public sealed partial class ReaderWindow : Window
{
    /// <summary>Its size when it opens, at 100 % (the design's 1100 × 700).</summary>
    public const int StartWidth = 1100;
    public const int StartHeight = 700;

    /// <summary>How long after scrolling stops the place is kept.</summary>
    private static readonly TimeSpan KeepAfter = TimeSpan.FromSeconds(2);

    private readonly DispatcherQueueTimer _keep;
    private bool _placed;

    public ReaderWindow(bool mica)
    {
        InitializeComponent();
        Root.FlowDirection = Localization.FlowDirection;
        SystemBackdrop = mica ? new MicaBackdrop() : null;
        Solid.Visibility = mica ? Visibility.Collapsed : Visibility.Visible;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Lamha.ico"));
        Handle = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        if (Localization.IsArabic)
        {
            Screens.LayOutRightToLeft(Handle);
        }

        Title = MessageTitle = Localization.Get("Reader_Opening");
        Page.AddHandler(UIElement.TappedEvent, new TappedEventHandler(OnTapped), handledEventsToo: true);
        _keep = DispatcherQueue.CreateTimer();
        _keep.Interval = KeepAfter;
        _keep.IsRepeating = false;
        _keep.Tick += (_, _) => ViewModel?.Save();
        Closed += (_, _) =>
        {
            _keep.Stop();
            ViewModel?.Save();
        };
    }

    public ReaderViewModel? ViewModel { get; private set; }

    public nint Handle { get; }

    public string BookTitle { get; private set; } = "";

    public string BookAuthor { get; private set; } = "";

    public string MessageTitle { get; private set; }

    public string MessageText { get; private set; } = "";

    public string AaLabel { get; } = Localization.Get("Reader_Aa");

    public string AaGlyph { get; } = Localization.Get("Reader_AaGlyph");

    public string FontLabel { get; } = Localization.Get("Reader_Font");

    public string SitkaLabel { get; } = Localization.Get("Reader_FontSitka");

    public string GeorgiaLabel { get; } = Localization.Get("Reader_FontGeorgia");

    public string SegoeLabel { get; } = Localization.Get("Reader_FontSegoe");

    public string SizeLabel { get; } = Localization.Get("Reader_Size");

    public string SmallerLabel { get; } = Localization.Get("Reader_Smaller");

    public string LargerLabel { get; } = Localization.Get("Reader_Larger");

    public string SmallerGlyph { get; } = Localization.Get("Reader_SmallerGlyph");

    public string LargerGlyph { get; } = Localization.Get("Reader_LargerGlyph");

    public string SpacingLabel { get; } = Localization.Get("Reader_Spacing");

    public string Spacing15 { get; } = Localization.Get("Reader_Spacing15");

    public string Spacing17 { get; } = Localization.Get("Reader_Spacing17");

    public string Spacing20 { get; } = Localization.Get("Reader_Spacing20");

    public string WidthLabel { get; } = Localization.Get("Reader_Width");

    public string NarrowLabel { get; } = Localization.Get("Reader_Narrow");

    public string NormalLabel { get; } = Localization.Get("Reader_Normal");

    public string WideLabel { get; } = Localization.Get("Reader_Wide");

    public string PageLabel { get; } = Localization.Get("Reader_Page");

    public string PageLamhaLabel { get; } = Localization.Get("Reader_PageLamha");

    public string PagePaperLabel { get; } = Localization.Get("Reader_PagePaper");

    public string PageBlackLabel { get; } = Localization.Get("Reader_PageBlack");

    /// <summary>A word was clicked: the word, its sentence.</summary>
    public event Action<string, WordContext>? WordClicked;

    /// <summary>Settings → Appearance → Theme: "light", "dark", or "auto" (Windows').</summary>
    public void UseTheme(string theme)
    {
        Root.RequestedTheme = theme switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        AppWindow.TitleBar.PreferredTheme = theme switch
        {
            "light" => TitleBarTheme.Light,
            "dark" => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode,
        };
    }

    /// <summary>Opens in the middle of the screen the mouse is on, at the design's size (or the screen's, when smaller).</summary>
    public void ShowCentred()
    {
        var cursor = Screens.Cursor;
        var area = Screens.WorkAreaAt(cursor);
        var scale = Screens.ScaleAt(cursor);
        var width = (int)Math.Min(StartWidth * scale, area.Width);
        var height = (int)Math.Min(StartHeight * scale, area.Height);
        AppWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(area.X + ((area.Width - width) / 2), area.Y + ((area.Height - height) / 2), width, height));
        Activate();
        ForegroundWindow.ForceForeground(Handle);
    }

    /// <summary>The book, opened: shown where the reader was.</summary>
    public void Show(ReaderViewModel viewModel)
    {
        ViewModel = viewModel;
        BookTitle = viewModel.Title;
        BookAuthor = viewModel.Author;
        Title = viewModel.Window;
        Bindings.Update();
        Message.Visibility = Visibility.Collapsed;
        Footer.Visibility = Visibility.Visible;
        AaButton.Visibility = Visibility.Visible;
        ApplyLook(redraw: false);
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ReaderViewModel.Look))
            {
                ApplyLook(redraw: true);
            }
        };
        ViewModel.ReadingAt(viewModel.StartBlock);
        Page.Loaded += (_, _) => GoToStart();

        // A short part (a title page) leaves nothing to scroll, and so no scrolling to load the next: the page is filled
        // ahead each time it changes size.
        Page.SizeChanged += (_, _) => DispatcherQueue.TryEnqueue(FillAhead);
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, FillAhead);
        GoToStart();
    }

    /// <summary>The book couldn't be opened: why, and what to do.</summary>
    public void ShowProblem(string title, string text)
    {
        MessageTitle = title;
        MessageText = text;
        Title = title;
        Bindings.Update();
    }

    // Aa's look on the page: its colour and controls' theme, the column's width, the footer's ink and line, and the blocks
    // drawn again (keeping the block at the top where it is).
    private void ApplyLook(bool redraw)
    {
        if (ViewModel is not { } model)
        {
            return;
        }

        var look = model.Look;
        var top = redraw ? TopBlock() : null;
        Scroller.RequestedTheme = Footer.RequestedTheme = look.Theme;
        Page.MaxWidth = look.ColumnWidth;
        Paper.Background = look.Page;
        Paper.BorderBrush = look.Line;
        Paper.BorderThickness = new Thickness(0, look.Page is null ? 0 : 1, 0, 0);
        Footer.BorderBrush = look.Line ?? ThemeLookup.Get(Footer, "LamhaLineBrush") as Brush;
        SetOrClear(ChapterText, TextBlock.ForegroundProperty, look.Muted);
        SetOrClear(ProgressText, TextBlock.ForegroundProperty, look.Muted);
        if (!redraw)
        {
            return;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(Page); i++)
        {
            (VisualTreeHelper.GetChild(Page, i) as BookBlockView)?.Refresh();
        }

        if (top is not null && model.Blocks.IndexOf(top) is >= 0 and var index)
        {
            Page.UpdateLayout();
            Page.GetOrCreateElement(index).StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0, AnimationDesired = false });
        }
    }

    // A brush from Aa, or the style's (the theme's) when Aa has none for this page.
    private static void SetOrClear(DependencyObject element, DependencyProperty property, Brush? brush)
    {
        if (brush is null)
        {
            element.ClearValue(property);
        }
        else
        {
            element.SetValue(property, brush);
        }
    }

    // Once, when the page has room: the block where the reader was at the top.
    private void GoToStart()
    {
        if (_placed || ViewModel is not { } model || !Page.IsLoaded)
        {
            return;
        }

        _placed = true;
        if (model.StartBlock > 0 && Page.GetOrCreateElement(model.StartBlock) is { } element)
        {
            element.UpdateLayout();
            element.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0, AnimationDesired = false });
        }
    }

    // The next part, while less than three screens are left below what's shown.
    private void FillAhead()
    {
        if (ViewModel is { } model && Scroller.ViewportHeight > 0 && Scroller.VerticalOffset + (Scroller.ViewportHeight * 3) > Scroller.ExtentHeight)
        {
            model.LoadNext();
        }
    }

    /// <summary>Scrolling: the footer follows, a part is loaded before the reader reaches it, and the place is kept soon.</summary>
    private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (ViewModel is not { } model)
        {
            return;
        }

        FillAhead();

        if (Scroller.VerticalOffset < Scroller.ViewportHeight && _placed)
        {
            model.LoadPrevious();
        }

        if (TopBlock() is { } top)
        {
            model.ReadingAt(model.Blocks.IndexOf(top));
        }

        if (!e.IsIntermediate)
        {
            _keep.Stop();
            _keep.Start();
        }
    }

    // The block at the top of the page: the first under the view's top edge, in the column's middle (a gap between two
    // blocks is no block, so a little lower is tried too).
    private ReaderBlock? TopBlock()
    {
        var x = Page.TransformToVisual(null).TransformPoint(new global::Windows.Foundation.Point(Page.ActualWidth / 2, 0)).X;
        var top = Scroller.TransformToVisual(null).TransformPoint(default).Y;
        foreach (var down in new[] { 8, 24, 40 })
        {
            var found = VisualTreeHelper.FindElementsInHostCoordinates(new global::Windows.Foundation.Point(x, top + down), Page).OfType<BookBlockView>().FirstOrDefault();
            if (found?.Block is { } block)
            {
                return block;
            }
        }

        return null;
    }

    /// <summary>A click on a word (not the end of a drag that selected text): the card on it, in its sentence.</summary>
    private void OnTapped(object sender, TappedRoutedEventArgs e)
    {
        if (ViewModel is not { } model)
        {
            return;
        }

        var element = e.OriginalSource as DependencyObject;
        while (element is not null and not BookBlockView)
        {
            element = VisualTreeHelper.GetParent(element);
        }

        if (element is not BookBlockView { Block: { } block } view || view.Text?.SelectedText.Length > 0)
        {
            return;
        }

        if (view.CharacterAt(e.GetPosition(view)) is { } index && model.WordAt(block, index) is { } word)
        {
            WordClicked?.Invoke(word.Word, word.Context);
        }
    }
}
