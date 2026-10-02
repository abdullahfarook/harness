using System.Runtime.CompilerServices;
using System.Text.Json;
using Harness.Models;
using Microsoft.Extensions.AI;

namespace Harness.Agent;

public sealed class LocalChatClient(ILocalTextModel model, Action<string> record, int maxTokens = 1536) : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        List<ChatMessage> history = messages.ToList();
        List<AIFunction> tools = options?.Tools?.OfType<AIFunction>().ToList() ?? [];
        AgentRequest request=AgentPrompt.Build(history,tools,model is LfmThinkingModel,model is Qwen3Model);
        tools=request.Tools;
        record(JsonSerializer.Serialize(new { timestamp=DateTimeOffset.UtcNow,kind="model_request",model=model.ModelId,tools=tools.Select(t=>t.Name).ToArray(),messages=history.Count }));
        List<ChatMessage> prompt = request.Messages;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            System.Diagnostics.Stopwatch timer=System.Diagnostics.Stopwatch.StartNew();
            string output = model is Qwen3Model qwen3 ? await qwen3.GenerateAsync(prompt,tools,maxTokens,cancellationToken) : await model.GenerateAsync(prompt,maxTokens,cancellationToken);
            record(JsonSerializer.Serialize(new { timestamp=DateTimeOffset.UtcNow,kind="model_response",model=model.ModelId,output,stage=tools.Count==0 ? "summary" : "decision",seconds=timer.Elapsed.TotalSeconds,attempt }));
            try
            {
                ParsedAction action = model is Qwen3Model ? Qwen3Protocol.Parse(output,tools.Select(t=>t.Name).ToArray(),request.HasClassification) : ActionProtocol.Parse(output,tools.Select(t => t.Name).ToArray(),request.HasClassification);
                ActionProtocol.ValidateArguments(action,tools);
                ChatMessage response;
                if (action.Tool is not null) { response = new(ChatRole.Assistant,[new FunctionCallContent(Guid.NewGuid().ToString("N"),action.Tool,action.Arguments)]); }
                else
                {
                    if (!request.HasPage || !request.HasClassification) { throw new InvalidDataException("Final answer requires an observed page and a successful classify result."); }
                    AgentPrompt.ValidateSummary(action.Final!,history);
                    response = new(ChatRole.Assistant,action.Final!);
                }
                return new(response) { ModelId=model.ModelId };
            }
            catch (InvalidDataException exception) when (attempt < 2)
            { prompt.Add(new(ChatRole.User,request.HasClassification && tools.Count==0 ? $"Correct the summary: {exception.Message} Return factual summary text, not commentary." : $"Invalid response: {exception.Message} "+(model is Qwen3Model ? "Return exactly one <tool_call> JSON with name and arguments using only the supplied schemas; no extra parameters." : "Return one JSON object with tool and arguments fields using only available tools."))); }
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
