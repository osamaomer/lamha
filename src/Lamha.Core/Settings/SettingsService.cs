using System.Text.Json;
using Lamha.Core.Abstractions;

namespace Lamha.Core.Settings;

/// <summary>Reads and saves <see cref="LamhaSettings"/>, kept as JSON in the state store and in memory once read.</summary>
public sealed class SettingsService(IStateStore state)
{
    private const string Key = "settings";
    private readonly Lock _gate = new();
    private LamhaSettings? _current;

    /// <summary>Raised after the settings were saved, with the new ones.</summary>
    public event EventHandler<LamhaSettings>? Changed;

    public LamhaSettings Current
    {
        get
        {
            lock (_gate)
            {
                return _current ??= Read();
            }
        }
    }

    public void Save(LamhaSettings settings)
    {
        lock (_gate)
        {
            state.Set(Key, JsonSerializer.Serialize(settings, SettingsJson.Default.LamhaSettings));
            _current = settings;
        }

        Changed?.Invoke(this, settings);
    }

    /// <summary>Saves the settings <paramref name="change"/> makes from the current ones.</summary>
    public LamhaSettings Update(Func<LamhaSettings, LamhaSettings> change)
    {
        LamhaSettings next;
        lock (_gate)
        {
            next = change(Current);
            state.Set(Key, JsonSerializer.Serialize(next, SettingsJson.Default.LamhaSettings));
            _current = next;
        }

        Changed?.Invoke(this, next);
        return next;
    }

    private LamhaSettings Read()
    {
        var json = state.Get(Key);
        if (json is null)
        {
            return new LamhaSettings();
        }

        try
        {
            return JsonSerializer.Deserialize(json, SettingsJson.Default.LamhaSettings) ?? new LamhaSettings();
        }
        catch (JsonException)
        {
            // Damaged settings start over from the defaults rather than stopping the app.
            return new LamhaSettings();
        }
    }
}
