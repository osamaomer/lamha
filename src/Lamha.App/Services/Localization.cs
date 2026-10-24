using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.Windows.Globalization;

namespace Lamha.App.Services;

/// <summary>
/// The interface language and its strings (Strings/ar and Strings/en). "Automatic", as in Lamha 2: Arabic on an
/// Arabic system, English otherwise. The setting to choose comes with Settings (phase 4).
/// </summary>
public static class Localization
{
    private static ResourceLoader? s_loader;

    public static bool IsArabic { get; private set; }

    /// <summary>Right to left in Arabic, for every window's root.</summary>
    public static FlowDirection FlowDirection => IsArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    /// <summary>Picks the language; call it before any window or string loads.</summary>
    public static void Apply()
    {
        IsArabic = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";
        ApplicationLanguages.PrimaryLanguageOverride = IsArabic ? "ar" : "en";
    }

    /// <summary>The string <paramref name="key"/> in the interface language.</summary>
    public static string Get(string key)
    {
        s_loader ??= new ResourceLoader();
        var value = s_loader.GetString(key);
        return string.IsNullOrEmpty(value) ? key : value;
    }

    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
