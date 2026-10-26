// dotnet run --project tools/DictionaryBuilder -- <shards folder> <output file>
// e.g. dotnet run --project tools/DictionaryBuilder -- dict artifacts/dict/lamha-dict.db
//
// dotnet run --project tools/DictionaryBuilder -- pack <Lamha 2 pack.json.gz>… <output folder>
// e.g. dotnet run --project tools/DictionaryBuilder -- pack packs-v1/fr.json.gz packs-v1/tr.json.gz artifacts/packs
// writes <lang>.db (to try) and <lang>.db.gz (for the packs release) for each of Lamha 2's language packs.
using System.Diagnostics;
using Lamha.Data.Dictionary;

if (args.Length >= 3 && args[0] == "pack")
{
    var output = args[^1];
    Directory.CreateDirectory(output);
    foreach (var source in args[1..^1])
    {
        var watch = Stopwatch.StartNew();
        var temp = Path.Combine(output, Path.GetFileName(source) + ".db");
        var lang = PackBuilder.Build(source, temp);
        var db = Path.Combine(output, lang + ".db");
        File.Move(temp, db, overwrite: true);
        PackBuilder.Compress(db, db + ".gz");
        using var pack = new PackFile(db, lang, check: true);
        Console.WriteLine($"{lang}: {pack.Meta.Words} words, version {pack.Meta.Version}; {new FileInfo(db).Length / 1024} KB, {new FileInfo(db + ".gz").Length} bytes gzip'd, in {watch.Elapsed.TotalSeconds:0.0} s");
    }

    return 0;
}

if (args.Length != 2 || !Directory.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: DictionaryBuilder <shards folder> <output file>");
    Console.Error.WriteLine("       DictionaryBuilder pack <Lamha 2 pack.json.gz>... <output folder>");
    return 2;
}

var time = Stopwatch.StartNew();
DictionaryBuilder.Build(args[0], args[1]);
using (var dictionary = new DictionaryFile(args[1]))
{
    Console.WriteLine($"{args[1]}: {new FileInfo(args[1]).Length / 1024 / 1024} MB in {time.Elapsed.TotalSeconds:0.0} s. {dictionary.Meta}");
}

return 0;
