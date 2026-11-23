using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[Serializable]
public class ImporterState
{
    public string importer;
    public string meta;
}

[Serializable]
public class ImporterSettingsRecord
{
    private const string Folder = "Library/ImporterSettingsRecords";

    public string currentImporter;
    public List<ImporterState> states = new();
    public PortableImageSettings common;

    public string MetaOf(string importer)
    {
        return states.FirstOrDefault(state => state.importer == importer)?.meta;
    }

    public void Remember(string importer, string meta, PortableImageSettings settings)
    {
        states.RemoveAll(state => state.importer == importer);
        states.Add(new ImporterState { importer = importer, meta = meta });
        currentImporter = importer;
        common = settings;
    }

    public static ImporterSettingsRecord Read(string guid)
    {
        var path = PathOf(guid);
        return File.Exists(path) ? JsonUtility.FromJson<ImporterSettingsRecord>(File.ReadAllText(path)) : null;
    }

    public static bool Exists(string guid)
    {
        return File.Exists(PathOf(guid));
    }

    public void Write(string guid)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(PathOf(guid), JsonUtility.ToJson(this));
    }

    private static string PathOf(string guid)
    {
        return Path.Combine(Folder, guid + ".json");
    }
}

public class ImporterSwitchSettingsTransfer : AssetPostprocessor
{
    private static readonly string[] SwitchableExtensions = { ".png", ".jpg", ".jpeg" };

    [InitializeOnLoadMethod]
    private static void RecordUnrecordedImagesAfterLoad()
    {
        EditorApplication.delayCall += () =>
        {
            foreach (var path in AssetDatabase.GetAllAssetPaths().Where(IsSwitchableImage))
                if (!ImporterSettingsRecord.Exists(AssetDatabase.AssetPathToGUID(path)))
                    Record(path, new ImporterSettingsRecord());
        };
    }

    private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
    {
        foreach (var path in importedAssets.Where(IsSwitchableImage))
        {
            var record = ImporterSettingsRecord.Read(AssetDatabase.AssetPathToGUID(path)) ?? new ImporterSettingsRecord();
            if (record.common != null && record.currentImporter != ImporterNameOf(path))
                EditorApplication.delayCall += () => RestoreSettingsAfterSwitch(path);
            else
                Record(path, record);
        }
    }

    private static void RestoreSettingsAfterSwitch(string path)
    {
        var guid = AssetDatabase.AssetPathToGUID(path);
        var record = ImporterSettingsRecord.Read(guid);
        var importerName = ImporterNameOf(path);
        if (record?.common == null || importerName == null || importerName == record.currentImporter)
            return;

        var settingsOfPreviousImporter = record.common;
        var previousMeta = record.MetaOf(importerName);
        record.currentImporter = importerName;
        record.Write(guid);
        if (previousMeta != null)
        {
            File.WriteAllText(path + ".meta", previousMeta);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        var importer = AssetImporter.GetAtPath(path);
        if (importer is QtxImageImporter qtxImporter)
            settingsOfPreviousImporter.ApplyTo(qtxImporter);
        else if (importer is TextureImporter textureImporter)
            settingsOfPreviousImporter.ApplyTo(textureImporter);
        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();
    }

    private static void Record(string path, ImporterSettingsRecord record)
    {
        var importer = AssetImporter.GetAtPath(path);
        var settings = importer switch
        {
            QtxImageImporter qtxImporter => PortableImageSettings.Of(qtxImporter),
            TextureImporter textureImporter => PortableImageSettings.Of(textureImporter),
            _ => null,
        };
        if (settings == null)
            return;

        record.Remember(importer.GetType().FullName, File.ReadAllText(path + ".meta"), settings);
        record.Write(AssetDatabase.AssetPathToGUID(path));
    }

    private static bool IsSwitchableImage(string path)
    {
        return path.StartsWith("Assets/")
            && SwitchableExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            && AssetImporter.GetAtPath(path) is TextureImporter or QtxImageImporter;
    }

    private static string ImporterNameOf(string path)
    {
        return AssetImporter.GetAtPath(path)?.GetType().FullName;
    }
}
