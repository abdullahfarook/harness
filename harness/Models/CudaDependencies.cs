namespace Harness.Models;

public static class CudaDependencies
{
    public static void Configure()
    {
        string[] candidates=[Path.Combine(Environment.GetEnvironmentVariable("CUDA_PATH") ?? "","bin"),Path.Combine(Environment.GetEnvironmentVariable("CUDA_PATH") ?? "","bin","x64"),Environment.GetEnvironmentVariable("CUDNN_PATH") ?? "",Path.GetFullPath(".local/website-bot-work/qwen25-export/Lib/site-packages/nvidia/cudnn/bin"),Path.GetFullPath(".local/website-bot-work/qwen25-export/Lib/site-packages/nvidia/cu13/bin")];
        string additions=string.Join(Path.PathSeparator,candidates.Where(path=>path.Length>0 && Path.IsPathFullyQualified(path) && Directory.Exists(path)).Distinct(StringComparer.OrdinalIgnoreCase));
        Environment.SetEnvironmentVariable("PATH",additions+Path.PathSeparator+Environment.GetEnvironmentVariable("PATH"));
    }
}
