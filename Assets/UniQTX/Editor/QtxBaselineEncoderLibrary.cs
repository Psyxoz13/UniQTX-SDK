using System;
using System.Runtime.InteropServices;

internal class QtxBaselineEncoderLibrary : IQtxEncoderLibrary
{
    public string FilePath
    {
        get
        {
#if UNITY_EDITOR_OSX
            return QtxPackageFolder.GetFilePath("Plugins/x86_64/qtx_encoder.dylib");
#elif UNITY_EDITOR_LINUX
            return QtxPackageFolder.GetFilePath("Plugins/x86_64/qtx_encoder.so");
#else
            return QtxPackageFolder.GetFilePath("Plugins/x86_64/qtx_encoder.dll");
#endif
        }
    }

    private const string Library = "qtx_encoder";

    public int Encode(byte[] rgba, uint width, uint height, ref QtxEncodeParams parameters, out IntPtr output, out UIntPtr outputSize)
    {
        return EncodeNative(rgba, width, height, ref parameters, out output, out outputSize);
    }

    public int EncodeLevels(byte[] levels, uint levelCount, uint width, uint height, ref QtxEncodeParams parameters, out IntPtr output, out UIntPtr outputSize)
    {
        return EncodeLevelsNative(levels, levelCount, width, height, ref parameters, out output, out outputSize);
    }

    public void Free(IntPtr output)
    {
        FreeNative(output);
    }

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "qtx_encode")]
    private static extern int EncodeNative(byte[] rgba, uint width, uint height, ref QtxEncodeParams parameters, out IntPtr output, out UIntPtr outputSize);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "qtx_encode_levels")]
    private static extern int EncodeLevelsNative(byte[] levels, uint levelCount, uint width, uint height, ref QtxEncodeParams parameters, out IntPtr output, out UIntPtr outputSize);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "qtx_free")]
    private static extern void FreeNative(IntPtr output);
}
