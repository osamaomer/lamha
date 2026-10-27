using System.Windows.Input;
using Lamha.App.Controls;
using Lamha.App.ViewModels.Main;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Lamha.App.Views;

/// <summary>A paragraph's answer as two columns (<see cref="SideBySideAnswer"/>), on the Translate page.</summary>
public sealed partial class SideBySideView : UserControl
{
    public static readonly DependencyProperty SideProperty =
        DependencyProperty.Register(nameof(Side), typeof(SideBySideAnswer), typeof(SideBySideView), new PropertyMetadata(null, (d, _) => ((SideBySideView)d).Bindings.Update()));

    public static readonly DependencyProperty CopyCommandProperty =
        DependencyProperty.Register(nameof(CopyCommand), typeof(ICommand), typeof(SideBySideView), new PropertyMetadata(null, (d, _) => ((SideBySideView)d).Bindings.Update()));

    public SideBySideView()
    {
        InitializeComponent();
    }

    public SideBySideAnswer? Side
    {
        get => (SideBySideAnswer?)GetValue(SideProperty);
        set => SetValue(SideProperty, value);
    }

    /// <summary>The translation's Copy (the answer's own, which says "Copied").</summary>
    public ICommand? CopyCommand
    {
        get => (ICommand?)GetValue(CopyCommandProperty);
        set => SetValue(CopyCommandProperty, value);
    }

    /// <summary>The sentence under the pointer and its translation light up together (the drawing's mark, behind both).</summary>
    private void OnRowEntered(object sender, PointerRoutedEventArgs e) => Mark(sender, on: true);

    private void OnRowExited(object sender, PointerRoutedEventArgs e) => Mark(sender, on: false);

    private static void Mark(object row, bool on)
    {
        if (row is not Grid grid)
        {
            return;
        }

        foreach (var cell in grid.Children.OfType<Border>())
        {
            if (on)
            {
                cell.Background = ThemeLookup.Get(cell, "LamhaMarkBrush") as Brush; // in the window's theme, as History's marks
                if (cell.Child is TextBlock text)
                {
                    text.Foreground = ThemeLookup.Get(cell, "LamhaMarkTextBrush") as Brush;
                }
            }
            else
            {
                cell.ClearValue(Border.BackgroundProperty);
                cell.Child?.ClearValue(TextBlock.ForegroundProperty);
            }
        }
    }
}
