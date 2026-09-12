#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// 包目录文件扫描：把目录树展开成包根相对路径的 <see cref="FileDigest"/> 集合。
/// </summary>
/// <remarks>
/// 口径约定（计划 T2）：
/// 1. 名称使用 '/' 分隔的包根相对路径（例如 bundles/a.bundle），跨平台稳定；
/// 2. 包目录只含发布内容，扫描不再需要排除构建元数据；
/// 3. 单个文件不可读时返回失败原因，由调用方决定中止还是降级，不静默跳过。
/// </remarks>
public static class PackageFileScanner
{
    /// <summary>扫描目录内全部发布文件（含子目录），按名称排序。</summary>
    public static bool TryScan(string rootDir, out List<FileDigest> files, out string error)
    {
        files = new List<FileDigest>();
        error = string.Empty;

        if (string.IsNullOrEmpty(rootDir) || !FileHelper.DirectoryExists(rootDir))
        {
            error = $"目录不存在: {rootDir}";
            return false;
        }

        string[] paths = FileHelper.GetFiles(rootDir, "*", SearchOption.AllDirectories);
        for (int i = 0; i < paths.Length; i++)
        {
            string relative = ToPackageRelativeName(rootDir, paths[i]);
            if (!FileDigest.TryCreate(paths[i], relative, out FileDigest digest))
            {
                error = $"文件摘要计算失败（缺失、被占用或不可读）: {paths[i]}";
                return false;
            }

            files.Add(digest);
        }

        files.Sort(CompareByName);
        return true;
    }

    /// <summary>扫描单个内容目录（例如包根下的 bundles），名称仍是包根相对路径。</summary>
    public static bool TryScanContentDirectory(
        string packageRoot,
        string contentDirectory,
        out List<FileDigest> files,
        out string error)
    {
        files = new List<FileDigest>();
        error = string.Empty;

        if (string.IsNullOrEmpty(contentDirectory) || !FileHelper.DirectoryExists(contentDirectory))
            return true;

        string[] paths = FileHelper.GetFiles(contentDirectory, "*", SearchOption.AllDirectories);
        for (int i = 0; i < paths.Length; i++)
        {
            string relative = ToPackageRelativeName(packageRoot, paths[i]);
            if (!FileDigest.TryCreate(paths[i], relative, out FileDigest digest))
            {
                error = $"文件摘要计算失败（缺失、被占用或不可读）: {paths[i]}";
                return false;
            }

            files.Add(digest);
        }

        files.Sort(CompareByName);
        return true;
    }

    /// <summary>把绝对路径转换成 '/' 分隔的包根相对路径。</summary>
    public static string ToPackageRelativeName(string rootDir, string filePath)
    {
        string relative = FYAssetPathUtility.GetRelativeFilePath(rootDir, filePath);
        return string.IsNullOrEmpty(relative)
            ? Path.GetFileName(filePath)
            : relative.Replace('\\', '/');
    }

    /// <summary>按名称建立查找表，便于按名称核验文件集合。</summary>
    public static Dictionary<string, FileDigest> IndexByName(IReadOnlyList<FileDigest> files) =>
        FileDiff.IndexByName(files);

    private static int CompareByName(FileDigest left, FileDigest right) =>
        string.CompareOrdinal(left.Name, right.Name);
}
#endif
