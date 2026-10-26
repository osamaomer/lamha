using Lamha.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Lamha.App.Controls;

/// <summary>
/// Listen: says <see cref="Text"/> in <see cref="Lang"/> (<see cref="Speech"/>), in the accent colour with its waves
/// moving while it does; pressed again it stops (Lamha 2's <c>.playing</c>). <see cref="Round"/>: the card's round
/// button by the headword, with the bigger speaker; otherwise an icon button's.
/// </summary>
public sealed partial class SpeakButton : Button
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(SpeakButton), new PropertyMetadata(""));

    public static readonly DependencyProperty LangProperty =
        DependencyProperty.Register(nameof(Lang), typeof(string), typeof(SpeakButton), new PropertyMetadata("en"));

    public static readonly DependencyProperty RoundProperty =
        DependencyProperty.Register(nameof(Round), typeof(bool), typeof(SpeakButton), new PropertyMetadata(false, (d, _) => ((SpeakButton)d).Shape()));

    private readonly LineIcon _icon = new();

    public SpeakButton()
    {
        Content = _icon;
        Shape();
        Click += (_, _) => Speech.Toggle(this, Text, Lang);
        Loaded += (_, _) =>
        {
            Speech.Changed += Refresh;
            Refresh();
        };
        Unloaded += (_, _) => Speech.Changed -= Refresh;
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>The text's language: "en", "ar"…</summary>
    public string Lang
    {
        get => (string)GetValue(LangProperty);
        set => SetValue(LangProperty, value);
    }

    public bool Round
    {
        get => (bool)GetValue(RoundProperty);
        set => SetValue(RoundProperty, value);
    }

    private void Shape()
    {
        _icon.Glyph = Round ? Glyph.Speak : Glyph.SpeakSmall;
        _icon.Width = _icon.Height = Round ? 17 : 15;
    }

    private void Refresh()
    {
        var playing = ReferenceEquals(Speech.Playing, this);
        _icon.Waving = playing;
        if (playing && !Round && ThemeLookup.Get(this, "LamhaAccentBrush") is Brush accent)
        {
            Foreground = accent; // the round one is in the accent colour already
        }
        else
        {
            ClearValue(ForegroundProperty);
        }
    }
}
