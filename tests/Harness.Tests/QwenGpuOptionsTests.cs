using System.Text.Json;
using NUnit.Framework;

namespace Harness.Tests;

public class QwenGpuOptionsTests
{
    [Test]
    public void GenericModelOverrideDoesNotInventABrainName()
    {
        Assert.That(BotOptions.Parse(["--model","custom"]).BrainModel,Is.EqualTo("custom"));
    }
    [Test]
    public void CapturedSummaryReplayIsAnExplicitOptionNotALiveRun()
    {
        using JsonDocument json=JsonDocument.Parse(JsonSerializer.Serialize(BotOptions.Parse(["--replay-file","page.json"])));
        Assert.That(json.RootElement.GetProperty("ReplayFile").GetString(),Is.EqualTo("page.json"));
    }
    [Test]
    public void DefaultBrainIsRetainedQwen25CpuBaseline()
    {
        Assert.That(BotOptions.Parse([]).BrainModel,Does.EndWith("website-models/qwen"));
    }
    [Test]
    public void ExplicitProviderAndPrecisionAreRecorded()
    {
        BotOptions options=BotOptions.Parse(["--brain","qwen","--provider","cuda","--precision","q4f16","--threads","2","--context","4096"]);
        using JsonDocument json=JsonDocument.Parse(JsonSerializer.Serialize(options));
        Assert.That(json.RootElement.GetProperty("Provider").GetString(),Is.EqualTo("cuda"));
        Assert.That(json.RootElement.GetProperty("Precision").GetString(),Is.EqualTo("q4f16"));
        Assert.That(json.RootElement.GetProperty("Threads").GetInt32(),Is.EqualTo(2));
        Assert.That(json.RootElement.GetProperty("Context").GetInt32(),Is.EqualTo(4096));
        Assert.That(options.BrainModel,Does.EndWith("website-models/qwen-q4f16"));
    }
    [Test]
    public void OlderBrainPathInfersItsIdentityWithoutDeletingOtherModels()
    {
        using JsonDocument json=JsonDocument.Parse(JsonSerializer.Serialize(BotOptions.Parse(["--qwen35-model","custom"])));
        Assert.That(json.RootElement.GetProperty("Brain").GetString(),Is.EqualTo("qwen35"));
    }
    [TestCase("--provider","fake")]
    [TestCase("--precision","fake")]
    [TestCase("--context","32768")]
    [TestCase("--threads","0")]
    public void InvalidRuntimeSelectionFailsClearly(string option,string value)=>Assert.Throws<ArgumentException>(()=>BotOptions.Parse([option,value]));
    [Test]
    public void ThinkingCannotBeAdvertisedForQwen25()=>Assert.Throws<ArgumentException>(()=>BotOptions.Parse(["--brain","qwen","--thinking","on"]));
}
