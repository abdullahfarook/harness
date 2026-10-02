using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Harness.Models;

public sealed class QwenCudaModel : ILocalTextModel
{
    private readonly InferenceSession session;
    private readonly NativeTokenizer tokenizer;
    private readonly HashSet<int> eos;
    private readonly BotOptions options;
    private readonly Action<GenerationTiming> timing;
    private readonly Action<string> diagnostics;
    private readonly SemaphoreSlim gate=new(1,1);
    private readonly string evidencePath;
    private bool placementChecked;
    public string ModelId { get; }
    public QwenCudaModel(ModelAssets assets,BotOptions options,Action<GenerationTiming> timing,Action<string> diagnostics)
    {
        this.options=options; this.timing=timing; this.diagnostics=diagnostics;
        ModelId=assets.Manifest.GetProperty("repo").GetString()!;
        evidencePath=Path.GetFullPath(options.Output); Directory.CreateDirectory(evidencePath);
        CudaDependencies.Configure();
        using OrtCUDAProviderOptions cudaOptions=new();
        cudaOptions.UpdateOptions(new Dictionary<string,string> { ["device_id"]="0",["arena_extend_strategy"]="kSameAsRequested",["gpu_mem_limit"]="5368709120",["cudnn_conv_algo_search"]="DEFAULT",["do_copy_in_default_stream"]="1" });
        using SessionOptions configuration=new() { IntraOpNumThreads=options.Threads,InterOpNumThreads=1,GraphOptimizationLevel=GraphOptimizationLevel.ORT_ENABLE_ALL,ProfileOutputPathPrefix=Path.Combine(evidencePath,"cuda-prefill"),EnableProfiling=true };
        configuration.AppendExecutionProvider_CUDA(cudaOptions);
        session=new(assets.GraphPath,configuration);
        tokenizer=new(assets.TokenizerPath);
        using JsonDocument generation=JsonDocument.Parse(File.ReadAllText(Path.Combine(assets.DirectoryPath,"generation_config.json")));
        JsonElement stop=generation.RootElement.GetProperty("eos_token_id"); eos=stop.ValueKind==JsonValueKind.Array ? stop.EnumerateArray().Select(item=>item.GetInt32()).ToHashSet() : [stop.GetInt32()];
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
        using OrtMemoryInfo cuda=new("Cuda",OrtAllocatorType.DeviceAllocator,0,OrtMemType.Default);
        using OrtAllocator allocator=new(session,cuda);
        Dictionary<string,OrtValue> cache=EmptyCache(allocator);
        OrtValue[] empty=cache.Values.ToArray();
        IDisposableReadOnlyCollection<OrtValue>? previous=null;
        List<int> generated=[]; double prefill=0,first=0,last=0;
        using RunOptions run=new(); using CancellationTokenRegistration registration=token.Register(()=>run.Terminate=true);
        try
        {
            for (int step=0;step<maxTokens;step++)
            {
                token.ThrowIfCancellationRequested(); int total=initial.Length+generated.Count;
                long[] ids=step==0 ? initial.Select(id=>(long)id).ToArray() : [generated[^1]];
                using OrtValue tokens=OrtValue.CreateTensorValueFromMemory(ids,new long[] {1,ids.Length});
                using OrtValue mask=OrtValue.CreateTensorValueFromMemory(Enumerable.Repeat(1L,total).ToArray(),new long[] {1,total});
                using OrtValue positions=OrtValue.CreateTensorValueFromMemory(Enumerable.Range(total-ids.Length,ids.Length).Select(position=>(long)position).ToArray(),new long[] {1,ids.Length});
                using OrtIoBinding binding=session.CreateIoBinding();
                foreach (KeyValuePair<string,OrtValue> input in cache) { binding.BindInput(input.Key,input.Value); }
                binding.BindInput("input_ids",tokens); binding.BindInput("attention_mask",mask); binding.BindInput("position_ids",positions);
                foreach (string name in session.OutputNames) { binding.BindOutputToDevice(name,name=="logits" ? OrtMemoryInfo.DefaultInstance : cuda); }
                session.RunWithBinding(run,binding); binding.SynchronizeBoundOutputs();
                IDisposableReadOnlyCollection<OrtValue> outputs=binding.GetOutputValues();
                string[] names=binding.GetOutputNames(); OrtValue[] values=outputs.ToArray();
                try
                {
                    if (step==0) { prefill=elapsed.Elapsed.TotalSeconds; CheckPlacement(values,names); }
                    int next=LfmThinkingModel.SelectToken(CudaTensor.LastLogits(values[Array.IndexOf(names,"logits")]),generated,1.1);
                    cache=names.Select((name,index)=>(name:name.Replace("present.","past_key_values.",StringComparison.Ordinal),value:values[index])).Where(item=>session.InputMetadata.ContainsKey(item.name)).ToDictionary(item=>item.name,item=>item.value);
                    previous?.Dispose(); previous=outputs; outputs=null!;
                    if (eos.Contains(next)) { string text=tokenizer.Decode(generated); diagnostics(text); timing(new(initial.Length,generated.Count,prefill,elapsed.Elapsed.TotalSeconds,first,last)); return text; }
                    generated.Add(next); last=elapsed.Elapsed.TotalSeconds; if (generated.Count==1) { first=last; }
                    if (step%50==0) { diagnostics(tokenizer.Decode(generated)); Console.Error.WriteLine($"[Qwen CUDA] inputTokens={initial.Length} generatedTokens={generated.Count}"); }
                }
                finally { outputs?.Dispose(); }
            }
            throw new InvalidDataException($"Qwen CUDA reached {maxTokens} tokens without EOS; no partial answer accepted.");
        }
        finally { previous?.Dispose(); foreach (OrtValue value in empty) { value.Dispose(); } }
    }
    private Dictionary<string,OrtValue> EmptyCache(OrtAllocator allocator)
    {
        Dictionary<string,OrtValue> cache=[];
        try
        {
            foreach (KeyValuePair<string,NodeMetadata> input in session.InputMetadata.Where(item=>item.Key.StartsWith("past_key_values.",StringComparison.Ordinal)))
            {
                if (input.Value.ElementDataType is not (TensorElementType.Float or TensorElementType.Float16)) { throw new InvalidDataException("Unsupported KV cache type."); }
                cache[input.Key]=OrtValue.CreateAllocatedTensorValue(allocator,input.Value.ElementDataType,QwenModel.CacheShape(input.Value.Dimensions,input.Value.SymbolicDimensions).Select(d=>(long)d).ToArray());
            }
            return cache;
        }
        catch { foreach (OrtValue value in cache.Values) { value.Dispose(); } throw; }
    }
    private void CheckPlacement(OrtValue[] outputs,string[] names)
    {
        for (int i=0;i<outputs.Length;i++) { if (names[i]!="logits") { using OrtMemoryInfo info=outputs[i].GetTensorMemoryInfo(); if (info.Name!="Cuda") { throw new InvalidDataException("KV output is not CUDA-resident."); } } }
        if (placementChecked) { return; }
        string profile=session.EndProfiling(); CudaPlacement.Validate(profile); placementChecked=true;
        File.WriteAllText(Path.Combine(evidencePath,"cuda-placement.json"),JsonSerializer.Serialize(new {profile,kvResident=true,logitsLocation="CPU",smallInputsLocation="CPU copied on binding",cacheAllocation="dynamic device past/present, not in-place shared",validation="No CPU transformer or large payload execution in first prefill"},new JsonSerializerOptions {WriteIndented=true}));
    }
    public void Dispose() { session.Dispose(); tokenizer.Dispose(); gate.Dispose(); }
}
