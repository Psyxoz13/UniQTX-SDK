using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditor.U2D;
using UnityEditor.U2D.Sprites;
using UnityEngine;

public abstract class SpriteTextureImporter : ScriptedImporter, ISpriteEditorDataProvider, ITextureDataProvider, ISpriteOutlineDataProvider, ISpritePhysicsOutlineDataProvider
{
    protected const string TextureIdentifier = "texture";
    protected const string SingleSpriteIdentifier = "sprite";
    private const int GeneratorMaxTextureSize = 16384;

    [Serializable]
    private struct SpriteGeometrySettings
    {
        public TextureImporterType textureType;
        public SpriteImportMode spriteMode;
        public SpriteMeshType meshType;
        public int extrudeEdges;
        public bool generatePhysicsShape;
        public SpriteSheetEntry singleSprite;
        public List<SpriteSheetEntry> sheetSprites;
    }

    public TextureImporterType textureType = TextureImporterType.Sprite;
    public SpriteImportMode spriteMode = SpriteImportMode.Single;
    public float pixelsPerUnit = 100f;
    public SpriteMeshType meshType = SpriteMeshType.Tight;
    [Range(0, 32)] public int extrudeEdges = 1;
    public bool generatePhysicsShape = true;
    public TextureWrapMode wrapMode = TextureWrapMode.Clamp;
    public FilterMode filterMode = FilterMode.Bilinear;
    public SpriteSheetEntry singleSprite = new() { pivot = new Vector2(0.5f, 0.5f) };
    public List<SpriteSheetEntry> sheetSprites = new();

    [NonSerialized] private List<SpriteSheetEntry> editedSprites;
    [NonSerialized] private Texture2D decodedSourceImage;
    [NonSerialized] private DateTime decodedSourceWriteTime;

    private bool ProducesSprites => textureType == TextureImporterType.Sprite;

    protected abstract Texture2D DecodeSourceImage();

    protected ImageAnalysis AnalysisOf(AssetImportContext ctx, byte[] source, Vector2Int size, Vector2Int sourceSize, Func<byte[]> decodeRgba)
    {
        var geometrySettings = new SpriteGeometrySettings
        {
            textureType = textureType,
            spriteMode = spriteMode,
            meshType = meshType,
            extrudeEdges = extrudeEdges,
            generatePhysicsShape = generatePhysicsShape,
            singleSprite = singleSprite,
            sheetSprites = sheetSprites,
        };
        var cachePath = ImageAnalysisCache.PathOf(source, size, JsonUtility.ToJson(geometrySettings));
        return ImageAnalysisCache.TryRead(cachePath) ?? ImageAnalysisCache.Write(cachePath, Analyze(ctx, decodeRgba(), size, sourceSize));
    }

    protected void ApplyTextureSettings(Texture2D texture, string path)
    {
        texture.name = Path.GetFileNameWithoutExtension(path);
        texture.wrapMode = wrapMode;
        texture.filterMode = filterMode;
    }

    protected Sprite[] CreateSprites(string path, Texture2D texture, ImageAnalysis analysis, Vector2Int sourceSize)
    {
        if (!ProducesSprites)
            return Array.Empty<Sprite>();

        var size = new Vector2Int(texture.width, texture.height);
        var scale = new Vector2((float)size.x / sourceSize.x, (float)size.y / sourceSize.y);
        return SpriteEntriesOf(path, sourceSize)
            .Select((entry, index) => CreateSprite(texture, ImportDataOf(entry, scale, size), pixelsPerUnit * scale.x, analysis.Sprites[index]))
            .ToArray();
    }

    protected void AddTextureAndSprites(AssetImportContext ctx, Texture2D texture, ImageAnalysis analysis, Sprite[] sprites)
    {
        ctx.AddObjectToAsset(TextureIdentifier, texture, analysis.CreateThumbnail());
        foreach (var sprite in sprites)
            ctx.AddObjectToAsset(spriteMode == SpriteImportMode.Multiple ? sprite.GetSpriteID().ToString() : SingleSpriteIdentifier, sprite);
        ctx.SetMainObject(texture);
    }

    private Sprite CreateSprite(Texture2D texture, SpriteImportData data, float spritePixelsPerUnit, SpriteGeometry geometry)
    {
        var sprite = Sprite.Create(texture, data.rect, data.pivot, spritePixelsPerUnit, (uint)extrudeEdges, SpriteMeshType.FullRect, data.border, false);
        sprite.name = data.name;
        sprite.SetSpriteID(new GUID(data.spriteID));
        geometry.ApplyTo(sprite);
        return sprite;
    }

    private ImageAnalysis Analyze(AssetImportContext ctx, byte[] rgba, Vector2Int size, Vector2Int sourceSize)
    {
        var scale = new Vector2((float)size.x / sourceSize.x, (float)size.y / sourceSize.y);
        var entries = ProducesSprites ? SpriteEntriesOf(ctx.assetPath, sourceSize) : Array.Empty<SpriteSheetEntry>();
        var settings = new TextureGenerationSettings(ProducesSprites ? TextureImporterType.Sprite : TextureImporterType.Default)
        {
            assetPath = ctx.assetPath,
            enablePostProcessor = false,
            sourceTextureInformation = new SourceTextureInformation { width = size.x, height = size.y, containsAlpha = true, hdr = false },
            platformSettings = new TextureImporterPlatformSettings
            {
                name = "DefaultTexturePlatform",
                format = TextureImporterFormat.RGBA32,
                textureCompression = TextureImporterCompression.Uncompressed,
                maxTextureSize = GeneratorMaxTextureSize,
            },
            spriteImportData = entries.Select(entry => ImportDataOf(entry, scale, size)).ToArray(),
        };
        var importerSettings = settings.textureImporterSettings;
        importerSettings.mipmapEnabled = false;
        importerSettings.npotScale = TextureImporterNPOTScale.None;
        importerSettings.readable = true;
        importerSettings.alphaIsTransparency = false;
        importerSettings.spriteMode = ProducesSprites ? (int)spriteMode : (int)SpriteImportMode.None;
        importerSettings.spritePixelsPerUnit = pixelsPerUnit * scale.x;
        importerSettings.spriteMeshType = spriteMode == SpriteImportMode.Polygon ? SpriteMeshType.Tight : meshType;
        importerSettings.spriteExtrude = (uint)extrudeEdges;
        importerSettings.spriteGenerateFallbackPhysicsShape = generatePhysicsShape;

        using var pixels = new NativeArray<byte>(rgba, Allocator.Temp);
        var output = TextureGenerator.GenerateTexture(settings, pixels.Reinterpret<Color32>(1));
        LogGenerationWarnings(ctx, output);
        if (output.texture == null)
            throw new InvalidOperationException($"{ctx.assetPath}: texture generation failed");

        var sprites = output.sprites ?? Array.Empty<Sprite>();
        OverridePhysicsShapes(sprites, entries, scale);
        var geometries = entries.Select(entry => SpriteGeometry.Of(sprites.First(sprite => sprite.GetSpriteID() == entry.spriteID))).ToArray();
        var analysis = new ImageAnalysis(IsOpaque(rgba), output.thumbNail, geometries);

        foreach (var sprite in sprites)
            DestroyImmediate(sprite);
        DestroyImmediate(output.thumbNail);
        DestroyImmediate(output.texture);
        return analysis;
    }

    private static bool IsOpaque(byte[] rgba)
    {
        for (var alphaIndex = 3; alphaIndex < rgba.Length; alphaIndex += 4)
            if (rgba[alphaIndex] != 255)
                return false;
        return true;
    }

    protected static void LogGenerationWarnings(AssetImportContext ctx, TextureGenerationOutput output)
    {
        if (!string.IsNullOrWhiteSpace(output.importInspectorWarnings))
            ctx.LogImportWarning($"{ctx.assetPath}: {output.importInspectorWarnings.Trim()}");
        foreach (var warning in output.importWarnings ?? Array.Empty<string>())
            ctx.LogImportWarning($"{ctx.assetPath}: {warning}");
    }

    private SpriteSheetEntry[] SpriteEntriesOf(string path, Vector2Int sourceSize)
    {
        if (spriteMode == SpriteImportMode.Multiple)
            return sheetSprites.ToArray();

        var sprite = singleSprite.Clone();
        sprite.name = Path.GetFileNameWithoutExtension(path);
        sprite.rect = new Rect(0, 0, sourceSize.x, sourceSize.y);
        sprite.spriteID = new GUID(AssetDatabase.AssetPathToGUID(path));
        return new[] { sprite };
    }

    private static SpriteImportData ImportDataOf(SpriteSheetEntry entry, Vector2 scale, Vector2Int size)
    {
        var rect = entry.rect;
        return new SpriteImportData
        {
            name = entry.name,
            rect = Rect.MinMaxRect(
                Mathf.Max(0, rect.xMin * scale.x),
                Mathf.Max(0, rect.yMin * scale.y),
                Mathf.Min(size.x, rect.xMax * scale.x),
                Mathf.Min(size.y, rect.yMax * scale.y)),
            alignment = SpriteAlignment.Custom,
            pivot = entry.NormalizedPivot(),
            border = Vector4.Scale(entry.border, new Vector4(scale.x, scale.y, scale.x, scale.y)),
            outline = ScaledOutlines(entry.outline, scale),
            tessellationDetail = entry.outlineTessellationDetail,
            spriteID = entry.spriteID.ToString(),
        };
    }

    private static List<Vector2[]> ScaledOutlines(IEnumerable<SpriteOutline> outlines, Vector2 scale)
    {
        return outlines.Select(outline => outline.points.Select(point => Vector2.Scale(point, scale)).ToArray()).ToList();
    }

    private static void OverridePhysicsShapes(Sprite[] sprites, SpriteSheetEntry[] entries, Vector2 scale)
    {
        foreach (var sprite in sprites)
        {
            var spriteId = sprite.GetSpriteID();
            var shapes = entries.First(entry => entry.spriteID == spriteId).physicsOutline.Where(shape => shape.points.Length > 2).ToArray();
            if (shapes.Length == 0)
                continue;

            var rectCenter = sprite.rect.size / 2;
            sprite.OverridePhysicsShape(ScaledOutlines(shapes, scale).Select(points => points.Select(point => point + rectCenter).ToArray()).ToList());
        }
    }

    private void OnDisable()
    {
        ReleaseSourceImage();
    }

    private Texture2D SourceImage()
    {
        var writeTime = File.GetLastWriteTimeUtc(assetPath);
        if (decodedSourceImage == null || decodedSourceWriteTime != writeTime)
        {
            ReleaseSourceImage();
            decodedSourceImage = DecodeSourceImage();
            decodedSourceImage.hideFlags = HideFlags.HideAndDontSave;
            decodedSourceWriteTime = writeTime;
        }
        return decodedSourceImage;
    }

    private void ReleaseSourceImage()
    {
        if (decodedSourceImage != null)
            DestroyImmediate(decodedSourceImage);
    }

    private List<SpriteSheetEntry> EditedSprites()
    {
        var sourceSize = new Vector2Int(SourceImage().width, SourceImage().height);
        return editedSprites ??= SpriteEntriesOf(assetPath, sourceSize).Select(entry => entry.Clone()).ToList();
    }

    private SpriteSheetEntry EditedSprite(GUID spriteId)
    {
        return EditedSprites().First(entry => entry.spriteID == spriteId);
    }

    SpriteImportMode ISpriteEditorDataProvider.spriteImportMode => ProducesSprites ? spriteMode : SpriteImportMode.None;

    float ISpriteEditorDataProvider.pixelsPerUnit => pixelsPerUnit;

    UnityEngine.Object ISpriteEditorDataProvider.targetObject => this;

    void ISpriteEditorDataProvider.InitSpriteEditorDataProvider()
    {
        editedSprites = null;
    }

    SpriteRect[] ISpriteEditorDataProvider.GetSpriteRects()
    {
        return EditedSprites().Select(entry => entry.ToSpriteRect()).ToArray();
    }

    void ISpriteEditorDataProvider.SetSpriteRects(SpriteRect[] spriteRects)
    {
        var previous = EditedSprites();
        editedSprites = spriteRects
            .Select(rect => (previous.FirstOrDefault(entry => entry.spriteID == rect.spriteID) ?? new SpriteSheetEntry()).WithRectOf(rect))
            .ToList();
    }

    void ISpriteEditorDataProvider.Apply()
    {
        var applied = EditedSprites().Select(entry => entry.Clone()).ToList();
        if (spriteMode == SpriteImportMode.Multiple)
            sheetSprites = applied;
        else
            singleSprite = applied[0];
        EditorUtility.SetDirty(this);
    }

    T ISpriteEditorDataProvider.GetDataProvider<T>()
    {
        return this as T;
    }

    bool ISpriteEditorDataProvider.HasDataProvider(Type type)
    {
        return type.IsInstanceOfType(this);
    }

    Texture2D ITextureDataProvider.texture => SourceImage();

    Texture2D ITextureDataProvider.previewTexture => SourceImage();

    void ITextureDataProvider.GetTextureActualWidthAndHeight(out int width, out int height)
    {
        width = SourceImage().width;
        height = SourceImage().height;
    }

    Texture2D ITextureDataProvider.GetReadableTexture2D()
    {
        return SourceImage();
    }

    List<Vector2[]> ISpriteOutlineDataProvider.GetOutlines(GUID guid)
    {
        return SpriteOutline.PointsOf(EditedSprite(guid).outline);
    }

    void ISpriteOutlineDataProvider.SetOutlines(GUID guid, List<Vector2[]> data)
    {
        EditedSprite(guid).outline = SpriteOutline.ListOf(data);
    }

    float ISpriteOutlineDataProvider.GetTessellationDetail(GUID guid)
    {
        return EditedSprite(guid).outlineTessellationDetail;
    }

    void ISpriteOutlineDataProvider.SetTessellationDetail(GUID guid, float value)
    {
        EditedSprite(guid).outlineTessellationDetail = value;
    }

    List<Vector2[]> ISpritePhysicsOutlineDataProvider.GetOutlines(GUID guid)
    {
        return SpriteOutline.PointsOf(EditedSprite(guid).physicsOutline);
    }

    void ISpritePhysicsOutlineDataProvider.SetOutlines(GUID guid, List<Vector2[]> data)
    {
        EditedSprite(guid).physicsOutline = SpriteOutline.ListOf(data);
    }

    float ISpritePhysicsOutlineDataProvider.GetTessellationDetail(GUID guid)
    {
        return EditedSprite(guid).physicsTessellationDetail;
    }

    void ISpritePhysicsOutlineDataProvider.SetTessellationDetail(GUID guid, float value)
    {
        EditedSprite(guid).physicsTessellationDetail = value;
    }
}
