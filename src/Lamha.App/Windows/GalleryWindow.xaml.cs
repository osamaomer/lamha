using Lamha.App.Controls;
using Lamha.App.Services;
using Lamha.App.ViewModels.Main;
using Lamha.Windows.Shell;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace Lamha.App.Windows;

/// <summary>
/// <c>--gallery</c>: the design system live (docs/design/system.md), every component from the system's own styles as
/// the screens use them, in the interface language and theme asked for and with a switch to the other theme, for
/// checking a change in one place and for screenshots. A development window: the tray never opens it.
/// </summary>
public sealed partial class GalleryWindow : Window
{
    /// <summary>Its size at 100 %: Settings' width, tall enough for a few groups.</summary>
    private const int StartWidth = 1000;
    private const int StartHeight = 800;

    /// <summary>Windows' type ramp, then Lamha's content styles (system.md §2), each shown in itself by its name.</summary>
    private static readonly string[] TypeStyles =
    [
        "CaptionTextBlockStyle", "BodyTextBlockStyle", "BodyStrongTextBlockStyle", "BodyLargeTextBlockStyle", "SubtitleTextBlockStyle",
        "TitleTextBlockStyle", "TitleLargeTextBlockStyle", "DisplayTextBlockStyle",
        "LamhaHeadwordTextStyle", "LamhaHeroTextStyle", "LamhaReadingTextStyle",
    ];

    // The reader's book text (RichTextBlock styles), each shown on a line of Pride and Prejudice.
    private static readonly string[] BookStyles =
    [
        "LamhaBookChapterTextStyle", "LamhaBookHeadingTextStyle", "LamhaBookTextStyle", "LamhaBookFixedTextStyle",
    ];

    /// <summary>Lamha's colours for meaning, then Windows' for the frame (system.md §3).</summary>
    private static readonly string[] Brushes =
    [
        "LamhaAccentBrush", "LamhaButtonBrush", "LamhaAccentSoftBrush", "LamhaOkBrush", "LamhaDangerBrush", "LamhaMarkBrush",
        "LamhaBrandBrush", "LamhaBrandBarBrush", "LamhaChipBrush", "LamhaHoverBrush",
        "TextFillColorPrimaryBrush", "TextFillColorSecondaryBrush", "TextFillColorTertiaryBrush", "CardBackgroundFillColorDefaultBrush",
        "CardStrokeColorDefaultBrush", "SubtleFillColorSecondaryBrush", "ControlFillColorDefaultBrush", "DividerStrokeColorDefaultBrush",
        "LayerFillColorDefaultBrush",
    ];

    public GalleryWindow(string theme)
    {
        InitializeComponent();
        Title = Localization.Get("Gallery_Title");
        Root.FlowDirection = Localization.FlowDirection;
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragArea);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Lamha.ico"));
        var handle = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        if (Localization.IsArabic)
        {
            Screens.LayOutRightToLeft(handle);
        }

        DarkSwitch.IsOn = theme == "dark" || (theme != "light" && Application.Current.RequestedTheme == ApplicationTheme.Dark);
        UseTheme(DarkSwitch.IsOn);
        Fill();
        AppWindow.Changed += (_, e) =>
        {
            if (e.DidSizeChange)
            {
                FitCaption();
            }
        };
        Root.Loaded += (_, _) => FitCaption();
    }

    public string DarkLabel { get; } = Localization.Get("Gallery_Dark");

    public string Intro { get; } = Localization.Get("Gallery_Intro");

    public string TypeHeading { get; } = Localization.Get("Gallery_Type");

    public string ColoursHeading { get; } = Localization.Get("Gallery_Colours");

    public string IconsHeading { get; } = Localization.Get("Gallery_Icons");

    public string ButtonsHeading { get; } = Localization.Get("Gallery_Buttons");

    public string FieldsHeading { get; } = Localization.Get("Gallery_Fields");

    public string CardsHeading { get; } = Localization.Get("Gallery_Cards");

    public string MessagesHeading { get; } = Localization.Get("Gallery_Messages");

    public string ProgressHeading { get; } = Localization.Get("Gallery_Progress");

    public string DisabledLabel { get; } = Localization.Get("Gallery_Disabled");

    // ----- the components' content: Lamha's own strings, so each language shows its real text -----

    public string Proofread { get; } = Localization.Get("Tool_Proofread");

    public string Copy { get; } = Localization.Get("Common_Copy");

    public string SetUp { get; } = Localization.Get("Write_SetUp");

    public string Rewrite { get; } = Localization.Get("Write_Rewrite");

    public string Delete { get; } = Localization.Get("Common_Delete");

    public string Clear { get; } = Localization.Get("Common_Clear");

    public string Listen { get; } = Localization.Get("Settings_AutoSpeak");

    public string Pinned { get; } = Localization.Get("Clipboard_Pinned");

    public string All { get; } = Localization.Get("Clipboard_All");

    public string Translate { get; } = Localization.Get("Main_Translate");

    public string Placeholder { get; } = Localization.Get("Translate_Placeholder");

    public string SearchPlaceholder { get; } = Localization.Get("Clipboard_Search");

    public string Arabic { get; } = Localization.Get("Lang_ar");

    public string English { get; } = Localization.Get("Lang_en");

    public List<string> LanguageNames { get; } = [Localization.Get("Lang_ar"), Localization.Get("Lang_en")];

    public string PasteChip { get; } = Localization.Get("Translate_PasteChip");

    public string UpAuto { get; } = Localization.Get("Settings_UpAuto");

    public string UpAutoHint { get; } = Localization.Get("Settings_UpAutoHint");

    public string UiLang { get; } = Localization.Get("Settings_UiLang");

    public string UiLangHint { get; } = Localization.Get("Settings_UiLangHint");

    public string Languages { get; } = Localization.Get("Settings_Languages");

    public string PrivacyMore { get; } = Localization.Get("Settings_PrivacyMore");

    public string SetupTitle { get; } = Localization.Get("Write_SetupTitle");

    public string SetupText { get; } = Localization.Get("Write_SetupText");

    public string Copied { get; } = Localization.Get("Card_Copied");

    public string Paused { get; } = Localization.Get("Clipboard_PausedText");

    public string Problem { get; } = Localization.Get("Card_ErrFailed");

    public string EmptyTitle { get; } = Localization.Get("Review_EmptyTitle");

    public string EmptyText { get; } = Localization.Get("Review_EmptyText");

    public string SampleText { get; } = Localization.Get("Card_SampleText");

    public string Lamha { get; } = Localization.Get("Common_Lamha");

    public string Version { get; } = Localization.Format("Main_Version", UpdateService.Shown(UpdateService.Current));

    public string TitleSearchPlaceholder { get; } = Localization.Get("Search_Placeholder");

    /// <summary>Opens in the middle of the screen the mouse is on, at its size.</summary>
    public void ShowCentred()
    {
        var cursor = Screens.Cursor;
        var area = Screens.WorkAreaAt(cursor);
        var scale = Screens.ScaleAt(cursor);
        var width = (int)Math.Min(StartWidth * scale, area.Width);
        var height = (int)Math.Min(StartHeight * scale, area.Height);
        AppWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(area.X + ((area.Width - width) / 2), area.Y + ((area.Height - height) / 2), width, height));
        Activate();
    }

    private void OnDarkToggled(object sender, RoutedEventArgs e) => UseTheme(DarkSwitch.IsOn);

    private void UseTheme(bool dark)
    {
        Root.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
        AppWindow.TitleBar.PreferredTheme = dark ? TitleBarTheme.Dark : TitleBarTheme.Light;
    }

    /// <summary>What's drawn from lists: the type ramp, the colours, every icon, the keys.</summary>
    private void Fill()
    {
        foreach (var key in TypeStyles)
        {
            TypeList.Children.Add(new TextBlock { Style = (Style)Application.Current.Resources[key], Text = key, TextWrapping = TextWrapping.NoWrap });
        }

        foreach (var key in BookStyles)
        {
            var book = new RichTextBlock { Style = (Style)Application.Current.Resources[key], MaxWidth = (double)Application.Current.Resources["LamhaBookMeasure"] };
            var line = new Microsoft.UI.Xaml.Documents.Paragraph();
            line.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = $"{key}: It is a truth universally acknowledged, that a single man in possession of a good fortune must be in want of a wife." });
            book.Blocks.Add(line);
            TypeList.Children.Add(book);
        }

        // Drawn as XAML so each colour follows the theme switch, as a screen's ThemeResource does.
        foreach (var key in Brushes)
        {
            Swatches.Children.Add((UIElement)XamlReader.Load(
                "<StackPanel xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Width='128' Spacing='{StaticResource LamhaSpace1}'>"
                + $"<Border Height='48' CornerRadius='{{StaticResource LamhaRadiusControl}}' BorderThickness='1' BorderBrush='{{ThemeResource CardStrokeColorDefaultBrush}}' Background='{{ThemeResource {key}}}' />"
                + $"<TextBlock Style='{{StaticResource LamhaCaptionTextStyle}}' TextWrapping='Wrap' Text='{key}' /></StackPanel>"));
        }

        foreach (var glyph in Enum.GetValues<Glyph>().Where(g => g != Glyph.None))
        {
            var tile = new StackPanel { Width = 96, Spacing = Number("LamhaSpace2"), Padding = new Thickness(Number("LamhaSpace2")) };
            tile.Children.Add(new FluentIcon { Glyph = glyph, HorizontalAlignment = HorizontalAlignment.Center });
            tile.Children.Add(new TextBlock { Style = (Style)Application.Current.Resources["LamhaCaptionTextStyle"], Text = glyph.ToString(), HorizontalAlignment = HorizontalAlignment.Center });
            Icons.Children.Add(tile);
        }

        Keys.Keys = [new KeyPart("Ctrl", true), new KeyPart("+", false), new KeyPart("Alt", true), new KeyPart("+", false), new KeyPart("L", true)];
    }

    private static double Number(string key) => (double)Application.Current.Resources[key];

    /// <summary>Windows' buttons sit over the title bar's end: the theme's switch stops before them.</summary>
    private void FitCaption()
    {
        if (Root.XamlRoot is { RasterizationScale: > 0 } root)
        {
            var inset = Math.Max(AppWindow.TitleBar.LeftInset, AppWindow.TitleBar.RightInset);
            CaptionInset.Width = new GridLength(inset / root.RasterizationScale);
        }
    }
}
