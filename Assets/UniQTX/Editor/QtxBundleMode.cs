using UnityEditor;
using UnityEngine;

public static class QtxBundleMode
{
    public const string DependencyName = "QtxBundleMode";

    public static bool IsActive { get; private set; }

    public static void Set(bool active)
    {
        IsActive = active;
        AssetDatabase.RegisterCustomDependency(DependencyName, Hash128.Compute(active ? 1 : 0));
        AssetDatabase.Refresh();
    }
}
