using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Lamha.App.Controls;

/// <summary>How a check went: still going, worked, or didn't (Lamha 2's <c>.ai-status</c>, <c>.ok</c>, <c>.bad</c>).</summary>
public enum StatusKind
{
    Neutral,
    Ok,
    Bad,
}

/// <summary>
/// A line saying how a check went ("✓ Key saved.", "Checking the key…", an error), in the colour of how it went, in the
/// element's own theme. Narrator reads it when it changes. Hidden while empty.
/// </summary>
public sealed partial class StatusText : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(StatusText), new PropertyMetadata("", (d, _) => ((StatusText)d).Update()));

    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(nameof(Kind), typeof(StatusKind), typeof(StatusText), new PropertyMetadata(StatusKind.Neutral, (d, _) => ((StatusText)d).Update()));

    private readonly TextBlock _text = new()
    {
        TextWrapping = TextWrapping.Wrap,
        TextReadingOrder = TextReadingOrder.UseFlowDirection,
    };

    public StatusText()
    {
        IsTabStop = false;
        _text.Style = (Style)Application.Current.Resources["LamhaCaptionTextStyle"];
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(_text, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        Content = _text;
        ActualThemeChanged += (_, _) => Update();
        Update();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public StatusKind Kind
    {
        get => (StatusKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    private void Update()
    {
        _text.Text = Text;
        Visibility = string.IsNullOrEmpty(Text) ? Visibility.Collapsed : Visibility.Visible;
        var key = Kind switch
        {
            StatusKind.Ok => "LamhaOkBrush",
            StatusKind.Bad => "LamhaDangerBrush",
            _ => "LamhaTextMutedBrush",
        };
        if (ThemeLookup.Get(this, key) is Brush brush)
        {
            _text.Foreground = brush;
        }

        _text.FontWeight = Kind == StatusKind.Neutral ? Microsoft.UI.Text.FontWeights.Normal : Microsoft.UI.Text.FontWeights.SemiBold;
    }
}
