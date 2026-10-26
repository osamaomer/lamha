using System.Numerics;
using Lamha.App.Services;
using Lamha.App.ViewModels.Main;
using Lamha.Core.Appearance;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Lamha.Windows.Input;

namespace Lamha.App.Views;

/// <summary>The main window's Settings page: the categories, and each one's settings cards.</summary>
public sealed partial class SettingsPage : UserControl
{
    private string _left = "";

    public SettingsPage(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.Section))
            {
                DispatcherQueue.TryEnqueue(KeepFocus);
            }
        };
        ViewModel.MotionPreview += PlayMotionPreview;
    }

    public SettingsViewModel ViewModel { get; }

    /// <summary>"Settings": the home's title, and the breadcrumb's way back.</summary>
    public string Title { get; } = Localization.Get("Common_Settings");

    public SettingsStrings S { get; } = new();

    /// <summary>
    /// Animations' preview, Lamha 2's <c>demo()</c> with its numbers: at full the card springs in from its top start
    /// corner (its own top left, which Arabic's mirroring puts at the right) and its meaning rises in after it; at
    /// subtle it slides down into place; at off it's simply there.
    /// </summary>
    private void PlayMotionPreview()
    {
        if (MotionDemoCard is not { } card)
        {
            return;
        }

        var level = Animations.Level;
        if (level == MotionLevel.Full)
        {
            Animations.Enter(card, level, 280, from: 0, rise: -6, scale: 0.86f, (0.2f, 0.9f, 0.3f, 1.25f), origin: Vector2.Zero);
            Animations.Enter(MotionDemoMeaning, level, 260, from: 0, rise: 6, scale: 0.95f, Motion.EnterEasing, TimeSpan.FromMilliseconds(120));
        }
        else
        {
            Animations.ShowNow(MotionDemoMeaning);
            Animations.Enter(card, level, 180, from: 0, rise: -3, scale: 1, Motion.EnterEasing);
        }
    }

    /// <summary>
    /// The tile clicked, or the breadcrumb, goes when the page changes, and the focus would fall to the side pane's
    /// first page: it goes to the category's breadcrumb instead, and back home to the tile of the category just left.
    /// </summary>
    private void KeepFocus()
    {
        if (!ViewModel.IsHome)
        {
            _left = ViewModel.Section;
            Crumb.Focus(FocusState.Programmatic);
        }
        else if (ViewModel.Tiles.FindIndex(t => t.Id == _left) is var index and >= 0 && Home is { } home)
        {
            // The home was made again just now: its tiles may not be laid out yet.
            if (home.TryGetElement(index) is Control tile)
            {
                tile.Focus(FocusState.Programmatic);
                return;
            }

            void Prepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
            {
                if (args.Index == index && args.Element is Control prepared)
                {
                    sender.ElementPrepared -= Prepared;
                    DispatcherQueue.TryEnqueue(() => prepared.Focus(FocusState.Programmatic));
                }
            }

            home.ElementPrepared += Prepared;
        }
    }

    /// <summary>Enter in a field: its command (in its Tag) runs: Save &amp; verify for a key, an address is saved and read.</summary>
    private void OnEnterCommits(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == global::Windows.System.VirtualKey.Enter && sender is FrameworkElement { Tag: System.Windows.Input.ICommand command } && command.CanExecute(null))
        {
            e.Handled = true;
            command.Execute(null);
        }
    }

    /// <summary>
    /// Keyboard shortcuts: while a shortcut waits for its new keys, the keys pressed go to it, not to the control with
    /// the focus.
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!ViewModel.IsKeys || ViewModel.Shortcuts.Capturing is null)
        {
            return;
        }

        static bool Down(global::Windows.System.VirtualKey key) =>
            Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(global::Windows.UI.Core.CoreVirtualKeyStates.Down);
        var modifiers = HotkeyModifiers.None;
        if (Down(global::Windows.System.VirtualKey.Control))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (Down(global::Windows.System.VirtualKey.Menu))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (Down(global::Windows.System.VirtualKey.Shift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (Down(global::Windows.System.VirtualKey.LeftWindows) || Down(global::Windows.System.VirtualKey.RightWindows))
        {
            modifiers |= HotkeyModifiers.Win;
        }

        // WinUI's virtual keys are Windows' own numbers.
        e.Handled = ViewModel.Shortcuts.Press(modifiers, (Lamha.Windows.Input.VirtualKey)(ushort)e.Key);
    }

    /// <summary>Alt+←: back to the home, as the mouse's back button does in Windows' Settings.</summary>
    private void OnBack(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!ViewModel.IsHome)
        {
            args.Handled = true;
            ViewModel.GoHomeCommand.Execute(null);
        }
    }
}

/// <summary>The Settings page's words (x:Bind reads them once).</summary>
public sealed class SettingsStrings
{
    public string UiLang { get; } = Localization.Get("Settings_UiLang");

    public string UiLangHint { get; } = Localization.Get("Settings_UiLangHint");

    public string RestartPending { get; } = Localization.Get("Settings_RestartPending");

    public string TargetLang { get; } = Localization.Get("Settings_TargetLang");

    public string TargetLangHint { get; } = Localization.Get("Settings_TargetLangHint");

    public string Reverse { get; } = Localization.Get("Settings_Reverse");

    public string ReverseHint { get; } = Localization.Get("Settings_ReverseHint");

    public string Currency { get; } = Localization.Get("Settings_Currency");

    public string CurrencyHint { get; } = Localization.Get("Settings_CurrencyHint");

    public string AiProvider { get; } = Localization.Get("Settings_AiProvider");

    public string Model { get; } = Localization.Get("Settings_Model");

    public string AiPrivacy { get; } = Localization.Get("Settings_AiPrivacy");

    public string CbEnable { get; } = Localization.Get("Settings_CbEnable");

    public string CbEnableHint { get; } = Localization.Get("Settings_CbEnableHint");

    public string CbMax { get; } = Localization.Get("Settings_CbMax");

    public string CbMaxHint { get; } = Localization.Get("Settings_CbMaxHint");

    public string CbExpiry { get; } = Localization.Get("Settings_CbExpiry");

    public string CbExpiryHint { get; } = Localization.Get("Settings_CbExpiryHint");

    public string CbCards { get; } = Localization.Get("Settings_CbCards");

    public string CbCardsHint { get; } = Localization.Get("Settings_CbCardsHint");

    public string CbApps { get; } = Localization.Get("Settings_CbApps");

    public string CbAppsHint { get; } = Localization.Get("Settings_CbAppsHint");

    public string CbNoApps { get; } = Localization.Get("Settings_CbNoApps");

    public string CbAppName { get; } = Localization.Get("Settings_CbAppName");

    public string CbAdd { get; } = Localization.Get("Settings_CbAdd");

    public string CbFromHistory { get; } = Localization.Get("Settings_CbFromHistory");

    public string CbClear { get; } = Localization.Get("Settings_CbClear");

    public string CbClearHint { get; } = Localization.Get("Settings_CbClearHint");

    public string CbClearUnpinned { get; } = Localization.Get("Settings_CbClearUnpinned");

    public string CbClearAll { get; } = Localization.Get("Settings_CbClearAll");

    public string CbEncrypted { get; } = Localization.Get("Settings_CbEncrypted");

    public string SaveHistory { get; } = Localization.Get("Settings_SaveHistory");

    public string SaveHistoryHint { get; } = Localization.Get("Settings_SaveHistoryHint");

    public string ClearHistory { get; } = Localization.Get("Settings_ClearHistory");

    public string Clear { get; } = Localization.Get("Common_ClearShort");

    public string Backup { get; } = Localization.Get("Settings_Backup");

    public string BackupHint { get; } = Localization.Get("Settings_BackupHint");

    public string BackupSave { get; } = Localization.Get("Settings_BackupSave");

    public string BackupRestore { get; } = Localization.Get("Settings_BackupRestore");

    public string Report { get; } = Localization.Get("Settings_Report");

    public string ReportHint { get; } = Localization.Get("Settings_ReportHint");

    public string ReportSend { get; } = Localization.Get("Settings_ReportSend");

    public string ReportShow { get; } = Localization.Get("Settings_ReportShow");

    public string Copy { get; } = Localization.Get("Common_Copy");

    public string MyData { get; } = Localization.Get("Settings_MyData");

    /// <summary>The design's "%LOCALAPPDATA%\Lamha Data · Uninstalling or reinstalling Lamha leaves it", the path kept left to right.</summary>
    public string MyDataLine { get; } = "‎%LOCALAPPDATA%\\Lamha Data‎ · " + Localization.Get("Settings_MyDataHint");

    public string OpenFolder { get; } = Localization.Get("Settings_OpenFolder");

    public string DeleteData { get; } = Localization.Get("Settings_DeleteData");

    public string PrivacyMore { get; } = Localization.Get("Settings_PrivacyMore");

    public string PrivacyText { get; } = Localization.Get("Settings_PrivacyText");

    public string KeyClose { get; } = Localization.Get("Settings_KeyClose");

    public string Login { get; } = Localization.Get("Tray_StartWithWindows");

    public string LoginHint { get; } = Localization.Get("Settings_LoginHint");

    public string UpAuto { get; } = Localization.Get("Settings_UpAuto");

    public string UpAutoHint { get; } = Localization.Get("Settings_UpAutoHint");

    public string UpAll { get; } = Localization.Get("Settings_UpAll");

    public string Credits { get; } = Localization.Get("Settings_Credits");

    public string CreditsList { get; } = Localization.Get("Settings_CreditsList");

    public string CreditsLicense { get; } = Localization.Get("Settings_CreditsLicense");

    public string Context { get; } = Localization.Get("Settings_Context");

    public string ContextHint { get; } = Localization.Get("Settings_ContextHint");

    public string Explain { get; } = Localization.Get("Settings_Explain");

    public string ExplainHint { get; } = Localization.Get("Settings_ExplainHint");

    public string TranslateDefs { get; } = Localization.Get("Settings_TranslateDefs");

    public string TranslateDefsHint { get; } = Localization.Get("Settings_TranslateDefsHint");

    public string Wiki { get; } = Localization.Get("Settings_Wiki");

    public string WikiHint { get; } = Localization.Get("Settings_WikiHint");

    public string AutoSpeak { get; } = Localization.Get("Settings_AutoSpeak");

    public string AutoSpeakHint { get; } = Localization.Get("Settings_AutoSpeakHint");

    public string TrIntro { get; } = Localization.Get("Settings_TrIntro");

    public string TrAuto { get; } = Localization.Get("Settings_TrAuto");

    public string TrAutoHint { get; } = Localization.Get("Settings_TrAutoHint");

    public string TrGoogle { get; } = Localization.Get("Settings_TrGoogle");

    public string TrGoogleHint { get; } = Localization.Get("Settings_TrGoogleHint");

    public string TrAi { get; } = Localization.Get("Settings_TrAi");

    public string TrAiHint { get; } = Localization.Get("Settings_TrAiHint");

    public string TrProvider { get; } = Localization.Get("Settings_TrProvider");

    public string TrProviderHint { get; } = Localization.Get("Settings_TrProviderHint");

    public string ReviewIntro { get; } = Localization.Get("Settings_ReviewIntro");

    public string CardsAuto { get; } = Localization.Get("Settings_CardsAuto");

    public string CardsAutoHint { get; } = Localization.Get("Settings_CardsAutoHint");

    public string NewPerDay { get; } = Localization.Get("Settings_NewPerDay");

    public string NewPerDayHint { get; } = Localization.Get("Settings_NewPerDayHint");

    public string Goal { get; } = Localization.Get("Settings_Goal");

    public string GoalHint { get; } = Localization.Get("Settings_GoalHint");

    public string DeleteCards { get; } = Localization.Get("Settings_DeleteCards");

    public string DeleteCardsHint { get; } = Localization.Get("Settings_DeleteCardsHint");

    public string Delete { get; } = Localization.Get("Common_Delete");

    public string LocalFirst { get; } = Localization.Get("Settings_LocalFirst");

    public string LocalFirstHint { get; } = Localization.Get("Settings_LocalFirstHint");

    public string LocalOnly { get; } = Localization.Get("Settings_LocalOnly");

    public string LocalOnlyHint { get; } = Localization.Get("Settings_LocalOnlyHint");

    public string OnlineFirst { get; } = Localization.Get("Settings_OnlineFirst");

    public string OnlineFirstHint { get; } = Localization.Get("Settings_OnlineFirstHint");

    public string Theme { get; } = Localization.Get("Settings_Theme");

    public string ThemeHint { get; } = Localization.Get("Settings_ThemeHint");

    public string Motion { get; } = Localization.Get("Settings_Motion");

    public string MotionHint { get; } = Localization.Get("Settings_MotionHint");

    public string MotionPreview { get; } = Localization.Get("Settings_MotionPreview");

    public string MotionDemoWord { get; } = Localization.Get("Settings_MotionDemoWord");

    public string MotionDemoMeaning { get; } = Localization.Get("Settings_MotionDemoMeaning");

    public string CardBg { get; } = Localization.Get("Settings_CardBg");

    public string CardBgHint { get; } = Localization.Get("Settings_CardBgHint");

    public string Accent { get; } = Localization.Get("Settings_Accent");

    public string AccentHint { get; } = Localization.Get("Settings_AccentHint");

    public string Mica { get; } = Localization.Get("Settings_Mica");

    public string MicaHint { get; } = Localization.Get("Settings_MicaHint");
}
