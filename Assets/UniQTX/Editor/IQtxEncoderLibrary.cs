using System;

internal interface IQtxEncoderLibrary
{
    string FilePath { get; }

    int Encode(byte[] rgba, uint width, uint height, ref QtxEncodeParams parameters, out IntPtr output, out UIntPtr outputSize);

    int EncodeLevels(byte[] levels, uint levelCount, uint width, uint height, ref QtxEncodeParams parameters, out IntPtr output, out UIntPtr outputSize);

    void Free(IntPtr output);
}
