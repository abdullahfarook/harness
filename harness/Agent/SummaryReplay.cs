using System.Diagnostics;
using System.Text.Json;
using Harness.Browser;
using Harness.Models;
using Microsoft.Extensions.AI;

namespace Harness.Agent;

public static class SummaryReplay
{
    public static async Task RunAsync(BotOptions options,ILocalTextModel model,EvidenceWriter evidence,double startup,CancellationToken token)
    {
        string path=Path.GetFullPath(options.ReplayFile!);
        PageObservation page=JsonSerializer.Deserialize<PageObservation>(await File.ReadAllTextAsync(path,token)) ?? throw new InvalidDataException("Captured page is missing.");
        JsonElement? classification=null;
        foreach (string line in File.ReadLines(Path.Combine(Path.GetDirectoryName(path)!,"events.jsonl")))
        {
            using JsonDocument entry=JsonDocument.Parse(line);
            if (entry.RootElement.TryGetProperty("kind",out JsonElement kind) && kind.GetString()=="laya_result") { classification=entry.RootElement.GetProperty("data").GetProperty("result").Clone(); }
        }
        if (classification is null) { throw new InvalidDataException("Replay requires the original captured Laya result; no synthetic classification accepted."); }
        using LocalChatClient client=new(model,evidence.Record,options.Thinking ? 4096 : 1536);
        List<object> results=[];
        for (int i=0;i<options.Repeats;i++)
        {
            List<ChatMessage> history=[new(ChatRole.User,$"Summarize {page.Url}: offerings, audience, key capabilities and source URLs."),new(ChatRole.Tool,[new FunctionResultContent("captured-page",page)]),new(ChatRole.Tool,[new FunctionResultContent("captured-laya",classification.Value)])];
            Stopwatch elapsed=Stopwatch.StartNew();
            ChatResponse response=await client.GetResponseAsync(history,cancellationToken:token);
            double seconds=elapsed.Elapsed.TotalSeconds;
            string file=$"summary-replay-trial-{i+1}.txt";
            await File.WriteAllTextAsync(Path.Combine(evidence.DirectoryPath,file),response.Text,token);
            results.Add(new {trial=i+1,seconds,file,status="generated-awaiting-grounding-review"});
            Console.WriteLine(response.Text);
        }
        evidence.Write("replay.json",new {mode="captured-evidence-replay, not live browsing",model=model.ModelId,sourceEvidence=path,startupSeconds=startup,results});
    }
}
