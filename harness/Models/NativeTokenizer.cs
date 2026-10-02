using System.Text.Json.Nodes;
using Tokenizers.HuggingFace.Tokenizer;

namespace Harness.Models;

public sealed class NativeTokenizer : IDisposable
{
    private readonly Tokenizer tokenizer;
    private readonly string temporaryPath;

    public NativeTokenizer(string path)
    {
        JsonNode root = JsonNode.Parse(File.ReadAllText(path))!;
        root["truncation"] = null;
        root["padding"] = null;
        temporaryPath = Path.Combine(Path.GetTempPath(), $"website-tokenizer-{Guid.NewGuid():N}.json");
        File.WriteAllText(temporaryPath, root.ToJsonString());
        tokenizer = Tokenizer.FromFile(temporaryPath);
    }

    public int[] Encode(string text) => tokenizer.Encode(text, false).First().Ids.Select(id => checked((int)id)).ToArray();
    public string Decode(IReadOnlyList<int> ids) => tokenizer.Decode(ids.Select(id => checked((uint)id)), false);
    public int SpecialId(string text)
    {
        int[] ids = Encode(text);
        if (ids.Length != 1) { throw new InvalidDataException($"Special token must encode as one ID: {text}"); }
        return ids[0];
    }
    public void Dispose() { tokenizer.Dispose(); File.Delete(temporaryPath); }
}
