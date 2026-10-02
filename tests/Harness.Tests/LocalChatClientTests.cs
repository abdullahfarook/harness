using Harness.Agent;
using Harness.Models;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Harness.Tests;

public class LocalChatClientTests
{
    [Test]
    public async Task JsonDecisionBecomesHarnessFunctionCallAndRecordsModelAndTiming()
    {
        List<string> events=[];
        using FakeModel model=new("{\"tool\":\"navigate\",\"arguments\":{\"url\":\"https://openplatestudio.com/\"}}");
        using LocalChatClient client=new(model,events.Add);
        AIFunction navigate=AIFunctionFactory.Create((string url)=>url,"navigate");
        ChatResponse response=await client.GetResponseAsync([new(ChatRole.User,"Summarize https://openplatestudio.com/")],new() { Tools=[navigate] });
        Assert.That(response.Messages.Single().Contents.OfType<FunctionCallContent>().Single().Name,Is.EqualTo("navigate"));
        Assert.That(response.ModelId,Is.EqualTo(model.ModelId));
        Assert.That(events[^1],Does.Contain("seconds").And.Contain("decision").And.Contain(model.ModelId));
        Assert.That(model.LastPrompt![0].Text,Does.Contain("JSON").And.Not.Contain("<|tool_call_start|>"));
    }
    [Test]
    public void MissingPageCannotBeBypassedByPrematureFinal()
    {
        using FakeModel model=new("{\"final\":\"Invented summary\"}");
        using LocalChatClient client=new(model,_=>{});
        Assert.That(async()=>await client.GetResponseAsync([new(ChatRole.User,"Summarize a website")]),Throws.InstanceOf<InvalidDataException>());
    }
    private sealed class FakeModel(string output) : ILocalTextModel
    {
        public string ModelId=>"fake/gemma-test";
        public IReadOnlyList<ChatMessage>? LastPrompt { get; private set; }
        public Task<string> GenerateAsync(IReadOnlyList<ChatMessage> messages,int maxTokens,CancellationToken cancellationToken) { LastPrompt=messages; return Task.FromResult(output); }
        public void Dispose() { }
    }
}
