using Lamha.App.ViewModels.Main;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lamha.App.Controls;

/// <summary>
/// A shortcut drawn as key caps joined by "+" (Ctrl + Alt + L): the design system's key cap (docs/design/system.md §6,
/// Themes/Controls.xaml's LamhaKeyCapStyle), the same in the Translate page, Settings and first run. Keys read left to
/// right in Arabic too.
/// </summary>
public sealed partial class KeyCaps : StackPanel
{
    public static readonly DependencyProperty KeysProperty =
        DependencyProperty.Register(nameof(Keys), typeof(object), typeof(KeyCaps), new PropertyMetadata(null, (d, _) => ((KeyCaps)d).Draw()));

    public KeyCaps()
    {
        Orientation = Orientation.Horizontal;
        FlowDirection = FlowDirection.LeftToRight;
        VerticalAlignment = VerticalAlignment.Center;
        Spacing = (double)Application.Current.Resources["LamhaSpace1"];
    }

    public IReadOnlyList<KeyPart>? Keys
    {
        get => (IReadOnlyList<KeyPart>?)GetValue(KeysProperty);
        set => SetValue(KeysProperty, value);
    }

    private void Draw()
    {
        Children.Clear();
        foreach (var part in Keys ?? [])
        {
            var text = new TextBlock { Text = part.Text, VerticalAlignment = VerticalAlignment.Center };
            if (part.IsKey)
            {
                text.Style = (Style)Application.Current.Resources["LamhaKeyCapTextStyle"];
                Children.Add(new Border { Style = (Style)Application.Current.Resources["LamhaKeyCapStyle"], Child = text });
            }
            else
            {
                text.Style = (Style)Application.Current.Resources["LamhaCaptionTextStyle"];
                Children.Add(text);
            }
        }
    }
}
