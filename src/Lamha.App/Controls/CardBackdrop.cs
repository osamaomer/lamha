using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Lamha.App.Controls;

/// <summary>
/// The card's Acrylic (phase 1): a strong tint of the surface (Tokens.xaml's LamhaCardTint*), and always drawn as
/// active. Windows shows a window without the focus in the backdrop's solid "inactive" look, and a pinned card keeps
/// working after the user clicks elsewhere. Windows still turns it solid by itself when transparency effects are off,
/// on battery saver and in high contrast.
/// </summary>
public sealed partial class CardBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? _controller;
    private SystemBackdropConfiguration? _configuration;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        _configuration = new SystemBackdropConfiguration { IsInputActive = true };
        _controller = new DesktopAcrylicController();
        Apply(xamlRoot);
        _controller.AddSystemBackdropTarget(connectedTarget);
        _controller.SetSystemBackdropConfiguration(_configuration);
        if (xamlRoot.Content is FrameworkElement root)
        {
            root.ActualThemeChanged += (_, _) => Apply(xamlRoot);
        }
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        _controller?.RemoveSystemBackdropTarget(disconnectedTarget);
        _controller?.Dispose();
        _controller = null;
    }

    /// <summary>The tint for the card's theme, from the tokens.</summary>
    private void Apply(XamlRoot xamlRoot)
    {
        if (_controller is null || _configuration is null || xamlRoot.Content is not FrameworkElement root)
        {
            return;
        }

        var theme = root.ActualTheme;
        _configuration.Theme = theme == ElementTheme.Dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
        var resources = ThemeResources(theme);
        _controller.TintColor = resources["LamhaCardTintColor"] is Color tint ? tint : default;
        _controller.FallbackColor = resources["LamhaCardFallbackColor"] is Color fallback ? fallback : default;
        _controller.TintOpacity = resources["LamhaCardTintOpacity"] is double tintOpacity ? (float)tintOpacity : 0.85f;
        _controller.LuminosityOpacity = resources["LamhaCardLuminosityOpacity"] is double luminosity ? (float)luminosity : 0.9f;
    }

    /// <summary>The tokens' theme dictionary ("Light" or "Dark") among the app's merged resources.</summary>
    private static ResourceDictionary ThemeResources(ElementTheme theme)
    {
        var key = theme == ElementTheme.Dark ? "Dark" : "Light";
        foreach (var dictionary in Flatten(Application.Current.Resources))
        {
            if (dictionary.ThemeDictionaries.TryGetValue(key, out var found) && found is ResourceDictionary themed && themed.ContainsKey("LamhaCardTintColor"))
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
