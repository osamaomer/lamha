using System.IO.Compression;
using System.Text.RegularExpressions;
using Lamha.Core.Abstractions;

namespace Lamha.Data.Dictionary;

/// <summary>
/// Downloaded language packs as files in the data folder's <c>packs</c>: <c>fr.db</c>, <c>de.db</c>… (Lamha 2's
/// desktop/pack-store.js kept a folder of JSON parts). A download is written as <c>fr.db.part</c> and checked before it
/// takes the place of <c>fr.db</c>. Only a language code becomes a file name.
/// </summary>
public sealed partial class PackFolder(string folder) : IPackStore
{
    public IReadOnlyList<IPackData> Open()
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        // A download that never finished (Lamha closed, Windows shut down).
        foreach (var part in Directory.EnumerateFiles(folder, "*.part"))
        {
            TryDelete(part);
        }

        var packs = new List<IPackData>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.db").Order(StringComparer.Ordinal))
        {
            var lang = Path.GetFileNameWithoutExtension(file);
            if (!Language().IsMatch(lang))
            {
                continue;
            }

            try
            {
                packs.Add(new PackFile(file, lang));
            }
            catch (InvalidDataException)
            {
                // Another format or damaged: not used, and downloaded again from Settings.
            }
        }

        return packs;
    }

    public async Task ReceiveAsync(string lang, Stream gzip, CancellationToken cancel = default)
    {
        var part = PathOf(lang) + ".part";
        Directory.CreateDirectory(folder);
        try
        {
            var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await using (output.ConfigureAwait(false))
            {
                var unzip = new GZipStream(gzip, CompressionMode.Decompress, leaveOpen: true);
                await using (unzip.ConfigureAwait(false))
                {
                    await unzip.CopyToAsync(output, cancel).ConfigureAwait(false);
                }
            }

            new PackFile(part, lang, check: true).Dispose();
        }
        catch
        {
            TryDelete(part);
            throw;
        }
    }

    public IPackData Install(string lang)
    {
        var file = PathOf(lang);
        File.Move(file + ".part", file, overwrite: true);
        return new PackFile(file, lang);
    }

    public void Remove(string lang)
    {
        var file = PathOf(lang);
        File.Delete(file);
        TryDelete(file + ".part");
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Tried again at the next start.
        }
    }

    [GeneratedRegex("^[a-z]{2,3}$")]
    private static partial Regex Language();

    private string PathOf(string lang) =>
        Language().IsMatch(lang) ? Path.Combine(folder, lang + ".db") : throw new ArgumentException("Not a language pack's name", nameof(lang));
}
