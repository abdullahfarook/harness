using Harness.Agent;
using Harness.Models;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Harness.Tests;

public class Qwen35Tests
{
    [Test]
    public void ClassifierSchemaExplainsItsBackendStoredEvidence()
    {
        System.ComponentModel.DescriptionAttribute description=(System.ComponentModel.DescriptionAttribute)Attribute.GetCustomAttribute(typeof(Harness.Browser.BrowserTools).GetMethod("Classify")!,typeof(System.ComponentModel.DescriptionAttribute))!;
        Assert.That(description.Description,Does.Contain("stored").And.Contain("no parameters"));
    }
    [Test]
    public void CliRetainsQwen35AndPreservesModeSpecificPenaltyUnlessOverridden()
    {
        Assert.That(BotOptions.Parse(["--brain","qwen35"]).BrainModel,Does.EndWith("website-models/qwen35"));
        Assert.That(BotOptions.Parse([]).PresencePenalty,Is.Null);
        Assert.That(BotOptions.Parse(["--qwen35-model","custom","--presence-penalty","0"]).BrainModel,Is.EqualTo("custom"));
        Assert.That(BotOptions.Parse(["--presence-penalty","0"]).PresencePenalty,Is.Zero);
    }
    [TestCase("present_conv.0","past_conv.0")]
    [TestCase("present_recurrent.0","past_recurrent.0")]
    [TestCase("present.3.key","past_key_values.3.key")]
    public void AllThreeStateFamiliesMapToNextStepInputs(string output,string input) { Assert.That(Qwen35Model.StateInput(output),Is.EqualTo(input)); }
    [Test]
    public void TemplateUsesItsOwnOnPrefixAndXmlToolFormat()
    {
        AIFunction navigate=AIFunctionFactory.Create((string url)=>url,"navigate");
        string on=Qwen35Prompt.Build([new(ChatRole.User,"Read site")],[navigate],true);
        Assert.That(on,Does.EndWith("<|im_start|>assistant\n<think>\n").And.Contain("<function=example_function_name>"));
        Assert.That(Qwen35Prompt.Build([new(ChatRole.User,"Hi")],[],false),Does.EndWith("<think>\n\n</think>\n\n"));
    }
    [Test]
    public void NativeXmlMapsTypedArgumentsWithoutRepair()
    {
        AIFunction navigate=AIFunctionFactory.Create((string url)=>url,"navigate");
        ParsedAction action=Qwen35Protocol.Parse("<tool_call>\n<function=navigate>\n<parameter=url>\nhttps://openplatestudio.com/\n</parameter>\n</function>\n</tool_call>",[navigate],false);
        Assert.That(action.Tool,Is.EqualTo("navigate")); Assert.That(action.Arguments!["url"],Is.EqualTo("https://openplatestudio.com/"));
    }
    [TestCase("<tool_call><function=delete></function></tool_call>")]
    [TestCase("<tool_call><function=classify><parameter=page>text</parameter></function></tool_call>")]
    [TestCase("<tool_call><function=classify></function></tool_call><tool_call><function=classify></function></tool_call>")]
    [TestCase("<tool_call>{\"name\":\"classify\",\"arguments\":{}}</tool_call>")]
    public void InvalidXmlCallsFailClosed(string output)
    {
        AIFunction classify=AIFunctionFactory.Create(()=>true,"classify");
        Assert.Throws<InvalidDataException>(()=>Qwen35Protocol.Parse(output,[classify],true));
    }
    [Test]
    public void HybridStatesStartWithZeroSequenceButRetainRecurrentDimensions()
    {
        Assert.That(Qwen35Model.StateShape([-1,2,-1,256],["batch_size","","past_sequence_length",""]),Is.EqualTo(new[] {1,2,0,256}));
        Assert.That(Qwen35Model.StateShape([-1,16,128,128],["batch_size","","",""]),Is.EqualTo(new[] {1,16,128,128}));
    }
    [Test]
    public void SamplingDefaultsAreTextTaskSpecificAndOverrideable()
    {
        Assert.That(Qwen35Model.Settings(false).PresencePenalty,Is.EqualTo(2));
        Assert.That(Qwen35Model.Settings(true).PresencePenalty,Is.EqualTo(1.5));
        Assert.That(Qwen35Model.Settings(false).Temperature,Is.EqualTo(1));
        Assert.That(Qwen35Model.Settings(true,0).PresencePenalty,Is.Zero);
    }
}
