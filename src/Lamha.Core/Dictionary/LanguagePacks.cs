using System.Globalization;
using Lamha.Core.Abstractions;
using Lamha.Core.Net;

namespace Lamha.Core.Dictionary;

/// <summary>A pack in Settings' list: its download size and words, whether it's here, and how far a download has got.</summary>
/// <param name="Lang">"fr", "de", "es", "tr".</param>
/// <param name="Bytes">The download's size.</param>
/// <param name="Words">Its headwords.</param>
/// <param name="Installed">It's on this PC.</param>
/// <param name="Version">The installed pack's version ("2026-09-28"), or "".</param>
/// <param name="Progress">0…1 while it downloads, else null.</param>
public sealed record PackInfo(string Lang, long Bytes, int Words, bool Installed, string Version, double? Progress);

/// <summary>
/// The optional offline language packs: French–French, German–German, Spanish–Spanish and Turkish–Turkish dictionaries
/// (from each language's Wiktionary), downloaded from Settings instead of being part of everyone's copy of Lamha. With
/// one, words in that language are explained in it with no internet and no AI. Lamha 2's packs.js, rule for rule; the
/// packs themselves are SQLite files now (tools/DictionaryBuilder converts Lamha 2's), kept in <see cref="IPackStore"/>.
/// </summary>
public sealed class LanguagePacks : ILanguagePacks, IDisposable
{
    /// <summary>The GitHub release that holds the packs: <c>&lt;lang&gt;.db.gz</c>, from .github/workflows/packs.yml.</summary>
    public const string Release = "https://github.com/osamaomer/lamha/releases/download/packs-v2/";

    /// <summary>What can be downloaded, in Lamha 2's order: its size and headword count, shown before downloading.</summary>
    public static readonly IReadOnlyList<(string Lang, long Bytes, int Words)> Catalog =
    [
        ("fr", 5_875_972, 40000),
        ("de", 5_707_798, 40000),
        ("es", 3_690_217, 37539),
        ("tr", 2_112_012, 33902),
    ];

    /// <summary>Spanish and French Wiktionary don't list most plurals: "casas" → "casa", "maisons" → "maison".</summary>
    private static readonly Dictionary<string, string[]> Plurals = new() { ["es"] = ["es", "s"], ["fr"] = ["s", "x"] };

    private readonly IPackStore _store;
    private readonly Http _http;
    private readonly string _release;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, IPackData> _open = [];
    private readonly Dictionary<string, double> _progress = [];

    /// <param name="store">Where they're kept.</param>
    /// <param name="http">Downloads them.</param>
    /// <param name="release">Another address to download them from (ending in "/"), to try packs not published yet.</param>
    public LanguagePacks(IPackStore store, Http http, string? release = null)
    {
        _store = store;
        _http = http;
        _release = release ?? Release;
        foreach (var pack in store.Open())
        {
            if (Catalog.Any(c => c.Lang == pack.Meta.Lang) && _open.TryAdd(pack.Meta.Lang, pack))
            {
                continue;
            }

            pack.Dispose();
        }
    }

    /// <summary>A pack was installed or removed, or a download moved on: Settings' list. On any thread.</summary>
    public event Action? Changed;

    public IReadOnlyList<string> Languages
    {
        get
        {
            lock (_gate)
            {
                return [.. Catalog.Select(c => c.Lang).Where(_open.ContainsKey)];
            }
        }
    }

    /// <summary>The installed packs, for a lookup's cache key ("de,fr").</summary>
    public string Key => string.Join(',', Languages.Order(StringComparer.Ordinal));

    /// <summary>Settings' list: every pack with its size, whether it's here, and how far a download has got.</summary>
    public IReadOnlyList<PackInfo> List()
    {
        lock (_gate)
        {
            return [.. Catalog.Select(c => _open.TryGetValue(c.Lang, out var pack)
                ? new PackInfo(c.Lang, c.Bytes, pack.Meta.Words, true, pack.Meta.Version, Progress(c.Lang))
                : new PackInfo(c.Lang, c.Bytes, c.Words, false, "", Progress(c.Lang)))];
        }
    }

    /// <summary>
    /// Downloads the <paramref name="lang"/> pack and keeps it, in place of the one before. It counts as installed only
    /// once it's all here and checked. Throws <see cref="LamhaException"/>: pack_unknown, pack_busy, pack_download
    /// (no internet, not on the release, cut short) or pack_bad (damaged, or not that language's pack).
    /// </summary>
    public async Task InstallAsync(string lang, CancellationToken cancel = default)
    {
        if (!Catalog.Any(c => c.Lang == lang))
        {
            throw new LamhaException(ErrorCodes.PackUnknown);
        }

        lock (_gate)
        {
            if (!_progress.TryAdd(lang, 0))
            {
                throw new LamhaException(ErrorCodes.PackBusy);
            }
        }

        Changed?.Invoke();
        try
        {
            await ReceiveAsync(lang, cancel).ConfigureAwait(false);
            lock (_gate)
            {
                if (_open.Remove(lang, out var old))
                {
                    old.Dispose();
                }

                try
                {
                    _open[lang] = _store.Install(lang);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    throw new LamhaException(ErrorCodes.PackDownload);
                }
                catch (InvalidDataException)
                {
                    throw new LamhaException(ErrorCodes.PackBad);
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                _progress.Remove(lang);
            }

            Changed?.Invoke();
        }
    }

    /// <summary>Deletes the <paramref name="lang"/> pack: its words are no longer explained offline.</summary>
    public void Remove(string lang)
    {
        if (!Catalog.Any(c => c.Lang == lang))
        {
            throw new LamhaException(ErrorCodes.PackUnknown);
        }

        lock (_gate)
        {
            if (_open.Remove(lang, out var pack))
            {
                pack.Dispose();
            }

            _store.Remove(lang);
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// <paramref name="text"/> in the <paramref name="lang"/> pack: as written, in small letters, or capitalised (German
    /// nouns: "Haus"); then as an inflected form of a headword ("evler" → "ev"); then, in Spanish and French, as a
    /// plural. Lowercasing is the language's own (Turkish İ and I).
    /// </summary>
    public PackHit? Find(string lang, string text)
    {
        var t = text.Trim();
        if (t.Length is 0 or > 40)
        {
            return null;
        }

        lock (_gate)
        {
            if (!_open.TryGetValue(lang, out var pack))
            {
                return null;
            }

            var culture = CultureInfo.GetCultureInfo(lang);
            var lower = t.ToLower(culture);
            string[] candidates = [.. new[] { t, lower, culture.TextInfo.ToUpper(lower[0]) + lower[1..] }.Distinct(StringComparer.Ordinal)];
            foreach (var word in candidates)
            {
                if (pack.Entry(word) is { } entry)
                {
                    return new PackHit(word, "", entry);
                }
            }

            foreach (var word in candidates)
            {
                if (pack.Lemma(word) is { Length: > 0 } lemma && pack.Entry(lemma) is { } entry)
                {
                    return new PackHit(lemma, word, entry);
                }
            }

            foreach (var end in Plurals.GetValueOrDefault(lang) ?? [])
            {
                if (!lower.EndsWith(end, StringComparison.Ordinal) || lower.Length < end.Length + 3)
                {
                    continue;
                }

                var word = lower[..^end.Length];
                if (pack.Entry(word) is { } entry)
                {
                    return new PackHit(word, t, entry);
                }
            }

            return null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var pack in _open.Values)
            {
                pack.Dispose();
            }

            _open.Clear();
        }
    }

    /// <summary>The download, written and checked by the store; up to 95% while it comes (the rest is installing it).</summary>
    private async Task ReceiveAsync(string lang, CancellationToken cancel)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(Http.Get(_release + lang + ".db.gz"), TimeSpan.FromSeconds(30), cancel, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        }
        catch (HttpFailure)
        {
            throw new LamhaException(ErrorCodes.PackDownload);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new LamhaException(ErrorCodes.PackDownload);
            }

            var total = response.Content.Headers.ContentLength ?? Catalog.First(c => c.Lang == lang).Bytes;

            // A slow connection may take a while; one that stalls shouldn't keep the button busy forever.
            using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(10), _http.Time);
            using var both = CancellationTokenSource.CreateLinkedTokenSource(limit.Token, cancel);
            try
            {
                var body = await response.Content.ReadAsStreamAsync(both.Token).ConfigureAwait(false);
                await using (body.ConfigureAwait(false))
                {
                    var counted = new CountingStream(body, got => Report(lang, total > 0 ? Math.Min(0.95, 0.95 * got / total) : 0));
                    await _store.ReceiveAsync(lang, counted, both.Token).ConfigureAwait(false);
                }
            }
            catch (InvalidDataException)
            {
                throw new LamhaException(ErrorCodes.PackBad);
            }
            catch (Exception e) when (e is IOException or HttpRequestException or UnauthorizedAccessException
                || e is OperationCanceledException && !cancel.IsCancellationRequested)
            {
                throw new LamhaException(ErrorCodes.PackDownload);
            }
        }
    }

    private double? Progress(string lang) => _progress.TryGetValue(lang, out var p) ? p : null;

    /// <summary>A download's progress; Settings hears of it once per percent.</summary>
    private void Report(string lang, double progress)
    {
        lock (_gate)
        {
            if (!_progress.TryGetValue(lang, out var before))
            {
                return;
            }

            _progress[lang] = progress;
            if ((int)(before * 100) == (int)(progress * 100))
            {
                return;
            }
        }

        Changed?.Invoke();
    }

    /// <summary>A stream read as it comes, counting what it has read.</summary>
    private sealed class CountingStream(Stream inner, Action<long> read) : Stream
    {
        private long _count;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _count;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Counted(inner.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer) => Counted(inner.Read(buffer));

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Counted(await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Counted(await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Counted(int n)
        {
            _count += n;
            read(_count);
            return n;
        }
    }
}
