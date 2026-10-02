using Harness;
using Harness.Agent;
using Harness.Browser;
using Harness.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

try
{
    BotOptions options=BotOptions.Parse(args);
    EvidenceWriter evidence=new(options.Output);
    using CancellationTokenSource timeout=new(TimeSpan.FromSeconds(options.TimeoutSeconds));
    Console.CancelKeyPress+=(_,e)=> { e.Cancel=true; timeout.Cancel(); };
    Console.Error.WriteLine("Verifying requested local ONNX models...");
    ModelAssets lfmAssets=ModelAssets.Load(options.LfmModel,"lfm");
    ModelAssets layaAssets=ModelAssets.Load(options.LayaModel,"laya");
    evidence.Write("models.json",new { lfm=lfmAssets.Manifest,laya=layaAssets.Manifest });
    using LfmThinkingModel thinking=new(lfmAssets,diagnostics:text=>File.WriteAllText(Path.Combine(evidence.DirectoryPath,"generation-diagnostic.txt"),text));
    using LayaDecisionModel quick=new(layaAssets);
    using LfmChatClient client=new(thinking,evidence.Record);
    await using BrowserTools browser=new(options.Url,evidence,quick);
    await browser.StartAsync(options.Headless,timeout.Token);
    AITool[] tools=[AIFunctionFactory.Create(browser.NavigateAsync,"navigate"),AIFunctionFactory.Create(browser.ObserveAsync,"observe"),AIFunctionFactory.Create(browser.ClickAsync,"click"),AIFunctionFactory.Create(browser.ScrollAsync,"scroll"),AIFunctionFactory.Create(browser.Classify,"classify")];
    HarnessAgent agent=new(client,new HarnessAgentOptions
    {
        Name="website-summary",
        HarnessInstructions="Use only the supplied read-only website tools. Return verified findings, not planned actions.",
        ChatOptions=new() { Tools=tools,MaxOutputTokens=1536 },
        DisableTodoProvider=true,DisableAgentModeProvider=true,DisableFileMemory=true,
        DisableAgentSkillsProvider=true,DisableWebSearch=true,
        DisableOpenTelemetry=true
    });
    Console.Error.WriteLine($"Harness browsing {options.Url} (headed={!options.Headless})");
    AgentResponse response=await agent.RunAsync($"Summarize {options.Url.AbsoluteUri}: offerings, audience, key capabilities and source URLs.",cancellationToken:timeout.Token);
    if (browser.LastObservation is null || browser.Classifications==0 || string.IsNullOrWhiteSpace(response.Text)) { throw new InvalidDataException("Incomplete run: require real browser evidence, Laya usage and a nonempty summary."); }
    if (!browser.Visited.Any(url => response.Text.Contains(url,StringComparison.OrdinalIgnoreCase))) { throw new InvalidDataException("Summary is missing observed source URLs."); }
    File.WriteAllText(Path.Combine(evidence.DirectoryPath,"summary.txt"),response.Text);
    evidence.Write("run.json",new { target=options.Url.AbsoluteUri,headed=!options.Headless,visited=browser.Visited,layaCalls=browser.Classifications,status="generated-awaiting-grounding-review" });
    Console.WriteLine(response.Text);
    if (!options.Headless && options.KeepOpenSeconds>0) { await Task.Delay(TimeSpan.FromSeconds(options.KeepOpenSeconds),timeout.Token); }
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Website bot failed: {exception.Message}");
    return 1;
}
