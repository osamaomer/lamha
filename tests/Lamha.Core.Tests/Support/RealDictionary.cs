using Lamha.Data.Dictionary;

namespace Lamha.Core.Tests.Support;

/// <summary>
/// The real offline dictionary, built once from Lamha 2's shards (legacy/dict) next to the tests and opened read-only:
/// Lamha 2's tests with <c>realDict: true</c>. It's built again when the shards or the builder change.
/// </summary>
public static class RealDictionary
{
    private static readonly Lazy<DictionaryFile> File = new(Open);

    public static DictionaryFile Data => File.Value;

    /// <summary>The repository's root: the folder with Lamha.slnx.</summary>
    public static string RepositoryRoot
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                if (System.IO.File.Exists(Path.Combine(dir.FullName, "Lamha.slnx")))
                {
                    return dir.FullName;
                }
            }

            throw new InvalidOperationException("Lamha.slnx not found above " + AppContext.BaseDirectory);
        }
    }

    private static DictionaryFile Open()
    {
        var shards = Path.Combine(RepositoryRoot, "legacy", "dict");
        var path = Path.Combine(AppContext.BaseDirectory, "lamha-dict.db");
        var builtFrom = new[] { System.IO.File.GetLastWriteTimeUtc(Path.Combine(shards, "meta.json")), System.IO.File.GetLastWriteTimeUtc(Path.Combine(AppContext.BaseDirectory, "Lamha.Data.dll")) }.Max();
        if (!System.IO.File.Exists(path) || System.IO.File.GetLastWriteTimeUtc(path) < builtFrom)
        {
            DictionaryBuilder.Build(shards, path);
        }

        return new DictionaryFile(path);
    }
}
