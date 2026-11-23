using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

[Serializable]
public class PortablePlatformSettings
{
    public string name;
    public bool overridden;
    public int maxTextureSize;
    public bool compressed;
}

[Serializable]
public class PortableImageSettings
{
    private const string DefaultPlatformName = "DefaultTexturePlatform";
    private static readonly (BuildTargetGroup group, string name)[] PlatformNames =
    {
        (BuildTargetGroup.Standalone, "Standalone"),
        (BuildTargetGroup.WebGL, "WebGL"),
        (BuildTargetGroup.Android, "Android"),
        (BuildTargetGroup.iOS, "iPhone"),
    };

    public bool sprite;
    public SpriteImportMode spriteMode;
    public float pixelsPerUnit;
    public SpriteMeshType meshType;
    public int extrudeEdges;
    public bool generatePhysicsShape;
    public TextureWrapMode wrapMode;
    public FilterMode filterMode;
    public bool mipMaps;
    public float mipMapBias;
    public SpriteSheetEntry singleSprite;
    public List<SpriteSheetEntry> sheetSprites;
    public List<PortablePlatformSettings> platforms;

    public static PortableImageSettings Of(QtxImageImporter importer)
    {
        return new PortableImageSettings
        {
            sprite = importer.textureType == TextureImporterType.Sprite,
            spriteMode = importer.spriteMode,
            pixelsPerUnit = importer.pixelsPerUnit,
            meshType = importer.meshType,
            extrudeEdges = importer.extrudeEdges,
            generatePhysicsShape = importer.generatePhysicsShape,
            wrapMode = importer.wrapMode,
            filterMode = importer.filterMode,
            mipMaps = importer.generateMipMaps,
            mipMapBias = importer.mipMapBias,
            singleSprite = importer.singleSprite.Clone(),
            sheetSprites = importer.sheetSprites.Select(entry => entry.Clone()).ToList(),
            platforms = PlatformsOf(importer).Select(platform => new PortablePlatformSettings
            {
                name = PlatformNameOf(platform.platform),
                overridden = platform.overridden,
                maxTextureSize = platform.maxTextureSize,
                compressed = platform.compression != TextureImporterCompression.Uncompressed,
            }).ToList(),
        };
    }

    public static PortableImageSettings Of(TextureImporter importer)
    {
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        var spriteMode = (SpriteImportMode)settings.spriteMode == SpriteImportMode.None ? SpriteImportMode.Single : (SpriteImportMode)settings.spriteMode;
        var sprites = SpriteEntriesOf(importer);
        var single = new SpriteSheetEntry { alignment = (SpriteAlignment)settings.spriteAlignment, pivot = settings.spritePivot, border = settings.spriteBorder };
        return new PortableImageSettings
        {
            sprite = settings.textureType == TextureImporterType.Sprite,
            spriteMode = spriteMode,
            pixelsPerUnit = settings.spritePixelsPerUnit,
            meshType = settings.spriteMeshType,
            extrudeEdges = (int)settings.spriteExtrude,
            generatePhysicsShape = settings.spriteGenerateFallbackPhysicsShape,
            wrapMode = settings.wrapMode,
            filterMode = settings.filterMode,
            mipMaps = settings.mipmapEnabled,
            mipMapBias = settings.mipmapBias,
            singleSprite = spriteMode == SpriteImportMode.Multiple ? single : sprites.FirstOrDefault() ?? single,
            sheetSprites = spriteMode == SpriteImportMode.Multiple ? sprites : new List<SpriteSheetEntry>(),
            platforms = new[] { importer.GetDefaultPlatformTextureSettings() }
                .Concat(PlatformNames.Select(platform => importer.GetPlatformTextureSettings(platform.name)))
                .Select(platform => new PortablePlatformSettings
                {
                    name = platform.name,
                    overridden = platform.overridden,
                    maxTextureSize = platform.maxTextureSize,
                    compressed = platform.textureCompression != TextureImporterCompression.Uncompressed,
                }).ToList(),
        };
    }

    public void ApplyTo(QtxImageImporter importer)
    {
        importer.textureType = sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
        importer.spriteMode = spriteMode;
        importer.pixelsPerUnit = pixelsPerUnit;
        importer.meshType = meshType;
        importer.extrudeEdges = extrudeEdges;
        importer.generatePhysicsShape = generatePhysicsShape;
        importer.wrapMode = wrapMode;
        importer.filterMode = filterMode;
        importer.generateMipMaps = mipMaps;
        importer.mipMapBias = mipMapBias;
        importer.singleSprite = singleSprite.Clone();
        importer.sheetSprites = sheetSprites.Select(entry => entry.Clone()).ToList();
        foreach (var platform in PlatformsOf(importer))
        {
            var portable = platforms.FirstOrDefault(candidate => candidate.name == PlatformNameOf(platform.platform));
            if (portable == null)
                continue;
            platform.overridden = portable.overridden;
            platform.maxTextureSize = portable.maxTextureSize;
            platform.compression = CompressionKeeping(platform.compression, portable.compressed);
        }
    }

    public void ApplyTo(TextureImporter importer)
    {
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        if (sprite)
            settings.textureType = TextureImporterType.Sprite;
        else if (settings.textureType == TextureImporterType.Sprite)
            settings.textureType = TextureImporterType.Default;
        settings.textureShape = TextureImporterShape.Texture2D;
        settings.spriteMode = (int)spriteMode;
        settings.spritePixelsPerUnit = pixelsPerUnit;
        settings.spriteMeshType = meshType;
        settings.spriteExtrude = (uint)extrudeEdges;
        settings.spriteGenerateFallbackPhysicsShape = generatePhysicsShape;
        settings.spriteAlignment = (int)singleSprite.alignment;
        settings.spritePivot = singleSprite.pivot;
        settings.spriteBorder = singleSprite.border;
        settings.wrapMode = wrapMode;
        settings.filterMode = filterMode;
        settings.mipmapEnabled = mipMaps;
        settings.mipmapBias = mipMapBias;
        importer.SetTextureSettings(settings);

        foreach (var portable in platforms)
        {
            var platform = portable.name == DefaultPlatformName ? importer.GetDefaultPlatformTextureSettings() : importer.GetPlatformTextureSettings(portable.name);
            platform.overridden = portable.overridden;
            platform.maxTextureSize = portable.maxTextureSize;
            platform.textureCompression = CompressionKeeping(platform.textureCompression, portable.compressed);
            importer.SetPlatformTextureSettings(platform);
        }

        if (sprite)
            ApplySpritesTo(importer);
    }

    private void ApplySpritesTo(TextureImporter importer)
    {
        var entries = spriteMode == SpriteImportMode.Multiple ? sheetSprites : new List<SpriteSheetEntry> { singleSprite };
        var provider = SpriteDataProviderOf(importer);
        provider.InitSpriteEditorDataProvider();
        provider.SetSpriteRects(entries.Select(entry => entry.ToSpriteRect()).ToArray());
        var outlines = provider.GetDataProvider<ISpriteOutlineDataProvider>();
        var physicsOutlines = provider.GetDataProvider<ISpritePhysicsOutlineDataProvider>();
        foreach (var entry in entries)
        {
            outlines.SetOutlines(entry.spriteID, SpriteOutline.PointsOf(entry.outline));
            outlines.SetTessellationDetail(entry.spriteID, entry.outlineTessellationDetail);
            physicsOutlines.SetOutlines(entry.spriteID, SpriteOutline.PointsOf(entry.physicsOutline));
            physicsOutlines.SetTessellationDetail(entry.spriteID, entry.physicsTessellationDetail);
        }
        provider.Apply();

        if (spriteMode == SpriteImportMode.Multiple)
            ApplyFileIdsTo(importer, entries);
    }

    private static void ApplyFileIdsTo(TextureImporter importer, List<SpriteSheetEntry> entries)
    {
        var serializedImporter = new SerializedObject(importer);
        var sprites = serializedImporter.FindProperty("m_SpriteSheet.m_Sprites");
        for (var i = 0; i < sprites.arraySize; i++)
        {
            var sprite = sprites.GetArrayElementAtIndex(i);
            var spriteId = new GUID(sprite.FindPropertyRelative("m_SpriteID").stringValue);
            var entry = entries.FirstOrDefault(candidate => candidate.spriteID == spriteId);
            if (entry != null)
                sprite.FindPropertyRelative("m_InternalID").longValue = entry.FileId;
        }
        serializedImporter.ApplyModifiedPropertiesWithoutUndo();
    }

    private static List<SpriteSheetEntry> SpriteEntriesOf(TextureImporter importer)
    {
        var provider = SpriteDataProviderOf(importer);
        if (provider.spriteImportMode == SpriteImportMode.None)
            return new List<SpriteSheetEntry>();

        provider.InitSpriteEditorDataProvider();
        var outlines = provider.GetDataProvider<ISpriteOutlineDataProvider>();
        var physicsOutlines = provider.GetDataProvider<ISpritePhysicsOutlineDataProvider>();
        var fileIds = FileIdsOf(importer);
        return provider.GetSpriteRects().Select(rect =>
        {
            var entry = new SpriteSheetEntry().WithRectOf(rect);
            entry.outline = SpriteOutline.ListOf(outlines.GetOutlines(rect.spriteID));
            entry.outlineTessellationDetail = outlines.GetTessellationDetail(rect.spriteID);
            entry.physicsOutline = SpriteOutline.ListOf(physicsOutlines.GetOutlines(rect.spriteID));
            entry.physicsTessellationDetail = physicsOutlines.GetTessellationDetail(rect.spriteID);
            entry.internalID = fileIds.TryGetValue(rect.spriteID, out var fileId) ? fileId : 0;
            return entry;
        }).ToList();
    }

    private static Dictionary<GUID, long> FileIdsOf(TextureImporter importer)
    {
        var sprites = new SerializedObject(importer).FindProperty("m_SpriteSheet.m_Sprites");
        return Enumerable.Range(0, sprites.arraySize)
            .Select(sprites.GetArrayElementAtIndex)
            .GroupBy(sprite => new GUID(sprite.FindPropertyRelative("m_SpriteID").stringValue))
            .ToDictionary(group => group.Key, group => group.First().FindPropertyRelative("m_InternalID").longValue);
    }

    private static ISpriteEditorDataProvider SpriteDataProviderOf(TextureImporter importer)
    {
        var factories = new SpriteDataProviderFactories();
        factories.Init();
        return factories.GetSpriteEditorDataProviderFromObject(importer);
    }

    private static IEnumerable<QtxPlatformSettings> PlatformsOf(QtxImageImporter importer)
    {
        return new[] { importer.defaultPlatform }.Concat(importer.platformOverrides);
    }

    private static string PlatformNameOf(BuildTargetGroup group)
    {
        if (group == BuildTargetGroup.Unknown)
            return DefaultPlatformName;
        return PlatformNames.FirstOrDefault(platform => platform.group == group).name ?? group.ToString();
    }

    private static TextureImporterCompression CompressionKeeping(TextureImporterCompression current, bool compressed)
    {
        if (!compressed)
            return TextureImporterCompression.Uncompressed;
        return current == TextureImporterCompression.Uncompressed ? TextureImporterCompression.Compressed : current;
    }
}
