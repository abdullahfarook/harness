namespace Harness;

public sealed record BotOptions(Uri Url, string GemmaModel, string LayaModel, bool Headless, string Output, int KeepOpenSeconds, int TimeoutSeconds,string? Question=null,int Repeats=3)
{
    public static BotOptions Parse(string[] args)
    {
        Dictionary<string,string> options=[];
        bool headless=false;
        for (int i=0;i<args.Length;i++)
        {
            if (args[i]=="--headless") { headless=true; continue; }
            if (args[i] is not ("--url" or "--gemma-model" or "--laya-model" or "--output" or "--keep-open-seconds" or "--timeout-seconds" or "--question" or "--repeats") || i+1>=args.Length) { throw new ArgumentException($"Unknown or incomplete option: {args[i]}"); }
            options.Add(args[i],args[++i]);
        }
        int keep=Number(options,"--keep-open-seconds",15,0,3600), timeout=Number(options,"--timeout-seconds",1800,1,86400);
        Uri url=new(options.GetValueOrDefault("--url","https://openplatestudio.com/"));
        return new(url,options.GetValueOrDefault("--gemma-model") ?? Environment.GetEnvironmentVariable("GEMMA_MODEL_PATH") ?? ".local/website-models/gemma",options.GetValueOrDefault("--laya-model") ?? Environment.GetEnvironmentVariable("LAYA_MODEL_PATH") ?? ".local/website-models/laya",headless,options.GetValueOrDefault("--output","artifacts/website-summary"),keep,timeout,options.GetValueOrDefault("--question"),Number(options,"--repeats",3,1,10));
    }
    private static int Number(Dictionary<string,string> options,string key,int fallback,int min,int max)
    {
        if (!options.TryGetValue(key,out string? value)) { return fallback; }
        if (!int.TryParse(value,out int n) || n<min || n>max) { throw new ArgumentException($"Invalid {key}: range {min}..{max}"); }
        return n;
    }
}
