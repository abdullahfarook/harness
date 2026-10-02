using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Harness.Agent;

namespace Harness.Models;

public static class Qwen3Prompt
{
    public static string Build(IReadOnlyList<ChatMessage> messages,IReadOnlyList<AIFunction> tools,bool thinking)
    {
        StringBuilder result=new();
        string system=messages.FirstOrDefault()?.Role==ChatRole.System ? Clean(messages[0].Text ?? "") : "";
        if (tools.Count>0)
        {
            result.Append("<|im_start|>system\n").Append(system).Append("\n\n# Tools\n\nYou may call one function to assist with the user query.\n\nYou are provided with function signatures within <tools></tools> XML tags:\n<tools>");
            foreach (AIFunction tool in tools) { result.Append('\n').Append(JsonSerializer.Serialize(new { type="function",function=new { name=tool.Name,description=tool.Description,parameters=tool.JsonSchema } })); }
            result.Append("\n</tools>\n\nFor each function call, return a json object with function name and arguments within <tool_call></tool_call> XML tags:\n<tool_call>\n{\"name\": <function-name>, \"arguments\": <args-json-object>}\n</tool_call><|im_end|>\n");
        }
        else if (system.Length>0) { result.Append("<|im_start|>system\n").Append(system).Append("<|im_end|>\n"); }
        foreach (ChatMessage message in messages.Skip(messages.FirstOrDefault()?.Role==ChatRole.System ? 1 : 0)) { AppendMessage(result,message); }
        result.Append("<|im_start|>assistant\n");
        if (!thinking) { result.Append("<think>\n\n</think>\n\n"); }
        return result.ToString();
    }
    private static void AppendMessage(StringBuilder result,ChatMessage message)
    {
        string content=Clean(message.Role==ChatRole.Assistant ? ActionProtocol.RemoveThinking(message.Text ?? "") : message.Text ?? "");
        result.Append("<|im_start|>").Append(message.Role==ChatRole.Tool ? "user" : message.Role.Value).Append('\n');
        if (message.Role==ChatRole.Tool) { result.Append("<tool_response>\n").Append(content).Append("\n</tool_response>"); } else { result.Append(content); }
        foreach (FunctionCallContent call in message.Contents.OfType<FunctionCallContent>()) { result.Append("\n<tool_call>\n").Append(JsonSerializer.Serialize(new {name=call.Name,arguments=call.Arguments})).Append("\n</tool_call>"); }
        result.Append("<|im_end|>\n");
    }
    private static string Clean(string value)
    {
        value=value.Replace("<|","< |",StringComparison.Ordinal);
        foreach (string tag in new[] {"tool_call","tool_response","tools","think"}) { value=value.Replace("<"+tag+">","< "+tag+">",StringComparison.Ordinal).Replace("</"+tag+">","</ "+tag+">",StringComparison.Ordinal); }
        return value.Replace("/no_think","/no-think",StringComparison.Ordinal).Replace("/think","/ think",StringComparison.Ordinal);
    }
}
