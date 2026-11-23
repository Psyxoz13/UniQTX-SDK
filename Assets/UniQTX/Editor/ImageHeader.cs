public static class ImageHeader
{
    private const int PngWidthOffset = 16;
    private const int PngHeightOffset = 20;

    public static (int width, int height)? SizeOf(byte[] image)
    {
        if (image.Length >= 24 && image[0] == 0x89 && image[1] == 'P' && image[2] == 'N' && image[3] == 'G')
            return (BigEndian32(image, PngWidthOffset), BigEndian32(image, PngHeightOffset));
        if (image.Length >= 4 && image[0] == 0xFF && image[1] == 0xD8)
            return JpegSizeOf(image);
        return null;
    }

    private static (int width, int height)? JpegSizeOf(byte[] jpeg)
    {
        var offset = 2;
        while (offset + 9 < jpeg.Length)
        {
            if (jpeg[offset] != 0xFF || jpeg[offset + 1] == 0xFF)
            {
                offset++;
                continue;
            }

            var marker = jpeg[offset + 1];
            if (IsStartOfFrame(marker))
                return (BigEndian16(jpeg, offset + 7), BigEndian16(jpeg, offset + 5));
            offset += 2 + BigEndian16(jpeg, offset + 2);
        }
        return null;
    }

    private static bool IsStartOfFrame(byte marker)
    {
        return marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
    }

    private static int BigEndian16(byte[] bytes, int offset)
    {
        return bytes[offset] << 8 | bytes[offset + 1];
    }

    private static int BigEndian32(byte[] bytes, int offset)
    {
        return bytes[offset] << 24 | bytes[offset + 1] << 16 | bytes[offset + 2] << 8 | bytes[offset + 3];
    }
}
