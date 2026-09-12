#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 发布侧的独立维护入口：清理服务器上的旧包目录。
/// </summary>
/// <remarks>
/// 计划 T7：旧包清理与发布事务解耦。
/// 1. 发布本身永不删除旧包；
/// 2. 本入口只删除服务器包目录中**不被当前 PackageIndex 指向**的包；
/// 3. 当前 PackageIndex 无法读取时拒绝清理——无法确定当前包时宁可不动；
/// 4. 只处理形如 `Build_*` 的直接子目录，不递归、不跟随符号链接、不触碰其他文件。
/// </remarks>
public static class PublishMaintenance
{
    private const string LogPrefix = "[PublishMaintenance]";

    /// <summary>一次清理操作的结果。</summary>
    public sealed class CleanupResult
    {
        public bool Success;
        public string FailureReason = string.Empty;
        public string KeptPackage = string.Empty;
        public List<string> DeletedPackages = new List<string>();
        public List<string> SkippedEntries = new List<string>();
    }

    /// <summary>
    /// 删除服务器后端根下不被当前 PackageIndex 指向的包目录。
    /// </summary>
    /// <param name="serverBackendRoot">服务器后端根目录（例如 {serviceRoot}/AB）</param>
    /// <param name="packagesFolderName">包目录集合名，需与运行时读取远端包的设置一致</param>
    public static CleanupResult DeleteUnreferencedPackages(string serverBackendRoot, string packagesFolderName)
    {
        var result = new CleanupResult();
        if (string.IsNullOrWhiteSpace(serverBackendRoot))
        {
            result.FailureReason = "服务器后端根目录为空。";
            return result;
        }

        string root = FYAssetPathUtility.NormalizePath(Path.GetFullPath(serverBackendRoot));
        if (!PublishPathGuard.IsSafeSegment(packagesFolderName))
        {
            result.FailureReason = $"包集合名不是合法单段目录名: '{packagesFolderName}'";
            return result;
        }

        string packagesRoot = FYAssetPathUtility.JoinFilePath(root, packagesFolderName);
        if (!PublishPathGuard.IsContainedIn(root, packagesRoot))
        {
            result.FailureReason = $"包目录集合越出服务器后端根: {packagesRoot}";
            return result;
        }
        if (!PublishPathGuard.HasNoReparsePoint(root, out string reparseError))
        {
            result.FailureReason = reparseError;
            return result;
        }
        if (!FileHelper.DirectoryExists(packagesRoot))
        {
            result.FailureReason = $"服务器包目录集合不存在: {packagesRoot}";
            return result;
        }

        string indexPath = FYAssetPathUtility.JoinFilePath(root, FYAssetSettings.PACKAGE_INDEX_FILE_NAME);
        if (!TryReadCurrentPackage(indexPath, out string currentPackage, out string indexError))
        {
            // 无法确定当前包时不清理：删掉正在被客户端下载的包比留下旧包代价大得多。
            result.FailureReason = $"无法确定当前发布包，拒绝清理: {indexError}";
            return result;
        }

        result.KeptPackage = currentPackage;
        string[] directories = FileHelper.GetDirectories(packagesRoot, "Build_*");
        for (int i = 0; i < directories.Length; i++)
        {
            string directory = directories[i];
            string name = Path.GetFileName(directory);
            if (string.Equals(name, currentPackage, StringComparison.Ordinal))
                continue;

            if (!IsSafeChildPath(packagesRoot, directory))
            {
                result.SkippedEntries.Add($"{name}（不在包目录集合内）");
                continue;
            }

            if (FileHelper.TryDeleteDirectory(directory, true))
            {
                result.DeletedPackages.Add(name);
                Debug.Log($"{LogPrefix} 已删除旧包目录: {directory}");
            }
            else
            {
                result.SkippedEntries.Add($"{name}（删除失败）");
            }
        }

        result.Success = true;
        return result;
    }

    /// <summary>读取当前发布包名；索引缺失、损坏或缺少包名时返回 false。</summary>
    public static bool TryReadCurrentPackage(string packageIndexPath, out string packageName, out string error)
    {
        packageName = string.Empty;
        error = string.Empty;

        if (!FileHelper.Exists(packageIndexPath))
        {
            error = $"PackageIndex 不存在: {packageIndexPath}";
            return false;
        }

        try
        {
            string json = FileHelper.ReadAllText(packageIndexPath);
            PackageIndex index = SerializationUtility.DeserializeJson<PackageIndex>(json);
            if (index == null || string.IsNullOrEmpty(index.LatestPackage))
            {
                error = $"PackageIndex 未指向任何包: {packageIndexPath}";
                return false;
            }

            packageName = index.LatestPackage;
            return true;
        }
        catch (Exception ex)
        {
            error = $"{packageIndexPath} — {ex.Message}";
            return false;
        }
    }

    private static bool IsSafeChildPath(string rootPath, string childPath)
    {
        if (string.IsNullOrEmpty(rootPath) || string.IsNullOrEmpty(childPath))
            return false;

        string root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                      + Path.DirectorySeparatorChar;
        string child = Path.GetFullPath(childPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;
        return child.StartsWith(root, StringComparison.OrdinalIgnoreCase)
               && !string.Equals(child, root, StringComparison.OrdinalIgnoreCase);
    }
}
#endif
