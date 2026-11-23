using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

[CustomEditor(typeof(QtxImageImporter))]
[CanEditMultipleObjects]
public class QtxImageImporterEditor : SpriteTextureImporterEditor
{
    private const float PlatformTabHeight = 22f;
    private static readonly BuildTargetGroup[] TabPlatforms = { BuildTargetGroup.Standalone, BuildTargetGroup.Android, BuildTargetGroup.iOS, BuildTargetGroup.WebGL };

    private static readonly TextureImporterFormat[] DesktopFormats =
    {
        TextureImporterFormat.Automatic,
        TextureImporterFormat.DXT1,
        TextureImporterFormat.DXT5,
        TextureImporterFormat.RGBA32,
    };
    private static readonly TextureImporterFormat[] MobileFormats =
    {
        TextureImporterFormat.Automatic,
        TextureImporterFormat.ETC2_RGB4,
        TextureImporterFormat.ETC2_RGBA8,
        TextureImporterFormat.ASTC_4x4,
        TextureImporterFormat.ASTC_6x6,
        TextureImporterFormat.ASTC_8x8,
        TextureImporterFormat.RGBA32,
    };
    private static readonly int[] MaxSizes = { 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384 };
    private static readonly GUIContent[] MaxSizeLabels = MaxSizes.Select(size => new GUIContent(size.ToString())).ToArray();

    private static readonly int[] AfterLoadCompressionValues =
    {
        (int)TextureImporterCompression.Uncompressed,
        (int)TextureImporterCompression.Compressed,
    };
    private static readonly GUIContent[] AfterLoadCompressionLabels = { new("None"), new("Compressed") };

    private static readonly string[] PlatformFields = { "transcodeTime", "maxTextureSize", "format", "compression" };

    private static readonly Func<Texture, long> StorageSizeOf = (Func<Texture, long>)Delegate.CreateDelegate(
        typeof(Func<Texture, long>),
        typeof(Editor).Assembly.GetType("UnityEditor.TextureUtil").GetMethod("GetStorageMemorySizeLong"));

    private static readonly GUIContent QualityModeLabel = new("Quality", "Automatic: the lowest quality whose PSNR and SSIM stay within the limits in Project Settings > UniQTX from what this image gets at quality 95, predicted at import from two small trial encodes of a sample of the image\nCustom: you set the quality");
    private static readonly GUIContent CustomQualityLabel = new("Value", "Higher: better image, bigger file");
    private static readonly GUIContent PsnrLabel = new("PSNR", "Against the source image, measured after transcoding to the GPU format in brackets; color is weighted by alpha. Higher is better");
    private static readonly GUIContent AstcBlockSizeLabel = new("ASTC Block Size", "Block size wherever the texture becomes ASTC: after load on devices with ASTC, and at import when the format is Automatic. 6x6 and 8x8 take 44% and 25% of the memory of 4x4\nAutomatic: measures 4x4, 6x6 and 8x8 against the source at import and takes the largest one within the limits in Project Settings > UniQTX");
    private static readonly GUIContent AstcBlockLabel = new("ASTC Block", "The block size Automatic takes from the measurements below with the limits in Project Settings > UniQTX");
    private static readonly GUIContent AstcCandidatesLabel = new("ASTC Measured", "PSNR and SSIM against the source image for each block size, measured at import");
    private static readonly GUIContent AstcTableRowLabel = new(" ");
    private static readonly string[] AstcTableHeader = { "Block", "PSNR, dB", "SSIM" };
    private static readonly string[] AstcAlphaTableHeader = { "Block", "PSNR, dB", "Alpha, dB", "SSIM" };
    private static readonly int[] AllAstcCandidates = Enumerable.Range(0, QtxAstcBlockChoice.BlockSizes.Length).ToArray();
    private static readonly GUIContent AstcLimitsButtonLabel = new("Open Limits", "Opens Project Settings > UniQTX, where the limits set how much PSNR and SSIM Automatic may lose against ASTC 4x4");
    private static readonly GUIContent SsimLabel = new("SSIM", "Structural similarity to the source image after transcoding, on brightness in 8x8 windows. 1 means identical");

    private const string MipGenerationTooltip = "At Load: only the full-size level is stored; the lower levels are built from it while transcoding. About a third smaller file, transcoding takes about 1.3-1.5 times longer\nAt Import: every mip level is stored in the file, no extra work at load";

    private static BuildTargetGroup selectedPlatform = BuildTargetGroup.Unknown;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawTextureTypeSettings();
        GUILayout.Space(6);
        if (targets.All(importer => ((QtxImageImporter)importer).EncodesFromImage))
        {
            DrawQuality();
            GUILayout.Space(6);
        }
        EditorGUILayout.PropertyField(serializedObject.FindProperty("wrapMode"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("filterMode"));
        var generateMipMaps = serializedObject.FindProperty("generateMipMaps");
        EditorGUILayout.PropertyField(generateMipMaps, new GUIContent("Generate Mip Maps", "Mip levels from Max Size down to 1x1"));
        if (generateMipMaps.boolValue || generateMipMaps.hasMultipleDifferentValues)
        {
            EditorGUI.indentLevel++;
            if (targets.All(importer => ((QtxImageImporter)importer).EncodesFromImage))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("mipMapGeneration"), new GUIContent("Mip Maps", MipGenerationTooltip));
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("mipMapBias"), new GUIContent("Mip Map Bias", "Positive: blurrier, negative: sharper. Ignored on WebGL, OpenGL ES and Metal unless the shader applies it"));
            EditorGUI.indentLevel--;
        }
        GUILayout.Space(10);
        DrawPlatformGrouping();
        serializedObject.ApplyModifiedProperties();
        DrawImportMessages();
        DrawBundleTable();
        ApplyRevertGUI();
    }

    private void DrawQuality()
    {
        var qualityMode = serializedObject.FindProperty("qualityMode");

        EditorGUILayout.PropertyField(qualityMode, QualityModeLabel);

        if (qualityMode.hasMultipleDifferentValues || (QtxQualityMode)qualityMode.intValue != QtxQualityMode.Custom)
        {
            return;
        }

        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(serializedObject.FindProperty("quality"), CustomQualityLabel);
        EditorGUI.indentLevel--;
    }

    private void DrawImportMessages()
    {
        var assetPaths = targets.Cast<AssetImporter>().Select(importer => importer.assetPath).ToArray();
        string MessagesWith(ImportLogFlags flag) => string.Join("\n", assetPaths
            .SelectMany(assetPath => ImportMessagesOf(assetPath, flag, assetPaths.Length == 1))
            .Distinct());

        var errors = MessagesWith(ImportLogFlags.Error);
        var warnings = MessagesWith(ImportLogFlags.Warning);
        if (errors.Length > 0)
            EditorGUILayout.HelpBox(errors, MessageType.Error);
        if (warnings.Length > 0)
            EditorGUILayout.HelpBox(warnings, MessageType.Warning);
    }

    private static IEnumerable<string> ImportMessagesOf(string assetPath, ImportLogFlags flag, bool withoutAssetPath)
    {
        var importLog = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<ImportLog>().FirstOrDefault();
        if (importLog == null)
            return Enumerable.Empty<string>();

        var pathPrefix = assetPath + ": ";
        return importLog.logEntries
            .Where(entry => entry.flags.HasFlag(flag))
            .Select(entry => entry.message.Trim())
            .Select(message => withoutAssetPath && message.StartsWith(pathPrefix) ? message.Substring(pathPrefix.Length) : message);
    }

    private void DrawBundleTable()
    {
        var buildTarget = EditorUserBuildSettings.activeBuildTarget;
        var rows = targets.Cast<QtxImageImporter>().Select(importer => BundleRowOf(importer, buildTarget)).ToArray();
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField($"{PlatformName(BuildPipeline.GetBuildTargetGroup(buildTarget))} Bundle Size", EditorUtility.FormatBytes(rows.Sum(row => row.size)));
            EditorGUILayout.LabelField("Stored As", SameOrMixed(rows.Select(row => row.storedAs)));
            EditorGUILayout.LabelField("Quality", SameOrMixed(rows.Select(row => row.quality)));
            if (rows.All(row => row.measured != null))
            {
                EditorGUILayout.LabelField(PsnrLabel, new GUIContent(SameOrMixed(rows.Select(row => PsnrText(row.measured)))));
                EditorGUILayout.LabelField(SsimLabel, new GUIContent(SameOrMixed(rows.Select(row => row.measured.Ssim.ToString("F4", CultureInfo.InvariantCulture)))));
            }
        }
    }

    private void DrawAstcBlockBox()
    {
        var blockSize = serializedObject.FindProperty("astcBlockSize");

        GUILayout.Space(4);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            DrawAstcBlockSize(blockSize);

            if (blockSize.hasMultipleDifferentValues)
            {
                return;
            }

            DrawAstcChoice((QtxAstcBlockSize)blockSize.intValue);
        }
    }

    private static void DrawAstcBlockSize(SerializedProperty blockSize)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PropertyField(blockSize, AstcBlockSizeLabel);

            if (blockSize.hasMultipleDifferentValues || (QtxAstcBlockSize)blockSize.intValue != QtxAstcBlockSize.Automatic)
            {
                return;
            }

            if (GUILayout.Button(AstcLimitsButtonLabel, EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
            {
                EditorApplication.delayCall += OpenProjectSettings;
            }
        }
    }

    private void DrawAstcChoice(QtxAstcBlockSize selected)
    {
        var importers = targets.Cast<QtxImageImporter>().ToArray();
        var choices = importers
            .Select(importer => AssetDatabase.LoadAllAssetsAtPath(importer.assetPath).OfType<QtxAstcBlockChoice>().FirstOrDefault())
            .ToArray();

        if (choices.Any(choice => choice == null))
        {
            return;
        }

        if (selected != QtxAstcBlockSize.Automatic)
        {
            DrawAstcMeasurements(choices, new[] { (int)selected - 1 });

            return;
        }

        var texts = new string[choices.Length];

        for (var index = 0; index < choices.Length; index++)
        {
            texts[index] = GetAutomaticBlockText(importers[index], choices[index]);
        }

        EditorGUILayout.LabelField(AstcBlockLabel, new GUIContent(SameOrMixed(texts)));
        DrawAstcMeasurements(choices, AllAstcCandidates);
    }

    private static void DrawAstcMeasurements(QtxAstcBlockChoice[] choices, int[] candidates)
    {
        if (choices.All(choice => choice.IsMeasured) == false)
        {
            return;
        }

        var hasAlpha = choices.Any(choice => choice.HasAlpha);

        DrawAstcTableRow(AstcCandidatesLabel, hasAlpha ? AstcAlphaTableHeader : AstcTableHeader, EditorStyles.miniBoldLabel);

        for (var index = 0; index < candidates.Length; index++)
        {
            DrawAstcTableRow(AstcTableRowLabel, GetAstcTableCells(choices, candidates[index], hasAlpha), EditorStyles.label);
        }
    }

    private static string[] GetAstcTableCells(QtxAstcBlockChoice[] choices, int candidate, bool hasAlpha)
    {
        var size = QtxAstcBlockChoice.BlockSizes[candidate];
        var block = $"{size}x{size}";
        var color = SameOrMixed(choices.Select(choice => choice.ColorPsnr(candidate).ToString("F2", CultureInfo.InvariantCulture)));
        var ssim = SameOrMixed(choices.Select(choice => choice.Ssim(candidate).ToString("F4", CultureInfo.InvariantCulture)));

        if (hasAlpha == false)
        {
            return new[] { block, color, ssim };
        }

        var alpha = SameOrMixed(choices.Select(choice => choice.AlphaPsnr(candidate).ToString("F2", CultureInfo.InvariantCulture)));

        return new[] { block, color, alpha, ssim };
    }

    private static void DrawAstcTableRow(GUIContent label, string[] cells, GUIStyle style)
    {
        var cellsRect = EditorGUI.PrefixLabel(EditorGUILayout.GetControlRect(), label);
        var cellWidth = cellsRect.width / cells.Length;

        for (var index = 0; index < cells.Length; index++)
        {
            var cellRect = new Rect(cellsRect.x + index * cellWidth, cellsRect.y, cellWidth, cellsRect.height);

            EditorGUI.LabelField(cellRect, cells[index], style);
        }
    }

    private static string GetAutomaticBlockText(QtxImageImporter importer, QtxAstcBlockChoice choice)
    {
        if (choice.IsMeasured)
        {
            var settings = QtxProjectSettings.instance;
            var size = choice.GetAutomaticSize(settings.maxPsnrLoss, settings.maxSsimLoss);

            return $"{size}x{size}, automatic";
        }

        return importer.EncodesFromImage ? "Measured on Apply" : $"{choice.BlockSize}x{choice.BlockSize}, stored in file";
    }

    private static void OpenProjectSettings()
    {
        SettingsService.OpenProjectSettings(QtxProjectSettingsProvider.SettingsPath);
    }

    private static string PsnrText(QtxMeasuredQuality measured)
    {
        var color = measured.ColorPsnr.ToString("F2", CultureInfo.InvariantCulture);
        return measured.HasAlpha
            ? $"{color} dB color, {measured.AlphaPsnr.ToString("F2", CultureInfo.InvariantCulture)} dB alpha ({measured.Format})"
            : $"{color} dB ({measured.Format})";
    }

    private static string QualityOf(QtxImageImporter importer, UnityEngine.Object[] assets)
    {
        if (importer.EncodesFromImage)
        {
            var choice = assets.OfType<QtxQualityChoice>().FirstOrDefault();

            return choice == null ? importer.quality.ToString() : $"{choice.Quality}, automatic";
        }

        try
        {
            var quality = new QtxHeader(File.ReadAllBytes(importer.assetPath)).Quality;
            return quality >= 0 ? $"{quality} (stored in file)" : "Unknown (re-encode to store it)";
        }
        catch (InvalidDataException)
        {
            return "Unreadable file";
        }
    }

    private static (long size, string storedAs, string quality, QtxMeasuredQuality measured) BundleRowOf(QtxImageImporter importer, BuildTarget buildTarget)
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath(importer.assetPath);
        var quality = QualityOf(importer, assets);
        var measured = assets.OfType<QtxMeasuredQuality>().FirstOrDefault();
        if (importer.ResolvePlatformSettings(buildTarget).transcodeTime == QtxTranscodeTime.AfterLoad)
        {
            var image = assets.OfType<QtxImage>().FirstOrDefault();
            var size = image == null ? new FileInfo(importer.assetPath).Length : new SerializedObject(image).FindProperty("bytes").arraySize;
            return (size, "QTX", quality, measured);
        }

        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(importer.assetPath);
        return texture == null ? (0, "Import failed", quality, measured) : (StorageSizeOf(texture), texture.format.ToString(), quality, measured);
    }

    private static string SameOrMixed(IEnumerable<string> values)
    {
        var distinct = values.Distinct().ToArray();
        return distinct.Length == 1 ? distinct[0] : "Mixed";
    }

    private void DrawPlatformGrouping()
    {
        var defaultPlatform = serializedObject.FindProperty("defaultPlatform");
        using var group = new EditorGUILayout.VerticalScope(GUI.skin.GetStyle("FrameBox"));
        DrawPlatformTabs(group.rect);

        var platformOverride = selectedPlatform == BuildTargetGroup.Unknown ? null : OverrideOf(selectedPlatform);
        if (platformOverride == null)
            DrawPlatformSettings(defaultPlatform, BuildTargetGroup.Unknown);
        else
            DrawPlatformOverride(platformOverride, defaultPlatform);

        if (IsMobile(selectedPlatform))
        {
            DrawAstcBlockBox();
        }
    }

    private static void DrawPlatformTabs(Rect groupRect)
    {
        var wasEnabled = GUI.enabled;
        GUI.enabled = true;
        var tabCount = TabPlatforms.Length + 1;
        for (var tabIndex = 0; tabIndex < tabCount; tabIndex++)
        {
            var platform = tabIndex == 0 ? BuildTargetGroup.Unknown : TabPlatforms[tabIndex - 1];
            var content = tabIndex == 0 ? new GUIContent("Default") : PlatformTab(platform);
            if (GUI.Toggle(PlatformTabRect(groupRect, tabIndex, tabCount), selectedPlatform == platform, content, PlatformTabStyle(tabIndex, tabCount)))
                selectedPlatform = platform;
        }
        GUILayoutUtility.GetRect(10, PlatformTabHeight);
        GUI.enabled = wasEnabled;
    }

    private static Rect PlatformTabRect(Rect groupRect, int tabIndex, int tabCount)
    {
        var tabWidth = groupRect.width / tabCount;
        var left = Mathf.RoundToInt(tabIndex * tabWidth);
        var right = Mathf.RoundToInt((tabIndex + 1) * tabWidth);
        return new Rect(groupRect.x + left, groupRect.y, right - left, PlatformTabHeight);
    }

    private static GUIStyle PlatformTabStyle(int tabIndex, int tabCount)
    {
        if (tabIndex == 0)
            return GUI.skin.GetStyle("Tab first");
        return GUI.skin.GetStyle(tabIndex == tabCount - 1 ? "Tab last" : "Tab middle");
    }

    private SerializedProperty OverrideOf(BuildTargetGroup platform)
    {
        var overrides = serializedObject.FindProperty("platformOverrides");
        for (var i = 0; i < overrides.arraySize; i++)
            if (PlatformOf(overrides.GetArrayElementAtIndex(i)) == platform)
                return overrides.GetArrayElementAtIndex(i);
        return null;
    }

    private void DrawPlatformOverride(SerializedProperty platformOverride, SerializedProperty defaultPlatform)
    {
        var overridden = platformOverride.FindPropertyRelative("overridden");
        var toggleRect = EditorGUILayout.GetControlRect(true);
        var label = EditorGUI.BeginProperty(toggleRect, new GUIContent($"Override For {PlatformName(selectedPlatform)}"), overridden);
        EditorGUI.BeginChangeCheck();
        EditorGUI.showMixedValue = overridden.hasMultipleDifferentValues;
        var overrideAll = EditorGUI.ToggleLeft(toggleRect, label, overridden.boolValue);
        EditorGUI.showMixedValue = false;
        if (EditorGUI.EndChangeCheck())
            SetOverriddenInEveryImporter(platformOverride.propertyPath, overrideAll);
        EditorGUI.EndProperty();

        var allOverridden = overridden.boolValue && !overridden.hasMultipleDifferentValues;
        using (new EditorGUI.DisabledScope(!allOverridden))
            DrawPlatformSettings(allOverridden ? platformOverride : defaultPlatform, selectedPlatform);
    }

    private void SetOverriddenInEveryImporter(string overridePath, bool overridden)
    {
        serializedObject.ApplyModifiedProperties();
        foreach (var importer in serializedObject.targetObjects)
        {
            var importerObject = new SerializedObject(importer);
            var platformOverride = importerObject.FindProperty(overridePath);
            var overriddenProperty = platformOverride.FindPropertyRelative("overridden");
            if (overridden && !overriddenProperty.boolValue)
                CopyPlatformFields(importerObject.FindProperty("defaultPlatform"), platformOverride);
            overriddenProperty.boolValue = overridden;
            importerObject.ApplyModifiedProperties();
        }
        serializedObject.Update();
    }

    private static void DrawPlatformSettings(SerializedProperty settings, BuildTargetGroup platform)
    {
        var transcodeTime = settings.FindPropertyRelative("transcodeTime");
        EditorGUILayout.PropertyField(transcodeTime, new GUIContent("Transcode", "At Import: GPU format in bundle\nAfter Load: QTX in bundle, transcoded on load"));
        EditorGUILayout.IntPopup(settings.FindPropertyRelative("maxTextureSize"), MaxSizeLabels, MaxSizes, new GUIContent("Max Size"));
        if (transcodeTime.hasMultipleDifferentValues)
            return;

        if ((QtxTranscodeTime)transcodeTime.intValue == QtxTranscodeTime.AtImport)
            DrawImportFormat(settings, platform);
        else
            DrawAfterLoadCompression(settings);
    }

    private static void DrawImportFormat(SerializedProperty settings, BuildTargetGroup platform)
    {
        var format = settings.FindPropertyRelative("format");
        if (platform == BuildTargetGroup.Unknown)
        {
            if (format.hasMultipleDifferentValues || format.intValue != (int)TextureImporterFormat.Automatic)
                format.intValue = (int)TextureImporterFormat.Automatic;
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.Popup("Format", 0, new[] { FormatLabel(TextureImporterFormat.Automatic) });
        }
        else
        {
            var formats = IsMobile(platform) ? MobileFormats : DesktopFormats;
            EditorGUILayout.IntPopup(format, formats.Select(option => new GUIContent(FormatLabel(option))).ToArray(), formats.Select(option => (int)option).ToArray(), new GUIContent("Format"));
        }

        if (format.hasMultipleDifferentValues)
            return;

        if ((TextureImporterFormat)format.intValue != TextureImporterFormat.Automatic)
        {
            return;
        }

        EditorGUILayout.HelpBox("Desktop: DXT1/DXT5, Android: ETC2, iOS: ASTC at ASTC Block Size, Web: Build Settings texture compression\nTranscoded at import, no QTX saving, no work after load", MessageType.Info);
    }

    private static void DrawAfterLoadCompression(SerializedProperty settings)
    {
        var compression = settings.FindPropertyRelative("compression");
        var shown = compression.intValue == (int)TextureImporterCompression.Uncompressed ? (int)TextureImporterCompression.Uncompressed : (int)TextureImporterCompression.Compressed;
        EditorGUI.showMixedValue = compression.hasMultipleDifferentValues;
        EditorGUI.BeginChangeCheck();
        var picked = EditorGUILayout.IntPopup(new GUIContent("Compression"), shown, AfterLoadCompressionLabels, AfterLoadCompressionValues);
        if (EditorGUI.EndChangeCheck())
            compression.intValue = picked;
        EditorGUI.showMixedValue = false;
        if (compression.hasMultipleDifferentValues)
            return;

        var description = picked == (int)TextureImporterCompression.Uncompressed
            ? "RGBA32, 4 bytes/pixel\nTranscoded in background"
            : "Addressables: DXT1/DXT5 desktop, ASTC at ASTC Block Size mobile, ETC2 or RGBA32 if unsupported\nTranscoded in background, Web Worker in browser\nScenes, Resources: transcoded at import, no QTX saving\nWidth and height: multiples of 4";
        EditorGUILayout.HelpBox(description, MessageType.Info);
    }

    private static string FormatLabel(TextureImporterFormat format)
    {
        return format switch
        {
            TextureImporterFormat.Automatic => "Automatic",
            TextureImporterFormat.ETC2_RGB4 => "ETC2 RGB",
            TextureImporterFormat.ETC2_RGBA8 => "ETC2 RGBA",
            TextureImporterFormat.ASTC_4x4 => "ASTC 4x4",
            TextureImporterFormat.ASTC_6x6 => "ASTC 6x6",
            TextureImporterFormat.ASTC_8x8 => "ASTC 8x8",
            _ => format.ToString(),
        };
    }

    private static void CopyPlatformFields(SerializedProperty source, SerializedProperty target)
    {
        foreach (var field in PlatformFields)
            target.FindPropertyRelative(field).boxedValue = source.FindPropertyRelative(field).boxedValue;
    }

    private static BuildTargetGroup PlatformOf(SerializedProperty platformSettings)
    {
        return (BuildTargetGroup)platformSettings.FindPropertyRelative("platform").intValue;
    }

    private static bool IsMobile(BuildTargetGroup platform)
    {
        return platform == BuildTargetGroup.Android || platform == BuildTargetGroup.iOS;
    }

    private static string PlatformName(BuildTargetGroup platform)
    {
        return platform switch
        {
            BuildTargetGroup.iOS => "iOS",
            BuildTargetGroup.WebGL => "Web",
            _ => platform.ToString(),
        };
    }

    private static GUIContent PlatformTab(BuildTargetGroup platform)
    {
        var iconName = platform switch
        {
            BuildTargetGroup.Standalone => "BuildSettings.Standalone.Small",
            BuildTargetGroup.WebGL => "BuildSettings.WebGL.Small",
            BuildTargetGroup.Android => "BuildSettings.Android.Small",
            BuildTargetGroup.iOS => "BuildSettings.iPhone.Small",
            _ => null,
        };
        var icon = iconName == null ? null : EditorGUIUtility.FindTexture(iconName);
        return icon != null ? new GUIContent(icon, PlatformName(platform)) : new GUIContent(PlatformName(platform));
    }
}
