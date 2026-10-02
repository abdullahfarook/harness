using System.Text.Json;

namespace Harness.Agent;

public static class Qwen3Protocol
{
    public static ParsedAction Parse(string output,IReadOnlyList<string> allowedTools,bool allowFinal)
    {
        string content=FinalContent(output);
        const string start="<tool_call>",end="</tool_call>";
        if (!content.Contains(start,StringComparison.Ordinal))
        {
            if (!allowFinal || content.Length==0 || content.Contains(end,StringComparison.Ordinal) || content.Contains("<|",StringComparison.Ordinal) || content.StartsWith('{') || content.StartsWith("```",StringComparison.Ordinal)) { throw new InvalidDataException("Expected one native Qwen tool call."); }
            return new(null,null,content);
        }
        if (!content.StartsWith(start,StringComparison.Ordinal) || !content.EndsWith(end,StringComparison.Ordinal) || content.IndexOf(start,start.Length,StringComparison.Ordinal)>=0) { throw new InvalidDataException("Expected exactly one native Qwen tool call with no surrounding text."); }
        try
        {
            using JsonDocument document=JsonDocument.Parse(content[start.Length..^end.Length]);
            JsonElement root=document.RootElement;
            if (root.EnumerateObject().Count()!=2 || !root.TryGetProperty("name",out JsonElement name) || !root.TryGetProperty("arguments",out JsonElement arguments)) { throw new InvalidDataException("Native Qwen call needs name and arguments only."); }
            return ActionProtocol.Parse(JsonSerializer.Serialize(new {tool=name.GetString(),arguments}),allowedTools);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { throw new InvalidDataException("Malformed native Qwen call.",exception); }
    }
    public static string FinalContent(string output)
    {
        string content=output.Trim();
        if (content.StartsWith("<think>",StringComparison.Ordinal))
        {
            int end=content.IndexOf("</think>",StringComparison.Ordinal);
            if (end<0 || content.IndexOf("<think>",7,StringComparison.Ordinal)>=0 || content[..end].Contains("<tool_call>",StringComparison.Ordinal)) { throw new InvalidDataException("Malformed or unfinished leading reasoning block."); }
            content=content[(end+8)..].Trim();
        }
        if (content.Contains("<think>",StringComparison.Ordinal) || content.Contains("</think>",StringComparison.Ordinal)) { throw new InvalidDataException("Reasoning must be one complete leading block."); }
        return content;
    }
}
