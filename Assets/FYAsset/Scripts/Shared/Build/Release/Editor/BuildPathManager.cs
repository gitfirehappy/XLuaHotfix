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
    public static string ProjectRoot => FYAssetPathUtility.NormalizePath(Directory.GetParent(Application.dataPath).FullName);

    public static string OutputRoot => ResolveProjectRelativePath(FYAssetSettings.Instance.BuildOutputRoot);

    public static string PackagesDir => FYAssetPathUtility.JoinFilePath(OutputRoot, FYAssetSettings.Instance.BuildPackagesFolderName);

    public static string PackageIndexPath => FYAssetPathUtility.JoinFilePath(OutputRoot, FYAssetSettings.PACKAGE_INDEX_FILE_NAME);

    public static string GetPackageDir(string packageName)
    {
        return FYAssetPathUtility.JoinFilePath(PackagesDir, packageName);
    }

    public static string GetBundlesDir(string packageDir)
    {
        return FYAssetPathUtility.JoinFilePath(packageDir, FYAssetSettings.BUNDLES_DIRECTORY_NAME);
    }

    private static string ResolveProjectRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return ProjectRoot;

        return FYAssetPathUtility.ResolveFilePath(ProjectRoot, path);
    }
}
#endif
