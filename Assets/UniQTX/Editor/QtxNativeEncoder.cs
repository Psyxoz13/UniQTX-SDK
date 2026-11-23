using System;
using System.IO;
using System.Runtime.InteropServices;

public static class QtxNativeEncoder
{
    private static readonly IQtxEncoderLibrary Library = CreateLibrary();
    private static string failedVersion;
    public static bool UsedFallback { get; private set; }

    public static string Version => QtxLibraryVersion.Of(Library.FilePath);

    public static byte[] Encode(byte[] rgba, int width, int height, int quality)
    {
        if (rgba == null || width <= 0 || height <= 0 || width > QtxEncoder.MaxDimension || height > QtxEncoder.MaxDimension || (long)width * height * 4 != rgba.Length)
            throw new ArgumentException($"QTX requires RGBA pixels matching dimensions between 1 and {QtxEncoder.MaxDimension}");
        var parameters = ParametersFor(quality);
        return EncodeWithFallback(
            (out IntPtr output, out UIntPtr outputSize) => Library.Encode(rgba, (uint)width, (uint)height, ref parameters, out output, out outputSize),
            () => QtxEncoder.Encode(rgba, width, height, new QtxEncodeOptions { Quality = quality }),
            width, height, 1);
    }

    public static byte[] EncodeLevels(byte[][] levels, int width, int height, int quality)
    {
        if (levels == null || levels.Length < 1 || levels.Length > QtxEncoder.MaxLevels || width <= 0 || height <= 0 || width > QtxEncoder.MaxDimension || height > QtxEncoder.MaxDimension)
            throw new ArgumentException($"QTX requires 1 to {QtxEncoder.MaxLevels} levels with dimensions between 1 and {QtxEncoder.MaxDimension}");
        var sizes = new (int Width, int Height)[levels.Length];
        var total = 0L;
        for (var level = 0; level < levels.Length; level++)
        {
            sizes[level] = (Math.Max(1, width >> level), Math.Max(1, height >> level));
            if (levels[level] == null || levels[level].Length != sizes[level].Width * sizes[level].Height * 4)
                throw new ArgumentException($"QTX mip level {level} must hold {sizes[level].Width}x{sizes[level].Height} RGBA pixels");
            total += levels[level].Length;
        }
        var pixels = new byte[total];
        var offset = 0;
        foreach (var level in levels)
        {
            Buffer.BlockCopy(level, 0, pixels, offset, level.Length);
            offset += level.Length;
        }
        var parameters = ParametersFor(quality);
        return EncodeWithFallback(
            (out IntPtr output, out UIntPtr outputSize) => Library.EncodeLevels(pixels, (uint)levels.Length, (uint)width, (uint)height, ref parameters, out output, out outputSize),
            () => QtxEncoder.EncodeLevels(levels, width, height, new QtxEncodeOptions { Quality = quality }),
            width, height, levels.Length);
    }

    private delegate int NativeEncode(out IntPtr output, out UIntPtr outputSize);

    private static IQtxEncoderLibrary CreateLibrary()
    {
#if UNITY_EDITOR_WIN
        var avx2Library = new QtxAvx2EncoderLibrary();

        if (QtxProcessorFeatures.IsAvx2Available && File.Exists(avx2Library.FilePath))
        {
            return avx2Library;
        }
#endif

        return new QtxBaselineEncoderLibrary();
    }

    private static QtxEncodeParams ParametersFor(int quality)
    {
        if (quality < QtxEncodeOptions.MinQuality || quality > QtxEncodeOptions.MaxQuality)
            throw new ArgumentOutOfRangeException(nameof(quality), quality, $"QTX quality must be {QtxEncodeOptions.MinQuality} to {QtxEncodeOptions.MaxQuality}");
        return new QtxEncodeParams { StructSize = (uint)Marshal.SizeOf<QtxEncodeParams>(), Quality = quality };
    }

    private static byte[] EncodeWithFallback(NativeEncode native, Func<byte[]> managed, int width, int height, int levelCount)
    {
        UsedFallback = false;
        var version = Version;
        if (failedVersion == version)
        {
            UsedFallback = true;
            return managed();
        }
        try
        {
            var status = native(out var output, out var outputSize);
            try
            {
                if (status != 0 || output == IntPtr.Zero || (ulong)outputSize < QtxHeader.Size)
                    throw new InvalidOperationException("Native QTX encoder failed with status " + status);
                var bytes = new byte[(int)outputSize];
                Marshal.Copy(output, bytes, 0, bytes.Length);
                var header = new QtxHeader(bytes);
                if (header.Width != width || header.Height != height || header.MipCount != levelCount)
                    throw new InvalidDataException("Native QTX encoder returned mismatching dimensions or levels");
                return bytes;
            }
            finally
            {
                if (output != IntPtr.Zero)
                    Library.Free(output);
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException || exception is EntryPointNotFoundException || exception is BadImageFormatException || exception is InvalidOperationException || exception is InvalidDataException)
        {
            failedVersion = version;
            UsedFallback = true;
            UnityEngine.Debug.LogWarning($"QTX: native encoding failed; using the Unity encoder without caching until its binary changes or scripts reload. {exception.Message}");
            return managed();
        }
    }
}
