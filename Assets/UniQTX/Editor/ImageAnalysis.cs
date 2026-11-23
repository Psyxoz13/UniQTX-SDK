using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public class SpriteGeometry
{
    public readonly Vector2[] Vertices;
    public readonly ushort[] Triangles;
    public readonly Vector2[][] PhysicsShapes;

    public SpriteGeometry(Vector2[] vertices, ushort[] triangles, Vector2[][] physicsShapes)
    {
        Vertices = vertices;
        Triangles = triangles;
        PhysicsShapes = physicsShapes;
    }

    public static SpriteGeometry Of(Sprite sprite)
    {
        var physicsShapes = Enumerable.Range(0, sprite.GetPhysicsShapeCount())
            .Select(index =>
            {
                var points = new List<Vector2>();
                sprite.GetPhysicsShape(index, points);
                return points.Select(point => InRectPixels(sprite, point)).ToArray();
            })
            .ToArray();
        return new SpriteGeometry(sprite.vertices.Select(vertex => InRectPixels(sprite, vertex)).ToArray(), sprite.triangles, physicsShapes);
    }

    public void ApplyTo(Sprite sprite)
    {
        sprite.OverrideGeometry(Vertices, Triangles);
        var polygons = PhysicsShapes.Where(shape => shape.Length >= 3).ToArray();
        if (polygons.Length > 0)
            sprite.OverridePhysicsShape(polygons);
    }

    private static Vector2 InRectPixels(Sprite sprite, Vector2 localPosition)
    {
        return Vector2.Min(Vector2.Max(localPosition * sprite.pixelsPerUnit + sprite.pivot, Vector2.zero), sprite.rect.size);
    }
}

public class ImageAnalysis
{
    public readonly bool Opaque;
    public readonly SpriteGeometry[] Sprites;
    private readonly int thumbnailWidth;
    private readonly int thumbnailHeight;
    private readonly byte[] thumbnailPixels;

    public ImageAnalysis(bool opaque, Texture2D thumbnail, SpriteGeometry[] sprites)
        : this(opaque, sprites, thumbnail != null ? thumbnail.width : 0, thumbnail != null ? thumbnail.height : 0, thumbnail != null ? thumbnail.GetRawTextureData() : Array.Empty<byte>())
    {
    }

    private ImageAnalysis(bool opaque, SpriteGeometry[] sprites, int thumbnailWidth, int thumbnailHeight, byte[] thumbnailPixels)
    {
        Opaque = opaque;
        Sprites = sprites;
        this.thumbnailWidth = thumbnailWidth;
        this.thumbnailHeight = thumbnailHeight;
        this.thumbnailPixels = thumbnailPixels;
    }

    public Texture2D CreateThumbnail()
    {
        if (thumbnailPixels.Length == 0)
            return null;

        var thumbnail = new Texture2D(thumbnailWidth, thumbnailHeight, TextureFormat.RGBA32, false);
        thumbnail.LoadRawTextureData(thumbnailPixels);
        thumbnail.Apply();
        return thumbnail;
    }

    public void WriteTo(BinaryWriter writer)
    {
        writer.Write(Opaque);
        writer.Write(thumbnailWidth);
        writer.Write(thumbnailHeight);
        writer.Write(thumbnailPixels.Length);
        writer.Write(thumbnailPixels);
        writer.Write(Sprites.Length);
        foreach (var sprite in Sprites)
        {
            WritePoints(writer, sprite.Vertices);
            writer.Write(sprite.Triangles.Length);
            foreach (var index in sprite.Triangles)
                writer.Write(index);
            writer.Write(sprite.PhysicsShapes.Length);
            foreach (var shape in sprite.PhysicsShapes)
                WritePoints(writer, shape);
        }
    }

    public static ImageAnalysis ReadFrom(BinaryReader reader)
    {
        var opaque = reader.ReadBoolean();
        var width = reader.ReadInt32();
        var height = reader.ReadInt32();
        var pixels = reader.ReadBytes(reader.ReadInt32());
        var sprites = new SpriteGeometry[reader.ReadInt32()];
        for (var i = 0; i < sprites.Length; i++)
        {
            var vertices = ReadPoints(reader);
            var triangles = new ushort[reader.ReadInt32()];
            for (var t = 0; t < triangles.Length; t++)
                triangles[t] = reader.ReadUInt16();
            var shapes = new Vector2[reader.ReadInt32()][];
            for (var s = 0; s < shapes.Length; s++)
                shapes[s] = ReadPoints(reader);
            sprites[i] = new SpriteGeometry(vertices, triangles, shapes);
        }
        return new ImageAnalysis(opaque, sprites, width, height, pixels);
    }

    private static void WritePoints(BinaryWriter writer, Vector2[] points)
    {
        writer.Write(points.Length);
        foreach (var point in points)
        {
            writer.Write(point.x);
            writer.Write(point.y);
        }
    }

    private static Vector2[] ReadPoints(BinaryReader reader)
    {
        var points = new Vector2[reader.ReadInt32()];
        for (var i = 0; i < points.Length; i++)
            points[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        return points;
    }
}

public static class ImageAnalysisCache
{
    private const int CacheFormatVersion = 1;
    private const string Folder = "Library/ImageAnalysisCache";

    public static string PathOf(byte[] source, Vector2Int size, string importerSettings)
    {
        var key = new Hash128();
        key.Append(source);
        key.Append(size.x);
        key.Append(size.y);
        key.Append(importerSettings);
        key.Append(Application.unityVersion);
        key.Append(CacheFormatVersion);
        return Path.Combine(Folder, key + ".bin");
    }

    public static ImageAnalysis TryRead(string path)
    {
        if (!File.Exists(path))
            return null;

        using var reader = new BinaryReader(File.OpenRead(path));
        try
        {
            return ImageAnalysis.ReadFrom(reader);
        }
        catch (EndOfStreamException)
        {
            return null;
        }
    }

    public static ImageAnalysis Write(string path, ImageAnalysis analysis)
    {
        Directory.CreateDirectory(Folder);
        var temporaryPath = path + ".tmp";
        using (var writer = new BinaryWriter(File.Create(temporaryPath)))
            analysis.WriteTo(writer);
        if (File.Exists(path))
            File.Delete(path);
        File.Move(temporaryPath, path);
        return analysis;
    }
}
