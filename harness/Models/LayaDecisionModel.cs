using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Harness.Models;

public sealed class LayaDecisionModel : IDisposable
{
    private readonly InferenceSession session;
    private readonly NativeTokenizer tokenizer;
    private readonly JsonElement config;
    public LayaDecisionModel(ModelAssets assets, int threads = 4)
    {
        tokenizer = new(assets.TokenizerPath);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets.DirectoryPath, "laya_config.json")));
        config = document.RootElement.Clone();
        using SessionOptions options = new() { IntraOpNumThreads = threads, InterOpNumThreads = 1 };
        session = new(assets.GraphPath, options);
    }
    public DecisionResult Decide(string state, IReadOnlyDictionary<string,DecisionQuestion> questions)
    {
        if (questions.Count == 0) { throw new ArgumentException("At least one question required"); }
        LayaSequence[] sequences = questions.Values.Select(q => LayaSequence.Build(tokenizer.Encode, tokenizer.SpecialId("[CLS]"), tokenizer.SpecialId("[SEP]"), tokenizer.SpecialId("[MASK]"), state, q, config.GetProperty("max_len").GetInt32(), config.GetProperty("head_max_len").GetInt32())).ToArray();
        int n = sequences.Length, length = sequences.Max(s => s.Ids.Length), width = sequences.Max(s => s.Markers.Length);
        DenseTensor<long> ids = new([n,length]), attention = new([n,length]), markers = new([n,width]), types = new([n]);
        DenseTensor<bool> markerMask = new([n,width]);
        int pad = tokenizer.SpecialId("[PAD]");
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < length; j++) { ids[i,j] = j < sequences[i].Ids.Length ? sequences[i].Ids[j] : pad; attention[i,j] = j < sequences[i].Ids.Length ? 1 : 0; }
            for (int j = 0; j < sequences[i].Markers.Length; j++) { markers[i,j] = sequences[i].Markers[j]; markerMask[i,j] = true; }
            types[i] = TypeId(questions.Values.ElementAt(i).Type);
        }
        NamedOnnxValue[] inputs = [NamedOnnxValue.CreateFromTensor("input_ids",ids), NamedOnnxValue.CreateFromTensor("attention_mask",attention), NamedOnnxValue.CreateFromTensor("marker_pos",markers), NamedOnnxValue.CreateFromTensor("marker_mask",markerMask), NamedOnnxValue.CreateFromTensor("qtype",types)];
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = session.Run(inputs);
        Tensor<float> logits = results.Single(r => r.Name == "logits").AsTensor<float>();
        Dictionary<string,DecisionAnswer> answers = [];
        for (int i = 0; i < n; i++)
        {
            KeyValuePair<string,DecisionQuestion> entry = questions.ElementAt(i);
            int k = sequences[i].Markers.Length;
            string bucket = $"{entry.Value.Type}:{(k <= 2 ? "2" : k <= 5 ? "3-5" : k <= 10 ? "6-10" : "11+")}";
            double temperature = config.GetProperty("temperature_by_options").TryGetProperty(bucket, out JsonElement calibrated) ? calibrated.GetDouble() : config.GetProperty("temperature")[TypeId(entry.Value.Type)].GetDouble();
            double[] p = LayaSequence.Softmax(Enumerable.Range(0,k).Select(j => (double)logits[i,j]),temperature);
            string[] keys = entry.Value.Type == "choice" ? entry.Value.Criteria.Keys.ToArray() : Enumerable.Range(0,k).Select(j => j.ToString()).ToArray();
            int best = Array.IndexOf(p,p.Max());
            double confidence = k < 2 ? 1 : 1 + p.Sum(v => v * Math.Log(Math.Max(v,1e-12))) / Math.Log(k);
            answers[entry.Key] = new(entry.Value.Type, entry.Value.Type == "choice" ? keys[best] : null, entry.Value.Type == "score" ? p.Select((v,j) => v*j).Sum() : null, entry.Value.Type == "noul" ? p[1] : null, keys.Select((key,j) => (key,p[j])).ToDictionary(v => v.key,v => Math.Round(v.Item2,4)), Math.Round(confidence,4));
        }
        return new(answers,sequences.Sum(s => s.Ids.Length));
    }
    private static int TypeId(string type) => type switch { "choice" => 0, "score" => 1, "noul" => 2, _ => throw new ArgumentException("Unknown question type") };
    public void Dispose() { session.Dispose(); tokenizer.Dispose(); }
}
