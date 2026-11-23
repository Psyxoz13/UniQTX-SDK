using System.Globalization;
using System.IO;
using UnityEngine;

public static class QtxQualityChoiceCache
{
    private const int CacheFormatVersion = 2;
    private const string Folder = "Library/QtxQualityChoiceCache";

    public static string GetPath(byte[] image, int firstLevel, float maxPsnrLoss, float maxSsimLoss)
    {
        var key = new Hash128();
        key.Append(image);
        key.Append(firstLevel);
        key.Append(maxPsnrLoss);
        key.Append(maxSsimLoss);
        key.Append(QtxNativeEncoder.Version);
        key.Append(QtxLibraryVersion.Decoder);
        key.Append(CacheFormatVersion);

        return Path.Combine(Folder, key + ".txt");
    }

    public static bool TryRead(string path, out int quality)
    {
        quality = 0;

        if (File.Exists(path) == false)
        {
            return false;
        }

        return int.TryParse(File.ReadAllText(path), NumberStyles.Integer, CultureInfo.InvariantCulture, out quality);
    }

    public static void Write(string path, int quality)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(path, quality.ToString(CultureInfo.InvariantCulture));
    }
}
