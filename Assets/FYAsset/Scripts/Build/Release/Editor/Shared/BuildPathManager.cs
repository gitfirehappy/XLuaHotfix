#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 构建侧路径管理。
/// 管理构建输出根目录、package root 和 Addressables ServerData 路径。
/// </summary>
public static class BuildPathManager
{
    public static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

    public static string OutputRoot => ResolveProjectRelativePath(FYAssetSettings.Instance.BuildOutputRoot);

    public static string PackagesDir => Path.Combine(OutputRoot, FYAssetSettings.Instance.BuildPackagesFolderName);

    public static string PackageIndexPath => Path.Combine(OutputRoot, FYAssetSettings.PACKAGE_INDEX_FILE_NAME);

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

    private static string ResolveProjectRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return ProjectRoot;

        string normalized = path.Replace('\\', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized))
            return normalized;

        return Path.Combine(ProjectRoot, normalized);
    }
}
#endif
