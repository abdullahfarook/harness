using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Harness.Agent;

public static class NativeToolCall
{
    public static ParsedAction Parse(string text,IReadOnlyList<string> allowedTools)
    {
        Match call=Regex.Match(text,@"^<\|tool_call_start\|>\s*\[(?<name>[A-Za-z_]\w*)\((?<args>.*)\)\]\s*<\|tool_call_end\|>$",RegexOptions.Singleline,TimeSpan.FromSeconds(1));
        if (!call.Success || !allowedTools.Contains(call.Groups["name"].Value)) { throw new InvalidDataException("Invalid or unknown native LFM tool call."); }
        string arguments=call.Groups["args"].Value.Trim(); Dictionary<string,object?> values=[]; int offset=0;
        while (offset<arguments.Length)
        {
            Match argument=Regex.Match(arguments[offset..],@"^\s*(?<key>[A-Za-z_]\w*)\s*=\s*(?<value>'(?:[^'\\]|\\.)*'|""(?:[^""\\]|\\.)*""|-?\d+(?:\.\d+)?|True|False|None)\s*(?<comma>,)?",RegexOptions.Singleline,TimeSpan.FromSeconds(1));
            if (!argument.Success) { throw new InvalidDataException("Native arguments must be literals, not executable expressions."); }
            string key=argument.Groups["key"].Value;
            if (!values.TryAdd(key,Literal(argument.Groups["value"].Value))) { throw new InvalidDataException("Duplicate tool argument."); }
            offset+=argument.Length;
            if (offset<arguments.Length && !argument.Groups["comma"].Success) { throw new InvalidDataException("Invalid native argument separator."); }
        }
        return new(call.Groups["name"].Value,values,null);
    }
    private static object? Literal(string text)
    {
        try
        {
            if (text.StartsWith('"')) { return JsonSerializer.Deserialize<string>(text); }
            if (text.StartsWith('\'')) { return JsonSerializer.Deserialize<string>("\""+text[1..^1].Replace("\\'","'",StringComparison.Ordinal).Replace("\"","\\\"",StringComparison.Ordinal)+"\""); }
            return text switch { "True"=>true,"False"=>false,"None"=>null,_=>long.TryParse(text,NumberStyles.Integer,CultureInfo.InvariantCulture,out long number) ? (object)number : double.Parse(text,CultureInfo.InvariantCulture) };
        }
        catch (Exception exception) when (exception is JsonException or FormatException) { throw new InvalidDataException("Invalid native argument literal.",exception); }
    }
}
