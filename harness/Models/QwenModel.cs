using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Harness.Models;

public sealed class QwenModel : ILocalTextModel
{
    private readonly InferenceSession session;
    private readonly NativeTokenizer tokenizer;
    private readonly HashSet<int> eos;
    private readonly Action<GenerationTiming>? timing;
    private readonly Action<string>? diagnostics;
    private readonly SamplingSettings? sampling;
    private readonly Random random;
    private readonly SemaphoreSlim gate=new(1,1);
    public string ModelId { get; }
    public QwenModel(ModelAssets assets,int threads=4,Action<GenerationTiming>? timing=null,Action<string>? diagnostics=null,SamplingSettings? sampling=null,int seed=42)
    {
        this.timing=timing;
        this.diagnostics=diagnostics;
        this.sampling=sampling; random=new(seed); ModelId=assets.Manifest.GetProperty("repo").GetString()!;
        tokenizer=new(assets.TokenizerPath);
        using JsonDocument configuration=JsonDocument.Parse(File.ReadAllText(Path.Combine(assets.DirectoryPath,"generation_config.json")));
        JsonElement stop=configuration.RootElement.GetProperty("eos_token_id");
        eos=stop.ValueKind==JsonValueKind.Array ? stop.EnumerateArray().Select(v=>v.GetInt32()).ToHashSet() : [stop.GetInt32()];
        using SessionOptions options=new() { IntraOpNumThreads=threads,InterOpNumThreads=1 };
        session=new(assets.GraphPath,options);
    }
    public static int[] CacheShape(int[] dimensions,string[] symbols)=>dimensions.Select((d,i)=>symbols[i].Contains("sequence",StringComparison.OrdinalIgnoreCase) ? 0 : d>0 ? d : 1).ToArray();
    public static string Prompt(IReadOnlyList<ChatMessage> messages)
    {
        StringBuilder result=new();
        if (messages.Count==0 || messages[0].Role!=ChatRole.System) { result.Append("<|im_start|>system\nYou are Qwen, created by Alibaba Cloud. You are a helpful assistant.<|im_end|>\n"); }
        foreach (ChatMessage message in messages)
        {
            string content=(message.Text ?? "").Replace("<|","< |",StringComparison.Ordinal).Replace("<tool_response>","<tool response>",StringComparison.Ordinal).Replace("</tool_response>","</tool response>",StringComparison.Ordinal);
            result.Append("<|im_start|>").Append(message.Role==ChatRole.Tool ? "user" : message.Role.Value).Append('\n');
            if (message.Role==ChatRole.Tool) { result.Append("<tool_response>\n").Append(content).Append("\n</tool_response>"); } else { result.Append(content); }
            result.Append("<|im_end|>\n");
        }
        return result.Append("<|im_start|>assistant\n").ToString();
    }
    public async Task<string> GenerateAsync(IReadOnlyList<ChatMessage> messages,int maxTokens,CancellationToken cancellationToken)
        =>await GeneratePromptAsync(Prompt(messages),maxTokens,cancellationToken);
    public async Task<string> GeneratePromptAsync(string prompt,int maxTokens,CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { return await Task.Run(()=>Generate(prompt,maxTokens,cancellationToken),cancellationToken); }
        finally { gate.Release(); }
    }
    private string Generate(string prompt,int maxTokens,CancellationToken token)
    {
        Stopwatch elapsed=Stopwatch.StartNew();
        int[] initial=tokenizer.Encode(prompt);
        if (initial.Length+maxTokens>8192) { throw new InvalidDataException("Qwen input and output exceed the application 8192-token budget."); }
        Dictionary<string,NamedOnnxValue> cache=EmptyCache();
        List<int> generated=[];
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue>? previous=null;
        double prefill=0;
        using RunOptions runOptions=new();
        using CancellationTokenRegistration registration=token.Register(()=>runOptions.Terminate=true);
        try
        {
            for (int step=0;step<maxTokens;step++)
            {
                token.ThrowIfCancellationRequested();
                long[] ids=step==0 ? initial.Select(i=>(long)i).ToArray() : [generated[^1]];
                int total=initial.Length+generated.Count;
                List<NamedOnnxValue> inputs=[..cache.Values,NamedOnnxValue.CreateFromTensor("input_ids",new DenseTensor<long>(ids,[1,ids.Length])),NamedOnnxValue.CreateFromTensor("attention_mask",new DenseTensor<long>(Enumerable.Repeat(1L,total).ToArray(),[1,total])),NamedOnnxValue.CreateFromTensor("position_ids",new DenseTensor<long>(Enumerable.Range(total-ids.Length,ids.Length).Select(i=>(long)i).ToArray(),[1,ids.Length]))];
                IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs=session.Run(inputs,session.OutputNames,runOptions);
                if (step==0) { prefill=elapsed.Elapsed.TotalSeconds; }
                Tensor<float> logits=outputs.First(o=>o.Name=="logits").AsTensor<float>();
                int vocabulary=logits.Dimensions[^1],last=logits.Dimensions[1]-1;
                float[] values=Enumerable.Range(0,vocabulary).Select(i=>logits[0,last,i]).ToArray();
                int next=sampling is null ? LfmThinkingModel.SelectToken(values,generated,1.1) : TokenSampler.Select(values,generated,sampling,random);
                cache.Clear();
                foreach (DisposableNamedOnnxValue output in outputs) { string name=output.Name.Replace("present.","past_key_values.",StringComparison.Ordinal); if (session.InputMetadata.ContainsKey(name)) { cache[name]=NamedOnnxValue.CreateFromTensor(name,output.AsTensor<float>()); } }
                previous?.Dispose(); previous=outputs;
                if (eos.Contains(next)) { string output=tokenizer.Decode(generated); diagnostics?.Invoke(output); timing?.Invoke(new(initial.Length,generated.Count,prefill,elapsed.Elapsed.TotalSeconds)); return output; }
                generated.Add(next);
                if (step%50==0) { Console.Error.WriteLine($"[Qwen] inputTokens={initial.Length} generatedTokens={generated.Count}"); diagnostics?.Invoke(tokenizer.Decode(generated)); }
            }
            throw new InvalidDataException($"Qwen reached {maxTokens} tokens without EOS; no partial answer accepted.");
        }
        finally { previous?.Dispose(); }
    }
    private Dictionary<string,NamedOnnxValue> EmptyCache()
    {
        Dictionary<string,NamedOnnxValue> cache=[];
        foreach (KeyValuePair<string,NodeMetadata> input in session.InputMetadata.Where(p=>p.Key.StartsWith("past_key_values",StringComparison.Ordinal)))
        {
            if (input.Value.ElementType!=typeof(float)) { throw new InvalidDataException($"Unsupported Qwen Q4 cache type: {input.Key} {input.Value.ElementType}"); }
            cache[input.Key]=NamedOnnxValue.CreateFromTensor(input.Key,new DenseTensor<float>(CacheShape(input.Value.Dimensions,input.Value.SymbolicDimensions)));
        }
        return cache;
    }
    public void Dispose() { session.Dispose(); tokenizer.Dispose(); gate.Dispose(); }
}
