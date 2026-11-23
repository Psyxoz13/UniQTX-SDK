using System;
using UnityEditor;
using UnityEngine;

public enum QtxTranscodeTime
{
    AfterLoad,
    AtImport,
}

[Serializable]
public class QtxPlatformSettings
{
    public BuildTargetGroup platform;
    public bool overridden;
    public QtxTranscodeTime transcodeTime = QtxTranscodeTime.AfterLoad;
    public int maxTextureSize = 2048;
    public TextureImporterFormat format = TextureImporterFormat.Automatic;
    public TextureImporterCompression compression = TextureImporterCompression.Compressed;

    public TextureFormat? ChosenTextureFormat => format == TextureImporterFormat.Automatic ? null : (TextureFormat)(int)format;
}
