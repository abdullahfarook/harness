using Harness.Models;
using Microsoft.ML.OnnxRuntime;
using NUnit.Framework;

namespace Harness.Tests;

public class CudaTensorTests
{
    [TestCase("QwenCudaModel")]
    [TestCase("QwenGenaiModel")]
    public void GpuBackendsRetainNativeTextModelContract(string name)
    {
        Type? type=typeof(QwenModel).Assembly.GetType("Harness.Models."+name);
        Assert.That(type,Is.Not.Null,"Requested GPU backend is not implemented.");
        Assert.That(typeof(ILocalTextModel).IsAssignableFrom(type!),Is.True);
    }
    [TestCase(false)]
    [TestCase(true)]
    public void NativeLastLogitsReadsOnlyLastSequenceAndSupportsFloat16(bool half)
    {
        Type? type=typeof(QwenModel).Assembly.GetType("Harness.Models.CudaTensor");
        Assert.That(type,Is.Not.Null,"Native device-tensor helper is missing.");
        Func<OrtValue,float[]> read=type!.GetMethod("LastLogits")!.CreateDelegate<Func<OrtValue,float[]>>();
        float[] values=[9,9,9,-1,0,1];
        using OrtValue tensor=half ? OrtValue.CreateTensorValueFromMemory(values.Select(v=>(Float16)v).ToArray(),new long[] {1,2,3}) : OrtValue.CreateTensorValueFromMemory(values,new long[] {1,2,3});
        Assert.That(read(tensor),Is.EqualTo(new float[] {-1,0,1}));
    }
    [Test]
    public void Int64LogitsAreRejectedInsteadOfReinterpreted()
    {
        Type? type=typeof(QwenModel).Assembly.GetType("Harness.Models.CudaTensor");
        Assert.That(type,Is.Not.Null);
        Func<OrtValue,float[]> read=type!.GetMethod("LastLogits")!.CreateDelegate<Func<OrtValue,float[]>>();
        using OrtValue tensor=OrtValue.CreateTensorValueFromMemory(new long[] {1,2},new long[] {1,1,2});
        Assert.Throws<InvalidDataException>(()=>read(tensor));
    }
    [Test]
    public void PlacementVerifierRejectsCpuTransformerFallback()
    {
        Type? type=typeof(QwenModel).Assembly.GetType("Harness.Models.CudaPlacement");
        Assert.That(type,Is.Not.Null,"Placement validation is missing.");
        Action<string> validate=type!.GetMethod("Validate")!.CreateDelegate<Action<string>>();
        string path=Path.GetTempFileName();
        try
        {
            File.WriteAllText(path,"[{\"cat\":\"Node\",\"args\":{\"provider\":\"CPUExecutionProvider\",\"op_name\":\"MatMulNBits\"}},{\"cat\":\"Node\",\"args\":{\"provider\":\"CUDAExecutionProvider\",\"op_name\":\"MatMul\"}}]");
            Assert.Throws<InvalidDataException>(()=>validate(path));
            File.WriteAllText(path,"[{\"cat\":\"Node\",\"args\":{\"provider\":\"CPUExecutionProvider\",\"op_name\":\"Shape\"}},{\"cat\":\"Node\",\"args\":{\"provider\":\"CUDAExecutionProvider\",\"op_name\":\"MatMulNBits\"}}]");
            Assert.DoesNotThrow(()=>validate(path));
            File.WriteAllText(path,"[]");
            Assert.Throws<InvalidDataException>(()=>validate(path));
        }
        finally { File.Delete(path); }
    }
    [Test]
    public void GenerationTimingSeparatesFirstTokenAndDecodeFromEos()
    {
        Assert.That(typeof(GenerationTiming).GetProperty("FirstTokenSeconds"),Is.Not.Null);
        Assert.That(typeof(GenerationTiming).GetProperty("LastTokenSeconds"),Is.Not.Null);
        Assert.That(typeof(GenerationTiming).GetProperty("DecodeTokensPerSecond"),Is.Not.Null);
    }
}
