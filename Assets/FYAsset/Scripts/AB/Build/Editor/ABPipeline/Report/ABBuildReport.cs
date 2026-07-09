#if UNITY_EDITOR
using System;
using System.Collections.Generic;

/// <summary>
/// AB 构建报告 DTO。
/// 仅用于 Editor 诊断面板，不参与运行时加载或远端 package 格式。
/// </summary>
[Serializable]
public class ABBuildReport
{
    public int SchemaVersion = 1;
    public ABBuildReportHeader Header = new();
    public ABBuildReportSummary Summary = new();
    public List<ABBuildReportBundle> Bundles = new();
    public List<ABBuildReportAsset> Assets = new();
    public List<ABBuildReportGroup> Groups = new();
    public List<ABBuildReportLabel> Labels = new();
    public List<ABBuildReportIssue> Issues = new();
}

/// <summary>
/// 构建报告头部信息。
/// </summary>
[Serializable]
public class ABBuildReportHeader
{
    public string Backend;
    public string BuildType;
    public string BuildTarget;
    public string Version;
    public string PackageName;
    public string PackagePath;
    public string ReportPath;
    public string StartedAtUtc;
    public string FinishedAtUtc;
    public double DurationSeconds;
    public bool Success;
    public string ErrorCode;
    public string ErrorMessage;
}

/// <summary>
/// 构建报告聚合摘要。
/// </summary>
[Serializable]
public class ABBuildReportSummary
{
    public int TotalTasks;
    public int CompletedTasks;
    public int SkippedTasks;
    public int FailedTasks;
    public int WarningCount;
    public int BundleCount;
    public int AssetCount;
    public int GroupCount;
    public int LabelCount;
    public long TotalBundleSize;
    public int DeliveryBundleCount;
    public long DeliveryBundleSize;
    public int VerificationErrorCount;
    public int VerificationWarningCount;
}

/// <summary>
/// 单个 AB Bundle 报告行。
/// </summary>
[Serializable]
public class ABBuildReportBundle
{
    public string BundleName;
    public string FileHash;
    public uint FileCRC;
    public long FileSize;
    public string BundleType;
    public string Tags;
    public string Group;
    public int AssetCount;
    public int DependencyCount;
    public bool Delivered;
    public List<string> Dependencies = new();
    public List<string> Assets = new();
}

/// <summary>
/// 单个 AB 资源报告行。
/// </summary>
[Serializable]
public class ABBuildReportAsset
{
    public string EntryId;
    public string SourcePath;
    public string Address;
    public string PrimaryType;
    public string Group;
    public string Labels;
    public string BundleName;
    public bool Delivered;
}

/// <summary>
/// 按 Group 聚合的报告行。
/// </summary>
[Serializable]
public class ABBuildReportGroup
{
    public string Group;
    public int AssetCount;
    public int BundleCount;
    public long TotalSize;
}

/// <summary>
/// 按 Label 聚合的报告行。
/// </summary>
[Serializable]
public class ABBuildReportLabel
{
    public string Label;
    public int AssetCount;
    public int BundleCount;
    public long TotalSize;
}

/// <summary>
/// 构建报告问题行。
/// </summary>
[Serializable]
public class ABBuildReportIssue
{
    public string Severity;
    public string Source;
    public string Code;
    public string Subject;
    public string Message;
}
#endif
