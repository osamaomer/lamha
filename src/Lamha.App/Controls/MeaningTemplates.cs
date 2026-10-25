using Lamha.App.ViewModels.Card;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lamha.App.Controls;

/// <summary>Picks how a line of meanings draws each item: a part of speech, a chip, a sense's hint, or "+3".</summary>
public sealed partial class MeaningTemplates : DataTemplateSelector
{
    public DataTemplate? Pos { get; set; }

    public DataTemplate? Chip { get; set; }

    public DataTemplate? Hint { get; set; }

    public DataTemplate? More { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item switch
    {
        PosItem => Pos,
        ChipItem => Chip,
        HintItem => Hint,
        _ => More,
    };

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
