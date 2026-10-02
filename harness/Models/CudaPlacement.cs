using System.Text.Json;

namespace Harness.Models;

public static class CudaPlacement
{
    public static void Validate(string path)
    {
        using JsonDocument document=JsonDocument.Parse(File.ReadAllText(path));
        bool gpu=false;
        string[] shapeOps=["Shape","Size","Gather","Unsqueeze","Squeeze","Concat","Cast","Add","Mul","Sub","Div","Range","Slice","Reshape","Equal","Where","Expand","ConstantOfShape","Not","Less"];
        foreach (JsonElement entry in document.RootElement.EnumerateArray())
        {
            if (!entry.TryGetProperty("args",out JsonElement args) || !args.TryGetProperty("provider",out JsonElement provider)) { continue; }
            string? op=args.TryGetProperty("op_name",out JsonElement name) ? name.GetString() : null;
            if (provider.GetString()=="CUDAExecutionProvider") { gpu=true; continue; }
            if (provider.GetString()!="CPUExecutionProvider") { continue; }
            if (op is null || !shapeOps.Contains(op,StringComparer.Ordinal)) { throw new InvalidDataException($"CUDA profile shows CPU compute fallback: {op}. See {path}"); }
            if (op is not ("Shape" or "Size") && args.TryGetProperty("input_type_shape",out JsonElement inputs) && LargePayload(inputs)) { throw new InvalidDataException($"CUDA profile shows large CPU payload for {op}. See {path}"); }
        }
        if (!gpu) { throw new InvalidDataException($"No CUDA node execution proved by {path}."); }
    }
    private static bool LargePayload(JsonElement inputs)
    {
        foreach (JsonElement input in inputs.EnumerateArray())
        {
            foreach (JsonProperty tensor in input.EnumerateObject())
            {
                long size=1;
                foreach (JsonElement dim in tensor.Value.EnumerateArray()) { size*=Math.Max(1,dim.GetInt64()); if (size>1024) { return true; } }
            }
        }
        return false;
    }
}
