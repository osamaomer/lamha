using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.Audio;

namespace Lamha.Windows.Media;

/// <summary>
/// Plays a sound through Windows' wave mapper, which decodes MP3 with Windows' own codec: Listen's voices. Lighter
/// than Media Foundation's player, whose media engine and network and sign-in stack of its own stayed loaded after
/// one word (about 4 MB more commit than this). <see cref="Play"/> blocks until the sound ends, so it runs off the
/// interface's thread; <see cref="Stop"/> ends it from any thread.
/// </summary>
public sealed class WavePlayer
{
    private readonly Lock _gate = new();
    private HWAVEOUT _out;
    private bool _stopped;

    /// <summary>
    /// Plays <paramref name="data"/> in <paramref name="format"/> (a WAVEFORMATEX and its extra bytes) to its end.
    /// False when Windows can't play it (no sound device, a format it can't decode); true when stopped.
    /// </summary>
    public unsafe bool Play(byte[] format, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(data);
        using var done = new AutoResetEvent(false);
        HWAVEOUT output;
        fixed (byte* f = format)
        fixed (byte* d = data)
        {
            lock (_gate)
            {
                if (_stopped)
                {
                    return true;
                }

                if (PInvoke.waveOutOpen(&output, PInvoke.WAVE_MAPPER, (WAVEFORMATEX*)f, (nuint)done.SafeWaitHandle.DangerousGetHandle(), 0, MIDI_WAVE_OPEN_TYPE.CALLBACK_EVENT) != 0)
                {
                    return false;
                }

                _out = output;
            }

            var header = new WAVEHDR { lpData = new PSTR(d), dwBufferLength = (uint)data.Length };
            var size = (uint)sizeof(WAVEHDR);
            var played = PInvoke.waveOutPrepareHeader(output, &header, size) == 0;
            if (played)
            {
                played = PInvoke.waveOutWrite(output, &header, size) == 0;

                // The event is set when the device opens, and again when the buffer is done (or reset by Stop).
                while (played && (Volatile.Read(ref header.dwFlags) & PInvoke.WHDR_DONE) == 0)
                {
                    done.WaitOne(TimeSpan.FromMilliseconds(250));
                }

                PInvoke.waveOutUnprepareHeader(output, &header, size);
            }

            lock (_gate)
            {
                _out = default;
                PInvoke.waveOutClose(output);
                return played || _stopped;
            }
        }
    }

    /// <summary>Ends what's playing, and anything this player is asked to play afterwards.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            _stopped = true;
            if (_out != default)
            {
                PInvoke.waveOutReset(_out);
            }
        }
    }
}
