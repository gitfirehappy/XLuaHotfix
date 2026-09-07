#if UNITY_EDITOR
using System.IO;
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

    /// <summary>
    /// attempt 布局的构建根：任务链只允许写这里，Runner finalize 成功后才 promote 到最终出口。
    /// 与 HotfixOutput / StreamingAssets 同属项目目录，两个 live 根都在同卷上，可用目录 move 原子 promote。
    /// </summary>
    public static string AttemptPackagesRoot => FYAssetPathUtility.JoinFilePath(OutputRoot, "_attempt");

    public static string PackageIndexPath => FYAssetPathUtility.JoinFilePath(OutputRoot, FYAssetSettings.PACKAGE_INDEX_FILE_NAME);

    public static string StandalonePackageDir => FYAssetPathUtility.JoinFilePath(
        Application.streamingAssetsPath,
        FYAssetSettings.STANDALONE_DIRECTORY_NAME);

    public static string GetPackageDir(string packageName)
    {
        return FYAssetPathUtility.JoinFilePath(PackagesDir, packageName);
    }

    public static string GetAttemptPackageDir(string packageName)
    {
        return FYAssetPathUtility.JoinFilePath(AttemptPackagesRoot, packageName);
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
