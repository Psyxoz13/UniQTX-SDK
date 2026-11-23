using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Build.DataBuilders;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.ResourceManagement.Util;

[CreateAssetMenu(fileName = "QtxBuildScript.asset", menuName = "Addressables/Content Builders/QTX Build Script")]
public class QtxBuildScript : BuildScriptSchemaDriven
{
    protected override TResult BuildDataImplementation<TResult>(AddressablesDataBuilderInput builderInput)
    {
        var settings = builderInput.AddressableSettings;
        UseQtxProviderInsteadOfDefaultProvider(settings);
        MoveReferencedImagesIntoReferencingGroups(settings);

        QtxBundleMode.Set(true);
        try
        {
            var notPrepared = FindImagesNotPreparedForBundles(settings);
            if (notPrepared.Length > 0)
                return AddressableAssetBuildResult.CreateResult<TResult>(null, 0, "QTX textures were not shrunk for bundles, build again: " + string.Join(", ", notPrepared));

            return base.BuildDataImplementation<TResult>(builderInput);
        }
        finally
        {
            QtxBundleMode.Set(false);
        }
    }

    private static string[] FindImagesNotPreparedForBundles(AddressableAssetSettings settings)
    {
        var entries = new List<AddressableAssetEntry>();
        settings.GetAllAssets(entries, false, entryFilter: entry => IsQtxImage(entry.AssetPath));
        return entries
            .Select(entry => entry.AssetPath)
            .Where(path => AssetDatabase.LoadAllAssetsAtPath(path).OfType<QtxImage>().Any())
            .Where(path => AssetDatabase.LoadAssetAtPath<Texture2D>(path) is not { width: 1, height: 1 })
            .ToArray();
    }

    private static void UseQtxProviderInsteadOfDefaultProvider(AddressableAssetSettings settings)
    {
        var providerType = new SerializedType { Value = typeof(QtxBundledAssetProvider) };
        foreach (var group in settings.groups.Where(group => group))
            if (group.GetSchema<BundledAssetGroupSchema>() is { } schema && (schema.BundledAssetProviderType.Value == null || schema.BundledAssetProviderType.Value == typeof(BundledAssetProvider)))
                schema.BundledAssetProviderType = providerType;
    }

    private static void MoveReferencedImagesIntoReferencingGroups(AddressableAssetSettings settings)
    {
        foreach (var group in settings.groups.Where(group => group && group.HasSchema<BundledAssetGroupSchema>()))
        {
            var referencingPaths = group.entries.Select(entry => entry.AssetPath).Where(path => !IsQtxImage(path)).ToArray();
            foreach (var path in AssetDatabase.GetDependencies(referencingPaths, true).Where(IsQtxImage))
            {
                var guid = AssetDatabase.AssetPathToGUID(path);
                if (settings.FindAssetEntry(guid)?.parentGroup != group)
                    settings.CreateOrMoveEntry(guid, group);
            }
        }
    }

    private static bool IsQtxImage(string path)
    {
        return AssetImporter.GetAtPath(path) is QtxImageImporter;
    }
}
