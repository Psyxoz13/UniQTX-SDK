using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

public enum QtxMipGeneration
{
    AtLoad,
    AtImport,
}

public enum QtxAstcBlockSize
{
    Automatic,
    [InspectorName("4x4")] Block4x4,
    [InspectorName("6x6")] Block6x6,
    [InspectorName("8x8")] Block8x8,
}

[ScriptedImporter(15, new[] { "qtx" }, overrideExts: new[] { "png", "jpg", "jpeg" })]
public class QtxImageImporter : SpriteTextureImporter
{
    private const int Texture2DClassId = 28;
    private const int SpriteClassId = 213;
    private const long TextureImporterTextureFileId = 2800000;
    private const long TextureImporterSpriteFileId = 21300000;

    public QtxQualityMode qualityMode = QtxQualityMode.Automatic;
    [Range(0, 100)] public int quality = 80;
    public bool generateMipMaps;
    public QtxMipGeneration mipMapGeneration = QtxMipGeneration.AtLoad;
    public float mipMapBias;
    public QtxAstcBlockSize astcBlockSize = QtxAstcBlockSize.Automatic;
    public QtxPlatformSettings defaultPlatform = new() { platform = BuildTargetGroup.Unknown };
    public QtxPlatformSettings[] platformOverrides =
    {
        new() { platform = BuildTargetGroup.Standalone },
        new() { platform = BuildTargetGroup.WebGL },
        new() { platform = BuildTargetGroup.Android },
        new() { platform = BuildTargetGroup.iOS },
    };

    public bool EncodesFromImage => !assetPath.EndsWith(".qtx", StringComparison.OrdinalIgnoreCase);

    public QtxPlatformSettings ResolvePlatformSettings(BuildTarget target)
    {
        var group = BuildPipeline.GetBuildTargetGroup(target);
        return platformOverrides.FirstOrDefault(settings => settings.overridden && settings.platform == group) ?? defaultPlatform;
    }

    public static TextureFormat AutomaticFormatFor(BuildTarget target, bool opaque)
    {
        return target switch
        {
            BuildTarget.Android => opaque ? TextureFormat.ETC2_RGB : TextureFormat.ETC2_RGBA8,
            BuildTarget.iOS => TextureFormat.ASTC_4x4,
            BuildTarget.WebGL when EditorUserBuildSettings.webGLBuildSubtarget == WebGLTextureSubtarget.ETC2 => opaque ? TextureFormat.ETC2_RGB : TextureFormat.ETC2_RGBA8,
            BuildTarget.WebGL when EditorUserBuildSettings.webGLBuildSubtarget == WebGLTextureSubtarget.ASTC => TextureFormat.ASTC_4x4,
            _ => opaque ? TextureFormat.DXT1 : TextureFormat.DXT5,
        };
    }

    public override void OnImportAsset(AssetImportContext ctx)
    {
        ctx.DependsOnCustomDependency(QtxBundleMode.DependencyName);

        var platform = ResolvePlatformSettings(ctx.selectedBuildTarget);
        var sourceBytes = File.ReadAllBytes(ctx.assetPath);
        var storeLevels = generateMipMaps && mipMapGeneration == QtxMipGeneration.AtImport;
        var (bytes, sourceSize, encodeCachePath, sourcePixels, encodedQuality) = EncodesFromImage ? EncodedFromImage(ctx, sourceBytes, platform.maxTextureSize, storeLevels) : LevelsOfQtx(ctx, sourceBytes, platform.maxTextureSize);
        var astcChoice = AstcBlockChoiceFor(ctx, bytes, encodeCachePath, sourcePixels, encodedQuality);
        bytes[QtxHeader.FirstLevelOffset(bytes) + QtxHeader.AstcBlockOffset] = (byte)astcChoice.BlockSize;
        var header = new QtxHeader(bytes);
        var mipsAtLoad = generateMipMaps && header.MipCount == 1;
        int width = header.Width, height = header.Height;
        var opaque = !header.HasAlpha;
        var transcodeAfterLoad = platform.transcodeTime == QtxTranscodeTime.AfterLoad;
        var keepForBundle = transcodeAfterLoad && QtxBundleMode.IsActive;
        var blockFormatAllowed = width % 4 == 0 && height % 4 == 0;
        var compressAfterLoad = platform.compression != TextureImporterCompression.Uncompressed && blockFormatAllowed;

        var wantedFormat = !transcodeAfterLoad
            ? platform.ChosenTextureFormat ?? AutomaticFormatFor(ctx.selectedBuildTarget, opaque)
            : platform.compression != TextureImporterCompression.Uncompressed ? AutomaticFormatFor(ctx.selectedBuildTarget, opaque) : TextureFormat.RGBA32;
        if (wantedFormat == TextureFormat.ASTC_4x4 && (transcodeAfterLoad || platform.ChosenTextureFormat == null))
            wantedFormat = QtxTargetFormat.AstcFormatFor(header.AstcBlockSize);
        if (!blockFormatAllowed && wantedFormat != TextureFormat.RGBA32)
            ctx.LogImportWarning($"{ctx.assetPath}: {width}x{height} is not a multiple of 4, stays RGBA32");

        var targetFormat = QtxTargetFormat.TranscodableOrUncompressed(blockFormatAllowed ? wantedFormat : TextureFormat.RGBA32, header);
        var analysis = AnalysisOf(ctx, bytes, new Vector2Int(width, height), sourceSize, () => FirstLevelPixels(bytes));
        var texture = keepForBundle ? new Texture2D(width, height, TextureFormat.RGBA32, false) : Transcoded(bytes, header, targetFormat, mipsAtLoad);
        var measuredQuality = sourcePixels == null ? null : MeasuredQuality(encodeCachePath, sourcePixels, bytes, header, targetFormat, keepForBundle ? null : texture);
        ApplyTextureSettings(texture, ctx.assetPath);
        texture.mipMapBias = generateMipMaps ? mipMapBias : 0;
        var sprites = CreateSprites(ctx.assetPath, texture, analysis, sourceSize);
        if (keepForBundle)
        {
            texture.Reinitialize(1, 1);
            texture.Apply();
        }

        if (EncodesFromImage)
            ReuseTextureImporterFileIds();
        AddTextureAndSprites(ctx, texture, analysis, sprites);
        if (measuredQuality != null)
        {
            measuredQuality.hideFlags = HideFlags.HideInHierarchy;
            ctx.AddObjectToAsset("measured quality", measuredQuality);
        }
        astcChoice.hideFlags = HideFlags.HideInHierarchy;
        ctx.AddObjectToAsset("astc block", astcChoice);

        if (EncodesFromImage && qualityMode == QtxQualityMode.Automatic)
        {
            var qualityChoice = QtxQualityChoice.Create(encodedQuality);
            qualityChoice.hideFlags = HideFlags.HideInHierarchy;
            ctx.AddObjectToAsset("quality", qualityChoice);
        }

        if (transcodeAfterLoad)
        {
            var image = QtxImage.Create(bytes, texture, width, height, header.MipCount, compressAfterLoad, mipsAtLoad);
            image.name = texture.name;
            ctx.AddObjectToAsset("image", image);
        }
    }

    protected override Texture2D DecodeSourceImage()
    {
        var bytes = File.ReadAllBytes(assetPath);
        if (EncodesFromImage)
        {
            var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            image.LoadImage(bytes);
            return image;
        }

        var header = new QtxHeader(bytes);
        var texture = new Texture2D(header.Width, header.Height, TextureFormat.RGBA32, false);
        texture.LoadRawTextureData(FirstLevelPixels(bytes));
        texture.Apply();
        return texture;
    }

    private static byte[] FirstLevelPixels(byte[] qtx)
    {
        var header = new QtxHeader(qtx);
        var pixels = QtxDecoder.Decode(qtx, QtxTarget.Rgba32);
        return pixels.AsSpan(0, header.Width * header.Height * 4).ToArray();
    }

    private static Texture2D Transcoded(byte[] qtx, QtxHeader header, QtxTargetFormat target, bool mipsAtLoad)
    {
        var texture = mipsAtLoad ? new Texture2D(header.Width, header.Height, target.TextureFormat, true, false) : new Texture2D(header.Width, header.Height, target.TextureFormat, header.MipCount, false);
        texture.LoadRawTextureData(mipsAtLoad ? QtxDecoder.DecodeWithBuiltMips(qtx, target.HighQuality.Target) : QtxDecoder.Decode(qtx, target.HighQuality.Target));
        texture.Apply(false, false);
        return texture;
    }

    private static QtxMeasuredQuality MeasuredQuality(string encodeCachePath, Func<Color32[]> sourcePixels, byte[] qtx, QtxHeader header, QtxTargetFormat target, Texture2D transcoded)
    {
        var texture = transcoded ?? Transcoded(qtx, header, target, false);
        try
        {
            var cachePath = QtxMeasuredQualityCache.PathOf(encodeCachePath, texture.GetRawTextureData());
            return QtxMeasuredQualityCache.TryRead(cachePath)
                ?? QtxMeasuredQualityCache.Write(cachePath, QtxMeasuredQuality.Measure(sourcePixels(), texture.GetPixels32(0), header.Width, header.Height, header.HasAlpha, texture.format));
        }
        finally
        {
            if (texture != transcoded)
                DestroyImmediate(texture);
        }
    }

    private QtxAstcBlockChoice AstcBlockChoiceFor(AssetImportContext ctx, byte[] qtx, string encodeCachePath, Func<Color32[]> sourcePixels, int encodedQuality)
    {
        var isForced = astcBlockSize != QtxAstcBlockSize.Automatic;
        var forcedSize = isForced ? QtxAstcBlockChoice.BlockSizes[(int)astcBlockSize - 1] : 0;

        if (sourcePixels == null)
        {
            return QtxAstcBlockChoice.Fixed(isForced ? forcedSize : new QtxHeader(qtx).AstcBlockSize);
        }

        var cachePath = QtxAstcBlockChoiceCache.PathOf(encodeCachePath);
        var choice = QtxAstcBlockChoiceCache.TryRead(cachePath) ?? QtxAstcBlockChoiceCache.Write(cachePath, MeasureAstcChoiceOnSample(sourcePixels(), new QtxHeader(qtx), encodedQuality));

        if (isForced)
        {
            return choice.ForceBlockSize(forcedSize);
        }

        ctx.DependsOnCustomDependency(QtxProjectSettings.AstcLimitsDependency);
        var settings = QtxProjectSettings.instance;

        return choice.ChooseBlockSize(settings.maxPsnrLoss, settings.maxSsimLoss);
    }

    private static QtxAstcBlockChoice MeasureAstcChoiceOnSample(Color32[] pixels, QtxHeader header, int encodedQuality)
    {
        var sample = QtxImageSample.Create(pixels, header.Width, header.Height, out int sampleWidth, out int sampleHeight);
        var sampleQtx = QtxNativeEncoder.Encode(MemoryMarshal.AsBytes(sample.AsSpan()).ToArray(), sampleWidth, sampleHeight, encodedQuality);
        var sampleHeader = new QtxHeader(sampleQtx);
        var candidates = new QtxMeasuredQuality[QtxAstcBlockChoice.BlockSizes.Length];

        try
        {
            for (var index = 0; index < candidates.Length; index++)
            {
                var target = QtxTargetFormat.Of(QtxTargetFormat.AstcFormatFor(QtxAstcBlockChoice.BlockSizes[index]));
                var texture = Transcoded(sampleQtx, sampleHeader, target, false);

                try
                {
                    candidates[index] = QtxMeasuredQuality.Measure(sample, texture.GetPixels32(0), sampleWidth, sampleHeight, sampleHeader.HasAlpha, texture.format);
                }
                finally
                {
                    DestroyImmediate(texture);
                }
            }

            return QtxAstcBlockChoice.Measured(candidates);
        }
        finally
        {
            for (var index = 0; index < candidates.Length; index++)
            {
                if (candidates[index] != null)
                {
                    DestroyImmediate(candidates[index]);
                }
            }
        }
    }

    private (byte[] qtx, Vector2Int sourceSize, string encodeCachePath, Func<Color32[]> sourcePixels, int encodedQuality) LevelsOfQtx(AssetImportContext ctx, byte[] qtx, int maxSize)
    {
        var header = new QtxHeader(qtx);
        if (Math.Max(header.Width, header.Height) > maxSize)
            ctx.LogImportWarning($"{ctx.assetPath}: {header.Width}x{header.Height} is larger than Max Size {maxSize}, an QTX file is used at its stored size");
        return (qtx, new Vector2Int(header.Width, header.Height), null, null, header.Quality);
    }

    private (byte[] qtx, Vector2Int sourceSize, string encodeCachePath, Func<Color32[]> sourcePixels, int encodedQuality) EncodedFromImage(AssetImportContext ctx, byte[] imageBytes, int maxSize, bool storeLevels)
    {
        var (width, height) = ImageHeader.SizeOf(imageBytes) ?? throw new InvalidOperationException($"{ctx.assetPath} could not be read as PNG or JPG");
        var sizes = Enumerable.Range(0, Convert.ToString(Math.Max(width, height), 2).Length)
            .Select(level => (Math.Max(1, width >> level), Math.Max(1, height >> level)))
            .ToArray();
        var firstLevel = LevelsToKeep(ctx, sizes, maxSize).firstLevel;
        var levelCount = storeLevels ? Math.Min(QtxHeader.MipLevelsMax, sizes.Length - firstLevel) : 1;
        var (levelWidth, levelHeight) = sizes[firstLevel];
        var assetPath = ctx.assetPath;
        var sourceLevels = new Lazy<Color32[][]>(() => SourceLevels(assetPath, imageBytes, firstLevel, levelCount));

        var encodedQuality = qualityMode == QtxQualityMode.Custom
            ? quality
            : GetAutomaticQuality(ctx, imageBytes, firstLevel, sourceLevels, levelWidth, levelHeight);
        var cachePath = QtxEncodeCache.PathOf(imageBytes, encodedQuality, firstLevel, levelCount, false);
        var qtx = QtxEncodeCache.TryRead(cachePath) ?? QtxEncodeCache.Write(cachePath, Encoded(sourceLevels.Value, levelWidth, levelHeight, encodedQuality));

        return (qtx, new Vector2Int(width, height), cachePath, () => sourceLevels.Value[0], encodedQuality);
    }

    private static int GetAutomaticQuality(AssetImportContext ctx, byte[] imageBytes, int firstLevel, Lazy<Color32[][]> sourceLevels, int width, int height)
    {
        ctx.DependsOnCustomDependency(QtxProjectSettings.QualityLimitsDependency);

        var settings = QtxProjectSettings.instance;
        var choicePath = QtxQualityChoiceCache.GetPath(imageBytes, firstLevel, settings.maxQualityPsnrLoss, settings.maxQualitySsimLoss);

        if (QtxQualityChoiceCache.TryRead(choicePath, out int cachedQuality))
        {
            return cachedQuality;
        }

        var sample = QtxImageSample.Create(sourceLevels.Value[0], width, height, out int sampleWidth, out int sampleHeight);
        var chosenQuality = QtxQualityPrediction.PredictQuality(sample, sampleWidth, sampleHeight, settings.maxQualityPsnrLoss, settings.maxQualitySsimLoss);

        QtxQualityChoiceCache.Write(choicePath, chosenQuality);

        return chosenQuality;
    }

    private static Color32[][] SourceLevels(string assetPath, byte[] imageBytes, int firstLevel, int levelCount)
    {
        var withMips = firstLevel > 0 || levelCount > 1;
        var image = new Texture2D(2, 2, TextureFormat.RGBA32, withMips, false);
        try
        {
            if (!image.LoadImage(imageBytes))
                throw new InvalidOperationException($"{assetPath} could not be read as PNG or JPG");
            image.Apply(withMips, false);
            return Enumerable.Range(firstLevel, levelCount).Select(level => image.GetPixels32(level)).ToArray();
        }
        finally
        {
            DestroyImmediate(image);
        }
    }

    private static byte[] Encoded(Color32[][] levels, int width, int height, int encodedQuality)
    {
        var levelBytes = levels.Select(level => MemoryMarshal.AsBytes(level.AsSpan()).ToArray()).ToArray();
        return levelBytes.Length > 1
            ? QtxNativeEncoder.EncodeLevels(levelBytes, width, height, encodedQuality)
            : QtxNativeEncoder.Encode(levelBytes[0], width, height, encodedQuality);
    }

    private (int firstLevel, int keptLevels) LevelsToKeep(AssetImportContext ctx, (int width, int height)[] sizes, int maxSize)
    {
        var firstLevel = Array.FindIndex(sizes, size => Math.Max(size.width, size.height) <= maxSize);
        if (firstLevel < 0)
        {
            firstLevel = sizes.Length - 1;
            ctx.LogImportWarning($"{ctx.assetPath}: no mip level fits Max Size {maxSize}, using {sizes[firstLevel].width}x{sizes[firstLevel].height}. Encode the QTX with mipmaps to allow smaller sizes");
        }

        return (firstLevel, generateMipMaps ? sizes.Length - firstLevel : 1);
    }

    private void ReuseTextureImporterFileIds()
    {
        var serializedImporter = new SerializedObject(this);
        var spriteFileIds = spriteMode == SpriteImportMode.Multiple
            ? sheetSprites.Select(entry => (entry.FileId, entry.spriteID.ToString())).ToArray()
            : new[] { (TextureImporterSpriteFileId, SingleSpriteIdentifier) };
        var table = serializedImporter.FindProperty("m_InternalIDToNameTable");
        table.arraySize = 1 + spriteFileIds.Length;
        MapFileId(table.GetArrayElementAtIndex(0), Texture2DClassId, TextureImporterTextureFileId, TextureIdentifier);
        for (var i = 0; i < spriteFileIds.Length; i++)
            MapFileId(table.GetArrayElementAtIndex(i + 1), SpriteClassId, spriteFileIds[i].Item1, spriteFileIds[i].Item2);
        serializedImporter.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void MapFileId(SerializedProperty entry, int classId, long fileId, string identifier)
    {
        entry.FindPropertyRelative("first.first").intValue = classId;
        entry.FindPropertyRelative("first.second").longValue = fileId;
        entry.FindPropertyRelative("second").stringValue = identifier;
    }
}
