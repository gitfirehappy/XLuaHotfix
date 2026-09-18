#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// 包目录文件扫描：把目录树展开成包根相对路径的 <see cref="FileHelper.FileDigest"/> 集合。
/// </summary>
/// <remarks>
/// 文件名统一为包根相对路径，扫描失败时返回原因，由发布流程决定是否中止。
/// </remarks>
internal static class PackageFileScanner
{
    /// <summary>扫描目录内全部文件，并使用包根相对路径作为名称。</summary>
    public static bool TryScan(string rootDir, out List<FileHelper.FileDigest> files, out string error)
    {
        return FileHelper.TryScanFiles(rootDir,
            path => ToPackageRelativeName(rootDir, path), out files, out error);
    }

    /// <summary>扫描包内容目录，并使用包根相对路径作为名称。</summary>
    public static bool TryScanContentDirectory(
        string packageRoot,
        string contentDirectory,
        out List<FileHelper.FileDigest> files,
        out string error)
    {
        if (string.IsNullOrEmpty(contentDirectory) || !FileHelper.DirectoryExists(contentDirectory))
        {
            files = new List<FileHelper.FileDigest>();
            error = string.Empty;
            return true;
        }

        return FileHelper.TryScanFiles(contentDirectory,
            path => ToPackageRelativeName(packageRoot, path), out files, out error);
    }

    /// <summary>把文件路径转换为 '/' 分隔的包根相对路径。</summary>
    public static string ToPackageRelativeName(string rootDir, string filePath)
    {
        string relative = FYAssetPathUtility.GetRelativeFilePath(rootDir, filePath);
        return string.IsNullOrEmpty(relative)
            ? Path.GetFileName(filePath)
            : relative.Replace('\\', '/');
    }
}
#endif
