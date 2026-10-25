using Lamha.App.ViewModels.Card;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lamha.App.Views;

/// <summary>A price in the user's currency (<see cref="PriceAnswer"/>): in the card, and on the Translate page.</summary>
public sealed partial class PriceView : UserControl
{
    public static readonly DependencyProperty PriceProperty =
        DependencyProperty.Register(nameof(Price), typeof(PriceAnswer), typeof(PriceView), new PropertyMetadata(null, (d, _) => ((PriceView)d).Bindings.Update()));

    public PriceView()
    {
        InitializeComponent();
    }

    public PriceAnswer? Price
    {
        get => (PriceAnswer?)GetValue(PriceProperty);
        set => SetValue(PriceProperty, value);
    }
}
