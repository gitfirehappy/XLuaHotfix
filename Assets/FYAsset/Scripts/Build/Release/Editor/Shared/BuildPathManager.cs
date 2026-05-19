#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 构建侧路径管理。
/// 管理项目目录下 HotfixOutput、Packages、package root 和 Addressables ServerData 路径。
/// </summary>
public static class BuildPathManager
{
    public static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

    public static string OutputRoot => Path.Combine(ProjectRoot, "HotfixOutput");

    public static string PackagesDir => Path.Combine(OutputRoot, "Packages");

    public static string PackageIndexPath => Path.Combine(OutputRoot, FYAssetSettings.MANIFEST_FILE_NAME);

    public static string GetPackageDir(string packageName)
    {
        return Path.Combine(PackagesDir, packageName);
    }

    public static string GetBundlesDir(string packageDir)
    {
        return Path.Combine(packageDir, "bundles");
    }

    public static string GetServerDataDir()
    {
        string platformSubDir = EditorUserBuildSettings.activeBuildTarget.ToString();
        return Path.Combine(ProjectRoot, "ServerData", platformSubDir);
    }
}
#endif
