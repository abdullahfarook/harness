using Harness.Agent;
using Harness.Models;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Harness.Tests;

[Category("ModelIntegration")]
public class ModelIntegrationTests
{
    [TestCase(false),TestCase(true),Explicit("Runs actual Qwen3 Q4 generation and native tool call")]
    public async Task Qwen3RealInference(bool thinking)
    {
        using Qwen3Model model=new(ModelAssets.Load(Path.Combine(Root,"qwen3"),"qwen3"),thinking);
        using CancellationTokenSource timeout=new(TimeSpan.FromMinutes(10));
        string answer=await model.GenerateAsync([new(ChatRole.User,"What is 6 times 7? Answer briefly.")],2048,timeout.Token);
        Assert.That(ActionProtocol.RemoveThinking(answer),Does.Contain("42"));
        AIFunction classify=AIFunctionFactory.Create(()=>true,"classify");
        string call=await model.GenerateAsync([new(ChatRole.System,"Call classify with no arguments. Return only the native tool call."),new(ChatRole.User,"The page is already captured; classify it now.")],[classify],2048,timeout.Token);
        ParsedAction action=Qwen3Protocol.Parse(call,["classify"],false);
        ActionProtocol.ValidateArguments(action,[classify]);
        Assert.That(action.Tool,Is.EqualTo("classify"));
    }
    [Test, Explicit("Runs real Qwen2.5-1.5B-Instruct Q4 ONNX weights on CPU")]
    public async Task QwenRealInference()
    {
        List<GenerationTiming> measurements=[];
        using QwenModel model=new(ModelAssets.Load(Path.Combine(Root,"qwen"),"qwen"),timing:measurements.Add);
        using CancellationTokenSource timeout=new(TimeSpan.FromMinutes(4));
        string answer=await model.GenerateAsync([new(ChatRole.User,"What is 6 times 7? Answer briefly.")],512,timeout.Token);
        Assert.That(answer,Does.Contain("42"));
        Assert.That(measurements,Has.Count.EqualTo(1));
        Assert.That(measurements[0].GenerationSeconds,Is.GreaterThanOrEqualTo(measurements[0].PrefillSeconds));
        TestContext.Out.WriteLine(System.Text.Json.JsonSerializer.Serialize(new {answer,timing=measurements[0]}));
    }
    [Test, Explicit("Runs real Gemma 4 E2B Q4 ONNX weights on CPU")]
    public async Task GemmaRealInference()
    {
        List<GenerationTiming> measurements=[];
        using GemmaModel model=new(ModelAssets.Load(Path.Combine(Root,"gemma"),"gemma"),timing:measurements.Add);
        using CancellationTokenSource timeout=new(TimeSpan.FromMinutes(4));
        string answer=await model.GenerateAsync([new(ChatRole.User,"What is 6 times 7? Answer briefly.")],512,timeout.Token);
        Assert.That(answer.Trim(),Is.EqualTo("42"));
        Assert.That(measurements,Has.Count.EqualTo(1));
        Assert.That(measurements[0].GeneratedTokens,Is.GreaterThan(0));
        Assert.That(measurements[0].GenerationSeconds,Is.GreaterThanOrEqualTo(measurements[0].PrefillSeconds));
        TestContext.Out.WriteLine(System.Text.Json.JsonSerializer.Serialize(measurements[0]));
    }
    private static string Root => Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "../../../../../.local/website-models"));
    [Test, Explicit("Compares all real typed outputs to the independent receptron package")]
    public void LayaTypedParity()
    {
        using System.Text.Json.JsonDocument reference=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory,"laya-reference.json")));
        Dictionary<string,DecisionQuestion> questions=[];
        foreach (System.Text.Json.JsonProperty entry in reference.RootElement.GetProperty("questions").EnumerateObject())
        {
            Dictionary<string,string> criteria=[];
            if (entry.Value.TryGetProperty("criteria",out System.Text.Json.JsonElement values))
            {
                if (values.ValueKind==System.Text.Json.JsonValueKind.Object) { foreach (System.Text.Json.JsonProperty item in values.EnumerateObject()) { criteria[item.Name]=item.Value.GetString()!; } }
                else { int i=0; foreach (System.Text.Json.JsonElement item in values.EnumerateArray()) { criteria[(i++).ToString()]=item.GetString()!; } }
            }
            questions[entry.Name]=new(entry.Value.GetProperty("type").GetString()!,entry.Value.GetProperty("instructions").GetString()!,criteria);
        }
        using LayaDecisionModel model=new(ModelAssets.Load(Path.Combine(Root,"laya"),"laya"));
        DecisionResult result=model.Decide(reference.RootElement.GetProperty("state").GetString()!,questions);
        Assert.That(result.InputTokens,Is.EqualTo(reference.RootElement.GetProperty("result").GetProperty("usage").GetProperty("input_tokens").GetInt32()));
        foreach (KeyValuePair<string,DecisionAnswer> entry in result.Answers)
        {
            System.Text.Json.JsonElement expected=reference.RootElement.GetProperty("result").GetProperty("answers").GetProperty(entry.Key);
            if (entry.Value.Noul is double noul) { Assert.That(Math.Round(noul,4),Is.EqualTo(expected.GetProperty("noul").GetDouble())); }
            if (entry.Value.Score is double score) { Assert.That(Math.Round(score,4),Is.EqualTo(expected.GetProperty("score").GetDouble())); }
            if (entry.Value.Choice is string choice) { Assert.That(choice,Is.EqualTo(expected.GetProperty("choice").GetString())); }
            if (expected.TryGetProperty("probabilities",out System.Text.Json.JsonElement probabilities)) { foreach (System.Text.Json.JsonProperty p in probabilities.EnumerateObject()) { Assert.That(entry.Value.Probabilities[p.Name],Is.EqualTo(p.Value.GetDouble())); } }
        }
    }
    [Test, Explicit("Runs real LFM weights on CPU")]
    public async Task LfmRealInference()
    {
        ModelAssets assets = ModelAssets.Load(Path.Combine(Root,"lfm"),"lfm");
        using LfmThinkingModel model = new(assets);
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(4));
        string answer = ActionProtocol.RemoveThinking(await model.GenerateAsync([new(ChatRole.User,"What is 6 times 7? Answer briefly.")],768,timeout.Token));
        TestContext.Out.WriteLine(answer);
        Assert.That(answer,Does.Contain("42"));
    }
    [Test, Explicit("Runs real Laya weights on CPU")]
    public void LayaRealInference()
    {
        ModelAssets assets = ModelAssets.Load(Path.Combine(Root,"laya"),"laya");
        using LayaDecisionModel model = new(assets);
        DecisionResult result = model.Decide("OpenPlate Studio offers an enterprise application boilerplate for developers.",new Dictionary<string,DecisionQuestion> { ["relevant"] = new("noul","Does the text describe what a software product offers?",new Dictionary<string,string>()) });
        TestContext.Out.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
        Assert.That(result.Answers["relevant"].Noul,Is.InRange(0d,1d));
        Assert.That(result.InputTokens,Is.GreaterThan(0));
        using System.Text.Json.JsonDocument reference=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory,"laya-reference.json")));
        Assert.That(Math.Round(result.Answers["relevant"].Noul!.Value,4),Is.EqualTo(reference.RootElement.GetProperty("result").GetProperty("answers").GetProperty("relevant").GetProperty("noul").GetDouble()));
        Assert.That(result.InputTokens,Is.EqualTo(49));
    }
}
