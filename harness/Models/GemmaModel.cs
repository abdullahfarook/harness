using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Harness.Models;

public sealed record GenerationTiming(int InputTokens,int GeneratedTokens,double PrefillSeconds,double GenerationSeconds,double FirstTokenSeconds=0,double LastTokenSeconds=0)
{
    public double? DecodeTokensPerSecond=>GeneratedTokens>1 && LastTokenSeconds>FirstTokenSeconds ? (GeneratedTokens-1)/(LastTokenSeconds-FirstTokenSeconds) : null;
}

public sealed class GemmaModel : ILocalTextModel
{
    private readonly InferenceSession decoder,embedding;
    private readonly NativeTokenizer tokenizer;
    private readonly HashSet<int> eos;
    private readonly Action<GenerationTiming>? timing;
    private readonly SemaphoreSlim gate=new(1,1);
    public string ModelId=>"onnx-community/gemma-4-E2B-it-ONNX";
    public GemmaModel(ModelAssets assets,int threads=4,Action<GenerationTiming>? timing=null)
    {
        this.timing=timing;
        tokenizer=new(assets.TokenizerPath);
        using JsonDocument configuration=JsonDocument.Parse(File.ReadAllText(Path.Combine(assets.DirectoryPath,"generation_config.json")));
        JsonElement stop=configuration.RootElement.GetProperty("eos_token_id");
        eos=stop.ValueKind==JsonValueKind.Array ? stop.EnumerateArray().Select(v=>v.GetInt32()).ToHashSet() : [stop.GetInt32()];
        using SessionOptions options=new() { IntraOpNumThreads=threads,InterOpNumThreads=1 };
        embedding=new(Path.Combine(assets.DirectoryPath,"onnx/embed_tokens_q4.onnx"),options);
        decoder=new(assets.GraphPath,options);
    }
    public static int[] CacheShape(int[] dimensions,string[] symbols)=>dimensions.Select((d,i)=>symbols[i].Contains("sequence",StringComparison.OrdinalIgnoreCase) ? 0 : d>0 ? d : 1).ToArray();
    public static string Prompt(IReadOnlyList<ChatMessage> messages)
    {
        StringBuilder result=new("<bos>");
        foreach (ChatMessage message in messages) { result.Append("<|turn>").Append(message.Role==ChatRole.Assistant ? "model" : message.Role==ChatRole.Tool ? "user" : message.Role.Value).Append('\n').Append(SafeContent(message.Text ?? "")).Append("<turn|>\n"); }
        return result.Append("<|turn>model\n").ToString();
    }
    private static string SafeContent(string text)=>text.Trim().Replace("<|","< |",StringComparison.Ordinal).Replace("<turn|>","<turn |>",StringComparison.Ordinal).Replace("<bos>","< bos>",StringComparison.Ordinal).Replace("<eos>","< eos>",StringComparison.Ordinal);
    public async Task<string> GenerateAsync(IReadOnlyList<ChatMessage> messages,int maxTokens,CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { return await Task.Run(()=>Generate(Prompt(messages),maxTokens,cancellationToken),cancellationToken); }
        finally { gate.Release(); }
    }
    private string Generate(string prompt,int maxTokens,CancellationToken token)
    {
        Stopwatch elapsed=Stopwatch.StartNew();
        int[] initial=tokenizer.Encode(prompt);
        if (initial.Length+maxTokens>8192) { throw new InvalidDataException("Gemma input and output exceed the application 8192-token budget."); }
        Dictionary<string,NamedOnnxValue> cache=EmptyCache();
        List<int> generated=[];
        long[] ids=initial.Select(i=>(long)i).ToArray();
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue>? previous=null;
        double prefill=0;
        using RunOptions runOptions=new();
        using CancellationTokenRegistration registration=token.Register(()=>runOptions.Terminate=true);
        try
        {
            for (int step=0;step<maxTokens;step++)
            {
                token.ThrowIfCancellationRequested();
                using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> embeds=embedding.Run([NamedOnnxValue.CreateFromTensor("input_ids",new DenseTensor<long>(ids,[1,ids.Length]))],embedding.OutputNames,runOptions);
                int total=initial.Length+generated.Count;
                List<NamedOnnxValue> inputs=[..cache.Values,NamedOnnxValue.CreateFromTensor("attention_mask",new DenseTensor<long>(Enumerable.Repeat(1L,total).ToArray(),[1,total])),NamedOnnxValue.CreateFromTensor("position_ids",new DenseTensor<long>(Enumerable.Range(total-ids.Length,ids.Length).Select(i=>(long)i).ToArray(),[1,ids.Length])),NamedOnnxValue.CreateFromTensor("num_logits_to_keep",new DenseTensor<long>(new[] {1L},[]))];
                foreach (DisposableNamedOnnxValue embed in embeds) { inputs.Add(NamedOnnxValue.CreateFromTensor(embed.Name,embed.AsTensor<float>())); }
                IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs=decoder.Run(inputs,decoder.OutputNames,runOptions);
                if (step==0) { prefill=elapsed.Elapsed.TotalSeconds; }
                Tensor<float> logits=outputs.First(o=>o.Name=="logits").AsTensor<float>();
                int next=ArgMax(logits);
                cache.Clear();
                foreach (DisposableNamedOnnxValue output in outputs) { string name=output.Name.Replace("present.","past_key_values.",StringComparison.Ordinal); if (decoder.InputMetadata.ContainsKey(name)) { cache[name]=NamedOnnxValue.CreateFromTensor(name,output.AsTensor<float>()); } }
                previous?.Dispose(); previous=outputs;
                if (eos.Contains(next)) { timing?.Invoke(new(initial.Length,generated.Count,prefill,elapsed.Elapsed.TotalSeconds)); return tokenizer.Decode(generated); }
                generated.Add(next); ids=[next];
                if (step%50==0) { Console.Error.WriteLine($"[Gemma] inputTokens={initial.Length} generatedTokens={generated.Count}"); }
            }
            throw new InvalidDataException($"Gemma reached {maxTokens} tokens without EOS; no partial answer accepted.");
        }
        finally { previous?.Dispose(); }
    }
    private Dictionary<string,NamedOnnxValue> EmptyCache()
    {
        Dictionary<string,NamedOnnxValue> cache=[];
        foreach (KeyValuePair<string,NodeMetadata> input in decoder.InputMetadata.Where(p=>p.Key.StartsWith("past_key_values",StringComparison.Ordinal)))
        {
            if (input.Value.ElementType!=typeof(float)) { throw new InvalidDataException($"Unsupported Gemma Q4 cache type: {input.Key} {input.Value.ElementType}"); }
            cache[input.Key]=NamedOnnxValue.CreateFromTensor(input.Key,new DenseTensor<float>(CacheShape(input.Value.Dimensions,input.Value.SymbolicDimensions)));
        }
        return cache;
    }
    private static int ArgMax(Tensor<float> logits)
    {
        int vocabulary=logits.Dimensions[^1],last=logits.Dimensions[1]-1,next=0;
        float best=float.NegativeInfinity;
        for (int i=0;i<vocabulary;i++) { float value=logits[0,last,i]; if (value>best) { best=value; next=i; } }
        return next;
    }
    public void Dispose() { decoder.Dispose(); embedding.Dispose(); tokenizer.Dispose(); gate.Dispose(); }
}
