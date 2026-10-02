namespace Harness.Models;

public sealed record SamplingSettings(double Temperature,double TopP,int TopK,double PresencePenalty=0)
{
    public static SamplingSettings ForMode(bool thinking,double presencePenalty=0)=>new(thinking ? 0.6 : 0.7,thinking ? 0.95 : 0.8,20,presencePenalty);
}

public static class TokenSampler
{
    public static int Select(float[] logits,IReadOnlyList<int> generated,SamplingSettings settings,Random random)
    {
        HashSet<int> seen=generated.ToHashSet();
        (int Id,double Logit)[] top=logits.Select((value,id)=>(Id:id,Logit:(value-(seen.Contains(id) ? settings.PresencePenalty : 0))/settings.Temperature)).Where(v=>double.IsFinite(v.Logit)).OrderByDescending(v=>v.Logit).Take(settings.TopK).ToArray();
        if (top.Length==0) { throw new InvalidDataException("No finite sampling logits."); }
        double[] weights=top.Select(v=>Math.Exp(v.Logit-top[0].Logit)).ToArray();
        double total=weights.Sum(),cumulative=0;
        int count=0;
        do { cumulative+=weights[count++]/total; } while (count<weights.Length && cumulative<settings.TopP);
        double draw=random.NextDouble()*weights.Take(count).Sum();
        for (int i=0;i<count;i++) { draw-=weights[i]; if (draw<=0) { return top[i].Id; } }
        return top[count-1].Id;
    }
}
