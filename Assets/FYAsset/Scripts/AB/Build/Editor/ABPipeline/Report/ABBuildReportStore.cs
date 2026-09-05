#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// AB 构建报告文件存储。
/// 报告只写入 BuildData/Reports/AB，不进入 package 输出。
/// </summary>
public static class ABBuildReportStore
{
    public const string ReportFileExtension = ".json";

    private const string ReportsRootSegment = "BuildData";
    private const string ReportsFolderSegment = "Reports";
    private const string BackendFolderSegment = "AB";

    #region Paths

    public static string ReportsDirectory =>
        FYAssetPathUtility.JoinFilePath(BuildPathManager.ProjectRoot, ReportsRootSegment, ReportsFolderSegment, BackendFolderSegment);

    public static string CreateReportPath(BuildPackageRequest request)
    {
        string packageName = request != null && !string.IsNullOrEmpty(request.PackageName)
            ? request.PackageName
            : "UnknownPackage_" + DateTime.UtcNow.ToString(BuildPackageRequest.PackageTimestampFormat);

        string fileName = packageName + "_ABBuildReport" + ReportFileExtension;
        return FYAssetPathUtility.JoinFilePath(ReportsDirectory, fileName);
    }

    #endregion

    #region Read Write

    public static string Write(ABBuildReport report, string path)
    {
        if (report == null)
            throw new ArgumentNullException(nameof(report));
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(nameof(path));

        report.Header.ReportPath = path;
        string json = SerializationUtility.SerializeToJson(report, true);
        FileHelper.WriteAllTextAtomic(path, json);
        return path;
    }

    public static ABBuildReport Read(string path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(nameof(path));

        string json = FileHelper.ReadAllText(path);
        ABBuildReport report = SerializationUtility.DeserializeJson<ABBuildReport>(json);
        if (report?.Bundles == null)
            return report;

        for (int i = 0; i < report.Bundles.Count; i++)
        {
            ABBuildReportBundle bundle = report.Bundles[i];
            if (bundle == null)
                continue;
            bundle.Dependencies ??= new List<string>();
            bundle.ReferencedBy ??= new List<string>();
            bundle.Assets ??= new List<string>();
        }
        return report;
    }

    public static List<string> ListReportPaths()
    {
        return ListReportPaths(ReportsDirectory);
    }

    internal static List<string> ListReportPathsByPackagePath(string packagePath, string reportsDirectory)
    {
        var result = new List<string>();
        string normalizedPackagePath = NormalizeComparablePath(packagePath);
        if (string.IsNullOrEmpty(normalizedPackagePath))
            return result;

        List<string> reportPaths = ListReportPaths(reportsDirectory);
        for (int i = 0; i < reportPaths.Count; i++)
        {
            try
            {
                ABBuildReport report = Read(reportPaths[i]);
                string reportPackagePath = NormalizeComparablePath(report?.Header?.PackagePath);
                if (string.Equals(reportPackagePath, normalizedPackagePath, StringComparison.OrdinalIgnoreCase))
                    result.Add(reportPaths[i]);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{nameof(ABBuildReportStore)}] 检查 report 失败：{reportPaths[i]}, {ex.Message}");
            }
        }

        return result;
    }

    public static List<string> ListReportPathsByPackagePath(string packagePath)
    {
        return ListReportPathsByPackagePath(packagePath, ReportsDirectory);
    }

    internal static int DeleteReportsByPackagePath(string packagePath, string reportsDirectory, List<string> failedPaths)
    {
        int deleted = 0;
        List<string> paths = ListReportPathsByPackagePath(packagePath, reportsDirectory);
        for (int i = 0; i < paths.Count; i++)
        {
            if (TryDeleteReport(paths[i], reportsDirectory))
                deleted++;
            else
                failedPaths?.Add(paths[i]);
        }

        return deleted;
    }

    public static int DeleteReportsByPackagePath(string packagePath, List<string> failedPaths)
    {
        return DeleteReportsByPackagePath(packagePath, ReportsDirectory, failedPaths);
    }

    public static bool TryDeleteReport(string path)
    {
        return TryDeleteReport(path, ReportsDirectory);
    }

    public static bool HasPackageConflict(ABBuildReport report)
    {
        return report?.Header != null
            && report.Header.Success
            && (string.IsNullOrWhiteSpace(report.Header.PackagePath)
                || !FileHelper.DirectoryExists(report.Header.PackagePath));
    }

    private static List<string> ListReportPaths(string reportsDirectory)
    {
        var result = new List<string>();
        string[] files = FileHelper.GetFiles(reportsDirectory, "*" + ReportFileExtension, SearchOption.TopDirectoryOnly);
        if (files == null || files.Length == 0)
            return result;

        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        for (int i = files.Length - 1; i >= 0; i--)
            result.Add(FYAssetPathUtility.NormalizePath(files[i]));
        return result;
    }

    private static bool TryDeleteReport(string path, string reportsDirectory)
    {
        if (!IsPathInsideDirectory(reportsDirectory, path))
        {
            Debug.LogError($"[{nameof(ABBuildReportStore)}] 拒绝删除 ReportsDirectory 外的 report：{path}");
            return false;
        }

        return FileHelper.TryDelete(path);
    }

    private static bool IsPathInsideDirectory(string directory, string path)
    {
        string root = NormalizeComparablePath(directory);
        string candidate = NormalizeComparablePath(path);
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(candidate))
            return false;

        root += Path.DirectorySeparatorChar;
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeComparablePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    public static void RevealReportsDirectory()
    {
        FileHelper.EnsureDirectory(ReportsDirectory);
        UnityEditor.EditorUtility.RevealInFinder(ReportsDirectory);
    }

    #endregion
}
#endif
