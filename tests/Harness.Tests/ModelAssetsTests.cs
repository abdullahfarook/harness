using Harness.Models;
using NUnit.Framework;

namespace Harness.Tests;

public class ModelAssetsTests
{
    [Test]
    public void ManifestCannotOmitRequiredExternalDataAndConfig()
    {
        string directory=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString()); Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory,"manifest.json"),"{\"name\":\"laya\",\"repo\":\"receptron/laya-onnx\",\"files\":[]}");
            File.WriteAllText(Path.Combine(directory,"laya.onnx"),"fake"); Directory.CreateDirectory(Path.Combine(directory,"tokenizer"));
            File.WriteAllText(Path.Combine(directory,"tokenizer/tokenizer.json"),"{}");
            Assert.Throws<InvalidDataException>(() => ModelAssets.Load(directory,"laya"));
        }
        finally { Directory.Delete(directory,true); }
    }
    [Test]
    public void MissingDirectoryFailsClearly()
    {
        Assert.Throws<DirectoryNotFoundException>(() => ModelAssets.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), "lfm"));
    }

    [Test]
    public void IncompleteBundleFailsClearly()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try { Assert.Throws<FileNotFoundException>(() => ModelAssets.Load(directory, "laya")); }
        finally { Directory.Delete(directory); }
    }
}
