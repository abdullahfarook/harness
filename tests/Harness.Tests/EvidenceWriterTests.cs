using Harness.Browser;
using NUnit.Framework;

namespace Harness.Tests;

public class EvidenceWriterTests
{
    [Test]
    public void ConcurrentEventsAreWrittenWithoutLoss()
    {
        string directory=Path.Combine(Path.GetTempPath(),"evidence-test-"+Guid.NewGuid().ToString("N"));
        EvidenceWriter writer=new(directory);
        Parallel.For(0,500,i=>writer.Event("test",new { i }));
        Assert.That(File.ReadAllLines(Path.Combine(directory,"events.jsonl")),Has.Length.EqualTo(500));
    }
}
