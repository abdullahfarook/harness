using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Harness.Models;

public sealed class Qwen35Model : ILocalTextModel
{
    private readonly InferenceSession decoder,embedding;
    private readonly NativeTokenizer tokenizer;
    private readonly HashSet<int> eos;
    private readonly Random random;
    private readonly Action<GenerationTiming>? timing;
    private readonly Action<string>? diagnostics;
    private readonly SemaphoreSlim gate=new(1,1);
    public bool Thinking { get; }
    public SamplingSettings Sampling { get; }
    public string ModelId=>"onnx-community/Qwen3.5-0.8B-ONNX";
    public Qwen35Model(ModelAssets assets,bool thinking=false,int seed=42,double? presencePenalty=null,Action<GenerationTiming>? timing=null,Action<string>? diagnostics=null)
    {
        Thinking=thinking; Sampling=Settings(thinking,presencePenalty); random=new(seed); this.timing=timing; this.diagnostics=diagnostics;
        tokenizer=new(assets.TokenizerPath);
        using JsonDocument configuration=JsonDocument.Parse(File.ReadAllText(Path.Combine(assets.DirectoryPath,"generation_config.json")));
        eos=configuration.RootElement.GetProperty("eos_token_id").EnumerateArray().Select(v=>v.GetInt32()).ToHashSet();
        using SessionOptions options=new() {IntraOpNumThreads=4,InterOpNumThreads=1};
        embedding=new(Path.Combine(assets.DirectoryPath,"onnx/embed_tokens_q4.onnx"),options);
        decoder=new(assets.GraphPath,options);
    }
    public static SamplingSettings Settings(bool thinking,double? penalty=null)=>new(1,thinking ? 0.95 : 1,20,penalty ?? (thinking ? 1.5 : 2));
    public static int[] StateShape(int[] dimensions,string[] symbols)=>QwenModel.CacheShape(dimensions,symbols);
    public static string StateInput(string output)=>output.Replace("present_conv.","past_conv.",StringComparison.Ordinal).Replace("present_recurrent.","past_recurrent.",StringComparison.Ordinal).Replace("present.","past_key_values.",StringComparison.Ordinal);
    public Task<string> GenerateAsync(IReadOnlyList<ChatMessage> messages,int maxTokens,CancellationToken cancellationToken)=>GenerateAsync(messages,[],maxTokens,cancellationToken);
    public async Task<string> GenerateAsync(IReadOnlyList<ChatMessage> messages,IReadOnlyList<AIFunction> tools,int maxTokens,CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { return await Task.Run(()=>Generate(Qwen35Prompt.Build(messages,tools,Thinking),maxTokens,cancellationToken),cancellationToken); }
        finally { gate.Release(); }
    }
    private string Generate(string prompt,int maxTokens,CancellationToken token)
    {
        Stopwatch elapsed=Stopwatch.StartNew();
        int[] initial=tokenizer.Encode(prompt);
        if (initial.Length+maxTokens>8192) { throw new InvalidDataException("Qwen3.5 exceeds the application 8192-token input/output budget."); }
        Dictionary<string,NamedOnnxValue> state=EmptyState();
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
                using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> embeds=embedding.Run([NamedOnnxValue.CreateFromTensor("input_ids",new DenseTensor<long>(ids,[1,ids.Length]))],embedding.OutputNames,runOptions);
                int total=initial.Length+generated.Count;
                List<NamedOnnxValue> inputs=Inputs(state,embeds,total,ids.Length);
                IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs=decoder.Run(inputs,decoder.OutputNames,runOptions);
                if (step==0) { prefill=elapsed.Elapsed.TotalSeconds; }
                int next=NextToken(outputs.First(o=>o.Name=="logits").AsTensor<float>(),generated);
                state=OutputsState(outputs);
                previous?.Dispose(); previous=outputs;
                if (eos.Contains(next))
                {
                    string output=(Thinking ? "<think>\n" : "")+tokenizer.Decode(generated);
                    diagnostics?.Invoke(output); timing?.Invoke(new(initial.Length,generated.Count,prefill,elapsed.Elapsed.TotalSeconds)); return output;
                }
                generated.Add(next);
                if (step%50==0) { Console.Error.WriteLine($"[Qwen3.5] inputTokens={initial.Length} generatedTokens={generated.Count}"); diagnostics?.Invoke((Thinking ? "<think>\n" : "")+tokenizer.Decode(generated)); }
            }
            throw new InvalidDataException($"Qwen3.5 reached {maxTokens} tokens without EOS; no partial answer accepted.");
        }
        finally { previous?.Dispose(); }
    }
    private static List<NamedOnnxValue> Inputs(Dictionary<string,NamedOnnxValue> state,IDisposableReadOnlyCollection<DisposableNamedOnnxValue> embeds,int total,int length)
    {
        long[] positions=Enumerable.Range(0,3).SelectMany(_=>Enumerable.Range(total-length,length).Select(i=>(long)i)).ToArray();
        return [..state.Values,NamedOnnxValue.CreateFromTensor("inputs_embeds",embeds.First().AsTensor<float>()),NamedOnnxValue.CreateFromTensor("attention_mask",new DenseTensor<long>(Enumerable.Repeat(1L,total).ToArray(),[1,total])),NamedOnnxValue.CreateFromTensor("position_ids",new DenseTensor<long>(positions,[3,1,length]))];
    }
    private int NextToken(Tensor<float> logits,List<int> generated)
    {
        int last=logits.Dimensions[1]-1,vocabulary=logits.Dimensions[^1];
        return TokenSampler.Select(Enumerable.Range(0,vocabulary).Select(i=>logits[0,last,i]).ToArray(),generated,Sampling,random);
    }
    private Dictionary<string,NamedOnnxValue> EmptyState()
    {
        Dictionary<string,NamedOnnxValue> state=[];
        foreach (KeyValuePair<string,NodeMetadata> input in decoder.InputMetadata.Where(p=>p.Key.StartsWith("past_",StringComparison.Ordinal)))
        {
            if (input.Value.ElementType!=typeof(float)) { throw new InvalidDataException("Unsupported Qwen3.5 state dtype."); }
            state[input.Key]=NamedOnnxValue.CreateFromTensor(input.Key,new DenseTensor<float>(StateShape(input.Value.Dimensions,input.Value.SymbolicDimensions)));
        }
        return state;
    }
    private Dictionary<string,NamedOnnxValue> OutputsState(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs)=>outputs.Where(o=>decoder.InputMetadata.ContainsKey(StateInput(o.Name))).ToDictionary(o=>StateInput(o.Name),o=>NamedOnnxValue.CreateFromTensor(StateInput(o.Name),o.AsTensor<float>()));
    public void Dispose() { decoder.Dispose(); embedding.Dispose(); tokenizer.Dispose(); gate.Dispose(); }
}
