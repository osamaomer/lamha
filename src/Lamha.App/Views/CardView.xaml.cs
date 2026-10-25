using System.ComponentModel;
using Lamha.App.Services;
using Lamha.App.ViewModels.Card;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace Lamha.App.Views;

/// <summary>
/// The card's content: the bar, the answer or the writing tools, the links. Its window (Windows/CardWindow) places it
/// and follows <see cref="ContentHeight"/>.
/// </summary>
public sealed partial class CardView : UserControl
{
    private CardViewModel? _viewModel;

    public CardView()
    {
        InitializeComponent();
        Root.FlowDirection = Localization.FlowDirection;
    }

    /// <summary>Set by the window before the card first shows.</summary>
    public CardViewModel ViewModel
    {
        get => _viewModel ?? throw new InvalidOperationException("The card has no view model yet.");
        set
        {
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged -= OnViewModelChanged;
            }

            _viewModel = value;
            _viewModel.PropertyChanged += OnViewModelChanged;
            Bindings.Update();
        }
    }

    /// <summary>The card's natural height changed (its answer arrived, a section opened, the links wrapped).</summary>
    public event Action? ContentResized;

    /// <summary>The user pressed on a pinned card's bar to move it.</summary>
    public event Action? MoveRequested;

    /// <summary>The height the card would like, in effective pixels: the bar, the body and the links.</summary>
    public double ContentHeight =>
        Bar.ActualHeight + Body.ActualHeight + (Foot.Visibility == Visibility.Visible ? Foot.ActualHeight : 0);

    /// <summary>The interface's direction, for lines inside a box that runs right to left in both languages.</summary>
    public FlowDirection InterfaceFlow { get; } = Localization.FlowDirection;

    // The card's own words (x:Bind reads them once).
    public string Lamha { get; } = Localization.Get("Common_Lamha");

    public string DictSwitchName { get; } = Localization.Get("Card_DictSwitch");

    public string ShowAll { get; } = Localization.Get("Card_ShowAll");

    public string DidYouMean { get; } = Localization.Get("Card_DidYouMean");

    public string DidYouMeanEnd { get; } = Localization.Get("Card_DidYouMeanEnd");

    public string OtherMeaningsHeading { get; } = Localization.Get("Card_OtherMeanings");

    public string DefinitionHeading { get; } = Localization.Get("Card_Definition");

    public string ExamplesHeading { get; } = Localization.Get("Card_Examples");

    public string WikipediaHeading { get; } = Localization.Get("Card_Wikipedia");

    public string ReadWiki { get; } = Localization.Get("Card_ReadWiki");

    public string WritingToolsName { get; } = Localization.Get("Card_WritingTools");

    public string CopyLabel { get; } = Localization.Get("Common_Copy");

    public string NoMistakes { get; } = Localization.Get("Card_NoMistakes");

    public string ComposeLabel { get; } = Localization.Get("Card_ComposeLabel");

    public string KindHeading { get; } = Localization.Get("Card_Kind");

    public string ToneHeading { get; } = Localization.Get("Card_Tone");

    /// <summary>Narrator says it (an answer arrived, "Copied").</summary>
    public void Announce(string text)
    {
        if (text.Length > 0 && FrameworkElementAutomationPeer.FromElement(Root) is { } peer)
        {
            peer.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantMostRecent, text, "LamhaCard");
        }
    }

    /// <summary>A solid background instead of the window's Acrylic.</summary>
    public void ShowSolid() => SolidBackground.Visibility = Visibility.Visible;

    /// <summary>A new card starts at the top.</summary>
    public void ScrollToTop() => Scroller.ChangeView(null, 0, null, disableAnimation: true);

    private void OnContentSizeChanged(object sender, SizeChangedEventArgs e) => ContentResized?.Invoke();

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(CardViewModel.Answer):
                ScrollToTop();
                break;
            case nameof(CardViewModel.Writing) when ViewModel.Writing is { } writing:
                writing.PropertyChanged += OnWritingChanged;
                FocusForm(writing);
                break;
        }
    }

    /// <summary>Reply's and Write new's box takes the focus when it shows (Lamha 2's <c>input.focus()</c>).</summary>
    private void OnWritingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WritingPanel.View) && sender is WritingPanel writing && writing == ViewModel.Writing)
        {
            FocusForm(writing);
        }
    }

    private void FocusForm(WritingPanel writing)
    {
        if (writing.IsForm)
        {
            DispatcherQueue.TryEnqueue(() => (FindName("IntentBox") as TextBox)?.Focus(FocusState.Programmatic));
        }
    }

    private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ViewModel.CloseCommand.Execute(null);
    }

    private void OnBackKey(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = ViewModel.BackCommand.CanExecute(null);
        ViewModel.BackCommand.Execute(null);
    }

    private void OnForwardKey(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = ViewModel.ForwardCommand.CanExecute(null);
        ViewModel.ForwardCommand.Execute(null);
    }

    /// <summary>1–9 runs that writing tool, unless the user is typing in a box (Lamha 2's <c>onWriteKey</c>).</summary>
    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.OriginalSource is TextBox || IsDown(VirtualKey.Control) || IsDown(VirtualKey.Menu))
        {
            return;
        }

        var number = e.Key is >= VirtualKey.Number1 and <= VirtualKey.Number9 ? e.Key - VirtualKey.Number1 + 1
            : e.Key is >= VirtualKey.NumberPad1 and <= VirtualKey.NumberPad9 ? e.Key - VirtualKey.NumberPad1 + 1
            : 0;
        if (number > 0 && ViewModel.PickTool(number))
        {
            e.Handled = true;
        }
    }

    private static bool IsDown(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    /// <summary>A pinned card moves by its bar (anywhere but its buttons).</summary>
    private void OnBarPressed(object sender, PointerRoutedEventArgs e)
    {
        if (ViewModel.IsPinned && e.GetCurrentPoint(Bar).Properties.IsLeftButtonPressed && !IsInButton(e.OriginalSource as DependencyObject))
        {
            e.Handled = true;
            MoveRequested?.Invoke();
        }
    }

    private bool IsInButton(DependencyObject? element)
    {
        for (var e = element; e is not null && e != Bar; e = VisualTreeHelper.GetParent(e))
        {
            if (e is ButtonBase)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The selected text, clamped to a few lines, shows all of it on a click (Lamha 2's <c>expandable()</c>).</summary>
    private void OnSourceTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is TextBlock text)
        {
            text.MaxLines = text.MaxLines == 0 ? (ReferenceEquals(text, FindName("WritingSource")) ? 2 : 3) : 0;
        }
    }

    private void OnSpellClick(Hyperlink sender, HyperlinkClickEventArgs args) => ViewModel.Answer?.OpenSpellCommand.Execute(null);

    private void OnWriteKey(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ViewModel.Writing?.GoCommand.Execute(null);
    }

    private void OnChoice(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ChoiceChip chip })
        {
            ViewModel.Writing?.ChooseCommand.Execute(chip);
        }
    }
}
