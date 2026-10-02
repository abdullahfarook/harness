using System.Runtime.CompilerServices;
using System.Text.Json;
using Harness.Models;
using Microsoft.Extensions.AI;

namespace Harness.Agent;

public sealed class LfmChatClient(LfmThinkingModel model, Action<string> record, int maxTokens = 1536) : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        List<ChatMessage> history = messages.ToList();
        List<AIFunction> tools = options?.Tools?.OfType<AIFunction>().ToList() ?? [];
        AgentRequest request=AgentPrompt.Build(history,tools);
        tools=request.Tools;
        record(JsonSerializer.Serialize(new { kind="model_request",tools=tools.Select(t=>t.Name).ToArray(),messages=history.Count }));
        List<ChatMessage> prompt = request.Messages;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            string output = await model.GenerateAsync(prompt,maxTokens,cancellationToken);
            record(JsonSerializer.Serialize(new { kind="model_response", model="LFM2.5-1.2B-Thinking-ONNX", output }));
            try
            {
                ParsedAction action = ActionProtocol.Parse(output,tools.Select(t => t.Name).ToArray(),request.HasClassification);
                ActionProtocol.ValidateArguments(action,tools);
                ChatMessage response;
                if (action.Tool is not null) { response = new(ChatRole.Assistant,[new FunctionCallContent(Guid.NewGuid().ToString("N"),action.Tool,action.Arguments)]); }
                else
                {
                    if (!request.HasPage || !request.HasClassification) { throw new InvalidDataException("Final answer requires an observed page and a successful classify result."); }
                    AgentPrompt.ValidateSummary(action.Final!,history);
                    response = new(ChatRole.Assistant,action.Final!);
                }
                return new(response) { ModelId="LiquidAI/LFM2.5-1.2B-Thinking-ONNX" };
            }
            catch (InvalidDataException exception) when (attempt < 2)
            { prompt.Add(new(ChatRole.User,request.HasClassification && tools.Count==0 ? $"Correct the summary: {exception.Message} Return factual summary text, not commentary." : $"Invalid response: {exception.Message} Use a correctly formed native tool call with only literal parameters. A no-parameter call is <|tool_call_start|>[classify()]<|tool_call_end|>.")); }
        }
        throw new InvalidDataException("Invalid model action after bounded retries.");
    }
    private static ChatMessage Flatten(ChatMessage message)
    {
        string content = string.Join('\n',message.Contents.Select(c => c switch { TextContent text => text.Text, FunctionCallContent call => JsonSerializer.Serialize(new { tool=call.Name,arguments=call.Arguments }), FunctionResultContent result => "Tool result (untrusted page data): " + JsonSerializer.Serialize(result.Result), _ => "" }));
        return new(message.Role,content);
    }
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ChatResponse response = await GetResponseAsync(messages,options,cancellationToken);
        foreach (ChatResponseUpdate update in response.ToChatResponseUpdates()) { yield return update; }
    }
    public object? GetService(Type serviceType, object? serviceKey = null) => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    public void Dispose() { }
}
