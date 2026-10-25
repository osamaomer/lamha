using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.Windows.Globalization;

namespace Lamha.App.Services;

/// <summary>
/// The interface language and its strings (Strings/ar and Strings/en): the setting (Settings → Languages), or
/// "automatic" as in Lamha 2, Arabic on an Arabic system and English otherwise (Core's <c>Localization</c>).
/// </summary>
public static class Localization
{
    /// <summary>Languages written right to left: Lamha 2's <c>textDir</c> in shared/i18n.js.</summary>
    private static readonly HashSet<string> RightToLeft = ["ar", "fa", "ur", "he", "iw", "ps", "yi", "ku", "sd"];

    private static readonly CultureInfo ArabicNumbers = CultureInfo.GetCultureInfo("ar-EG");
    private static readonly CultureInfo EnglishNumbers = CultureInfo.GetCultureInfo("en-US");

    private static ResourceLoader? s_loader;

    public static bool IsArabic { get; private set; }

    /// <summary>"ar" or "en".</summary>
    public static string Language => IsArabic ? "ar" : "en";

    /// <summary>Right to left in Arabic, for every window's root.</summary>
    public static FlowDirection FlowDirection => IsArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    /// <summary>Sets the language ("ar" or "en"); call it before any window or string loads.</summary>
    public static void Apply(string language)
    {
        IsArabic = language == "ar";
        ApplicationLanguages.PrimaryLanguageOverride = Language;
        // Language names (CultureInfo.DisplayName) follow the interface language, as Intl.DisplayNames did in Lamha 2.
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(Language);
    }

    /// <summary>The string <paramref name="key"/> in the interface language.</summary>
    public static string Get(string key)
    {
        s_loader ??= new ResourceLoader();
        var value = s_loader.GetString(key);
        return string.IsNullOrEmpty(value) ? key : value;
    }

    /// <summary>
    /// A mistake kind's name ("حروف الجر (in / on / at …)") with its English part in brackets isolated, as Lamha 2's
    /// <c>labelParts()</c> did with a bdi: otherwise the brackets and the "…" land on the wrong side in Arabic.
    /// </summary>
    public static string Category(string id)
    {
        var name = Get("Category_" + id);
        var open = name.LastIndexOf('(');
        return open > 0 && name.EndsWith(')') && name[open..].Any(char.IsAsciiLetter)
            ? name[..open] + "‎" + name[open..] + "‎" // left-to-right marks: the whole bracket reads left to right
            : name;
    }

    /// <summary>The string with its {0}, {1}… filled in; numbers as the interface writes them (<see cref="Number"/>).</summary>
    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), [.. args.Select(a => a is int or long ? Number(Convert.ToInt64(a, CultureInfo.InvariantCulture)) : a)]);

    /// <summary>A number as Lamha 2 wrote it: Arabic-Indic digits in Arabic ("١٢٬٣٤٥"), "12,345" in English.</summary>
    public static string Number(long n)
    {
        return Digits(n.ToString("#,0", IsArabic ? ArabicNumbers : EnglishNumbers));
    }

    /// <summary>Arabic-Indic digits in Arabic (.NET writes ASCII digits whatever the culture).</summary>
    private static string Digits(string text) => !IsArabic ? text : string.Create(text.Length, text, (span, s) =>
    {
        for (var i = 0; i < s.Length; i++)
        {
            span[i] = s[i] is >= '0' and <= '9' ? (char)('٠' + (s[i] - '0')) : s[i];
        }
    });

    /// <summary>A sum of money as the interface writes it: two decimals ("187.46", "١٨٧٫٤٦").</summary>
    public static string Money(decimal value) => Digits(value.ToString("#,0.00", IsArabic ? ArabicNumbers : EnglishNumbers));

    /// <summary>An exchange rate: up to four decimals, more for a currency worth far less ("3.75", "0.000027").</summary>
    public static string Rate(decimal value) =>
        Digits(value.ToString(value >= 0.01m ? "#,0.####" : "0.########", IsArabic ? ArabicNumbers : EnglishNumbers));

    /// <summary>"25 October", "٢٥ أكتوبر".</summary>
    public static string DayAndMonth(DateOnly day) => Digits(day.ToString("d MMMM", IsArabic ? ArabicNumbers : EnglishNumbers));

    /// <summary>"25 October 2025", "٢٥ أكتوبر ٢٠٢٥".</summary>
    public static string FullDate(DateOnly day) => Digits(day.ToString("d MMMM yyyy", IsArabic ? ArabicNumbers : EnglishNumbers));

    /// <summary>"Saturday", "السبت".</summary>
    public static string Weekday(DateOnly day) => day.ToString("dddd", IsArabic ? ArabicNumbers : EnglishNumbers);

    /// <summary>"10:24 AM", "١٠:٢٤ ص" (the main window's design).</summary>
    public static string Time(DateTime time) => Digits(time.ToString("h:mm tt", IsArabic ? ArabicNumbers : EnglishNumbers));

    /// <summary>A language's name in the interface language ("الإنجليزية", "French"): Lamha 2's <c>langName()</c>.</summary>
    public static string LanguageName(string code)
    {
        if (code is "ar" or "en")
        {
            return Get("Lang_" + code);
        }

        try
        {
            return CultureInfo.GetCultureInfo(code).DisplayName;
        }
        catch (CultureNotFoundException)
        {
            return code;
        }
    }

    /// <summary>Whether text in this language ("ar", "fa", "ur-PK"…) is written right to left.</summary>
    public static bool IsRightToLeft(string? code) => RightToLeft.Contains((code ?? "").Split('-')[0].ToLowerInvariant());

    /// <summary>The direction to lay out text in this language.</summary>
    public static FlowDirection FlowOf(string? code) => IsRightToLeft(code) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
}
