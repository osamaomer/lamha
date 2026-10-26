using Lamha.App.Services;
using Lamha.Core.Appearance;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Lamha.App.Controls;

/// <summary>Lamha's icons, by name.</summary>
public enum Glyph
{
    None,
    Speak,
    SpeakSmall,
    Copy,
    Close,
    Settings,
    Back,
    Forward,
    External,
    Search,
    Translate,
    Check,
    Retry,
    Book,
    Sparkle,
    Bookmark,
    Pin,
    Write,
    Review,
    History,
    Clipboard,
    Trash,
    Pause,
    Card,
    Keyboard,
    Globe,
    Palette,
    Shield,
    Info,
    Folder,
    Down,
    List,
    Text,
    Chevron,
    Journal,

    // Settings' cards (step 4: each setting card has its icon, as in Windows' Settings)
    Swap,
    Currency,
    Timer,
    PaymentCard,
    Apps,
    SaveCopy,
    Bug,
    Update,
    Power,
    Calendar,
    Goal,
    Download,
    Motion,
    Brush,
    Picture,
}

/// <summary>The design system's icon sizes (docs/design/system.md, Icons).</summary>
public enum IconSize
{
    /// <summary>12: chevrons, the small × in a field, a pinned mark.</summary>
    Small,

    /// <summary>16: buttons, rows, menus.</summary>
    Normal,

    /// <summary>20: setting cards, the pane.</summary>
    Large,

    /// <summary>40: an empty state, first run's big moments.</summary>
    Hero,
}

/// <summary>
/// One of Lamha's icons, drawn from Windows' own icon font (Segoe Fluent Icons) in the foreground colour, at one of the
/// design system's sizes (decision 0020; Lamha 2's line icons before it). They read the same in both languages, except
/// Back and Forward, which point the way the text goes.
/// </summary>
public sealed partial class FluentIcon : UserControl
{
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(Glyph), typeof(FluentIcon), new PropertyMetadata(Glyph.None, (d, _) => ((FluentIcon)d).Draw()));

    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(nameof(Size), typeof(IconSize), typeof(FluentIcon), new PropertyMetadata(IconSize.Normal, (d, _) => ((FluentIcon)d).Resize()));

    public static readonly DependencyProperty FilledProperty =
        DependencyProperty.Register(nameof(Filled), typeof(bool), typeof(FluentIcon), new PropertyMetadata(false, (d, _) => ((FluentIcon)d).Draw()));

    public static readonly DependencyProperty WavingProperty =
        DependencyProperty.Register(nameof(Waving), typeof(bool), typeof(FluentIcon), new PropertyMetadata(false, (d, _) => ((FluentIcon)d).Wave()));

    /// <summary>Each name's glyph in Segoe Fluent Icons (system.md's map, checked by drawing them on Windows 11).</summary>
    private static readonly Dictionary<Glyph, char> Glyphs = new()
    {
        [Glyph.Speak] = '', // Volume
        [Glyph.SpeakSmall] = '',
        [Glyph.Copy] = '',
        [Glyph.Close] = '', // Cancel
        [Glyph.Settings] = '',
        [Glyph.Back] = '',
        [Glyph.Forward] = '',
        [Glyph.External] = '', // OpenInNewWindow
        [Glyph.Search] = '',
        [Glyph.Translate] = '', // LocaleLanguage
        [Glyph.Check] = '', // CheckMark
        [Glyph.Retry] = '', // Refresh
        [Glyph.Book] = '', // Dictionary
        [Glyph.Sparkle] = '',
        [Glyph.Bookmark] = '', // FavoriteStar: Fluent has no bookmark (decided at the system's sign-off)
        [Glyph.Pin] = '',
        [Glyph.Write] = '', // Edit
        [Glyph.Review] = '', // TaskView: two cards
        [Glyph.History] = '',
        [Glyph.Clipboard] = '', // Paste
        [Glyph.Trash] = '', // Delete
        [Glyph.Pause] = '',
        [Glyph.Card] = '', // Frame
        [Glyph.Keyboard] = '', // KeyboardClassic
        [Glyph.Globe] = '',
        [Glyph.Palette] = '', // Color
        [Glyph.Shield] = '',
        [Glyph.Info] = '',
        [Glyph.Folder] = '',
        [Glyph.Down] = '', // ChevronDown
        [Glyph.List] = '', // BulletedList
        [Glyph.Text] = '', // Font
        [Glyph.Chevron] = '', // ChevronRight: a breadcrumb's step, a setting card that opens a page
        [Glyph.Journal] = '', // Education: the mistake journal
        [Glyph.Swap] = '', // Switch: Arabic text translated to English
        [Glyph.Currency] = '', // Calculator: prices in your currency
        [Glyph.Timer] = '', // Stopwatch: how long copies are kept
        [Glyph.PaymentCard] = '', // PaymentCard: bank card numbers skipped
        [Glyph.Apps] = '', // AllApps: programs left out
        [Glyph.SaveCopy] = '', // SaveCopy: a copy of my data
        [Glyph.Bug] = '', // Bug: report a problem
        [Glyph.Update] = '', // Sync: automatic updates
        [Glyph.Power] = '', // PowerButton: start with Windows
        [Glyph.Calendar] = '', // Calendar: new words per day
        [Glyph.Goal] = '', // Target: the day's goal
        [Glyph.Download] = '', // Download: dictionaries to download
        [Glyph.Motion] = '', // LightningBolt: animations (system.html's)
        [Glyph.Brush] = '', // Personalize: the accent colour from Windows
        [Glyph.Picture] = '', // Picture: the wallpaper behind the window
    };

    /// <summary>The filled glyph, where the icon shows a state: a word in review, a pinned copy.</summary>
    private static readonly Dictionary<Glyph, char> FilledGlyphs = new()
    {
        [Glyph.Bookmark] = '', // FavoriteStarFill
        [Glyph.Pin] = '', // Pinned
    };

    /// <summary>Speak's waves, one after another while Listen plays: Volume1, Volume2, Volume3.</summary>
    private static readonly char[] Waves = ['', '', ''];

    private readonly FontIcon _font = new() { IsTextScaleFactorEnabled = false };

    /// <summary>Whose foreground the icon is drawn in: the button's around it, or null for its own.</summary>
    private FrameworkElement? _colour;
    private long _watch;
    private DispatcherQueueTimer? _waveTimer;
    private int _wave;

    public FluentIcon()
    {
        IsTabStop = false;
        _font.FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"];
        Content = _font;
        Resize();
        RegisterPropertyChangedCallback(ForegroundProperty, (_, _) => Paint());
        Loaded += (_, _) =>
        {
            Watch();
            Wave();
        };
        Unloaded += (_, _) =>
        {
            Unwatch();
            StopWaving();
        };
    }

    public Glyph Glyph
    {
        get => (Glyph)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>One of the design system's sizes; the icon's box is its size square.</summary>
    public IconSize Size
    {
        get => (IconSize)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>The filled glyph (a word in review, a pinned copy), where the icon has one.</summary>
    public bool Filled
    {
        get => (bool)GetValue(FilledProperty);
        set => SetValue(FilledProperty, value);
    }

    /// <summary>
    /// Speak's waves grow one after the other while Listen plays (Lamha 2's <c>lm-wave</c>, 0.9 s a round); still when
    /// animations are off.
    /// </summary>
    public bool Waving
    {
        get => (bool)GetValue(WavingProperty);
        set => SetValue(WavingProperty, value);
    }

    /// <summary>The glyph of <paramref name="glyph"/>, as text for a FontIcon.</summary>
    public static string Code(Glyph glyph) => Glyphs.TryGetValue(glyph, out var code) ? code.ToString() : "";

    /// <summary>An icon size in pixels.</summary>
    public static double Pixels(IconSize size) => size switch
    {
        IconSize.Small => 12,
        IconSize.Large => 20,
        IconSize.Hero => 40,
        _ => 16,
    };

    private void Resize()
    {
        Width = Height = _font.FontSize = Pixels(Size);
    }

    private void Draw()
    {
        // Only the arrows and the chevron follow the text's direction.
        _font.MirroredWhenRightToLeft = Glyph is Glyph.Back or Glyph.Forward or Glyph.Chevron;
        _font.Glyph = Glyph == Glyph.None ? "" : (Filled && FilledGlyphs.TryGetValue(Glyph, out var filled) ? filled : Glyphs[Glyph]).ToString();
        Wave();
    }

    private void Wave()
    {
        var on = Waving && Glyph is Glyph.Speak or Glyph.SpeakSmall && Animations.Level != MotionLevel.Off && IsLoaded;
        if (!on)
        {
            StopWaving();
            return;
        }

        if (_waveTimer is not null)
        {
            return;
        }

        _waveTimer = DispatcherQueue.CreateTimer();
        _waveTimer.Interval = TimeSpan.FromMilliseconds(300); // three steps a round, Lamha 2's 0.9 s
        _waveTimer.Tick += (_, _) => _font.Glyph = Waves[_wave = (_wave + 1) % Waves.Length].ToString();
        _waveTimer.Start();
    }

    private void StopWaving()
    {
        _waveTimer?.Stop();
        _waveTimer = null;
        _wave = 0;
        if (Glyph is Glyph.Speak or Glyph.SpeakSmall)
        {
            _font.Glyph = Glyphs[Glyph].ToString();
        }
    }

    private void Paint()
    {
        _font.Foreground = _colour switch
        {
            ContentPresenter presenter => presenter.Foreground,
            Control control => control.Foreground,
            _ => Foreground,
        };
    }

    /// <summary>
    /// An icon without a colour of its own takes its button's, and follows it (Pin's accent when on, a disabled
    /// button's, the window's theme): its own Foreground is the app's theme's default, which is white in a light card
    /// when Windows is dark, where the button's text is the card's grey. A callback, not a Binding: Bindings made in
    /// code cost the published card 1 MB.
    /// </summary>
    private void Watch()
    {
        Unwatch();
        _colour = ReadLocalValue(ForegroundProperty) == DependencyProperty.UnsetValue ? Button() : null;
        _watch = _colour switch
        {
            ContentPresenter presenter => presenter.RegisterPropertyChangedCallback(ContentPresenter.ForegroundProperty, (_, _) => Paint()),
            Control control => control.RegisterPropertyChangedCallback(ForegroundProperty, (_, _) => Paint()),
            _ => 0,
        };
        Paint();
    }

    private void Unwatch()
    {
        switch (_colour)
        {
            case ContentPresenter presenter:
                presenter.UnregisterPropertyChangedCallback(ContentPresenter.ForegroundProperty, _watch);
                break;
            case Control control:
                control.UnregisterPropertyChangedCallback(ForegroundProperty, _watch);
                break;
        }

        _colour = null;
    }

    /// <summary>The content presenter (or control) the icon is drawn in: its button's.</summary>
    private FrameworkElement? Button()
    {
        for (var parent = VisualTreeHelper.GetParent(this); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is ContentPresenter or Control)
            {
                return (FrameworkElement)parent;
            }
        }

        return null;
    }
}
