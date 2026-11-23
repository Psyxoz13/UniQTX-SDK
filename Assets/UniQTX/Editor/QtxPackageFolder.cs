using System;
using UnityEditor;

public static class QtxPackageFolder
{
    private const string ManifestGuid = "23dcadbe9814422493eb7f3700c265b1";

    public static string GetAssetPath(string relativePath)
    {
        var manifestPath = AssetDatabase.GUIDToAssetPath(ManifestGuid);

        if (string.IsNullOrEmpty(manifestPath))
        {
            throw new InvalidOperationException("UniQTX: package.json is missing, so the plugin folder can't be found. Reinstall UniQTX with its .meta files.");
        }

        var folder = manifestPath.Substring(0, manifestPath.LastIndexOf('/') + 1);

        return folder + relativePath;
    }

    public static string GetFilePath(string relativePath)
    {
        return FileUtil.GetPhysicalPath(GetAssetPath(relativePath));
    }
}
