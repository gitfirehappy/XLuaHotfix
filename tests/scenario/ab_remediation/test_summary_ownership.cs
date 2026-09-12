using System;

/// <summary>
/// T1/T2 目标契约：构建事实落在 BuildData/Summaries，包目录只含发布内容，版本状态由 Summary Index 承载。
/// </summary>
internal static class SummaryOwnershipTests
{
    public static void Run()
    {
        GateChecks.RunAll(
            ("SummaryStorePathDeclared", VerifySummaryStorePathDeclared),
            ("SummaryIndexDeclared", VerifySummaryIndexDeclared),
            ("ExportTasksDoNotWriteSummaryIntoPackage", VerifyExportTasksDoNotWriteSummaryIntoPackage),
            ("PackageIdentityDoesNotReadPackageSummary", VerifyPackageIdentityDoesNotReadPackageSummary),
            ("ScannerDoesNotFilterBuildMetadata", VerifyScannerDoesNotFilterBuildMetadata),
            ("VersionRecordRetired", VerifyVersionRecordRetired));
    }

    /// <summary>正式摘要路径必须是 BuildData/Summaries（不进入包目录）。</summary>
    private static void VerifySummaryStorePathDeclared()
    {
        string source = RepoSource.RawTreeText("Assets/FYAsset/Scripts/Shared/Build");
        GateAssert.Contains(source, "Summaries", "构建侧必须声明 BuildData/Summaries 摘要存储路径");
    }

    /// <summary>Summary Index 承载项目版本与作用域最新 Summary 引用，字段名属于计划契约。</summary>
    private static void VerifySummaryIndexDeclared()
    {
        string source = RepoSource.RawTreeText("Assets/FYAsset/Scripts/Shared/Build");
        GateAssert.Contains(source, "LatestFullSummaryId", "Summary Index 必须声明作用域 LatestFullSummaryId");
        GateAssert.Contains(source, "LatestSuccessfulSummaryId", "Summary Index 必须声明作用域 LatestSuccessfulSummaryId");
    }

    /// <summary>Export 阶段不得把构建摘要写进包输出目录。</summary>
    private static void VerifyExportTasksDoNotWriteSummaryIntoPackage()
    {
        GateAssert.NotContains(
            RepoSource.ReadCode("Assets/FYAsset/Scripts/AA/Build/Pipeline/Editor/Tasks/ExportAAOutputTask.cs"),
            "WriteSummaryFiles",
            "AA Export 不得把构建摘要写入包目录");
        GateAssert.NotContains(
            RepoSource.ReadCode("Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/Tasks/ExportABOutputTask.cs"),
            "WriteSummaryFiles",
            "AB Export 不得把构建摘要写入包目录");
    }

    /// <summary>包身份不得从包目录内的构建摘要读取；包目录只含发布内容。</summary>
    private static void VerifyPackageIdentityDoesNotReadPackageSummary()
    {
        string code = RepoSource.ReadCode("Assets/FYAsset/Scripts/Shared/Build/Publish/PackageBuildIdentity.cs");
        GateAssert.NoSymbol(code, "TryReadFromPackageDir", "包身份不得从包目录读取构建摘要");
        GateAssert.NotContains(code, "PackageFileNames.BuildSummaryJson", "包身份不得引用包内摘要文件名");
    }

    /// <summary>包纯净后文件扫描器不需要过滤构建元数据。</summary>
    private static void VerifyScannerDoesNotFilterBuildMetadata()
    {
        GateAssert.NoSymbol(
            RepoSource.ReadCode("Assets/FYAsset/Scripts/Shared/Build/Publish/PackageFileScanner.cs"),
            "IsBuildMetadata",
            "包文件扫描器不得再以跳过构建元数据的方式工作");
    }

    /// <summary>VersionRecord 类型、资产、设置路径与重置工具处理全部退出正式路径。</summary>
    private static void VerifyVersionRecordRetired()
    {
        GateAssert.FileMissing(
            "Assets/FYAsset/Scripts/Shared/Build/Versioning/VersionRecord.cs",
            "VersionRecord 类型必须删除");
        GateAssert.FileMissing("Assets/Build/VersionRecord.asset", "VersionRecord 资产必须删除");
        GateAssert.TreeHasNoSymbol(
            "Assets/FYAsset/Scripts/Shared",
            "VersionRecord",
            "Shared 不得再引用 VersionRecord");
        GateAssert.NotContains(
            RepoSource.Read("CommandLine/fyasset_reset.py"),
            "VersionRecord",
            "重置工具不得再处理 VersionRecord");
    }
}
