using System.Text.Json;

namespace Harness.Browser;

public sealed class EvidenceWriter
{
    private readonly object gate=new();
    public string DirectoryPath { get; }
    public EvidenceWriter(string directory) { DirectoryPath=Path.GetFullPath(directory); Directory.CreateDirectory(DirectoryPath); }
    public void Record(string json) { lock (gate) { File.AppendAllText(Path.Combine(DirectoryPath,"events.jsonl"),json+Environment.NewLine); } }
    public void Event(string kind, object data) => Record(JsonSerializer.Serialize(new { timestamp=DateTimeOffset.UtcNow,kind,data }));
    public void Write(string name, object data) => File.WriteAllText(Path.Combine(DirectoryPath,name),JsonSerializer.Serialize(data,new JsonSerializerOptions { WriteIndented=true }));
}
