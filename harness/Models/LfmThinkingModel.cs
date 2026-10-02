using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Harness.Models;

public sealed class LfmThinkingModel : IDisposable
{
    private readonly InferenceSession session;
    private readonly NativeTokenizer tokenizer;
    private readonly int eos;
    private readonly Action<string>? diagnostics;
    private readonly int reasoningBudget;
    private readonly SemaphoreSlim gate = new(1,1);
    public LfmThinkingModel(ModelAssets assets, int threads = 4, Action<string>? diagnostics = null, int reasoningBudget = 128)
    {
        tokenizer = new(assets.TokenizerPath);
        this.diagnostics=diagnostics;
        this.reasoningBudget=reasoningBudget;
        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets.DirectoryPath,"config.json")));
        eos = config.RootElement.GetProperty("eos_token_id").GetInt32();
        using SessionOptions options = new() { IntraOpNumThreads = threads, InterOpNumThreads = 1 };
        session = new(assets.GraphPath,options);
    }
    public static int[] CacheShape(int[] dimensions, string[] symbols) => dimensions.Select((d,i) => symbols[i].Contains("sequence",StringComparison.OrdinalIgnoreCase) ? 0 : d > 0 ? d : 1).ToArray();
    public static bool ShouldEndThinking(string output,int generated,int budget) => budget>0 && generated>=budget && output.Contains("<think>",StringComparison.Ordinal) && !output.Contains("</think>",StringComparison.Ordinal);
    public static int SelectToken(IReadOnlyList<float> logits,IEnumerable<int> previous,double penalty=1.05)
    {
        HashSet<int> seen=previous.ToHashSet(); int next=0; double best=double.NegativeInfinity;
        for (int i=0;i<logits.Count;i++) { double score=logits[i]; if (seen.Contains(i)) { score=score>=0 ? score/penalty : score*penalty; } if (score>best) { best=score; next=i; } }
        return next;
    }
    public static string Prompt(IReadOnlyList<ChatMessage> messages)
    {
        StringBuilder result = new("<|startoftext|>");
        foreach (ChatMessage message in messages) { result.Append("<|im_start|>").Append(message.Role.Value).Append('\n').Append(message.Text).Append("<|im_end|>\n"); }
        return result.Append("<|im_start|>assistant\n").ToString();
    }
    public async Task<string> GenerateAsync(IReadOnlyList<ChatMessage> messages, int maxTokens, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { return await Task.Run(() => Generate(Prompt(messages),maxTokens,cancellationToken),cancellationToken); }
        finally { gate.Release(); }
    }
    private string Generate(string prompt, int maxTokens, CancellationToken token)
    {
        int[] initial = tokenizer.Encode(prompt);
        if (initial.Length + maxTokens > 8192) { throw new InvalidDataException($"Prompt exceeds configured 8192-token budget: {initial.Length} input + {maxTokens} output."); }
        Dictionary<string,NamedOnnxValue> cache = [];
        foreach (KeyValuePair<string,NodeMetadata> input in session.InputMetadata)
        {
            if (input.Key is "input_ids" or "attention_mask" or "position_ids") { continue; }
            if (input.Value.ElementType != typeof(float)) { throw new InvalidDataException($"Unsupported cache type: {input.Key} {input.Value.ElementType}"); }
            cache[input.Key] = NamedOnnxValue.CreateFromTensor(input.Key,new DenseTensor<float>(CacheShape(input.Value.Dimensions,input.Value.SymbolicDimensions)));
        }
        List<int> generated = [];
        long[]? pending=null;
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue>? previous = null;
        using RunOptions runOptions = new();
        using CancellationTokenRegistration registration = token.Register(() => runOptions.Terminate = true);
        try
        {
            for (int step = 0; step < maxTokens; step++)
            {
                token.ThrowIfCancellationRequested();
                long[] ids = step == 0 ? initial.Select(i => (long)i).ToArray() : pending ?? [(long)generated[^1]];
                pending=null;
                int total = initial.Length + generated.Count;
                List<NamedOnnxValue> inputs = [NamedOnnxValue.CreateFromTensor("input_ids",new DenseTensor<long>(ids,[1,ids.Length])),NamedOnnxValue.CreateFromTensor("attention_mask",new DenseTensor<long>(Enumerable.Repeat(1L,total).ToArray(),[1,total])),..cache.Values];
                if (session.InputMetadata.ContainsKey("position_ids")) { inputs.Add(NamedOnnxValue.CreateFromTensor("position_ids",new DenseTensor<long>(Enumerable.Range(total-ids.Length,ids.Length).Select(i => (long)i).ToArray(),[1,ids.Length]))); }
                IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = session.Run(inputs,session.OutputNames,runOptions);
                Tensor<float> logits = outputs.First(o => o.Name == "logits").AsTensor<float>();
                int vocabulary = logits.Dimensions[^1], last = logits.Dimensions[1]-1;
                int next = SelectToken(Enumerable.Range(0,vocabulary).Select(i=>logits[0,last,i]).ToArray(),generated);
                cache.Clear();
                foreach (DisposableNamedOnnxValue output in outputs)
                {
                    string name = output.Name.Replace("present_conv","past_conv",StringComparison.Ordinal).Replace("present.","past_key_values.",StringComparison.Ordinal);
                    if (session.InputMetadata.ContainsKey(name)) { cache[name] = NamedOnnxValue.CreateFromTensor(name,output.AsTensor<float>()); }
                }
                previous?.Dispose();
                previous=outputs;
                if (next == eos) { string output=tokenizer.Decode(generated); diagnostics?.Invoke(output); return output; }
                generated.Add(next);
                if (generated.Count==reasoningBudget && ShouldEndThinking(tokenizer.Decode(generated),generated.Count,reasoningBudget))
                {
                    int[] close=tokenizer.Encode("\n</think>\n");
                    pending=new[] { (long)next }.Concat(close.Select(id=>(long)id)).ToArray();
                    generated.AddRange(close);
                    Console.Error.WriteLine($"[LFM] reasoning budget {reasoningBudget} reached; closing reasoning, not inventing an action.");
                }
                if (step % 100 == 0) { Console.Error.WriteLine($"[LFM] inputTokens={initial.Length} generatedTokens={step+1}"); diagnostics?.Invoke(tokenizer.Decode(generated)); }
            }
            throw new InvalidDataException($"LFM reached {maxTokens} tokens without EOS. Increase output budget; no complete answer accepted.");
        }
        finally { previous?.Dispose(); }
    }
    public void Dispose() { session.Dispose(); tokenizer.Dispose(); gate.Dispose(); }
}
