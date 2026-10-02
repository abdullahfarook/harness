using System.Security.Cryptography;
using System.Text.Json;

namespace Harness.Models;

public sealed record ModelAssets(string DirectoryPath, string GraphPath, string TokenizerPath, JsonElement Manifest)
{
    public static ModelAssets Load(string directory, string name)
    {
        directory = Path.GetFullPath(directory);
        if (!Directory.Exists(directory)) { throw new DirectoryNotFoundException($"Model directory missing: {directory}. Run scripts/Setup-WebsiteBot.ps1."); }
        string manifestPath = Path.Combine(directory, "manifest.json");
        if (!File.Exists(manifestPath)) { throw new FileNotFoundException("Model manifest missing. Run scripts/Setup-WebsiteBot.ps1.", manifestPath); }
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        JsonElement manifest = document.RootElement.Clone();
        string expectedRepo = name switch { "gemma"=>"onnx-community/gemma-4-E2B-it-ONNX", "lfm" => "LiquidAI/LFM2.5-1.2B-Thinking-ONNX", "laya" => "receptron/laya-onnx", _ => throw new ArgumentException("Unknown model", nameof(name)) };
        if (manifest.GetProperty("name").GetString() != name || manifest.GetProperty("repo").GetString() != expectedRepo) { throw new InvalidDataException("Wrong model identity in manifest."); }
        string[] required=name switch { "gemma"=>["onnx/decoder_model_merged_q4.onnx","onnx/decoder_model_merged_q4.onnx_data","onnx/embed_tokens_q4.onnx","onnx/embed_tokens_q4.onnx_data","config.json","generation_config.json","tokenizer.json","tokenizer_config.json"],"lfm"=>["onnx/model_q4.onnx","onnx/model_q4.onnx_data","config.json","generation_config.json","tokenizer.json","tokenizer_config.json"],_=>["laya.onnx","laya.onnx.data","laya_config.json","tokenizer/tokenizer.json","tokenizer/tokenizer_config.json"] };
        HashSet<string> listed=manifest.GetProperty("files").EnumerateArray().Select(f=>f.GetProperty("path").GetString()!).ToHashSet(StringComparer.Ordinal);
        if (required.Any(file=>!listed.Contains(file))) { throw new InvalidDataException("Manifest omits required model files."); }
        foreach (JsonElement file in manifest.GetProperty("files").EnumerateArray())
        {
            string path = Path.GetFullPath(Path.Combine(directory, file.GetProperty("path").GetString()!));
            if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { throw new InvalidDataException("Unsafe manifest path."); }
            if (!File.Exists(path)) { throw new FileNotFoundException("Incomplete model bundle", path); }
            if (new FileInfo(path).Length != file.GetProperty("bytes").GetInt64()) { throw new InvalidDataException($"Wrong file size: {path}"); }
            using FileStream stream = File.OpenRead(path);
            if (!Convert.ToHexStringLower(SHA256.HashData(stream)).Equals(file.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase)) { throw new InvalidDataException($"Checksum mismatch: {path}"); }
        }
        string graph = Path.Combine(directory, name switch { "gemma"=>"onnx/decoder_model_merged_q4.onnx","lfm"=>"onnx/model_q4.onnx",_=>"laya.onnx" });
        string tokenizer = Path.Combine(directory, name != "laya" ? "tokenizer.json" : "tokenizer/tokenizer.json");
        if (!File.Exists(graph) || !File.Exists(tokenizer)) { throw new FileNotFoundException("Required graph/tokenizer missing."); }
        return new(directory, graph, tokenizer, manifest);
    }
}
