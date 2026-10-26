using System.Buffers.Binary;

namespace Lamha.Core.Speech;

/// <summary>
/// A sound as Windows' wave player takes it: its format (a WAVEFORMATEX, with the bytes a compressed format adds) and
/// its data. Google's voice is MP3, which Windows' own MP3 codec decodes; Windows' voice is a WAV file.
/// </summary>
public sealed record WaveSound(byte[] Format, byte[] Data)
{
    private const ushort Mp3Tag = 0x55; // WAVE_FORMAT_MPEGLAYER3

    // Layer III's bitrates (kbit/s, by the header's index 1–14) and sample rates (Hz, index 0–2), from the MPEG standard.
    private static readonly int[] Mpeg1Bitrates = [32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];
    private static readonly int[] Mpeg2Bitrates = [8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160];
    private static readonly int[] Mpeg1Rates = [44100, 48000, 32000];
    private static readonly int[] Mpeg2Rates = [22050, 24000, 16000];
    private static readonly int[] Mpeg25Rates = [11025, 12000, 8000];

    /// <summary>
    /// MP3 (Google's voice: MPEG-2 Layer III, 24 kHz, mono), described by its first frame; an ID3 tag before it is
    /// skipped. Null when it isn't MP3 (an error page, an empty answer).
    /// </summary>
    public static WaveSound? FromMp3(ReadOnlySpan<byte> mp3)
    {
        var start = 0;
        if (mp3.Length >= 10 && mp3[..3].SequenceEqual("ID3"u8))
        {
            // ID3v2: a 10-byte header, its size in 7-bit bytes, and a footer when flag 0x10 is set.
            var size = (mp3[6] & 0x7F) << 21 | (mp3[7] & 0x7F) << 14 | (mp3[8] & 0x7F) << 7 | mp3[9] & 0x7F;
            start = 10 + size + ((mp3[5] & 0x10) != 0 ? 10 : 0);
        }

        if (start + 4 > mp3.Length || mp3[start] != 0xFF || (mp3[start + 1] & 0xE0) != 0xE0)
        {
            return null;
        }

        var version = (mp3[start + 1] >> 3) & 3; // 3: MPEG-1, 2: MPEG-2, 0: MPEG-2.5
        var layer = (mp3[start + 1] >> 1) & 3; // 1: Layer III
        var bitrateIndex = mp3[start + 2] >> 4;
        var rateIndex = (mp3[start + 2] >> 2) & 3;
        if (version == 1 || layer != 1 || bitrateIndex is 0 or 15 || rateIndex == 3)
        {
            return null;
        }

        var mpeg1 = version == 3;
        var bitrate = (mpeg1 ? Mpeg1Bitrates : Mpeg2Bitrates)[bitrateIndex - 1] * 1000;
        var rate = (version switch { 3 => Mpeg1Rates, 2 => Mpeg2Rates, _ => Mpeg25Rates })[rateIndex];
        var channels = (mp3[start + 3] >> 6) == 3 ? 1 : 2;

        // MPEGLAYER3WAVEFORMAT: a WAVEFORMATEX and 12 bytes of its own.
        var format = new byte[30];
        var f = format.AsSpan();
        BinaryPrimitives.WriteUInt16LittleEndian(f, Mp3Tag);
        BinaryPrimitives.WriteUInt16LittleEndian(f[2..], (ushort)channels);
        BinaryPrimitives.WriteInt32LittleEndian(f[4..], rate);
        BinaryPrimitives.WriteInt32LittleEndian(f[8..], bitrate / 8); // bytes per second
        BinaryPrimitives.WriteUInt16LittleEndian(f[12..], 1); // block align
        BinaryPrimitives.WriteUInt16LittleEndian(f[14..], 0); // bits per sample
        BinaryPrimitives.WriteUInt16LittleEndian(f[16..], 12); // the extra bytes
        BinaryPrimitives.WriteUInt16LittleEndian(f[18..], 1); // MPEGLAYER3_ID_MPEG
        BinaryPrimitives.WriteUInt32LittleEndian(f[20..], 2); // MPEGLAYER3_FLAG_PADDING_OFF
        BinaryPrimitives.WriteUInt16LittleEndian(f[24..], (ushort)((mpeg1 ? 144 : 72) * bitrate / rate)); // a frame's bytes
        BinaryPrimitives.WriteUInt16LittleEndian(f[26..], 1); // frames per block
        BinaryPrimitives.WriteUInt16LittleEndian(f[28..], 1393); // the decoder's delay, in samples (Microsoft's value)
        return new WaveSound(format, mp3[start..].ToArray());
    }

    /// <summary>A WAV file (RIFF: its "fmt " and "data" chunks), as Windows' voice makes it; null when it isn't one.</summary>
    public static WaveSound? FromWave(ReadOnlySpan<byte> wav)
    {
        if (wav.Length < 12 || !wav[..4].SequenceEqual("RIFF"u8) || !wav[8..12].SequenceEqual("WAVE"u8))
        {
            return null;
        }

        byte[]? format = null;
        for (var at = 12; at + 8 <= wav.Length;)
        {
            var id = wav.Slice(at, 4);
            var size = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(wav[(at + 4)..]), (uint)(wav.Length - at - 8));
            var body = wav.Slice(at + 8, size);
            if (id.SequenceEqual("fmt "u8) && size >= 16)
            {
                // A 16-byte PCMWAVEFORMAT is a WAVEFORMATEX without its cbSize (0).
                format = size == 16 ? [.. body, 0, 0] : body.ToArray();
            }
            else if (id.SequenceEqual("data"u8))
            {
                return format is null ? null : new WaveSound(format, body.ToArray());
            }

            at += 8 + size + (size & 1); // chunks are padded to an even length
        }

        return null;
    }
}
