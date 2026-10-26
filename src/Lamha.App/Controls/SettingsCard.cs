using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Lamha.App.Controls;

/// <summary>
/// A setting as Windows' Settings draws it (docs/design/system.md §6): its icon, name and hint at the start, the control
/// at the end; narrower than Windows' own threshold, the control moves under the text. The look is in
/// Themes/Controls.xaml. Lamha's own rather than CommunityToolkit's SettingsCard: one more package to check under
/// NativeAOT for a border and two lines of text. The control is named after the setting for Narrator, unless it has a
/// name of its own.
/// </summary>
public sealed partial class SettingsCard : ContentControl
{
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingsCard), new PropertyMetadata("", (d, _) => ((SettingsCard)d).Update()));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingsCard), new PropertyMetadata("", (d, _) => ((SettingsCard)d).Update()));

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(Glyph), typeof(SettingsCard), new PropertyMetadata(Glyph.None, (d, _) => ((SettingsCard)d).Update()));

    /// <summary>WinUI's SettingsCardWrapThreshold: below this width the control goes under the text.</summary>
    private const double WrapWidth = 476;

    private TextBlock? _description;
    private FrameworkElement? _icon;
    private FrameworkElement? _control;

    public SettingsCard()
    {
        DefaultStyleKey = typeof(SettingsCard);
        IsTabStop = false;
        SizeChanged += (_, e) => Wrap(e.NewSize.Width);
    }

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>The hint under the name; none when empty.</summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>The icon at the start, at the system's 20; none by default.</summary>
    public Glyph Icon
    {
        get => (Glyph)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _description = GetTemplateChild("Description") as TextBlock;
        _icon = GetTemplateChild("IconBox") as FrameworkElement;
        _control = GetTemplateChild("Control") as FrameworkElement;
        Update();
        Wrap(ActualWidth);
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        Update();
    }

    private void Update()
    {
        if (_description is not null)
        {
            _description.Visibility = string.IsNullOrEmpty(Description) ? Visibility.Collapsed : Visibility.Visible;
        }

        if (_icon is not null)
        {
            _icon.Visibility = Icon == Glyph.None ? Visibility.Collapsed : Visibility.Visible;
        }

        if (Content is Control control && string.IsNullOrEmpty(AutomationProperties.GetName(control)))
        {
            AutomationProperties.SetName(control, Header);
            AutomationProperties.SetHelpText(control, Description);
        }
    }

    /// <summary>Windows' narrow card: the control under the text, where the text starts.</summary>
    private void Wrap(double width)
    {
        if (_control is null || width <= 0)
        {
            return;
        }

        var wrapped = width < WrapWidth;
        Grid.SetRow(_control, wrapped ? 1 : 0);
        Grid.SetColumn(_control, wrapped ? 1 : 2);
        _control.HorizontalAlignment = wrapped ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        _control.Margin = wrapped ? new Thickness(0, (double)Application.Current.Resources["LamhaSpace3"], 0, 0) : new Thickness((double)Application.Current.Resources["LamhaSpace4"], 0, 0, 0);
    }
}
