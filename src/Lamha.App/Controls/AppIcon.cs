using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Lamha.App.Controls;

/// <summary>
/// Lamha's own icon, as Windows shows it in the taskbar and Start (Assets/Lamha-256.png), at <see cref="Size"/>: the
/// card's bar and the main window's pane header (docs/design/system.md). Its art fills 240 of the picture's 256, so a
/// 64 box shows it at 60. Decoded at the size it's shown, and never mirrored in Arabic.
/// </summary>
public sealed partial class AppIcon : UserControl
{
    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(nameof(Size), typeof(double), typeof(AppIcon), new PropertyMetadata(0.0, (d, _) => ((AppIcon)d).Draw()));

    private static readonly Uri Picture = new("ms-appx:///Assets/Lamha-256.png");

    private readonly Image _image = new() { Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform };

    public AppIcon()
    {
        IsTabStop = false;
        FlowDirection = FlowDirection.LeftToRight;
        Content = _image;
    }

    /// <summary>The box, in design pixels.</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    private void Draw()
    {
        Width = Height = Size;
        _image.Source = new BitmapImage(Picture) { DecodePixelWidth = (int)Math.Ceiling(Size), DecodePixelType = DecodePixelType.Logical };
    }
}
