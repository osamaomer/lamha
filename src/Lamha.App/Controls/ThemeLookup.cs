using Microsoft.UI.Xaml;

namespace Lamha.App.Controls;

/// <summary>
/// A token in a given theme, from code. Application.Current.Resources answers in the app's theme, but a window can
/// have its own (Settings → Appearance → Theme), so code that needs a colour asks for the element's.
/// </summary>
public static class ThemeLookup
{
    /// <summary><paramref name="key"/> in the theme <paramref name="element"/> is drawn in.</summary>
    public static object? Get(FrameworkElement element, string key)
    {
        ArgumentNullException.ThrowIfNull(element);
        return Dictionary(element.ActualTheme).TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>The tokens' dictionary for <paramref name="theme"/> among the app's merged resources.</summary>
    public static ResourceDictionary Dictionary(ElementTheme theme)
    {
        var name = theme == ElementTheme.Dark ? "Dark" : "Light";
        foreach (var dictionary in Flatten(Application.Current.Resources))
        {
            if (dictionary.ThemeDictionaries.TryGetValue(name, out var found) && found is ResourceDictionary themed && themed.ContainsKey("LamhaTextBrush"))
            {
                return themed;
            }
        }

        return Application.Current.Resources;
    }

    private static IEnumerable<ResourceDictionary> Flatten(ResourceDictionary dictionary)
    {
        yield return dictionary;
        foreach (var merged in dictionary.MergedDictionaries)
        {
            foreach (var inner in Flatten(merged))
            {
                yield return inner;
            }
        }
    }
}
