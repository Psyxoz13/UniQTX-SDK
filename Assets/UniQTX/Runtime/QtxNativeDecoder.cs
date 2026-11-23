#if !UNITY_WEBGL || UNITY_EDITOR
using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

public static class QtxNativeDecoder
{
#if UNITY_IOS && !UNITY_EDITOR
    private const string Library = "__Internal";
#else
    private const string Library = "qtx_decoder";
#endif
    private const int Success = 0;
    private const uint BuildMipsFlag = 1;
    private const uint HighQualityFlag = 2;
    private static readonly object InitializeLock = new();
    private static int state;

    [StructLayout(LayoutKind.Sequential)]
    private struct TranscodeInfo
    {
        public UIntPtr OutputSize;
        public UIntPtr ScratchSize;
        public uint LevelCount;
        public uint WorkUnits;
    }

    [DllImport(Library)]
    private static extern void qtx_init();

    [DllImport(Library)]
    private static extern unsafe int qtx_get_transcode_info(byte* data, UIntPtr size, int format, uint flags, out TranscodeInfo info);

    [DllImport(Library)]
    private static extern unsafe int qtx_transcode(byte* data, UIntPtr size, int format, uint flags, byte* output, UIntPtr outputSize, byte* scratch, UIntPtr scratchSize);

    [DllImport(Library)]
    private static extern unsafe int qtx_transcode_units(byte* data, UIntPtr size, int format, uint flags, uint firstUnit, uint unitCount, byte* output, UIntPtr outputSize, byte* scratch, UIntPtr scratchSize);

    public static unsafe bool TryDecode(byte[] file, QtxTarget target, Span<byte> destination, bool buildMips)
    {
        if (!IsAvailable())
            return false;
        var highQuality = target == QtxTarget.Etc1HighQuality || target == QtxTarget.Etc2RgbaHighQuality;
        var format = target == QtxTarget.Etc1HighQuality ? (int)QtxTarget.Etc1 : target == QtxTarget.Etc2RgbaHighQuality ? (int)QtxTarget.Etc2Rgba : (int)target;
        var flags = (buildMips ? BuildMipsFlag : 0) | (highQuality ? HighQualityFlag : 0);
        fixed (byte* filePointer = file)
        fixed (byte* outputPointer = destination)
        {
            if (qtx_get_transcode_info(filePointer, (UIntPtr)file.Length, format, flags, out var info) != Success
                || (ulong)info.OutputSize > (ulong)destination.Length || (ulong)info.ScratchSize > int.MaxValue)
                return false;
            var workers = Math.Min((int)info.WorkUnits, Environment.ProcessorCount);
            if (workers > 1)
                return TranscodeInParallel((IntPtr)filePointer, file.Length, format, flags, (IntPtr)outputPointer, destination.Length, (int)info.ScratchSize, info.WorkUnits, workers);
            var scratch = ArrayPool<byte>.Shared.Rent((int)info.ScratchSize);
            try
            {
                fixed (byte* scratchPointer = scratch)
                    return qtx_transcode(filePointer, (UIntPtr)file.Length, format, flags, outputPointer, (UIntPtr)destination.Length, scratchPointer, (UIntPtr)scratch.Length) == Success;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(scratch);
            }
        }
    }

    private static unsafe bool TranscodeInParallel(IntPtr file, int fileLength, int format, uint flags, IntPtr output, int outputLength, int scratchSize, uint units, int workers)
    {
        var failures = 0;
        Parallel.For(0, workers, worker =>
        {
            var first = (uint)((long)units * worker / workers);
            var end = (uint)((long)units * (worker + 1) / workers);
            var scratch = ArrayPool<byte>.Shared.Rent(scratchSize);
            try
            {
                fixed (byte* scratchPointer = scratch)
                    if (qtx_transcode_units((byte*)file, (UIntPtr)fileLength, format, flags, first, end - first, (byte*)output, (UIntPtr)outputLength, scratchPointer, (UIntPtr)scratch.Length) != Success)
                        Interlocked.Increment(ref failures);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(scratch);
            }
        });
        return failures == 0;
    }

    private static bool IsAvailable()
    {
        if (state != 0)
            return state > 0;
        lock (InitializeLock)
        {
            if (state != 0)
                return state > 0;
            try
            {
                qtx_init();
                state = 1;
            }
            catch (DllNotFoundException)
            {
                state = -1;
            }
            catch (EntryPointNotFoundException)
            {
                state = -1;
            }
            return state > 0;
        }
    }
}
#endif
