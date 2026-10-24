using System.Diagnostics;
using System.Text;
using Lamha.Core.Abstractions;
using Lamha.Core.Clipboard;
using Lamha.Core.Settings;
using Lamha.Data.Database;
using Microsoft.Extensions.Time.Testing;

namespace Lamha.Core.Tests.Clipboard;

/// <summary>DPAPI's stand-in: reversible, but what's on disk mustn't contain the plain text. It can be made to fail.</summary>
public sealed class FakeProtector : IDataProtector
{
    /// <summary>Unprotect fails, as on another Windows account.</summary>
    public bool Broken { get; set; }

    public byte[] Protect(byte[] data) => [0x4C, .. data.Reverse()];

    public byte[]? Unprotect(byte[] data) => Broken || data is not [0x4C, ..] ? null : [.. data.Skip(1).Reverse()];
}

/// <summary>Lamha 2's test-clipboard.mjs, section "store", on the SQLite store.</summary>
public sealed class ClipboardHistoryTests : IDisposable
{
    private readonly LamhaDatabase _db = LamhaDatabase.InMemory();
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000));
    private readonly FakeProtector _protector = new();
    private readonly SettingsService _settings;
    private ClipboardHistory _history;
    private long _captured = 1_700_000_000_000;

    public ClipboardHistoryTests()
    {
        _settings = new SettingsService(new SqliteStateStore(_db));
        _settings.Save(new LamhaSettings { ClipboardEnabled = true });
        _history = New();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void IngestThenGetTheWholeCopy()
    {
        var id = _history.Ingest(Cap("Hello world", html: "<b>Hello</b>"));
        var c = _history.Get(id)!;
        Assert.Matches("^c_[0-9a-z]{15}$", id);
        Assert.Equal("Hello world", c.Text);
        Assert.Equal("<b>Hello</b>", c.Html);
        Assert.Equal((1, 0, false, "", "en", "notepad.exe"), (c.CopyCount, c.UseCount, c.Pinned, c.Label, c.Lang, c.SourceApp));
        Assert.Empty(c.Cache.Translation);
        Assert.Null(c.Cache.English);
    }

    [Fact]
    public void IdsAreUniqueAndTimeSortable()
    {
        var ids = Enumerable.Range(0, 300).Select(i =>
        {
            if (i % 7 == 0)
            {
                _clock.Advance(TimeSpan.FromMilliseconds(1));
            }

            return _history.Ingest(Cap("t" + i));
        }).ToList();
        Assert.Equal(300, ids.Distinct().Count());
        Assert.Equal(ids.Order(StringComparer.Ordinal), ids);
    }

    [Fact]
    public void TheSameTrimmedTextIsOneCopyWithItsSourceAndFormattingRefreshed()
    {
        var a = _history.Ingest(Cap("same text", html: "<i>x</i>"));
        var b = _history.Ingest(Cap("  same text\n", app: "winword.exe"));
        Assert.Equal(a, b);
        var c = _history.Get(a)!;
        Assert.Equal(2, c.CopyCount);
        Assert.Equal("winword.exe", c.SourceApp);
        Assert.Null(c.Html);
        Assert.Equal(1, _history.List().Total);
    }

    [Fact]
    public void NewestActivityFirstAndUsingACopyMovesItUp()
    {
        var a = _history.Ingest(Cap("first"));
        _history.Ingest(Cap("second"));
        Assert.Equal(["second", "first"], Texts());
        _clock.SetUtcNow(DateTimeOffset.FromUnixTimeMilliseconds(_captured + 1000));
        _history.MarkUsed(a);
        Assert.Equal(["first", "second"], Texts());
        Assert.Equal(1, _history.Get(a)!.UseCount);
    }

    [Fact]
    public void PinnedCopiesAreAlwaysOnTopInTheListAndInSearch()
    {
        var old = _history.Ingest(Cap("old note"));
        _history.Ingest(Cap("note two"));
        var three = _history.Ingest(Cap("note three"));
        _history.SetPinned(old, true);
        Assert.Equal(["old note", "note three", "note two"], Texts());
        _history.SetPinned(three, true);
        Assert.Equal(["note three", "old note", "note two"], Texts());
        _history.Ingest(Cap("note four"));
        Assert.Equal(["note three", "old note", "note four", "note two"], Texts()); // a new copy goes under the pinned ones
        Assert.Equal(["note three", "old note", "note four", "note two"], _history.List("note").Items.Select(i => i.Text));
        _history.SetPinned(three, false);
        Assert.Equal(["old note", "note four", "note three", "note two"], Texts()); // back to its place by time
    }

    [Fact]
    public void TheLimitKeepsTheNewestUnpinnedAndPinnedCopiesSurvive()
    {
        _settings.Update(s => s with { ClipboardMaxItems = 5 });
        var pinned = _history.Ingest(Cap("keep me"));
        _history.SetPinned(pinned, true);
        for (var i = 1; i <= 6; i++)
        {
            _history.Ingest(Cap("clip " + i));
        }

        Assert.Equal(["keep me", "clip 6", "clip 5", "clip 4", "clip 3", "clip 2"], Texts());
        Assert.Equal(1, _history.List(pinnedOnly: true).Total);
        _settings.Update(s => s with { ClipboardMaxItems = 2 });
        Assert.Equal(3, _history.ApplyLimit());
        Assert.Equal(["keep me", "clip 6", "clip 5"], Texts());
    }

    [Fact]
    public void PinningMoreThanTheLimitFails()
    {
        _settings.Update(s => s with { ClipboardMaxItems = 1000 });
        for (var i = 0; i < ClipboardHistory.MaxPinned; i++)
        {
            _history.SetPinned(_history.Ingest(Cap("p" + i)), true);
        }

        var extra = _history.Ingest(Cap("one too many"));
        Assert.Equal(ClipboardHistory.PinLimit, Assert.Throws<LamhaException>(() => _history.SetPinned(extra, true)).Code);
        Assert.False(_history.Get(extra)!.Pinned);
    }

    [Fact]
    public void LabelsRemoveAndClear()
    {
        var a = _history.Ingest(Cap("IBAN SA03 8000 0000 6080 1016 7519"));
        var b = _history.Ingest(Cap("something else"));
        Assert.Equal(60, _history.SetLabel(a, "  رقم   الحساب  " + new string('x', 80)).Length);
        Assert.StartsWith("رقم الحساب x", _history.Get(a)!.Label, StringComparison.Ordinal);
        Assert.Equal(a, _history.List("الحساب").Items[0].Id);
        _history.SetPinned(a, true);
        Assert.True(_history.Remove(b));
        Assert.False(_history.Remove(b));
        _history.Ingest(Cap("temp"));
        Assert.Equal(1, _history.Clear(keepPinned: true));
        Assert.Equal(0, _history.Clear());
        Assert.Equal(ClipboardHistory.NotFound, Assert.Throws<LamhaException>(() => _history.MarkUsed(a)).Code);
    }

    [Fact]
    public void SearchMatchesArabicVariantsNeedsEveryWordAndRanksLabelsAndWordStarts()
    {
        var ar = _history.Ingest(Cap("سيذهب أحمد إلى المدرسة غدًا"));
        _history.Ingest(Cap("Invoice_Q3 total ٢٠٢٤"));
        _history.Ingest(Cap("a reinvented wheel"));
        var labelled = _history.Ingest(Cap("text without the word"));
        _history.SetLabel(labelled, "invoice مهم");
        Assert.Equal(ar, _history.List("احمد مدرسه").Items[0].Id);
        Assert.Equal(0, _history.List("احمد بيت").Total);
        var inv = _history.List("inv").Items.Select(i => i.Label.Length > 0 ? i.Label : i.Text);
        Assert.Equal(["invoice مهم", "Invoice_Q3 total ٢٠٢٤", "a reinvented wheel"], inv); // label +30, then word start +10
        Assert.Equal(1, _history.List("2024").Total);
        Assert.Equal(4, _history.List("notepad").Total); // the program is searchable
    }

    [Fact]
    public void RemoveThenUndoBringsTheCopyBackWithItsIdAndPin()
    {
        var id = _history.Ingest(Cap("undo me"));
        _history.SetPinned(id, true);
        _history.Remove(id);
        Assert.Equal(0, _history.List().Total);
        Assert.Equal(id, _history.Restore(id));
        Assert.True(_history.Get(id)!.Pinned);
        Assert.Equal(1, _history.List("undo").Total);
        Assert.Equal(ClipboardHistory.NotFound, Assert.Throws<LamhaException>(() => _history.Restore(id)).Code); // only once
        _history.Remove(id);
        var again = _history.Ingest(Cap("undo me")); // copied again before the undo: that copy wins
        Assert.Equal(again, _history.Restore(id));
        Assert.Equal(1, _history.List().Total);
    }

    [Fact]
    public void AnUndoIsPossibleForThirtySeconds()
    {
        var id = _history.Ingest(Cap("gone soon"));
        _history.Remove(id);
        _clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(ClipboardHistory.NotFound, Assert.Throws<LamhaException>(() => _history.Restore(id)).Code);
    }

    [Fact]
    public void PreviewsCarry300CharactersAndPagesStartAfterThePinned()
    {
        var id = _history.Ingest(Cap(new string('x', 1000)));
        for (var i = 0; i < 10; i++)
        {
            _history.Ingest(Cap("n" + i));
        }

        _history.SetPinned(id, true);
        var long1 = _history.List(pinnedOnly: true).Items[0];
        Assert.Equal(300, long1.Text.Length);
        Assert.Equal(1000, long1.Length);
        var page = _history.List(limit: 4, offset: 4);
        Assert.Equal(11, page.Total);
        Assert.Equal(["n6", "n5", "n4", "n3"], page.Items.Select(i => i.Text)); // the pinned copy comes first
    }

    [Fact]
    public void SearchingAThousandCopiesTakesUnder16Milliseconds()
    {
        _settings.Update(s => s with { ClipboardMaxItems = 1000 });
        string[] words = ["مصرف", "invoice", "meeting", "تقرير", "الموظفين", "budget", "مدرسة", "project", "أحمد", "report"];
        for (var i = 0; i < 1000; i++)
        {
            var body = string.Join(' ', Enumerable.Range(0, 60).Select(j => words[((i * 7) + (j * 3)) % words.Length]));
            _history.Ingest(Cap($"{i} {body} {string.Concat(Enumerable.Repeat("lorem ipsum dolor ", i % 50))}"));
        }

        foreach (var q in new[] { "مصرف", "invoice report", "احمد مدرسه", "zzz", "" })
        {
            _history.List(q); // warm-up: the first look decrypts the history
        }

        var worst = new[] { "مصرف", "invoice report", "احمد مدرسه", "zzz", "rep", "" }.Max(q =>
        {
            var watch = Stopwatch.StartNew();
            _history.List(q);
            return watch.Elapsed.TotalMilliseconds;
        });
        Assert.True(worst < 16, $"slowest query {worst:0.0} ms");
    }

    [Fact]
    public void WhatWasCopiedIsEncryptedOnDiskAndReadBackByANewHistory()
    {
        var id = _history.Ingest(Cap("secret الحافظة text", html: "<b>secret</b>"));
        _history.SetPinned(id, true);
        _history.SetLabel(id, "my secret label");
        var row = new SqliteClipStore(_db).Get(id)!;
        var onDisk = Encoding.UTF8.GetString(row.Body) + Encoding.UTF8.GetString(row.TextKey);
        Assert.DoesNotContain("secret", onDisk, StringComparison.Ordinal);
        Assert.DoesNotContain("الحافظة", onDisk, StringComparison.Ordinal);

        _history = New();
        Assert.Equal("secret الحافظة text", _history.Get(id)!.Text);
        Assert.True(_history.Get(id)!.Pinned);
        Assert.Equal(1, _history.List("الحافظه").Total);
        Assert.Equal(id, _history.Ingest(Cap("secret الحافظة text"))); // still found when copied again
    }

    [Fact]
    public void CopiesThisAccountCantDecryptAreLeftAloneNotShown()
    {
        _history.Ingest(Cap("from another account"));
        _protector.Broken = true;
        _history = New();
        Assert.Equal(0, _history.List().Total);
        Assert.Single(new SqliteClipStore(_db).All()); // still on disk
    }

    [Fact]
    public void DecryptedCopiesLeaveMemoryWhenTheHistoryIsClosed()
    {
        _history.Ingest(Cap("hello"));
        Assert.False(_history.IsLoaded); // recording a copy decrypts nothing
        _history.List();
        Assert.True(_history.IsLoaded);
        _history.Unload();
        Assert.False(_history.IsLoaded);
        Assert.Equal("hello", _history.Get(_history.List().Items[0].Id)!.Text);
    }

    [Fact]
    public void ExpiryDeletesOldUnpinnedCopiesAndKeepsPinnedAndRecentOnes()
    {
        const long Day = 86_400_000, T0 = 1_800_000_000_000;
        var old = _history.Ingest(new CapturedClip("old", null, "a.exe", T0));
        var oldPinned = _history.Ingest(new CapturedClip("old pinned", null, "a.exe", T0));
        _history.SetPinned(oldPinned, true);
        var recent = _history.Ingest(new CapturedClip("recent", null, "a.exe", T0 + (25 * Day)));
        var usedLately = _history.Ingest(new CapturedClip("used lately", null, "a.exe", T0));
        _clock.SetUtcNow(DateTimeOffset.FromUnixTimeMilliseconds(T0 + (20 * Day)));
        _history.MarkUsed(usedLately); // being used counts as activity

        _clock.SetUtcNow(DateTimeOffset.FromUnixTimeMilliseconds(T0 + (31 * Day)));
        _settings.Update(s => s with { ClipboardExpiryDays = 30 });
        Assert.Equal(1, _history.Expire());
        Assert.Null(_history.Get(old));
        Assert.NotNull(_history.Get(oldPinned));
        Assert.NotNull(_history.Get(recent));
        Assert.NotNull(_history.Get(usedLately));

        _clock.SetUtcNow(DateTimeOffset.FromUnixTimeMilliseconds(T0 + (400 * Day)));
        _settings.Update(s => s with { ClipboardExpiryDays = 0 });
        Assert.Equal(0, _history.Expire()); // never
    }

    [Fact]
    public void ProgramsAreListedByCountAndOneCanBeForgotten()
    {
        _history.Ingest(Cap("a1", app: "word.exe"));
        _history.Ingest(Cap("a2", app: "word.exe"));
        _history.Ingest(Cap("b1", app: "chrome.exe"));
        Assert.Equal([("word.exe", 2), ("chrome.exe", 1)], _history.Apps());
        Assert.Equal(2, _history.RemoveApp("word.exe"));
        Assert.Equal(["b1"], Texts());
    }

    [Fact]
    public void TheLanguageIsTaggedOnCapture()
    {
        Assert.Equal("ar", _history.Get(_history.Ingest(Cap("نص عربي")))!.Lang);
        Assert.Equal("other", _history.Get(_history.Ingest(Cap("2024")))!.Lang);
    }

    [Fact]
    public void EnglishCopiesShowTheirCachedTranslation()
    {
        var id = _history.Ingest(Cap("bank"));
        Assert.Equal("", _history.List().Items[0].Translation);
        _history.SetTranslation(id, "ar", "مصرف");
        Assert.Equal("مصرف", _history.List(targetLang: "ar").Items[0].Translation);
        Assert.Equal("", _history.List(targetLang: "fr").Items[0].Translation);
        _history = New();
        Assert.Equal("مصرف", _history.Get(id)!.Cache.Translation["ar"]);
    }

    [Fact]
    public void ChangesAreAnnounced()
    {
        var changes = 0;
        _history.Changed += (_, _) => changes++;
        var id = _history.Ingest(Cap("x y"));
        _history.SetPinned(id, true);
        _history.SetPinned(id, true); // no change
        Assert.Equal(2, changes);
    }

    private ClipboardHistory New() =>
        new(new SqliteClipStore(_db), _protector, new SqliteStateStore(_db), _settings, _clock);

    private CapturedClip Cap(string text, string? html = null, string app = "notepad.exe") => new(text, html, app, ++_captured);

    private string[] Texts() => [.. _history.List(limit: 1000).Items.Select(i => i.Text)];
}
