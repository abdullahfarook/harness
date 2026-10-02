using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace Harness.Models;

public sealed class QwenGenaiModel : ILocalTextModel
{
    private readonly Model model;
    private readonly NativeTokenizer tokenizer;
    private readonly BotOptions options;
    private readonly Action<GenerationTiming> timing;
    private readonly Action<string> diagnostics;
    private readonly SemaphoreSlim gate=new(1,1);
    private readonly string profilePrefix;
    public string ModelId { get; }
    public QwenGenaiModel(ModelAssets assets,BotOptions options,Action<GenerationTiming> timing,Action<string> diagnostics)
    {
        this.options=options; this.timing=timing; this.diagnostics=diagnostics; ModelId=assets.Manifest.GetProperty("repo").GetString()!;
        Directory.CreateDirectory(Path.GetFullPath(options.Output)); profilePrefix=Path.Combine(Path.GetFullPath(options.Output),"genai-decoder");
        CudaDependencies.Configure();
        using Config configuration=new(assets.DirectoryPath);
        configuration.ClearProviders(); configuration.AppendProvider("cuda");
        configuration.SetProviderOption("cuda","device_id","0"); configuration.SetProviderOption("cuda","arena_extend_strategy","kSameAsRequested");
        configuration.SetProviderOption("cuda","gpu_mem_limit","5368709120"); configuration.SetProviderOption("cuda","do_copy_in_default_stream","1");
        configuration.Overlay(JsonSerializer.Serialize(new {model=new {context_length=options.Context,decoder=new {session_options=new {intra_op_num_threads=options.Threads,inter_op_num_threads=1,enable_profiling=profilePrefix}}}}));
        model=new(configuration); tokenizer=new(assets.TokenizerPath);
    }
    public async Task<string> GenerateAsync(IReadOnlyList<ChatMessage> messages,int maxTokens,CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { return await Task.Run(()=>Generate(QwenModel.Prompt(messages),maxTokens,cancellationToken),cancellationToken); }
        finally { gate.Release(); }
    }
    private string Generate(string prompt,int maxTokens,CancellationToken token)
    {
        Stopwatch elapsed=Stopwatch.StartNew(); int[] initial=tokenizer.Encode(prompt);
        if (initial.Length+maxTokens>options.Context) { throw new InvalidDataException($"Input plus reserved output exceeds {options.Context} tokens."); }
        using GeneratorParams parameters=new(model);
        parameters.SetSearchOption("max_length",options.Context); parameters.SetSearchOption("batch_size",1); parameters.SetSearchOption("num_beams",1);
        parameters.SetSearchOption("do_sample",false); parameters.SetSearchOption("repetition_penalty",1.1); parameters.SetSearchOption("past_present_share_buffer",true);
        using Generator generator=new(model,parameters);
        using CancellationTokenRegistration registration=token.Register(()=>generator.SetRuntimeOption("terminate_session","1"));
        generator.AppendTokens(initial);
        List<int> generated=[]; double first=0,last=0;
        for (int step=0;step<maxTokens;step++)
        {
            token.ThrowIfCancellationRequested(); generator.GenerateNextToken();
            int next=generator.GetSequence(0)[^1];
            if (next is 151645 or 151643)
            {
                string text=tokenizer.Decode(generated); diagnostics(text); timing(new(initial.Length,generated.Count,first,elapsed.Elapsed.TotalSeconds,first,last)); return text;
            }
            generated.Add(next); last=elapsed.Elapsed.TotalSeconds; if (generated.Count==1) { first=last; }
            if (step%50==0) { diagnostics(tokenizer.Decode(generated)); Console.Error.WriteLine($"[Qwen GenAI CUDA] inputTokens={initial.Length} generatedTokens={generated.Count}"); }
            if (generator.IsDone()) { break; }
        }
        throw new InvalidDataException($"Qwen GenAI reached a length limit without EOS; no partial answer accepted.");
    }
    public void Dispose()
    {
        model.Dispose(); tokenizer.Dispose(); gate.Dispose();
        string[] profiles=Directory.GetFiles(Path.GetDirectoryName(profilePrefix)!,Path.GetFileName(profilePrefix)+"*.json");
        if (profiles.Length==0) { throw new InvalidDataException("GenAI CUDA execution profile is missing; GPU execution not proven."); }
        foreach (string profile in profiles) { CudaPlacement.Validate(profile); }
    }
}
