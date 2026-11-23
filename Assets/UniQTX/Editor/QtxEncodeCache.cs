using System.IO;
using UnityEngine;

public static class QtxEncodeCache
{
    private const int CacheFormatVersion = 8;
    private const string EncoderVersion = "QTX format 1";
    private const string Folder = "Library/QtxEncodeCache";

    public static string PathOf(byte[] image, int quality, int firstLevel, int levelCount, bool mipsAtLoad)
    {
        var key = new Hash128();
        key.Append(image);
        key.Append(quality);
        key.Append(firstLevel);
        key.Append(levelCount);
        key.Append(mipsAtLoad ? 1 : 0);
        key.Append(EncoderVersion);
        key.Append(QtxNativeEncoder.Version);
        key.Append(CacheFormatVersion);
        return Path.Combine(Folder, key + ".qtx");
    }

    public static byte[] TryRead(string path)
    {
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    public static byte[] Write(string path, byte[] qtx)
    {
        if (QtxNativeEncoder.UsedFallback)
            return qtx;
        Directory.CreateDirectory(Folder);
        var temporaryPath = path + ".tmp";
        File.WriteAllBytes(temporaryPath, qtx);
        if (File.Exists(path))
            File.Delete(path);
        File.Move(temporaryPath, path);
        return qtx;
    }
}
