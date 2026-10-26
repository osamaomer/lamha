using Lamha.App.Services;
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
/// element's own theme. Narrator reads it when it changes. Hidden while empty. A mistake shakes its
/// <see cref="Field"/> once (the design system's "a field with a mistake shakes", full only).
/// </summary>
public sealed partial class StatusText : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(StatusText), new PropertyMetadata("", (d, _) => ((StatusText)d).Update()));

    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(nameof(Kind), typeof(StatusKind), typeof(StatusText), new PropertyMetadata(StatusKind.Neutral, (d, _) => ((StatusText)d).Update()));

    public static readonly DependencyProperty FieldProperty =
        DependencyProperty.Register(nameof(Field), typeof(UIElement), typeof(StatusText), new PropertyMetadata(null));

    public static readonly DependencyProperty FieldNameProperty =
        DependencyProperty.Register(nameof(FieldName), typeof(string), typeof(StatusText), new PropertyMetadata(""));

    /// <summary>The mistake shaken last, so the same one isn't shaken again when the theme or its kind is read again.</summary>
    private string _shaken = "";

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

    /// <summary>The field the line is about, which shakes when it says a mistake.</summary>
    public UIElement? Field
    {
        get => (UIElement?)GetValue(FieldProperty);
        set => SetValue(FieldProperty, value);
    }

    /// <summary>The field by its x:Name, in a template, where x:Bind can't reach it.</summary>
    public string FieldName
    {
        get => (string)GetValue(FieldNameProperty);
        set => SetValue(FieldNameProperty, value);
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

        var mistake = Kind == StatusKind.Bad ? Text : "";
        if (mistake.Length > 0 && mistake != _shaken && IsLoaded && (Field ?? (FieldName.Length > 0 ? (Parent as FrameworkElement)?.FindName(FieldName) as UIElement : null)) is { } field)
        {
            Animations.Shake(field);
        }

        _shaken = mistake;
    }
}
