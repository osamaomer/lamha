using Lamha.Core.Appearance;
using Lamha.Core.Settings;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;
using Color = Windows.UI.Color;

namespace Lamha.App.Services;

/// <summary>
/// Lamha's accent brushes (Themes/Tokens.xaml: LamhaAccentBrush, LamhaAccentSoftBrush, LamhaButtonBrush), in Lamha's own
/// colours or, with Settings → Appearance → "Accent colour from Windows" on, in the colour picked in Windows, made
/// readable for each theme (<see cref="AccentPalette"/>). It follows a new pick in Windows at once, as Lamha 2 did on
/// accent-color-changed. The brushes are changed in place, so whatever shows them updates.
/// </summary>
public sealed partial class AccentService : IDisposable
{
    /// <summary>Lamha's own colours: Lamha 2's shared/ui.css (--accent, --accent-soft, --btn) for each theme.</summary>
    private static readonly (ThemeAccent Light, ThemeAccent Dark) Own = (
        new ThemeAccent(new Rgb(0x4F, 0x46, 0xE5), 0.09, new Rgb(0x4F, 0x46, 0xE5)),
        new ThemeAccent(new Rgb(0x9A, 0xA1, 0xFF), 0.12, new Rgb(0x5E, 0x5C, 0xE6)));

    private readonly SettingsService _settings;
    private readonly DispatcherQueue _queue;
    private readonly UISettings _windows = new();
    private bool _painted;

    public AccentService(SettingsService settings, DispatcherQueue queue)
    {
        _settings = settings;
        _queue = queue;

        // Both arrive on other threads.
        _windows.ColorValuesChanged += OnWindowsColours;
        _settings.Changed += OnSettings;
        Apply();
    }

    /// <summary>The colours in use now.</summary>
    public (ThemeAccent Light, ThemeAccent Dark) Current { get; private set; } = Own;

    public void Dispose()
    {
        _windows.ColorValuesChanged -= OnWindowsColours;
        _settings.Changed -= OnSettings;
    }

    private void OnWindowsColours(UISettings sender, object args) => _queue.TryEnqueue(Apply);

    private void OnSettings(object? sender, LamhaSettings settings) => _queue.TryEnqueue(Apply);

    private void Apply()
    {
        var palette = Own;
        if (_settings.Current.AccentWindows)
        {
            var c = _windows.GetColorValue(UIColorType.Accent);
            palette = AccentPalette.From(new Rgb(c.R, c.G, c.B));
        }

        if (_painted && palette == Current)
        {
            return;
        }

        _painted = true;
        Current = palette;
        Paint("Light", palette.Light);
        Paint("Dark", palette.Dark);
    }

    /// <summary>Paints the theme's brushes wherever they're defined (Tokens.xaml is merged into the app's resources).</summary>
    private static void Paint(string theme, ThemeAccent accent)
    {
        foreach (var dictionary in WithMerged(Application.Current.Resources))
        {
            if (dictionary.ThemeDictionaries.TryGetValue(theme, out var found) && found is ResourceDictionary brushes)
            {
                Set(brushes, "LamhaAccentBrush", accent.Accent, 1);
                Set(brushes, "LamhaAccentSoftBrush", accent.Accent, accent.SoftAlpha);
                Set(brushes, "LamhaButtonBrush", accent.Button, 1);
            }
        }
    }

    private static IEnumerable<ResourceDictionary> WithMerged(ResourceDictionary root)
    {
        yield return root;
        foreach (var merged in root.MergedDictionaries)
        {
            foreach (var inner in WithMerged(merged))
            {
                yield return inner;
            }
        }
    }

    private static void Set(ResourceDictionary brushes, string key, Rgb rgb, double alpha)
    {
        if (brushes.TryGetValue(key, out var value) && value is SolidColorBrush brush)
        {
            brush.Color = Color.FromArgb((byte)Math.Round(alpha * 255), rgb.R, rgb.G, rgb.B);
        }
    }
}
