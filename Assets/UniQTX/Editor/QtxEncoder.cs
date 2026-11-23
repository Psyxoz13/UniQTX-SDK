
using System;
using System.Collections.Generic;


public class QtxEncodeOptions
{
    public const int DefaultQuality = 50;
    public const int MinQuality = 0;
    public const int MaxQuality = 100;

    public int Quality { get; set; } = DefaultQuality;

    public bool Mipmaps { get; set; }
}

public static class QtxEncoder
{
    public const int MaxDimension = ushort.MaxValue;
    public const int MaxLevels = QtxImageEncoder.MipLevelsMax;

    public static byte[] Encode(ReadOnlySpan<byte> rgba, int width, int height, QtxEncodeOptions options = null)
    {
        options ??= new QtxEncodeOptions();
        ValidateQuality(options);
        ValidateImage(rgba.Length, width, height, nameof(rgba));
        var settings = QtxEncoderSettings.ForQuality(options.Quality);
        var pixels = rgba.ToArray();
        return options.Mipmaps ? QtxImageEncoder.EncodeWithMips(pixels, width, height, settings) : QtxImageEncoder.Encode(pixels, width, height, settings, out _);
    }

    public static byte[] EncodeLevels(IReadOnlyList<byte[]> levels, int width, int height, QtxEncodeOptions options = null)
    {
        options ??= new QtxEncodeOptions();
        ValidateQuality(options);
        if (levels == null || levels.Count < 1 || levels.Count > MaxLevels)
            throw new ArgumentException($"QTX stores 1 to {MaxLevels} mip levels", nameof(levels));
        var pixels = new byte[levels.Count][];
        var sizes = new (int Width, int Height)[levels.Count];
        for (var level = 0; level < levels.Count; level++)
        {
            sizes[level] = (Math.Max(1, width >> level), Math.Max(1, height >> level));
            ValidateImage(levels[level]?.Length ?? 0, sizes[level].Width, sizes[level].Height, $"{nameof(levels)}[{level}]");
            pixels[level] = levels[level];
        }
        return QtxImageEncoder.EncodeLevels(pixels, sizes, QtxEncoderSettings.ForQuality(options.Quality));
    }

    private static void ValidateQuality(QtxEncodeOptions options)
    {
        if (options.Quality < QtxEncodeOptions.MinQuality || options.Quality > QtxEncodeOptions.MaxQuality)
            throw new ArgumentOutOfRangeException(nameof(options), options.Quality, $"quality must be {QtxEncodeOptions.MinQuality} to {QtxEncodeOptions.MaxQuality}");
    }

    private static void ValidateImage(int length, int width, int height, string name)
    {
        if (width < 1 || height < 1 || width > MaxDimension || height > MaxDimension)
            throw new ArgumentOutOfRangeException(name, $"{width}x{height} is outside 1x1 to {MaxDimension}x{MaxDimension}");
        if (length != (long)width * height * 4)
            throw new ArgumentException($"{width}x{height} RGBA needs {(long)width * height * 4} bytes, got {length}", name);
    }
}
