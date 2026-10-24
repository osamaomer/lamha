// dotnet run --project tools/DictionaryBuilder -- <shards folder> <output file>
// e.g. dotnet run --project tools/DictionaryBuilder -- legacy/dict artifacts/dict/lamha-dict.db
using System.Diagnostics;
using Lamha.Data.Dictionary;

if (args.Length != 2 || !Directory.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: DictionaryBuilder <shards folder> <output file>");
    return 2;
}

var watch = Stopwatch.StartNew();
DictionaryBuilder.Build(args[0], args[1]);
using (var dictionary = new DictionaryFile(args[1]))
{
    Console.WriteLine($"{args[1]}: {new FileInfo(args[1]).Length / 1024 / 1024} MB in {watch.Elapsed.TotalSeconds:0.0} s. {dictionary.Meta}");
}

return 0;
