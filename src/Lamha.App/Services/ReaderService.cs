using Lamha.App.ViewModels.Reader;
using Lamha.App.Windows;
using Lamha.Core;

namespace Lamha.App.Services;

/// <summary>
/// The reader's Lamha (<c>--book</c>, decision 0022): opens the book's window at once, adds the file to the library (or
/// finds the library's book by id), opens it where the reader was, and opens the card on a clicked word in its sentence.
/// Closing the window ends this Lamha (<see cref="Ended"/>), so all its memory goes back.
/// </summary>
public sealed partial class ReaderService(Brain brain, CardService card, IdleMemory idle) : IDisposable
{
    private ReaderViewModel? _viewModel;

    /// <summary><c>--theme</c>: the window's theme for this run instead of the setting.</summary>
    public string? Theme { get; init; }

    /// <summary>The book's window closed: this Lamha ends.</summary>
    public Action? Ended { get; init; }

    /// <summary>Opens <paramref name="book"/>: a file's path, or a library book's id.</summary>
    public async Task OpenAsync(string book)
    {
        ArgumentNullException.ThrowIfNull(book);
        Animations.Refresh(brain.Settings.Current);
        var window = new ReaderWindow(brain.Settings.Current.WindowMica);
        window.UseTheme(Theme ?? brain.Settings.Current.Theme);
        window.WordClicked += (word, context) => card.LookUp(word, 0, context);
        window.Closed += (_, _) =>
        {
            idle.WindowClosed();
            Ended?.Invoke();
        };
        idle.WindowOpened();
        window.ShowCentred();

        try
        {
            var library = brain.Library;
            var record = await Task.Run(async () =>
            {
                if (!File.Exists(book))
                {
                    return library.Get(book) ?? throw new LamhaException(ErrorCodes.BookBad);
                }

                await using var file = new FileStream(book, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                return (await library.AddAsync(file, Path.GetFileName(book)).ConfigureAwait(false)).Book;
            }).ConfigureAwait(true);
            var opened = await Task.Run(() => library.Open(record)).ConfigureAwait(true);
            _viewModel = new ReaderViewModel(library, opened, record);
            AppLog.Info($"reader: opened a {record.Format} book, {record.Words} words, {opened.Parts.Count} parts");
            window.Show(_viewModel);

            // How far through the book is by words: counted in a second copy off the UI thread, so the page's own
            // reading of the book never meets it.
            var words = await Task.Run(() =>
            {
                using var copy = library.Open(record);
                return ReaderViewModel.CountWords(copy);
            }).ConfigureAwait(true);
            _viewModel?.UseWordCounts(words);
        }
        catch (LamhaException ex)
        {
            AppLog.Info($"reader: book not opened ({ex.Code})");
            window.ShowProblem(Localization.Get($"Reader_{ex.Code}_Title"), Localization.Get($"Reader_{ex.Code}_Hint"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("reader: book not read", ex);
            window.ShowProblem(Localization.Get("Reader_book_bad_Title"), Localization.Get("Reader_NotRead_Hint"));
        }
    }

    public void Dispose()
    {
        _viewModel?.Dispose();
        _viewModel = null;
    }
}
