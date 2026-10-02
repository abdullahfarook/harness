using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Harness.Agent;

public static class Qwen35Protocol
{
    public static ParsedAction Parse(string output,IReadOnlyList<AIFunction> tools,bool allowFinal)
    {
        string content=Qwen3Protocol.FinalContent(output);
        Match outer=Regex.Match(content,@"\A<tool_call>\s*<function=([A-Za-z_]\w*)>\s*([\s\S]*?)\s*</function>\s*</tool_call>\z",RegexOptions.CultureInvariant);
        if (!outer.Success)
        {
            if (!allowFinal || content.Length==0 || content.Contains('<') || content.StartsWith('{') || content.StartsWith("```",StringComparison.Ordinal)) { throw new InvalidDataException("Expected exactly one native XML function call."); }
            return new(null,null,content);
        }
        string name=outer.Groups[1].Value;
        AIFunction tool=tools.SingleOrDefault(t=>t.Name==name) ?? throw new InvalidDataException("Unknown native XML function.");
        Dictionary<string,object?> arguments=Arguments(outer.Groups[2].Value,tool.JsonSchema);
        ParsedAction action=new(name,arguments,null);
        ActionProtocol.ValidateArguments(action,tools);
        return action;
    }
    private static Dictionary<string,object?> Arguments(string body,JsonElement schema)
    {
        Dictionary<string,object?> result=[];
        int offset=0;
        foreach (Match match in Regex.Matches(body,@"<parameter=([A-Za-z_]\w*)>([\s\S]*?)</parameter>",RegexOptions.CultureInvariant))
        {
            if (!string.IsNullOrWhiteSpace(body[offset..match.Index])) { throw new InvalidDataException("Unexpected XML argument content."); }
            string name=match.Groups[1].Value;
            if (result.ContainsKey(name) || !schema.GetProperty("properties").TryGetProperty(name,out JsonElement property)) { throw new InvalidDataException("Duplicate or unknown XML argument."); }
            result[name]=ConvertValue(match.Groups[2].Value.Trim(),property);
            offset=match.Index+match.Length;
        }
        if (!string.IsNullOrWhiteSpace(body[offset..])) { throw new InvalidDataException("Malformed XML arguments."); }
        return result;
    }
    private static object? ConvertValue(string value,JsonElement property)
    {
        JsonElement type=property.GetProperty("type");
        string[] types=type.ValueKind==JsonValueKind.Array ? type.EnumerateArray().Select(v=>v.GetString()!).ToArray() : [type.GetString()!];
        if (types.Contains("string")) { return value; }
        if (types.Contains("integer") && long.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out long integer)) { return integer; }
        if (types.Contains("number") && double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out double number) && double.IsFinite(number)) { return number; }
        if (types.Contains("boolean") && bool.TryParse(value,out bool boolean)) { return boolean; }
        if (types.Contains("null") && value=="null") { return null; }
        throw new InvalidDataException("Wrong native XML argument type.");
    }
}
