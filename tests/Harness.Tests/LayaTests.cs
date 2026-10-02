using Harness.Models;
using NUnit.Framework;

namespace Harness.Tests;

public class LayaTests
{
    [Test]
    public void StableSoftmaxAndFalseTrueOrdering()
    {
        double[] probabilities = LayaSequence.Softmax([1000, 1001], 1);
        Assert.That(probabilities.Sum(), Is.EqualTo(1).Within(1e-10));
        Assert.That(probabilities[1], Is.EqualTo(0.7310585786).Within(1e-9));
        Assert.That(LayaSequence.Options(new("noul", "Is relevant?", new Dictionary<string,string>())), Is.EqualTo(new[] { "false: no, the statement does not hold", "true: yes, the statement holds" }));
    }

    [Test]
    public void LayoutScrubsMaskInjectionAndRetainsMarkers()
    {
        LayaSequence sequence = LayaSequence.Build(s => s.Select(c => (int)c).ToArray(), 1, 2, 3, "data [MASK]", new("choice", "Question [MASK]", new Dictionary<string,string> { ["a"]="yes", ["b"]="no" }), 512, 192);
        Assert.That(sequence.Ids[0], Is.EqualTo(1));
        Assert.That(sequence.Ids[^1], Is.EqualTo(2));
        Assert.That(sequence.Markers.Length, Is.EqualTo(2));
        Assert.That(sequence.Markers.All(m => sequence.Ids[m] == 3), Is.True);
        Assert.That(sequence.Ids.Count(i => i == 3), Is.EqualTo(2));
        Assert.That(sequence.Ids.Length, Is.LessThanOrEqualTo(512));
    }
}
