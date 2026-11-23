using System;
using System.IO;

public enum QtxTarget
{
    Rgba32,
    Bc1,
    Bc3,
    Etc1,
    Etc2Rgba,
    Astc4x4,
    Bc7,
    Astc6x6,
    Astc8x8,
    Etc1HighQuality,
    Etc2RgbaHighQuality,
}

public class QtxHeader
{
    public const int Size = 24;
    public const byte FormatVersion = 1;
    public const int MipHeaderSize = 8;
    public const int MipLevelsMax = 16;
    public const int QualityOffset = 5;
    public const int AstcBlockOffset = 21;

    public readonly int Width;
    public readonly int Height;
    public readonly bool HasAlpha;
    public readonly int BlocksWide;
    public readonly int BlocksHigh;
    public readonly int MipCount;
    public readonly int Quality = -1;
    public readonly int AstcBlockSize;
    private readonly (int Width, int Height)[] levels;

    public static int FirstLevelOffset(byte[] file)
    {
        return file.Length >= MipHeaderSize && file[0] == 'Q' && file[1] == 'T' && file[2] == 'X' && file[3] == 'M' ? MipHeaderSize + file[4] * 4 : 0;
    }

    public QtxHeader(byte[] file)
    {
        if (file.Length >= MipHeaderSize && file[0] == 'Q' && file[1] == 'T' && file[2] == 'X' && file[3] == 'M')
        {
            MipCount = file[4];
            if (MipCount < 1 || MipCount > MipLevelsMax || file.Length < MipHeaderSize + MipCount * 4)
                throw new InvalidDataException("QTX mip container is corrupt");
            Quality = file[QualityOffset] - 1;
            levels = new (int, int)[MipCount];
            long offset = MipHeaderSize + MipCount * 4;
            for (var level = 0; level < MipCount; level++)
            {
                var length = BitConverter.ToUInt32(file, MipHeaderSize + level * 4);
                if (offset + length > file.Length)
                    throw new InvalidDataException("QTX mip container is truncated");
                var hasAlpha = ReadLevel(file, (int)offset, out levels[level].Width, out levels[level].Height);
                if (level == 0)
                    HasAlpha = hasAlpha;
                else if (levels[level].Width != Math.Max(1, levels[0].Width >> level) || levels[level].Height != Math.Max(1, levels[0].Height >> level))
                    throw new InvalidDataException("QTX mip level sizes do not halve");
                offset += length;
            }
        }
        else
        {
            MipCount = 1;
            levels = new (int, int)[1];
            HasAlpha = ReadLevel(file, 0, out levels[0].Width, out levels[0].Height);
        }
        Width = levels[0].Width;
        Height = levels[0].Height;
        BlocksWide = (Width + 3) >> 2;
        BlocksHigh = (Height + 3) >> 2;
        var astcBlock = file[FirstLevelOffset(file) + AstcBlockOffset];
        AstcBlockSize = astcBlock == 6 || astcBlock == 8 ? astcBlock : 4;
    }

    private static bool ReadLevel(byte[] file, int offset, out int width, out int height)
    {
        if (file.Length - offset < Size || file[offset] != 'Q' || file[offset + 1] != 'T' || file[offset + 2] != 'X' || file[offset + 3] != ' ')
            throw new InvalidDataException("Not an QTX file");
        if (file[offset + 8] != FormatVersion)
            throw new InvalidDataException($"QTX file has format version {file[offset + 8]}, this decoder reads version {FormatVersion}; re-encode the image");
        width = BitConverter.ToUInt16(file, offset + 4);
        height = BitConverter.ToUInt16(file, offset + 6);
        if (width == 0 || height == 0)
            throw new InvalidDataException("QTX file has an empty image");
        return file[offset + 9] != 0;
    }

    public int OutputLength(QtxTarget target)
    {
        var total = 0;
        for (var level = 0; level < MipCount; level++)
            total = checked(total + LevelOutputLength(target, level));
        return total;
    }

    public int OutputLengthWithBuiltMips(QtxTarget target)
    {
        if (MipCount != 1)
            throw new InvalidOperationException("QTX builds mip levels at load only from a file with a single level");
        var total = 0;
        for (int width = Width, height = Height; ; width = Math.Max(1, width >> 1), height = Math.Max(1, height >> 1))
        {
            total = checked(total + QtxDecoder.LevelLength(target, width, height));
            if (width == 1 && height == 1)
                return total;
        }
    }

    public int LevelOutputLength(QtxTarget target, int level)
    {
        var (width, height) = levels[level];
        return QtxDecoder.LevelLength(target, width, height);
    }
}

public static class QtxDecoder
{
    private static readonly int[] BlockBytes = { 64, 8, 16, 8, 16, 16, 16, 16, 16, 8, 16 };
    private static readonly int[] BlockSides = { 1, 4, 4, 4, 4, 4, 4, 6, 8, 4, 4 };

    public static bool CanTranscode(QtxHeader header, QtxTarget target)
    {
        return true;
    }

    public static int BytesPerBlock(QtxTarget target)
    {
        return BlockBytes[(int)target];
    }

    public static int BlockSide(QtxTarget target)
    {
        return BlockSides[(int)target];
    }

    public static int LevelLength(QtxTarget target, int width, int height)
    {
        if (target == QtxTarget.Rgba32)
            return checked(width * height * 4);
        var side = BlockSide(target);
        return checked((width + side - 1) / side * ((height + side - 1) / side) * BytesPerBlock(target));
    }

    public static byte[] Decode(byte[] file, QtxTarget target)
    {
        var header = new QtxHeader(file);
        var output = new byte[header.OutputLength(target)];
        Decode(file, header, target, output, false);
        return output;
    }

    public static byte[] DecodeWithBuiltMips(byte[] file, QtxTarget target)
    {
        var header = new QtxHeader(file);
        var output = new byte[header.OutputLengthWithBuiltMips(target)];
        Decode(file, header, target, output, true);
        return output;
    }

    public static void Decode(byte[] file, QtxHeader header, QtxTarget target, Span<byte> destination, bool buildMips)
    {
        var length = buildMips ? header.OutputLengthWithBuiltMips(target) : header.OutputLength(target);
        if (destination.Length < length)
            throw new ArgumentException($"QTX {target} output needs {length} bytes, destination has {destination.Length}");
#if !UNITY_WEBGL || UNITY_EDITOR
        if (QtxNativeDecoder.TryDecode(file, target, destination, buildMips))
            return;
        throw new InvalidDataException($"QTX native decoder failed for {target}: the decoder library is missing or the file is corrupt");
#else
        throw new NotSupportedException("QTX decodes in browser workers on WebGL, use QtxImage.TranscodeIntoTextureAsync");
#endif
    }
}
