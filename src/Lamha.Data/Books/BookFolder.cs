using System.Security.Cryptography;
using Lamha.Core.Abstractions;

namespace Lamha.Data.Books;

/// <summary>
/// The books folder (<c>Lamha Data\books</c>): Lamha's copy of each book and its cover. A book is received as a
/// ".part" file and named for good only once it's been read, so a book that fails to open never sits among the others.
/// </summary>
public sealed class BookFolder(string folder) : IBookFiles
{
    public async Task<ReceivedBook> ReceiveAsync(Stream source, CancellationToken cancel = default)
    {
        Directory.CreateDirectory(folder);
        var temp = $"{Guid.NewGuid():N}.part";
        var path = Path.Combine(folder, temp);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long size = 0;
            var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await using (output.ConfigureAwait(false))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
                    size += read;
                }
            }

            return new ReceivedBook(temp, Convert.ToHexStringLower(hash.GetHashAndReset()), size);
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    public Stream Open(string name) => new FileStream(PathOf(name), FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess);

    public void Keep(ReceivedBook received, string name) => File.Move(PathOf(received.TempFile), PathOf(name), overwrite: true);

    public void Discard(ReceivedBook received) => TryDelete(PathOf(received.TempFile));

    public void SaveCover(string name, Stream picture)
    {
        Directory.CreateDirectory(folder);
        using var output = new FileStream(PathOf(name), FileMode.Create, FileAccess.Write, FileShare.None);
        picture.CopyTo(output);
    }

    public void Delete(string name) => TryDelete(PathOf(name));

    public void Tidy()
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var part in Directory.EnumerateFiles(folder, "*.part"))
        {
            TryDelete(part);
        }
    }

    // Only a plain name in the folder: a name from the database can never reach outside it.
    private string PathOf(string name) =>
        name.Length > 0 && Path.GetFileName(name) == name && name is not ("." or "..")
            ? Path.Combine(folder, name)
            : throw new ArgumentException("Not a file name of the books folder.", nameof(name));

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // In use (a book open in the reader): Tidy or the next purge tries again.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
