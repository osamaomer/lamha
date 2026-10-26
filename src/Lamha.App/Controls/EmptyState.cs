using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lamha.App.Controls;

/// <summary>
/// The design system's empty state (docs/design/system.md §6): a 40 icon in the tertiary colour, a line, and one button
/// (the accent one: the way on), centred. A title over the line where the screen has one (Review's "No words yet").
/// </summary>
public sealed partial class EmptyState : StackPanel
{
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(Glyph), typeof(EmptyState), new PropertyMetadata(Glyph.None, (d, _) => ((EmptyState)d).Draw()));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(EmptyState), new PropertyMetadata("", (d, _) => ((EmptyState)d).Draw()));

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(EmptyState), new PropertyMetadata("", (d, _) => ((EmptyState)d).Draw()));

    public static readonly DependencyProperty ActionProperty =
        DependencyProperty.Register(nameof(Action), typeof(string), typeof(EmptyState), new PropertyMetadata("", (d, _) => ((EmptyState)d).Draw()));

    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(EmptyState), new PropertyMetadata(null, (d, _) => ((EmptyState)d).Draw()));

    private readonly FluentIcon _icon = new();
    private readonly TextBlock _title = new() { HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };
    private readonly TextBlock _text = new() { HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };
    private readonly Button _button = new() { HorizontalAlignment = HorizontalAlignment.Center };

    public EmptyState()
    {
        var resources = Application.Current.Resources;
        Spacing = (double)resources["LamhaSpace2"];
        HorizontalAlignment = HorizontalAlignment.Center;
        _icon.Style = (Style)resources["LamhaEmptyIconStyle"]; // a style's ThemeResource follows the window's theme
        _title.Style = (Style)resources["LamhaSectionHeadingTextStyle"];
        _text.Style = (Style)resources["LamhaBodyTextStyle"];
        _text.TextReadingOrder = _title.TextReadingOrder = TextReadingOrder.UseFlowDirection;
        _button.Style = (Style)resources["LamhaButtonStyle"];
        _button.Margin = new Thickness(0, (double)resources["LamhaSpace1"], 0, 0);
        Children.Add(_icon);
        Children.Add(_title);
        Children.Add(_text);
        Children.Add(_button);
        Draw();
    }

    public Glyph Glyph
    {
        get => (Glyph)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>A title over the line; none when empty.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>The button's words; no button when empty.</summary>
    public string Action
    {
        get => (string)GetValue(ActionProperty);
        set => SetValue(ActionProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    private void Draw()
    {
        _icon.Glyph = Glyph;
        _icon.Visibility = Glyph == Glyph.None ? Visibility.Collapsed : Visibility.Visible;
        _title.Text = Title;
        _title.Visibility = Title.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _text.Text = Text;
        _button.Content = Action;
        _button.Command = Command;
        _button.Visibility = Action.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
