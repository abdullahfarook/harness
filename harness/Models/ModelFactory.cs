namespace Harness.Models;

public static class ModelFactory
{
    public static ILocalTextModel Create(BotOptions options,ModelAssets assets,Action<GenerationTiming> timing,Action<string> diagnostics)
    {
        if (options.Provider=="cuda") { return new QwenCudaModel(assets,options,timing,diagnostics); }
        if (options.Provider=="genai") { return new QwenGenaiModel(assets,options,timing,diagnostics); }
        if (options.Provider!="cpu") { throw new NotSupportedException("GPU backend has not been initialized; no CPU fallback accepted."); }
        return options.Brain switch
        {
            "qwen"=>new QwenModel(assets,options.Threads,timing,diagnostics,seed:options.Seed,context:options.Context),
            "qwen3"=>new Qwen3Model(assets,options.Thinking,options.Seed,options.PresencePenalty ?? 0,timing,diagnostics),
            "qwen35"=>new Qwen35Model(assets,options.Thinking,options.Seed,options.PresencePenalty,timing,diagnostics),
            "gemma"=>new GemmaModel(assets,options.Threads,timing),
            "lfm"=>new LfmThinkingModel(assets,options.Threads,diagnostics),
            _=>throw new ArgumentException("Unknown model identity.")
        };
    }
    public static object Sampling(BotOptions options)=>options.Brain switch
    {
        "qwen3"=>SamplingSettings.ForMode(options.Thinking,options.PresencePenalty ?? 0),
        "qwen35"=>Qwen35Model.Settings(options.Thinking,options.PresencePenalty),
        "qwen"=>new { mode="greedy",repetitionPenalty=1.1,penaltyScope=options.Provider=="genai" ? "native GenAI full sequence" : "generated answer tokens" },
        _=>new { mode="retained model defaults" }
    };
}
