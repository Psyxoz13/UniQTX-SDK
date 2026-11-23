using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

public class SwitchedImporterInspectorCopyRelease : AssetPostprocessor
{
    private static readonly MethodInfo ReleaseInspectorCopy = typeof(AssetImporterEditor).GetMethod("ReleaseInspectorCopy", BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly FieldInfo RegisteredTargetIds = typeof(AssetImporterEditor).GetField("m_TargetsInstanceID", BindingFlags.NonPublic | BindingFlags.Instance);

    private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
    {
        foreach (var editor in Resources.FindObjectsOfTypeAll<AssetImporterEditor>())
            ReleaseCopiesOfSwitchedImporters(editor);
    }

    private static void ReleaseCopiesOfSwitchedImporters(AssetImporterEditor editor)
    {
        if (RegisteredTargetIds?.GetValue(editor) is not List<int> registeredIds)
            return;

        foreach (var switchedImporterId in editor.targets.Where(ImporterTypeChanged).Select(importer => importer.GetInstanceID()))
            if (registeredIds.Remove(switchedImporterId))
                ReleaseInspectorCopy.Invoke(null, new object[] { switchedImporterId, editor });
    }

    private static bool ImporterTypeChanged(Object editedImporter)
    {
        var currentImporter = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(editedImporter.GetInstanceID()));
        return currentImporter != null && currentImporter.GetType() != editedImporter.GetType();
    }
}
