using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Lamha.App.Controls;

/// <summary>
/// Lamha's mark: Translate (or another icon) in white on the brand gradient, in one of two sizes: 16 beside a window's
/// name (the mini translator's title bar), 56 for first run's big moments (docs/design/system.md). The card and the main
/// window show Lamha's own icon instead (<see cref="AppIcon"/>). The gradient is the implicit
/// style's Background in Themes/Controls.xaml, so it follows the theme (Windows' highlight in high contrast).
/// </summary>
public sealed partial class BrandMark : UserControl
{
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(Glyph), typeof(BrandMark), new PropertyMetadata(Glyph.Translate, (d, _) => ((BrandMark)d).Draw()));

    public static readonly DependencyProperty IsLargeProperty =
        DependencyProperty.Register(nameof(IsLarge), typeof(bool), typeof(BrandMark), new PropertyMetadata(false, (d, _) => ((BrandMark)d).Draw()));

    private readonly Border _tile = new();
    private readonly FontIcon _icon = new() { Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), IsTextScaleFactorEnabled = false };

    public BrandMark()
    {
        IsTabStop = false;
        _icon.FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"];
        _tile.Child = _icon;
        Content = _tile;
        RegisterPropertyChangedCallback(BackgroundProperty, (_, _) => _tile.Background = Background);
        Draw();
    }

    /// <summary>The icon on the mark: Translate, or Check when first run is done.</summary>
    public Glyph Glyph
    {
        get => (Glyph)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>56 instead of 16.</summary>
    public bool IsLarge
    {
        get => (bool)GetValue(IsLargeProperty);
        set => SetValue(IsLargeProperty, value);
    }

    private void Draw()
    {
        // 16: the icon 10 (Windows' title bar icon); 56: the icon 32, Windows' overlay corners.
        Width = Height = IsLarge ? 56 : 16;
        _tile.CornerRadius = new CornerRadius(IsLarge ? 8 : 4);
        _icon.FontSize = IsLarge ? 32 : 10;
        _icon.Glyph = FluentIcon.Code(Glyph);
    }
}
