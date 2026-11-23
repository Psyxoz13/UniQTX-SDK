using System;
using UnityEditor;
using UnityEngine;

[FilePath("ProjectSettings/UniQTX.asset", FilePathAttribute.Location.ProjectFolder)]
public class QtxProjectSettings : ScriptableSingleton<QtxProjectSettings>
{
    public const string AstcLimitsDependency = "QtxAstcLimits";
    public const string QualityLimitsDependency = "QtxQualityLimits";

    public float maxQualityPsnrLoss = 1f;
    public float maxQualitySsimLoss = .005f;
    public float maxPsnrLoss = 1.0f;
    public float maxSsimLoss = 0.01f;

    public void SaveAndReimport()
    {
        Save(true);
        RegisterDependencies();
        AssetDatabase.Refresh();
    }

    [InitializeOnLoadMethod]
    private static void RegisterDependencies()
    {
        var qualityLimits = new Hash128();
        qualityLimits.Append(instance.maxQualityPsnrLoss);
        qualityLimits.Append(instance.maxQualitySsimLoss);
        AssetDatabase.RegisterCustomDependency(QualityLimitsDependency, qualityLimits);

        var astcLimits = new Hash128();
        astcLimits.Append(instance.maxPsnrLoss);
        astcLimits.Append(instance.maxSsimLoss);
        AssetDatabase.RegisterCustomDependency(AstcLimitsDependency, astcLimits);
    }
}

public static class QtxProjectSettingsProvider
{
    public const string SettingsPath = "Project/UniQTX";

    [SettingsProvider]
    private static SettingsProvider Create()
    {
        return new SettingsProvider(SettingsPath, SettingsScope.Project)
        {
            label = "UniQTX",
            keywords = new[] { "QTX", "ASTC", "block size", "quality" },
            guiHandler = _ =>
            {
                var settings = QtxProjectSettings.instance;
                EditorGUI.BeginChangeCheck();

                EditorGUILayout.LabelField("Automatic Quality", EditorStyles.boldLabel);
                var qualityPsnr = EditorGUILayout.DelayedFloatField(new GUIContent("Max PSNR Loss (dB)", "How much lower than at quality 95 the color and alpha PSNR may be, counted on visible pixels"), settings.maxQualityPsnrLoss);
                var qualitySsim = EditorGUILayout.DelayedFloatField(new GUIContent("Max SSIM Loss", "How much lower than at quality 95 the SSIM may be, counted on windows with visible pixels"), settings.maxQualitySsimLoss);

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Automatic ASTC Block Size", EditorStyles.boldLabel);
                var psnr = EditorGUILayout.DelayedFloatField(new GUIContent("Max PSNR Loss (dB)", "How much lower than ASTC 4x4 the color and alpha PSNR of ASTC 6x6 or 8x8 may be"), settings.maxPsnrLoss);
                var ssim = EditorGUILayout.DelayedFloatField(new GUIContent("Max SSIM Loss", "How much lower than ASTC 4x4 the SSIM of ASTC 6x6 or 8x8 may be"), settings.maxSsimLoss);

                if (EditorGUI.EndChangeCheck() == false)
                {
                    return;
                }

                settings.maxQualityPsnrLoss = Math.Max(0, qualityPsnr);
                settings.maxQualitySsimLoss = Math.Max(0, qualitySsim);
                settings.maxPsnrLoss = Math.Max(0, psnr);
                settings.maxSsimLoss = Math.Max(0, ssim);
                settings.SaveAndReimport();
            },
        };
    }
}
