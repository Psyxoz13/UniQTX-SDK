using System;
using System.Collections.Generic;
using UnityEngine;

public static class QtxImageSample
{
    private const int TileSize = 24;
    private const int MaxTiles = 64;

    public static Color32[] Create(Color32[] pixels, int width, int height, out int sampleWidth, out int sampleHeight)
    {
        if (width * height <= MaxTiles * TileSize * TileSize || width < TileSize || height < TileSize)
        {
            sampleWidth = width;
            sampleHeight = height;

            return pixels;
        }

        var tiles = GetTiles(pixels, width, height);
        var chosenCount = Math.Min(tiles.Count, MaxTiles);
        var columns = (int)Math.Ceiling(Math.Sqrt(chosenCount));
        var rows = Math.Max(1, chosenCount / columns);
        sampleWidth = columns * TileSize;
        sampleHeight = rows * TileSize;
        var sample = new Color32[sampleWidth * sampleHeight];

        for (var index = 0; index < rows * columns; index++)
        {
            var tile = tiles[index * tiles.Count / chosenCount];
            CopyTile(pixels, width, tile, sample, sampleWidth, new Vector2Int(index % columns * TileSize, index / columns * TileSize));
        }

        return sample;
    }

    private static List<Vector2Int> GetTiles(Color32[] pixels, int width, int height)
    {
        var all = new List<Vector2Int>();
        var visible = new List<Vector2Int>();

        for (var y = 0; y + TileSize <= height; y += TileSize)
        {
            for (var x = 0; x + TileSize <= width; x += TileSize)
            {
                var tile = new Vector2Int(x, y);
                all.Add(tile);

                if (IsVisible(pixels, width, tile))
                {
                    visible.Add(tile);
                }
            }
        }

        return visible.Count > 0 ? visible : all;
    }

    private static bool IsVisible(Color32[] pixels, int width, Vector2Int tile)
    {
        for (var y = tile.y; y < tile.y + TileSize; y++)
        {
            for (var x = tile.x; x < tile.x + TileSize; x++)
            {
                if (pixels[y * width + x].a > 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void CopyTile(Color32[] pixels, int width, Vector2Int tile, Color32[] sample, int sampleWidth, Vector2Int target)
    {
        for (var row = 0; row < TileSize; row++)
        {
            Array.Copy(pixels, (tile.y + row) * width + tile.x, sample, (target.y + row) * sampleWidth + target.x, TileSize);
        }
    }
}
