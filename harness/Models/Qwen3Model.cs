using Microsoft.Extensions.AI;

namespace Harness.Models;

public sealed class Qwen3Model : ILocalTextModel
{
    private readonly QwenModel decoder;
    public bool Thinking { get; }
    public SamplingSettings Sampling { get; }
    public string ModelId=>decoder.ModelId;
    public Qwen3Model(ModelAssets assets,bool thinking=false,int seed=42,double presencePenalty=0,Action<GenerationTiming>? timing=null,Action<string>? diagnostics=null)
    {
        Thinking=thinking; Sampling=SamplingSettings.ForMode(thinking,presencePenalty);
        decoder=new(assets,timing:timing,diagnostics:diagnostics,sampling:Sampling,seed:seed);
    }
    public Task<string> GenerateAsync(IReadOnlyList<ChatMessage> messages,int maxTokens,CancellationToken cancellationToken)=>GenerateAsync(messages,[],maxTokens,cancellationToken);
    public Task<string> GenerateAsync(IReadOnlyList<ChatMessage> messages,IReadOnlyList<AIFunction> tools,int maxTokens,CancellationToken cancellationToken)=>decoder.GeneratePromptAsync(Qwen3Prompt.Build(messages,tools,Thinking),maxTokens,cancellationToken);
    public void Dispose() { decoder.Dispose(); }
}
