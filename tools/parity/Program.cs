// The parity tool's Lamha 3 half. parity.mjs starts it (node tools/parity/parity.mjs) with the repository's root:
//   dotnet <Parity.dll> <repository root>
// It opens the real dictionary (built from legacy/dict into artifacts/parity/ when the shards are newer), then runs each
// case parity.mjs sends through Lamha 3's Core and sends back the result.
using Lamha.Core.Dictionary;
using Lamha.Data.Dictionary;
using Lamha.Tools.Parity;

if (args.Length != 1 || !File.Exists(Path.Combine(args[0], "Lamha.slnx")))
{
    Console.Error.WriteLine("Usage: Parity <repository root> (start it through parity.mjs)");
    return 2;
}

var shards = Path.Combine(args[0], "legacy", "dict");
var file = Path.Combine(args[0], "artifacts", "parity", "lamha-dict.db");
if (!File.Exists(file) || File.GetLastWriteTimeUtc(file) < File.GetLastWriteTimeUtc(Path.Combine(shards, "meta.json")))
{
    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
    DictionaryBuilder.Build(shards, file);
}

using var data = new DictionaryFile(file);
var dictionary = new LocalDictionary(data);
var bridge = new Bridge();
bridge.Start();
bridge.Send(new() { ["op"] = "ready" });
await foreach (var message in bridge.Cases.ReadAllAsync())
{
    if ((string?)message["op"] == "case")
    {
        bridge.Send(await CoreCase.RunAsync(bridge, dictionary, message));
    }
}

return 0;
