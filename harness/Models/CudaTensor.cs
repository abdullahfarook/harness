using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Harness.Models;

public static class CudaTensor
{
    public static float[] LastLogits(OrtValue value)
    {
        OrtTensorTypeAndShapeInfo info=value.GetTensorTypeAndShape();
        if (info.Shape.Length!=3 || info.Shape[0]!=1 || info.Shape[1]<1 || info.Shape[2]<1) { throw new InvalidDataException("Expected batch-one nonempty rank-three logits."); }
        int vocabulary=checked((int)info.Shape[2]),offset=checked((int)((info.Shape[1]-1)*vocabulary));
        if (info.ElementDataType==TensorElementType.Float) { return value.GetTensorDataAsSpan<float>().Slice(offset,vocabulary).ToArray(); }
        if (info.ElementDataType!=TensorElementType.Float16) { throw new InvalidDataException("Only FP32 or FP16 logits are supported."); }
        ReadOnlySpan<Float16> data=value.GetTensorDataAsSpan<Float16>().Slice(offset,vocabulary);
        float[] result=new float[vocabulary];
        for (int i=0;i<vocabulary;i++) { result[i]=(float)data[i]; }
        return result;
    }
}
