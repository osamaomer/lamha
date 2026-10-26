using System.Buffers.Binary;
using System.Net;
using System.Web;
using Lamha.Core.Net;
using Lamha.Core.Settings;
using Lamha.Core.Speech;
using Lamha.Core.Tests.Support;

namespace Lamha.Core.Tests.Translation;

/// <summary>Listen: Google's voice (Lamha 2's <c>speak()</c> in background.js), and the sounds Windows' wave player gets.</summary>
public class SpeechTests
{
    [Fact]
    public void AWordIsOnePieceInItsLanguageFromTheFirstAddress()
    {
        using var lamha = new TestLamha(new FakeNet(_ => FakeNet.Json(404, "{}")));
        var url = Assert.Single(lamha.Google.SpeechUrls("settle", "en"));
        var uri = new Uri(url);
        Assert.Equal("https://translate.googleapis.com/translate_tts", uri.GetLeftPart(UriPartial.Path));
        var q = HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal(("UTF-8", "gtx", "en", "1", "0", "6", "settle"), (q["ie"], q["client"], q["tl"], q["total"], q["idx"], q["textlen"], q["q"]));
    }

    [Fact]
    public void ALongTextIsReadInNumberedPiecesUpTo3000Characters()
    {
        using var lamha = new TestLamha(new FakeNet(_ => FakeNet.Json(404, "{}")));
        var text = string.Concat(Enumerable.Repeat("The meeting has been pushed back to next Thursday, so we have more time to prepare. ", 60));
        var urls = lamha.Google.SpeechUrls(text, "en");
        var pieces = urls.Select(u => HttpUtility.ParseQueryString(new Uri(u).Query)).ToList();
        Assert.All(pieces, (q, i) =>
        {
            Assert.Equal(urls.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), q["total"]);
            Assert.Equal(i.ToString(System.Globalization.CultureInfo.InvariantCulture), q["idx"]);
            Assert.True(q["q"]!.Length <= 190);
            Assert.Equal(q["q"]!.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), q["textlen"]);
        });
        Assert.Equal(System.Text.RegularExpressions.Regex.Replace(text[..3000], @"\s+", " ").Trim(), string.Join(' ', pieces.Select(q => q["q"])));
    }

    [Fact]
    public async Task WhileGoogleCantBeReachedItsVoiceIsntTriedSoWindowsVoiceSpeaksAtOnce()
    {
        using var lamha = new TestLamha(new FakeNet(_ => throw FakeNet.Offline()), new LamhaSettings { DictSource = DictSource.Online });
        Assert.Equal("network", await lamha.LookupError("One sentence here."));
        Assert.True(lamha.Google.IsDown);
        Assert.Equal("network", Assert.Throws<LamhaException>(() => lamha.Google.SpeechUrls("settle", "en")).Code);
    }

    [Fact]
    public async Task APieceIsFetchedAsItsMp3AndAFailureSaysSo()
    {
        byte[] mp3 = [0xFF, 0xF3, 0x84, 0xC4, 1, 2, 3];
        var net = new FakeNet(c => c.Url.Contains("idx=0", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(mp3) }
            : FakeNet.Json(503, "{}"));
        using var lamha = new TestLamha(net);
        var urls = lamha.Google.SpeechUrls(string.Join(' ', Enumerable.Repeat("A short sentence to say.", 12)), "en");
        Assert.Equal(mp3, await lamha.Google.SpeechPieceAsync(urls[0], TestContext.Current.CancellationToken));
        Assert.Equal(503, (await Assert.ThrowsAsync<HttpFailure>(() => lamha.Google.SpeechPieceAsync(urls[1], TestContext.Current.CancellationToken))).Status);
    }

    [Fact]
    public void GooglesMp3IsDescribedByItsFirstFrame()
    {
        // MPEG-2 Layer III, 64 kbit/s, 24 kHz, mono: what Google's voice sends.
        byte[] mp3 = [0xFF, 0xF3, 0x84, 0xC4, 0, 0, 0, 0];
        var sound = WaveSound.FromMp3(mp3)!;
        var f = sound.Format;
        Assert.Equal(30, f.Length);
        Assert.Equal((0x55, 1, 24000, 8000, 12), (U16(f, 0), U16(f, 2), I32(f, 4), I32(f, 8), U16(f, 16)));
        Assert.Equal(192, U16(f, 24)); // 72 × 64,000 / 24,000 bytes a frame
        Assert.Equal(mp3, sound.Data);
    }

    [Fact]
    public void AnMpeg1Mp3AfterAnId3TagIsReadFromItsFirstFrame()
    {
        // ID3v2 with 5 bytes of tag, then MPEG-1 Layer III, 128 kbit/s, 44.1 kHz, stereo.
        byte[] frame = [0xFF, 0xFB, 0x90, 0x00, 9, 9];
        byte[] mp3 = [.. "ID3"u8, 4, 0, 0, 0, 0, 0, 5, 1, 2, 3, 4, 5, .. frame];
        var sound = WaveSound.FromMp3(mp3)!;
        Assert.Equal((2, 44100, 16000), (U16(sound.Format, 2), I32(sound.Format, 4), I32(sound.Format, 8)));
        Assert.Equal(417, U16(sound.Format, 24)); // 144 × 128,000 / 44,100
        Assert.Equal(frame, sound.Data);
    }

    [Fact]
    public void AnAnswerThatIsntMp3IsNoSound()
    {
        Assert.Null(WaveSound.FromMp3("<html>Too many requests</html>"u8));
        Assert.Null(WaveSound.FromMp3([]));
        Assert.Null(WaveSound.FromMp3([0xFF, 0xFD, 0x90, 0x00])); // Layer II
    }

    [Fact]
    public void WindowsVoicesWaveGivesItsFormatAndDataPastOtherChunks()
    {
        byte[] pcm = [1, 2, 3, 4];
        var fmt = new byte[16];
        BinaryPrimitives.WriteUInt16LittleEndian(fmt, 1); // PCM
        BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(2), 1);
        BinaryPrimitives.WriteInt32LittleEndian(fmt.AsSpan(4), 16000);
        byte[] wav = [.. "RIFF"u8, 0, 0, 0, 0, .. "WAVE"u8, .. Chunk("fmt ", fmt), .. Chunk("LIST", [7, 7, 7]), .. Chunk("data", pcm)];
        var sound = WaveSound.FromWave(wav)!;
        Assert.Equal(18, sound.Format.Length); // a WAVEFORMATEX: cbSize added
        Assert.Equal(16000, I32(sound.Format, 4));
        Assert.Equal(pcm, sound.Data);
        Assert.Null(WaveSound.FromWave("RIFF0000WAVEdata"u8));
    }

    private static byte[] Chunk(string id, byte[] body)
    {
        var size = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(size, body.Length);
        return [.. System.Text.Encoding.ASCII.GetBytes(id), .. size, .. body, .. body.Length % 2 == 1 ? new byte[] { 0 } : []];
    }

    private static int U16(byte[] b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at));

    private static int I32(byte[] b, int at) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at));
}
