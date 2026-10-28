using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lamha.App.Controls;
using Lamha.App.Services;
using Lamha.Core;
using Lamha.Core.Abstractions;
using Lamha.Core.Reading;

namespace Lamha.App.ViewModels.Reader;

/// <summary>A block of the book on the page, and where it is: its part of the reading order and its place in that part.</summary>
/// <param name="Block">The block.</param>
/// <param name="Part">Its part of the book's reading order.</param>
/// <param name="Index">Its place in that part.</param>
/// <param name="Pictures">Opens a picture of the book by its path (for an image block).</param>
/// <param name="Look">The page's look (Aa) as it is now.</param>
public sealed record ReaderBlock(BookBlock Block, int Part, int Index, Func<string, Stream?> Pictures, Func<BookLook> Look);

/// <summary>
/// The reading page (docs/design/reader.html, signed off): the book's blocks as one scroll, a part loaded when the reader
/// comes near it, the place kept as they read, and the footer's chapter and progress. The window shows it; the card
/// opens on the word a click lands on.
/// </summary>
public sealed partial class ReaderViewModel : ObservableObject, IDisposable
{
    private readonly Library _library;
    private readonly IFlowingBook _book;
    private readonly BookRecord _record;
    private readonly List<ContentsEntry> _contents;
    private int _first;
    private int _last;
    private double _progress;
    private int _at;

    // Each part's blocks' words (counted after the book opens), for how far through the book the reader is.
    private IReadOnlyList<int[]>? _words;
    private long _allWords;
    private readonly ReaderLookStore _looks;

    /// <param name="library">The library, which keeps the place.</param>
    /// <param name="book">The book, opened (the view model disposes it).</param>
    /// <param name="record">The book in the library.</param>
    /// <param name="looks">Where Aa is kept, for every book.</param>
    public ReaderViewModel(Library library, IFlowingBook book, BookRecord record, ReaderLookStore looks)
    {
        _library = library;
        _book = book;
        _record = record;
        _looks = looks;
        Look = new BookLook(looks.Get());
        _contents = [.. Flatten(book.Contents)];
        Title = record.Title;
        Author = string.Join(Localization.Get("Reader_AuthorsAnd"), record.Authors);
        Window = Author.Length > 0 ? $"{Title} · {Author}" : Title;
        var start = Math.Clamp(record.Place.Part, 0, book.Parts.Count - 1);
        _first = _last = start;
        Add(start, Blocks.Count);
        StartBlock = Math.Clamp(record.Place.Block, 0, Math.Max(0, Blocks.Count - 1));
        Place = new BookPlace(start, StartBlock);
    }

    public string Title { get; }

    public string Author { get; }

    /// <summary>The window's name in the taskbar: the title and the author.</summary>
    public string Window { get; }

    /// <summary>The blocks loaded, in the book's order (a <see cref="ObservableCollection{T}"/>, as WinUI binds it under AOT).</summary>
    public ObservableCollection<ReaderBlock> Blocks { get; } = [];

    /// <summary>The block to show first: where the reader was.</summary>
    public int StartBlock { get; }

    /// <summary>Where the reader is: the first block at the top of the page.</summary>
    public BookPlace Place { get; private set; }

    /// <summary>The footer's start: the part's line in the contents ("Chapter 3"), or its number.</summary>
    [ObservableProperty]
    public partial string Chapter { get; private set; } = "";

    /// <summary>The footer's end: how far through the book ("12% of the book").</summary>
    [ObservableProperty]
    public partial string Progress { get; private set; } = "";

    /// <summary>Aa: the page's face, size, spacing, width and colour, the same for every book.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText), nameof(CanBeSmaller), nameof(CanBeLarger))]
    public partial BookLook Look { get; private set; }

    /// <summary>Aa's text size, in the interface's digits.</summary>
    public string SizeText => Localization.Number(Look.Look.Size);

    public bool CanBeSmaller => Look.Look.Size > ReaderLook.SmallestSize;

    public bool CanBeLarger => Look.Look.Size < ReaderLook.LargestSize;

    /// <summary>Whether Aa's <paramref name="choice"/> ("font", "spacing", "width", "page") is <paramref name="value"/>, for its buttons.</summary>
    public static bool Chosen(BookLook look, string choice, string value)
    {
        ArgumentNullException.ThrowIfNull(look);
        return choice switch
        {
            "font" => look.Look.Font == value,
            "spacing" => look.Look.LineSpacing.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) == value,
            "width" => look.Look.Width == value,
            "page" => look.Look.Page == value,
            _ => false,
        };
    }

    [RelayCommand]
    private void SetFont(string font) => Change(Look.Look with { Font = font });

    [RelayCommand]
    private void SetSpacing(string spacing) =>
        Change(Look.Look with { LineSpacing = double.Parse(spacing, System.Globalization.CultureInfo.InvariantCulture) });

    [RelayCommand]
    private void SetWidth(string width) => Change(Look.Look with { Width = width });

    [RelayCommand]
    private void SetPage(string page) => Change(Look.Look with { Page = page });

    [RelayCommand]
    private void Resize(string steps) => Change(Look.Look.Resized(int.Parse(steps, System.Globalization.CultureInfo.InvariantCulture)));

    // Kept for every book, and a new look each time, so Aa's buttons all follow even when the same one is pressed again.
    private void Change(ReaderLook look)
    {
        try
        {
            Look = new BookLook(_looks.Save(look));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AppLog.Error("reader: keeping Aa", ex);
            Look = new BookLook(look.Checked());
        }
    }

    /// <summary>The reader is at <paramref name="index"/> of the blocks loaded: the footer follows, and the place is kept.</summary>
    public void ReadingAt(int index)
    {
        if (index < 0 || index >= Blocks.Count)
        {
            return;
        }

        _at = index;
        var block = Blocks[index];
        Place = new BookPlace(block.Part, block.Index);
        Chapter = ChapterName(index);
        if (_words is { } words && block.Part < words.Count)
        {
            // By words: the parts of a book are often of very different lengths (one file for a whole novel).
            var before = words.Take(block.Part).Sum(w => (long)w.Sum()) + words[block.Part].Take(block.Index).Sum();
            _progress = _allWords > 0 ? (double)before / _allWords : 0;
        }
        else
        {
            var inPart = Blocks.Count(b => b.Part == block.Part);
            _progress = (block.Part + ((double)block.Index / Math.Max(1, inPart))) / _book.Parts.Count;
        }

        Progress = Localization.Format("Reader_OfTheBook", Localization.Number((int)Math.Round(_progress * 100)));
    }

    /// <summary>The words of each part's blocks, counted off the UI thread once the book is open.</summary>
    public void UseWordCounts(IReadOnlyList<int[]> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        _words = words;
        _allWords = words.Sum(w => (long)w.Sum());
        ReadingAt(_at);
    }

    /// <summary>Counts each part's blocks' words in <paramref name="book"/> (a second copy, off the UI thread).</summary>
    public static IReadOnlyList<int[]> CountWords(IFlowingBook book)
    {
        ArgumentNullException.ThrowIfNull(book);
        return [.. book.Parts.Select(p =>
        {
            try
            {
                return book.ReadPart(p).Select(b => Library.CountWords(b.Text)).ToArray();
            }
            catch (LamhaException)
            {
                return [];
            }
        })];
    }

    /// <summary>The reader came near the end of what's loaded: the next part goes after it. True when one was added.</summary>
    public bool LoadNext()
    {
        if (_last + 1 >= _book.Parts.Count)
        {
            return false;
        }

        _last++;
        Add(_last, Blocks.Count);
        return true;
    }

    /// <summary>The reader came near the start of what's loaded: the part before goes in front. Returns how many blocks came.</summary>
    public int LoadPrevious()
    {
        if (_first == 0)
        {
            return 0;
        }

        _first--;
        return Add(_first, 0);
    }

    /// <summary>The word a click landed on in a block, and its sentence, for the card; null on a space or punctuation.</summary>
    public (string Word, Core.Dictionary.WordContext Context)? WordAt(ReaderBlock block, int index)
    {
        ArgumentNullException.ThrowIfNull(block);
        var text = block.Block.Text;
        if (ReaderText.WordAt(text, index) is not { } word)
        {
            return null;
        }

        return (text.Substring(word.Start, word.Length), ReaderText.ContextAround(text, word.Start, word.Length));
    }

    /// <summary>Keeps the place (when the window closes, and now and then while reading).</summary>
    public void Save()
    {
        try
        {
            _library.SavePlace(_record.Id, Place, _progress);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AppLog.Error("reader: keeping the place", ex);
        }
    }

    public void Dispose() => _book.Dispose();

    // A part's blocks put at <paramref name="at"/> in the list; a part that can't be read is left out (and logged).
    private int Add(int part, int at)
    {
        IReadOnlyList<BookBlock> blocks;
        try
        {
            blocks = _book.ReadPart(_book.Parts[part]);
        }
        catch (LamhaException ex)
        {
            AppLog.Info($"reader: part {part} not read ({ex.Code})");
            return 0;
        }

        for (var i = 0; i < blocks.Count; i++)
        {
            Blocks.Insert(at + i, new ReaderBlock(blocks[i], part, i, _book.OpenResource, () => Look));
        }

        return blocks.Count;
    }

    // The contents' line for the block at <paramref name="index"/>: the nearest anchor above it (a book in one file
    // marks its chapters by anchors), else its part's own line, else the part's number.
    private string ChapterName(int index)
    {
        var block = Blocks[index];
        var path = _book.Parts[block.Part].Path;
        var here = _contents.Where(c => c.Path == path).ToList();
        for (var i = index; i >= 0 && Blocks[i].Part == block.Part; i--)
        {
            foreach (var anchor in Blocks[i].Block.Anchors)
            {
                if (here.FirstOrDefault(c => c.Anchor == anchor) is { Title.Length: > 0 } marked)
                {
                    return marked.Title;
                }
            }
        }

        return here.FirstOrDefault(c => c.Anchor.Length == 0 && c.Title.Length > 0)?.Title
            ?? here.FirstOrDefault(c => c.Title.Length > 0)?.Title
            ?? Localization.Format("Reader_PartOf", Localization.Number(block.Part + 1), Localization.Number(_book.Parts.Count));
    }

    private static IEnumerable<ContentsEntry> Flatten(IEnumerable<ContentsEntry> entries) =>
        entries.SelectMany(e => new[] { e }.Concat(Flatten(e.Children)));
}
