using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Harness.Agent;

namespace Harness.Models;

public static class Qwen35Prompt
{
    public static string Build(IReadOnlyList<ChatMessage> messages,IReadOnlyList<AIFunction> tools,bool thinking)
    {
        StringBuilder result=new();
        bool system=messages.FirstOrDefault()?.Role==ChatRole.System;
        if (tools.Count>0)
        {
            result.Append("<|im_start|>system\n# Tools\n\nYou have access to the following functions:\n\n<tools>");
            foreach (AIFunction tool in tools) { result.Append('\n').Append(JsonSerializer.Serialize(new {type="function",function=new {name=tool.Name,description=tool.Description,parameters=tool.JsonSchema}})); }
            result.Append("\n</tools>\n\nIf you choose to call a function ONLY reply in the following format with NO suffix:\n\n<tool_call>\n<function=example_function_name>\n<parameter=example_parameter_1>\nvalue_1\n</parameter>\n</function>\n</tool_call>\nRequired parameters MUST be specified. Use exactly one available function; do not invent parameters.");
            if (system) { result.Append("\n\n").Append(Clean(messages[0].Text ?? "")); }
            result.Append("<|im_end|>\n");
        }
        else if (system) { result.Append("<|im_start|>system\n").Append(Clean(messages[0].Text ?? "")).Append("<|im_end|>\n"); }
        foreach (ChatMessage message in messages.Skip(system ? 1 : 0)) { AppendMessage(result,message); }
        result.Append("<|im_start|>assistant\n").Append(thinking ? "<think>\n" : "<think>\n\n</think>\n\n");
        return result.ToString();
    }
    private static void AppendMessage(StringBuilder result,ChatMessage message)
    {
        string content=Clean(message.Role==ChatRole.Assistant ? Qwen3Protocol.FinalContent(message.Text ?? "") : message.Text ?? "");
        result.Append("<|im_start|>").Append(message.Role==ChatRole.Tool ? "user" : message.Role.Value).Append('\n');
        if (message.Role==ChatRole.Tool) { result.Append("<tool_response>\n").Append(content).Append("\n</tool_response>"); } else { result.Append(content); }
        foreach (FunctionCallContent call in message.Contents.OfType<FunctionCallContent>())
        {
            result.Append("\n<tool_call>\n<function=").Append(call.Name).Append(">\n");
            foreach (KeyValuePair<string,object?> argument in call.Arguments ?? new Dictionary<string,object?>()) { result.Append("<parameter=").Append(argument.Key).Append(">\n").Append(argument.Value).Append("\n</parameter>\n"); }
            result.Append("</function>\n</tool_call>");
        }
        result.Append("<|im_end|>\n");
    }
    private static string Clean(string value)=>value.Replace("<|","< |",StringComparison.Ordinal).Replace("<","< ",StringComparison.Ordinal).Replace("/no_think","/no-think",StringComparison.Ordinal).Replace("/think","/ think",StringComparison.Ordinal);
}
