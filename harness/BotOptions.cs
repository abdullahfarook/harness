namespace Harness;

public sealed record BotOptions(Uri Url, string BrainModel, string LayaModel, bool Headless, string Output, int KeepOpenSeconds, int TimeoutSeconds,string? Question=null,int Repeats=3,bool Thinking=false,int Seed=42,double? PresencePenalty=null,string Brain="qwen",string Provider="cpu",string Precision="q4",int Threads=4,int Context=8192,bool Profile=false,string? ReplayFile=null)
{
    public static BotOptions Parse(string[] args)
    {
        Dictionary<string,string> options=[];
        bool headless=false;
        for (int i=0;i<args.Length;i++)
        {
            if (args[i]=="--profile") { options["--profile"]="true"; continue; }
            if (args[i]=="--headless") { headless=true; continue; }
            if (args[i] is not ("--replay-file" or "--brain" or "--provider" or "--precision" or "--threads" or "--context" or "--model" or "--qwen-model" or "--qwen3-model" or "--gemma-model" or "--lfm-model" or "--url" or "--qwen35-model" or "--laya-model" or "--output" or "--keep-open-seconds" or "--timeout-seconds" or "--question" or "--repeats" or "--thinking" or "--seed" or "--presence-penalty") || i+1>=args.Length) { throw new ArgumentException($"Unknown or incomplete option: {args[i]}"); }
            options.Add(args[i],args[++i]);
        }
        int keep=Number(options,"--keep-open-seconds",15,0,3600), timeout=Number(options,"--timeout-seconds",1800,1,86400);
        Uri url=new(options.GetValueOrDefault("--url","https://openplatestudio.com/"));
        bool thinking=options.GetValueOrDefault("--thinking","off") switch { "on"=>true,"off"=>false,_=>throw new ArgumentException("--thinking must be on or off.") };
        double? penalty=options.TryGetValue("--presence-penalty",out string? supplied) ? double.Parse(supplied,System.Globalization.CultureInfo.InvariantCulture) : null;
        if (penalty is double valuePenalty && (!double.IsFinite(valuePenalty) || valuePenalty<0 || valuePenalty>2)) { throw new ArgumentException("--presence-penalty must be 0..2."); }
        string brain=Select(options,"--brain",new[] {"qwen","qwen3","qwen35","gemma","lfm"},options.Keys.FirstOrDefault(key=>key.EndsWith("-model",StringComparison.Ordinal) && key!="--laya-model" && key!="--model")?.Replace("--","").Replace("-model","") ?? "qwen");
        string provider=Select(options,"--provider",new[] {"cpu","cuda","genai"},"cpu"),precision=Select(options,"--precision",new[] {"q4","q4f16","fp16"},"q4");
        if (brain!="qwen" && (provider!="cpu" || precision!="q4")) { throw new ArgumentException("GPU variants are only supported for Qwen2.5; other retained models use their original CPU backend."); }
        if (thinking && brain is not ("qwen3" or "qwen35")) { throw new ArgumentException("Thinking on/off is only supported by retained Qwen3 and Qwen3.5 models."); }
        string path=options.GetValueOrDefault("--model") ?? options.GetValueOrDefault($"--{brain}-model") ?? Environment.GetEnvironmentVariable($"{brain.ToUpperInvariant()}_MODEL_PATH") ?? $".local/website-models/{(provider=="genai" ? "qwen-genai" : brain=="qwen" && precision!="q4" ? "qwen-"+precision : brain)}";
        return new(url,path,options.GetValueOrDefault("--laya-model") ?? Environment.GetEnvironmentVariable("LAYA_MODEL_PATH") ?? ".local/website-models/laya",headless,options.GetValueOrDefault("--output","artifacts/website-summary"),keep,timeout,options.GetValueOrDefault("--question"),Number(options,"--repeats",3,1,10),thinking,Number(options,"--seed",42,0,int.MaxValue),penalty,brain,provider,precision,Number(options,"--threads",4,1,16),Number(options,"--context",8192,2048,8192),options.ContainsKey("--profile"),options.GetValueOrDefault("--replay-file"));
    }
    private static string Select(Dictionary<string,string> options,string key,string[] allowed,string fallback)
    {
        string value=options.GetValueOrDefault(key,fallback);
        return allowed.Contains(value,StringComparer.Ordinal) ? value : throw new ArgumentException($"{key} must be {string.Join(", ",allowed)}.");
    }
    private static int Number(Dictionary<string,string> options,string key,int fallback,int min,int max)
    {
        if (!options.TryGetValue(key,out string? value)) { return fallback; }
        if (!int.TryParse(value,out int n) || n<min || n>max) { throw new ArgumentException($"Invalid {key}: range {min}..{max}"); }
        return n;
    }
}
