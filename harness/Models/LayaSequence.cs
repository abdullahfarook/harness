namespace Harness.Models;

public sealed record DecisionQuestion(string Type, string Instructions, IReadOnlyDictionary<string, string> Criteria);
public sealed record DecisionAnswer(string Type, string? Choice, double? Score, double? Noul, IReadOnlyDictionary<string,double> Probabilities, double Confidence);
public sealed record DecisionResult(IReadOnlyDictionary<string,DecisionAnswer> Answers, int InputTokens);

public sealed record LayaSequence(int[] Ids, int[] Markers)
{
    public static string[] Options(DecisionQuestion question) => question.Type switch
    {
        "choice" => question.Criteria.Select(p => string.IsNullOrEmpty(p.Value) ? p.Key : $"{p.Key}: {p.Value}").ToArray(),
        "score" => question.Criteria.Values.Select((v,i) => $"level {i}: {v}").ToArray(),
        "noul" => ["false: " + (question.Criteria.GetValueOrDefault("false") ?? "no, the statement does not hold"), "true: " + (question.Criteria.GetValueOrDefault("true") ?? "yes, the statement holds")],
        _ => throw new ArgumentException("Unknown decision type")
    };

    public static LayaSequence Build(Func<string,int[]> encode, int cls, int sep, int mask, string state, DecisionQuestion question, int maxLen, int headMaxLen)
    {
        string Scrub(string s) => s.Replace("[MASK]", " ", StringComparison.Ordinal);
        int[] head = encode($"{question.Type} question: {Scrub(question.Instructions)}");
        int[][] options = Options(question).Select(o => new[] { mask }.Concat(encode(" " + Scrub(o)).Take(48)).ToArray()).ToArray();
        if (options.Length == 0) { throw new ArgumentException("At least one option required"); }
        int budget = headMaxLen - options.Sum(o => o.Length);
        if (budget < 16)
        {
            int per = Math.Max(4, (headMaxLen - 16) / options.Length);
            options = options.Select(o => o.Take(per).ToArray()).ToArray();
            budget = headMaxLen - options.Sum(o => o.Length);
        }
        List<int> ids = [cls, ..head.Take(Math.Max(8, budget)), sep];
        List<int> markers = [];
        foreach (int[] option in options) { markers.Add(ids.Count); ids.AddRange(option); }
        ids.Add(sep);
        ids.AddRange(encode(Scrub(state)).Take(Math.Max(0, maxLen - ids.Count - 1)));
        ids.Add(sep);
        if (markers.Any(m => m >= maxLen)) { throw new ArgumentException("Options exceed model header capacity"); }
        return new(ids.Take(maxLen).ToArray(), markers.ToArray());
    }

    public static double[] Softmax(IEnumerable<double> logits, double temperature)
    {
        if (!double.IsFinite(temperature) || temperature <= 0) { throw new InvalidDataException("Invalid calibration temperature"); }
        double[] values = logits.Select(v => v / temperature).ToArray();
        double maximum = values.Max();
        double[] exponents = values.Select(v => Math.Exp(v - maximum)).ToArray();
        double sum = exponents.Sum();
        if (!double.IsFinite(sum) || sum <= 0) { throw new InvalidDataException("Invalid model logits"); }
        return exponents.Select(v => v / sum).ToArray();
    }
}
