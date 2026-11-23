using System;
using UnityEngine;

public readonly struct QtxTargetFormat
{
    public readonly QtxTarget Target;
    public readonly TextureFormat TextureFormat;

    private QtxTargetFormat(QtxTarget target, TextureFormat textureFormat)
    {
        Target = target;
        TextureFormat = textureFormat;
    }

    public QtxTargetFormat HighQuality => Target switch
    {
        QtxTarget.Etc1 => new QtxTargetFormat(QtxTarget.Etc1HighQuality, TextureFormat),
        QtxTarget.Etc2Rgba => new QtxTargetFormat(QtxTarget.Etc2RgbaHighQuality, TextureFormat),
        _ => this,
    };

    public static QtxTargetFormat Of(TextureFormat format)
    {
        return format switch
        {
            TextureFormat.DXT1 => new QtxTargetFormat(QtxTarget.Bc1, format),
            TextureFormat.DXT5 => new QtxTargetFormat(QtxTarget.Bc3, format),
            TextureFormat.ETC_RGB4 or TextureFormat.ETC2_RGB => new QtxTargetFormat(QtxTarget.Etc1, format),
            TextureFormat.ETC2_RGBA8 => new QtxTargetFormat(QtxTarget.Etc2Rgba, format),
            TextureFormat.ASTC_4x4 => new QtxTargetFormat(QtxTarget.Astc4x4, format),
            TextureFormat.ASTC_6x6 => new QtxTargetFormat(QtxTarget.Astc6x6, format),
            TextureFormat.ASTC_8x8 => new QtxTargetFormat(QtxTarget.Astc8x8, format),
            TextureFormat.BC7 => new QtxTargetFormat(QtxTarget.Bc7, format),
            _ => new QtxTargetFormat(QtxTarget.Rgba32, TextureFormat.RGBA32),
        };
    }

    public static TextureFormat AstcFormatFor(int blockSize)
    {
        return blockSize switch
        {
            6 => TextureFormat.ASTC_6x6,
            8 => TextureFormat.ASTC_8x8,
            _ => TextureFormat.ASTC_4x4,
        };
    }

    public static QtxTargetFormat SupportedBy(Func<TextureFormat, bool> supports, QtxHeader header, TextureFormat? preferred = null)
    {
        var opaque = !header.HasAlpha;
        var astc = AstcFormatFor(header.AstcBlockSize);
        var candidates = new[] { opaque ? TextureFormat.DXT1 : TextureFormat.DXT5, supports(astc) ? astc : TextureFormat.ASTC_4x4, opaque ? TextureFormat.ETC2_RGB : TextureFormat.ETC2_RGBA8, TextureFormat.BC7 };
        if (preferred is { } preferredFormat && supports(preferredFormat) && QtxDecoder.CanTranscode(header, Of(preferredFormat).Target))
            return Of(preferredFormat);
        foreach (var format in candidates)
            if (supports(format) && QtxDecoder.CanTranscode(header, Of(format).Target))
                return Of(format);
        return Of(TextureFormat.RGBA32);
    }

    public static QtxTargetFormat TranscodableOrUncompressed(TextureFormat wanted, QtxHeader header)
    {
        var target = Of(wanted);
        if (QtxDecoder.CanTranscode(header, target.Target))
            return target;
        if (wanted == TextureFormat.BC7 && QtxDecoder.CanTranscode(header, QtxTarget.Bc3))
            return Of(header.HasAlpha ? TextureFormat.DXT5 : TextureFormat.DXT1);
        return Of(TextureFormat.RGBA32);
    }
}
