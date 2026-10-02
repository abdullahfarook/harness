using System.Text.Json;

namespace Harness.Agent;

public sealed record ParsedAction(string? Tool, Dictionary<string,object?>? Arguments, string? Final);

public static class ActionProtocol
{
    public static string RemoveThinking(string text)
    {
        int end = text.LastIndexOf("</think>", StringComparison.Ordinal);
        if (end >= 0) { return text[(end+8)..].Trim(); }
        if (text.Contains("<think>", StringComparison.Ordinal)) { throw new InvalidDataException("Generation stopped inside reasoning."); }
        return text.Trim();
    }
    public static ParsedAction Parse(string text, IReadOnlyList<string> allowedTools, bool allowPlainFinal=false)
    {
        text = RemoveThinking(text);
        if (text.Contains("<|tool_call_start|>",StringComparison.Ordinal)) { return NativeToolCall.Parse(text,allowedTools); }
        if (allowPlainFinal && text.Length>0 && !text.StartsWith('{') && !text.StartsWith("```",StringComparison.Ordinal)) { return new(null,null,text); }
        if (text.StartsWith("```",StringComparison.Ordinal)) { text = string.Join('\n',text.Split('\n').Skip(1).SkipLast(1)).Trim(); }
        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("final",out JsonElement final) && final.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(final.GetString())) { return new(null,null,final.GetString()); }
            string? tool = root.GetProperty("tool").GetString();
            if (tool is null || !allowedTools.Contains(tool)) { throw new InvalidDataException("Unknown model-selected tool."); }
            JsonElement arguments = root.GetProperty("arguments");
            if (arguments.ValueKind != JsonValueKind.Object) { throw new InvalidDataException("Tool arguments must be an object."); }
            return new(tool,arguments.EnumerateObject().ToDictionary(p => p.Name,p => ConvertValue(p.Value)),null);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        { throw new InvalidDataException("Expected one JSON tool action or final answer.",exception); }
    }
    public static void ValidateArguments(ParsedAction action, IReadOnlyList<Microsoft.Extensions.AI.AIFunction> tools)
    {
        if (action.Tool is null) { return; }
        JsonElement schema=tools.Single(t=>t.Name==action.Tool).JsonSchema;
        JsonElement properties=schema.GetProperty("properties");
        if (schema.TryGetProperty("required",out JsonElement required))
        { foreach (JsonElement key in required.EnumerateArray()) { if (!action.Arguments!.ContainsKey(key.GetString()!)) { throw new InvalidDataException($"Missing argument: {key.GetString()}"); } } }
        foreach (KeyValuePair<string,object?> argument in action.Arguments!)
        {
            if (!properties.TryGetProperty(argument.Key,out JsonElement property)) { throw new InvalidDataException($"Unknown argument: {argument.Key}"); }
            if (!property.TryGetProperty("type",out JsonElement type)) { continue; }
            string[] types=type.ValueKind==JsonValueKind.Array ? type.EnumerateArray().Select(v=>v.GetString()!).ToArray() : [type.GetString()!];
            bool match=types.Any(t=>t switch { "string"=>argument.Value is string,"integer"=>argument.Value is long or int,"number"=>argument.Value is long or int or double,"boolean"=>argument.Value is bool,"null"=>argument.Value is null,"object"=>argument.Value is JsonElement e && e.ValueKind==JsonValueKind.Object,_=>false });
            if (!match) { throw new InvalidDataException($"Wrong type for {argument.Key}: expected {string.Join('/',types)}"); }
        }
    }
    private static object? ConvertValue(JsonElement value) => value.ValueKind switch { JsonValueKind.String => value.GetString(), JsonValueKind.Number => value.TryGetInt64(out long n) ? (object)n : value.GetDouble(), JsonValueKind.True => true, JsonValueKind.False => false, JsonValueKind.Null => null, _ => value.Clone() };
}
