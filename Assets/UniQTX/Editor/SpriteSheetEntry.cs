using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

[Serializable]
public class SpriteOutline
{
    public Vector2[] points;

    public static List<SpriteOutline> ListOf(IEnumerable<Vector2[]> outlines)
    {
        return outlines?.Select(points => new SpriteOutline { points = points.ToArray() }).ToList() ?? new List<SpriteOutline>();
    }

    public static List<Vector2[]> PointsOf(IEnumerable<SpriteOutline> outlines)
    {
        return outlines.Select(outline => outline.points.ToArray()).ToList();
    }
}

[Serializable]
public class SpriteSheetEntry : SpriteRect
{
    public List<SpriteOutline> outline = new();
    public float outlineTessellationDetail;
    public List<SpriteOutline> physicsOutline = new();
    public float physicsTessellationDetail;
    public long internalID;

    public long FileId => internalID != 0 ? internalID : spriteID.GetHashCode();

    public SpriteSheetEntry Clone()
    {
        return WithRectOf(this);
    }

    public SpriteSheetEntry WithRectOf(SpriteRect source)
    {
        return new SpriteSheetEntry
        {
            name = source.name,
            rect = source.rect,
            alignment = source.alignment,
            pivot = source.pivot,
            border = source.border,
            customData = source.customData,
            spriteID = source.spriteID,
            outline = SpriteOutline.ListOf(SpriteOutline.PointsOf(outline)),
            outlineTessellationDetail = outlineTessellationDetail,
            physicsOutline = SpriteOutline.ListOf(SpriteOutline.PointsOf(physicsOutline)),
            physicsTessellationDetail = physicsTessellationDetail,
            internalID = internalID,
        };
    }

    public SpriteRect ToSpriteRect()
    {
        return new SpriteRect
        {
            name = name,
            rect = rect,
            alignment = alignment,
            pivot = pivot,
            border = border,
            customData = customData,
            spriteID = spriteID,
        };
    }

    public Vector2 NormalizedPivot()
    {
        return alignment switch
        {
            SpriteAlignment.TopLeft => new Vector2(0f, 1f),
            SpriteAlignment.TopCenter => new Vector2(0.5f, 1f),
            SpriteAlignment.TopRight => new Vector2(1f, 1f),
            SpriteAlignment.LeftCenter => new Vector2(0f, 0.5f),
            SpriteAlignment.RightCenter => new Vector2(1f, 0.5f),
            SpriteAlignment.BottomLeft => new Vector2(0f, 0f),
            SpriteAlignment.BottomCenter => new Vector2(0.5f, 0f),
            SpriteAlignment.BottomRight => new Vector2(1f, 0f),
            SpriteAlignment.Custom => pivot,
            _ => new Vector2(0.5f, 0.5f),
        };
    }
}
