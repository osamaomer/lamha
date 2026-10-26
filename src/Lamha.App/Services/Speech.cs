using Lamha.Core;
using Lamha.Core.Net;
using Lamha.Core.Settings;
using Lamha.Core.Speech;
using Lamha.Windows.Media;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Streams;

namespace Lamha.App.Services;

/// <summary>
/// Listen: Google's voice when there's a network (and not "Offline only"), else, or when its first piece can't be had
/// or played, Windows' own voice for the language. One thing is said at a time; pressing the button that's playing
/// stops it. Lamha 2's <c>speak()</c> in background.js and the card's <c>speak()</c> in content.js. Both voices play
/// through Windows' wave player (<see cref="WavePlayer"/>), not Media Foundation's, which stayed loaded after one word.
/// </summary>
public static class Speech
{
    private static Brain? _brain;
    private static WavePlayer? _player;
    private static CancellationTokenSource? _cancel;
    private static int _token;

    /// <summary>What is saying something now (a Listen button), or null.</summary>
    public static object? Playing { get; private set; }

    /// <summary><see cref="Playing"/> changed, on the interface's thread.</summary>
    public static event Action? Changed;

    public static void Use(Brain brain) => _brain = brain;

    /// <summary>A Listen button pressed: says the text, or stops when <paramref name="owner"/> is the one playing.</summary>
    public static void Toggle(object owner, string text, string lang)
    {
        if (Playing is not null && ReferenceEquals(Playing, owner))
        {
            Stop();
            return;
        }

        _ = SayAsync(text, lang, owner);
    }

    /// <summary>Says <paramref name="text"/> in <paramref name="lang"/> ("en", "ar"…), after stopping whatever was being said.</summary>
    public static async Task SayAsync(string text, string lang, object? owner = null)
    {
        if (_brain is not { } brain || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Stop();
        var token = _token;
        var player = _player = new WavePlayer();
        using var cancel = _cancel = new CancellationTokenSource();
        Playing = owner;
        Changed?.Invoke();
        try
        {
            var online = brain.Settings.Current.DictSource != DictSource.Offline && !brain.Network.IsOffline;
            if (!(online && await GoogleAsync(brain, player, text, lang, cancel.Token).ConfigureAwait(true)) && token == _token)
            {
                await WindowsVoiceAsync(player, text, lang, cancel.Token).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            // stopped
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            AppLog.Error("speech", e); // no voice for the language, or Windows' sound failing: the button just stops
        }
        finally
        {
            if (token == _token)
            {
                _player = null;
                _cancel = null;
                Playing = null;
                Changed?.Invoke();
            }
        }
    }

    /// <summary>Stops what's being said.</summary>
    public static void Stop()
    {
        _token++;
        _cancel?.Cancel();
        _player?.Stop();
        _player = null;
        _cancel = null;
        if (Playing is not null)
        {
            Playing = null;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Google's voice, piece by piece, the next one fetched while one plays; false when nothing played, so Windows' voice
    /// says it.
    /// </summary>
    private static async Task<bool> GoogleAsync(Brain brain, WavePlayer player, string text, string lang, CancellationToken cancel)
    {
        var google = brain.Translation.Google;
        IReadOnlyList<string> urls;
        try
        {
            urls = google.SpeechUrls(text, lang);
        }
        catch (LamhaException)
        {
            return false; // Google can't be reached just now
        }

        async Task<WaveSound?> Fetch(string url)
        {
            try
            {
                return WaveSound.FromMp3(await google.SpeechPieceAsync(url, cancel).ConfigureAwait(false));
            }
            catch (HttpFailure)
            {
                return null;
            }
        }

        var next = Fetch(urls[0]);
        for (var i = 0; i < urls.Count; i++)
        {
            var sound = await next.ConfigureAwait(true);
            cancel.ThrowIfCancellationRequested();
            next = i + 1 < urls.Count ? Fetch(urls[i + 1]) : Task.FromResult<WaveSound?>(null);
            if (sound is null || !await Task.Run(() => player.Play(sound.Format, sound.Data), CancellationToken.None).ConfigureAwait(true))
            {
                AppLog.Info($"speech: Google's voice failed at piece {i + 1} of {urls.Count} ({(sound is null ? "not had" : "not played")})");
                return i > 0; // after the first piece, what was said stays said
            }
        }

        AppLog.Info($"speech: Google's voice, {urls.Count} pieces");
        return true;
    }

    /// <summary>Windows' voice for the language (Lamha 2's ar-SA and en-US first), or its default voice.</summary>
    private static async Task WindowsVoiceAsync(WavePlayer player, string text, string lang, CancellationToken cancel)
    {
        WaveSound? sound;
        using (var synthesizer = new SpeechSynthesizer())
        {
            var voice = Voice(lang);
            if (voice is not null)
            {
                synthesizer.Voice = voice;
            }

            AppLog.Info($"speech: Windows' voice, {voice?.Language ?? "the default one"}");
            using var stream = await synthesizer.SynthesizeTextToStreamAsync(text);
            using var reader = new DataReader(stream.GetInputStreamAt(0));
            var size = (uint)stream.Size;
            await reader.LoadAsync(size);
            var wav = new byte[size];
            reader.ReadBytes(wav);
            sound = WaveSound.FromWave(wav);
        }

        cancel.ThrowIfCancellationRequested();
        if (sound is not null)
        {
            await Task.Run(() => player.Play(sound.Format, sound.Data), CancellationToken.None).ConfigureAwait(true);
        }
    }

    private static VoiceInformation? Voice(string lang)
    {
        var tag = lang switch
        {
            "ar" => "ar-SA",
            "en" => "en-US",
            _ => lang,
        };
        var voices = SpeechSynthesizer.AllVoices;
        return voices.FirstOrDefault(v => string.Equals(v.Language, tag, StringComparison.OrdinalIgnoreCase))
            ?? voices.FirstOrDefault(v => v.Language.Split('-')[0].Equals(lang.Split('-')[0], StringComparison.OrdinalIgnoreCase));
    }
}
