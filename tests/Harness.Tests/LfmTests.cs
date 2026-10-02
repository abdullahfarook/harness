using Harness.Models;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Harness.Tests;

public class LfmTests
{
    [Test]
    public void ReasoningBudgetOnlyClosesAnUnfinishedThinkingBlock()
    {
        Assert.That(LfmThinkingModel.ShouldEndThinking("<think>still deciding",128,128),Is.True);
        Assert.That(LfmThinkingModel.ShouldEndThinking("<think>brief</think>{}",128,128),Is.False);
        Assert.That(LfmThinkingModel.ShouldEndThinking("<think>still deciding",127,128),Is.False);
        Assert.That(LfmThinkingModel.ShouldEndThinking("{\"tool\":\"navigate\"}",128,128),Is.False);
    }
    [Test]
    public void RepetitionPenaltyDiscouragesGreedyReasoningLoops()
    {
        Assert.That(LfmThinkingModel.SelectToken([10,9],[0],1.2),Is.EqualTo(1));
        Assert.That(LfmThinkingModel.SelectToken([-1,-1.1f],[0],1.2),Is.EqualTo(1));
    }
    [Test]
    public void CacheShapeSeparatesAttentionAndConvolution()
    {
        Assert.That(LfmThinkingModel.CacheShape([ -1,8,-1,256 ],["batch_size","","past_sequence_length",""]), Is.EqualTo(new[] {1,8,0,256}));
        Assert.That(LfmThinkingModel.CacheShape([-1,2048,3],["batch_size","",""]), Is.EqualTo(new[] {1,2048,3}));
    }
    [Test]
    public void ChatTemplateMatchesLfmTokens()
    {
        Assert.That(LfmThinkingModel.Prompt([new(ChatRole.User,"Hello")]), Is.EqualTo("<|startoftext|><|im_start|>user\nHello<|im_end|>\n<|im_start|>assistant\n"));
    }
}
