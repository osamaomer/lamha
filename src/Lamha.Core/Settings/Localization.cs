using System.Globalization;

namespace Lamha.Core.Settings;

/// <summary>The interface language in use, "ar" or "en": the setting, or the system's when it's "auto".</summary>
public sealed class Localization(SettingsService settings, Func<string>? systemLanguage = null)
{
    private readonly Func<string> _system = systemLanguage ?? (() => CultureInfo.CurrentUICulture.Name);

    public string Language => settings.Current.InterfaceLanguage(_system());
}
