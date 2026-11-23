using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

public static class QtxLibraryVersion
{
    private static readonly Dictionary<string, (long length, DateTime written, string version)> Known = new();

    public static string Decoder => Of(DecoderPath);

    private static string DecoderPath
    {
        get
        {
#if UNITY_EDITOR_OSX
            return QtxPackageFolder.GetFilePath("Plugins/macOS/libqtx_decoder.dylib");
#elif UNITY_EDITOR_LINUX
            return QtxPackageFolder.GetFilePath("Plugins/Linux/x86_64/libqtx_decoder.so");
#else
            return QtxPackageFolder.GetFilePath("Plugins/x86_64/qtx_decoder.dll");
#endif
        }
    }

    public static string Of(string path)
    {
        if (!File.Exists(path))
            return "Unity";
        var info = new FileInfo(path);
        if (Known.TryGetValue(path, out var known) && known.length == info.Length && known.written == info.LastWriteTimeUtc)
            return known.version;
        using var hash = SHA256.Create();
        var version = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path)));
        Known[path] = (info.Length, info.LastWriteTimeUtc, version);
        return version;
    }
}
