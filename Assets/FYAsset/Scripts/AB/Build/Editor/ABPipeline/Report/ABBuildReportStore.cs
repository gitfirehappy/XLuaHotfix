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
        return SerializationUtility.DeserializeJson<ABBuildReport>(json);
    }

    public static List<string> ListReportPaths()
    {
        var result = new List<string>();
        string[] files = FileHelper.GetFiles(ReportsDirectory, "*" + ReportFileExtension, SearchOption.TopDirectoryOnly);
        if (files == null || files.Length == 0)
            return result;

        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        for (int i = files.Length - 1; i >= 0; i--)
            result.Add(FYAssetPathUtility.NormalizePath(files[i]));
        return result;
    }

    public static string GetLatestReportPath()
    {
        List<string> paths = ListReportPaths();
        return paths.Count > 0 ? paths[0] : string.Empty;
    }

    public static void RevealReportsDirectory()
    {
        FileHelper.EnsureDirectory(ReportsDirectory);
        UnityEditor.EditorUtility.RevealInFinder(ReportsDirectory);
    }

    #endregion
}
#endif
