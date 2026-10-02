using Harness.Models;
using NUnit.Framework;
using System.Security.Cryptography;
using System.Text.Json;

namespace Harness.Tests;

public class QwenVariantAssetsTests
{
    [Test]
    public void GenaiExportUsesPinnedOfficialCheckpointAndRequiresCompatibleConfig()
    {
        string directory=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString()); Directory.CreateDirectory(directory);
        try
        {
            string[] files=["model.onnx","model.onnx.data","genai_config.json","tokenizer.json","tokenizer_config.json"];
            foreach (string file in files) { File.WriteAllText(Path.Combine(directory,file),"test"); }
            object[] records=files.Select(file=>(object)new {path=file,bytes=4,sha256=Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("test")))}).ToArray();
            File.WriteAllText(Path.Combine(directory,"manifest.json"),JsonSerializer.Serialize(new {name="qwen-genai",repo="Qwen/Qwen2.5-1.5B-Instruct",revision="989aa7980e4cf806f80c7fef2b1adb7bc71aa306",files=records}));
            Assert.That(ModelAssets.Load(directory,"qwen-genai").GraphPath,Does.EndWith("model.onnx"));
            File.WriteAllText(Path.Combine(directory,"manifest.json"),JsonSerializer.Serialize(new {name="qwen-genai",repo="Qwen/Qwen2.5-1.5B-Instruct",revision="wrong",files=records}));
            Assert.That(()=>ModelAssets.Load(directory,"qwen-genai"),Throws.TypeOf<InvalidDataException>().With.Message.Contains("revision"));
        }
        finally { Directory.Delete(directory,true); }
    }
    [TestCase("qwen-q4f16","model_q4f16.onnx",false)]
    [TestCase("qwen-fp16","model_fp16.onnx",true)]
    public void PrecisionVariantHasOwnPinnedManifestAndGraph(string name,string graph,bool external)
    {
        string directory=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString()); Directory.CreateDirectory(Path.Combine(directory,"onnx"));
        try
        {
            string[] files=["onnx/"+graph,"config.json","generation_config.json","tokenizer.json","tokenizer_config.json",..external ? new[] {"onnx/"+graph+"_data"} : []];
            foreach (string file in files) { File.WriteAllText(Path.Combine(directory,file),"test"); }
            File.WriteAllText(Path.Combine(directory,"manifest.json"),JsonSerializer.Serialize(new {name,repo="onnx-community/Qwen2.5-1.5B-Instruct",revision="6287331f475a3e20e8c879be8fd4bf3551ad9d34",files=files.Select(file=>new {path=file,bytes=4,sha256=Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("test")))})}));
            Assert.That(ModelAssets.Load(directory,name).GraphPath,Does.EndWith(graph));
            using JsonDocument json=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"manifest.json")));
            JsonElement manifest=json.RootElement;
            File.WriteAllText(Path.Combine(directory,"manifest.json"),JsonSerializer.Serialize(new {name,repo="onnx-community/Qwen2.5-1.5B-Instruct",revision="wrong",files=manifest.GetProperty("files")}));
            Assert.That(()=>ModelAssets.Load(directory,name),Throws.TypeOf<InvalidDataException>().With.Message.Contains("revision"));
        }
        finally { Directory.Delete(directory,true); }
    }
}
