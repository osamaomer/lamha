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
/// accent-color-changed. The brushes are changed in place, so whatever shows them updates. Windows' own controls follow
/// the same choice through the SystemAccentColor family (decision 0020).
/// </summary>
public sealed partial class AccentService : IDisposable
{
    /// <summary>
    /// Lamha's own colours: Lamha 2's shared/ui.css (--accent, --accent-soft, --btn) for each theme, the dark button
    /// the light accent itself, as Windows draws its accent buttons (decision 0020).
    /// </summary>
    private static readonly (ThemeAccent Light, ThemeAccent Dark) Own = (
        new ThemeAccent(new Rgb(0x4F, 0x46, 0xE5), 0.09, new Rgb(0x4F, 0x46, 0xE5)),
        new ThemeAccent(new Rgb(0x9A, 0xA1, 0xFF), 0.12, new Rgb(0x9A, 0xA1, 0xFF)));

    /// <summary>Windows' accent colours (its controls' accent fills and text), which Tokens.xaml sets to Lamha's indigo.</summary>
    private static readonly string[] AccentFamily =
    [
        "SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
        "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3",
    ];

    private readonly SettingsService _settings;
    private readonly DispatcherQueue _queue;
    private readonly UISettings _windows = new();
    private readonly Dictionary<string, object> _lamhaFamily = [];
    private ResourceDictionary? _family;
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

        var repaint = _painted;
        _painted = true;
        Current = palette;
        Paint("Light", palette.Light);
        Paint("Dark", palette.Dark);
        UseFamily(own: !_settings.Current.AccentWindows);
        if (repaint)
        {
            Repainted?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// The accent changed while Lamha runs: Windows' controls take a new accent only when their theme is applied again,
    /// so open windows do that (Lamha's own brushes changed in place already).
    /// </summary>
    public event EventHandler? Repainted;

    /// <summary>
    /// Windows' controls in Lamha's accent (Tokens.xaml's SystemAccentColor family), or, for Windows' own accent, the
    /// family taken out so Windows' colours show through.
    /// </summary>
    private void UseFamily(bool own)
    {
        if (_family is null)
        {
            _family = WithMerged(Application.Current.Resources).FirstOrDefault(d => d.ContainsKey(AccentFamily[0]));
            if (_family is null)
            {
                return;
            }

            foreach (var key in AccentFamily)
            {
                _lamhaFamily[key] = _family[key];
            }
        }

        foreach (var key in AccentFamily)
        {
            if (own)
            {
                _family[key] = _lamhaFamily[key];
            }
            else
            {
                _family.Remove(key);
            }
        }
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
