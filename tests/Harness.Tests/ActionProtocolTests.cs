using Harness.Agent;
using NUnit.Framework;

namespace Harness.Tests;

public class ActionProtocolTests
{
    [TestCase("<|tool_call_start|>[navigate(url='https://openplatestudio.com/')]<|tool_call_end|>","navigate","url","https://openplatestudio.com/")]
    [TestCase("<|tool_call_start|>[click(observedLinkId='link-0-3')]<|tool_call_end|>","click","observedLinkId","link-0-3")]
    public void AcceptsNativeLfmToolCallsWithoutEvaluatingCode(string text,string tool,string key,string value)
    {
        ParsedAction action=ActionProtocol.Parse(text,[tool]); Assert.That(action.Tool,Is.EqualTo(tool)); Assert.That(action.Arguments![key],Is.EqualTo(value));
    }
    [Test]
    public void AcceptsNativeEmptyArgumentCallAndRejectsExecutableExpressions()
    {
        Assert.That(ActionProtocol.Parse("<|tool_call_start|>[classify()]<|tool_call_end|>",["classify"]).Arguments,Is.Empty);
        Assert.Throws<InvalidDataException>(()=>ActionProtocol.Parse("<|tool_call_start|>[navigate(url=__import__('os').system('bad'))]<|tool_call_end|>",["navigate"]));
    }
    [Test]
    public void RejectsMissingUnexpectedAndWrongTypedArguments()
    {
        Microsoft.Extensions.AI.AIFunction navigate=Microsoft.Extensions.AI.AIFunctionFactory.Create((string url)=>url,"navigate");
        foreach (string text in new[] { "{\"tool\":\"navigate\",\"arguments\":{}}", "{\"tool\":\"navigate\",\"arguments\":{\"url\":42}}", "{\"tool\":\"navigate\",\"arguments\":{\"url\":\"https://example.com\",\"shell\":\"bad\"}}" })
        { Assert.Throws<InvalidDataException>(() => ActionProtocol.ValidateArguments(ActionProtocol.Parse(text,["navigate"]),[navigate])); }
    }
    [Test]
    public void ValidActionBecomesNamedToolCall()
    {
        ParsedAction action = ActionProtocol.Parse("<think>private reasoning</think>{\"tool\":\"navigate\",\"arguments\":{\"url\":\"https://openplatestudio.com/\"}}", ["navigate"]);
        Assert.That(action.Tool, Is.EqualTo("navigate"));
        Assert.That(action.Arguments!["url"]!.ToString(), Is.EqualTo("https://openplatestudio.com/"));
    }
    [TestCase("{\"tool\":\"shell\",\"arguments\":{}}")]
    [TestCase("<think>unfinished")]
    [TestCase("{\"tool\":\"navigate\",\"arguments\":[]}")]
    public void InvalidActionFails(string text) => Assert.Throws<InvalidDataException>(() => ActionProtocol.Parse(text,["navigate"]));
    [Test]
    public void FinalTextIsPreservedWithoutThinking()
    {
        Assert.That(ActionProtocol.Parse("<think>reasoning</think>{\"final\":\"Site summary\"}",[]).Final, Is.EqualTo("Site summary"));
    }
}
