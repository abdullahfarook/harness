using Harness.Models;
using NUnit.Framework;

namespace Harness.Tests;

public class ModelAssetsTests
{
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
