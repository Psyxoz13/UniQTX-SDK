using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

public abstract class SpriteTextureImporterEditor : ScriptedImporterEditor
{
    private const string SpriteEditorMenuPath = "Window/2D/Sprite Editor";

    private static readonly GUIContent[] TextureTypeLabels = { new("Default"), new("Sprite (2D and UI)") };
    private static readonly int[] TextureTypeValues = { (int)TextureImporterType.Default, (int)TextureImporterType.Sprite };
    private static readonly string[] TextureShapeLabels = { "2D" };
    private static readonly GUIContent[] SpriteModeLabels = { new("Single"), new("Multiple"), new("Polygon") };
    private static readonly int[] SpriteModeValues = { (int)SpriteImportMode.Single, (int)SpriteImportMode.Multiple, (int)SpriteImportMode.Polygon };
    private static readonly GUIContent[] MeshTypeLabels = { new("Full Rect"), new("Tight") };
    private static readonly int[] MeshTypeValues = { (int)SpriteMeshType.FullRect, (int)SpriteMeshType.Tight };
    private static readonly GUIContent[] PivotLabels =
    {
        new("Center"), new("Top Left"), new("Top"), new("Top Right"), new("Left"),
        new("Right"), new("Bottom Left"), new("Bottom"), new("Bottom Right"), new("Custom"),
    };
    private static readonly int[] PivotValues =
    {
        (int)SpriteAlignment.Center, (int)SpriteAlignment.TopLeft, (int)SpriteAlignment.TopCenter, (int)SpriteAlignment.TopRight, (int)SpriteAlignment.LeftCenter,
        (int)SpriteAlignment.RightCenter, (int)SpriteAlignment.BottomLeft, (int)SpriteAlignment.BottomCenter, (int)SpriteAlignment.BottomRight, (int)SpriteAlignment.Custom,
    };

    protected void DrawTextureTypeSettings()
    {
        var textureType = serializedObject.FindProperty("textureType");
        EditorGUILayout.IntPopup(textureType, TextureTypeLabels, TextureTypeValues, new GUIContent("Texture Type"));
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.Popup("Texture Shape", 0, TextureShapeLabels);
        GUILayout.Space(6);

        if (!textureType.hasMultipleDifferentValues && textureType.intValue == (int)TextureImporterType.Sprite)
            DrawSpriteSettings();
    }

    private void DrawSpriteSettings()
    {
        var spriteMode = serializedObject.FindProperty("spriteMode");
        EditorGUILayout.IntPopup(spriteMode, SpriteModeLabels, SpriteModeValues, new GUIContent("Sprite Mode"));
        var sameSpriteMode = !spriteMode.hasMultipleDifferentValues;

        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(serializedObject.FindProperty("pixelsPerUnit"), new GUIContent("Pixels Per Unit"));
        if (sameSpriteMode && spriteMode.intValue != (int)SpriteImportMode.Polygon)
            EditorGUILayout.IntPopup(serializedObject.FindProperty("meshType"), MeshTypeLabels, MeshTypeValues, new GUIContent("Mesh Type"));
        EditorGUILayout.IntSlider(serializedObject.FindProperty("extrudeEdges"), 0, 32, new GUIContent("Extrude Edges"));
        if (sameSpriteMode && spriteMode.intValue == (int)SpriteImportMode.Single)
            DrawPivot();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("generatePhysicsShape"), new GUIContent("Generate Physics Shape"));
        EditorGUI.indentLevel--;

        using (new EditorGUI.DisabledScope(targets.Length != 1))
            if (GUILayout.Button("Open Sprite Editor"))
                OpenSpriteEditor();
    }

    private void DrawPivot()
    {
        var alignment = serializedObject.FindProperty("singleSprite.m_Alignment");
        EditorGUILayout.IntPopup(alignment, PivotLabels, PivotValues, new GUIContent("Pivot"));
        if (!alignment.hasMultipleDifferentValues && alignment.intValue == (int)SpriteAlignment.Custom)
            EditorGUILayout.PropertyField(serializedObject.FindProperty("singleSprite.m_Pivot"), new GUIContent("Custom Pivot"));
    }

    private void OpenSpriteEditor()
    {
        if (HasModified())
        {
            var assetPath = ((AssetImporter)target).assetPath;
            if (EditorUtility.DisplayDialog("Unapplied import settings", $"Unapplied import settings for '{assetPath}'", "Apply", "Revert"))
                SaveChanges();
            else
                DiscardChanges();
        }

        EditorApplication.ExecuteMenuItem(SpriteEditorMenuPath);
        GUIUtility.ExitGUI();
    }
}
