using Lamha.App.Services;
using Lamha.App.ViewModels.Main;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Lamha.App.Views;

/// <summary>
/// The writing tools' five providers, the chosen one's setup under it (<see cref="WritingSetupViewModel"/>): in Settings →
/// Writing tools, and first run's optional step. Its host gives it the heading and the lines around it.
/// </summary>
public sealed partial class WritingSetupView : UserControl
{
    public static readonly DependencyProperty SetupProperty =
        DependencyProperty.Register(nameof(Setup), typeof(WritingSetupViewModel), typeof(WritingSetupView), new PropertyMetadata(null, (d, _) => ((WritingSetupView)d).Bindings.Update()));

    public WritingSetupView()
    {
        InitializeComponent();
    }

    public WritingSetupViewModel? Setup
    {
        get => (WritingSetupViewModel?)GetValue(SetupProperty);
        set => SetValue(SetupProperty, value);
    }

    public WritingSetupStrings S { get; } = new();

    /// <summary>Enter in a field: its command (in its Tag) runs: Save &amp; verify for a key, an address is saved and read.</summary>
    private void OnEnterCommits(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == global::Windows.System.VirtualKey.Enter && sender is FrameworkElement { Tag: System.Windows.Input.ICommand command } && command.CanExecute(null))
        {
            e.Handled = true;
            command.Execute(null);
        }
    }

    /// <summary>Leaving an address field saves it, as Lamha 2's change event did.</summary>
    private void OnLeaveCommits(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: System.Windows.Input.ICommand command } && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }
}

/// <summary>The providers' words (x:Bind reads them once).</summary>
public sealed class WritingSetupStrings
{
    public string Model { get; } = Localization.Get("Settings_Model");

    public string Test { get; } = Localization.Get("Settings_Test");

    /// <summary>Ollama's steps, numbered as the interface writes numbers.</summary>
    public string One { get; } = Localization.Number(1) + ".";

    public string Two { get; } = Localization.Number(2) + ".";

    public string OllamaFree { get; } = Localization.Get("Settings_OllamaFree");

    public string OllamaHint { get; } = Localization.Get("Settings_OllamaHint");

    public string Ollama1 { get; } = Localization.Get("Settings_Ollama1");

    public string Ollama3 { get; } = Localization.Get("Settings_Ollama3");

    public string OllamaUrl { get; } = Localization.Get("Settings_OllamaUrl");

    public string OllamaUrlHint { get; } = Localization.Get("Settings_OllamaUrlHint");

    public string RefreshList { get; } = Localization.Get("Settings_RefreshList");

    public string OllamaModelsHint { get; } = Localization.Get("Settings_OllamaModelsHint");

    public string GeminiFree { get; } = Localization.Get("Settings_GeminiFree");

    public string GeminiHint { get; } = Localization.Get("Settings_GeminiHint");

    public string GeminiTip { get; } = Localization.Get("Settings_GeminiTip");

    public string GeminiWarn { get; } = Localization.Get("Settings_GeminiWarn");

    public string GeminiModelsHint { get; } = Localization.Get("Settings_GeminiModelsHint");

    public string ClaudeBest { get; } = Localization.Get("Settings_ClaudeBest");

    public string ClaudeHint { get; } = Localization.Get("Settings_ClaudeHint");

    public string ClaudeTip { get; } = Localization.Get("Settings_ClaudeTip");

    public string ClaudeModelsHint { get; } = Localization.Get("Settings_ClaudeModelsHint");

    public string OpenAiHint { get; } = Localization.Get("Settings_OpenAiHint");

    public string OpenAiTip { get; } = Localization.Get("Settings_OpenAiTip");

    public string OpenAiModels { get; } = Localization.Get("Settings_OpenAiModels");

    public string CustomHint { get; } = Localization.Get("Settings_CustomHint");

    public string ServerUrl { get; } = Localization.Get("Settings_ServerUrl");

    public string ServerUrlHint { get; } = Localization.Get("Settings_ServerUrlHint");

    public string ServerKey { get; } = Localization.Get("Settings_ServerKey");

    public string ServerKeyHint { get; } = Localization.Get("Settings_ServerKeyHint");

    public string ServerModels { get; } = Localization.Get("Settings_ServerModels");
}
