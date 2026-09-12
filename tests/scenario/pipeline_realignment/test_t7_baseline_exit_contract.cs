using System;

/// <summary>
/// 文件摘要和比较能力集中在 FileHelper。
/// </summary>
internal static class BaselineExitContractTests
{
    private const string ScriptsDir = "Assets/FYAsset/Scripts";
    private const string SharedBuildDir = "Assets/FYAsset/Scripts/Shared/Build";
    private const string BaselineDir = "Assets/FYAsset/Scripts/Shared/Build/Baseline";
    private const string SnapshotsDir = "Assets/FYAsset/Scripts/Shared/Build/Snapshots";
    private const string RepositoryDir = "Assets/FYAsset/Scripts/Shared/Build/Repository";
    private const string CompatRepositoryDir = "Assets/FYAsset/Scripts/Compat/Editor/Repository";
    private const string BaselineHandlerFile =
        "Assets/FYAsset/Scripts/Shared/Build/Release/Editor/IBaselinePackageHandler.cs";
    private const string ABRepositoryPreviewFile = "Assets/FYAsset/Scripts/AB/Build/Editor/ABRepositoryPreview.cs";
    private const string AARepositoryPreviewFile = "Assets/FYAsset/Scripts/AA/Build/Editor/AARepositoryPreview.cs";
    private const string PublisherFile = "Assets/FYAsset/Scripts/Shared/Build/Publish/BuildPublisher.cs";
    private const string RunnerFile = "Assets/FYAsset/Scripts/Shared/Build/Pipeline/Editor/BuildPipelineRunner.cs";

    public static void Run()
    {
        GateChecks.RunAll(
            ("BaselineDirectoryRemoved", VerifyBaselineDirectoryRemoved),
            ("SnapshotsDirectoryRemoved", VerifySnapshotsDirectoryRemoved),
            ("RepositoryDirectoryRemoved", VerifyRepositoryDirectoryRemoved),
            ("CompatRepositoryDirectoryRemoved", VerifyCompatRepositoryDirectoryRemoved),
            ("BaselineSymbolsRemoved", VerifyBaselineSymbolsRemoved),
            ("RepositoryAndDiffSymbolsRemoved", VerifyRepositoryAndDiffSymbolsRemoved),
            ("BaselineJsonLiteralRemoved", VerifyBaselineJsonLiteralRemoved),
            ("BaselineHandlerFileRemoved", VerifyBaselineHandlerFileRemoved),
            ("RepositoryPreviewFilesRemoved", VerifyRepositoryPreviewFilesRemoved),
            ("FileHelperComparisonDeclared", VerifyFileHelperComparisonDeclared),
            ("BuildSummaryAndCacheTypesDeclared", VerifyBuildSummaryAndCacheTypesDeclared),
            ("PublisherOwnsPackageIndex", VerifyPublisherOwnsPackageIndex),
            ("RunnerDoesNotWritePackageIndex", VerifyRunnerDoesNotWritePackageIndex));
    }

    /// <summary>baseline 目录整体删除：Latest/LatestFull 指针语义不再存在。</summary>
    private static void VerifyBaselineDirectoryRemoved()
    {
        GateAssert.DirectoryMissing(
            BaselineDir,
            "计划 T7 要求删除 BuildBaseline/BuildBaselineStore/Latest/LatestFull 与 baseline.json 持久化，"
            + "baseline 目录整体退出正式路径");
    }

    /// <summary>快照目录删除：比较改为无状态。</summary>
    private static void VerifySnapshotsDirectoryRemoved()
    {
        GateAssert.DirectoryMissing(
            SnapshotsDir,
            "无状态文件比较直接由 FileHelper 提供，旧的独立比较类型不得保留");
    }

    /// <summary>正式 Repository 命名删除：HEAD/Commit/Repair/PushHistory 残留不得保留。</summary>
    private static void VerifyRepositoryDirectoryRemoved()
    {
        GateAssert.DirectoryMissing(
            RepositoryDir,
            "计划 T7 要求删除正式 Repository 命名与 HEAD/Commit/Repair/PushHistory 残留，"
            + "Shared 构建目录内不得保留 Repository 目录");
    }

    /// <summary>Compat 下的 Repository CLI 目录删除：Compat 只能是本仓库测试/宿主胶水。</summary>
    private static void VerifyCompatRepositoryDirectoryRemoved()
    {
        GateAssert.DirectoryMissing(
            CompatRepositoryDir,
            "计划 T7 要求删除 BuildRepositoryCLI，Compat 只允许作为本仓库测试/宿主胶水，"
            + "不得保留正式 Repository 入口目录");
    }

    /// <summary>baseline 与 store 相关类型在源码树中不得再出现。</summary>
    private static void VerifyBaselineSymbolsRemoved()
    {
        GateAssert.TreeHasNoSymbol(
            ScriptsDir,
            "BuildBaseline",
            "计划 T7 要求 BuildBaseline 从正式路径消失（成功标准：BuildBaselineStore 与 Repository 语义退出）");
        GateAssert.TreeHasNoSymbol(
            ScriptsDir,
            "BuildBaselineStore",
            "计划 T7 要求 BuildBaselineStore 从正式路径消失，构建不再依赖基线存储");
        GateAssert.TreeHasNoSymbol(
            ScriptsDir,
            "LatestFull",
            "计划 T7 要求 Latest/LatestFull 指针语义从正式路径消失，"
            + "当前包事实改由本地 PackageIndex 与 BuildIndex 表达");
        GateAssert.TreeHasNoSymbol(
            ScriptsDir,
            "IBaselinePackageHandler",
            "计划 T7 删除 baseline 包处理器接口，热更状态机不再以 baseline 概念描述本地包");
    }

    /// <summary>Repository 预览与 Artifact 差异类型在源码树中不得再出现。</summary>
    private static void VerifyRepositoryAndDiffSymbolsRemoved()
    {
        GateAssert.TreeHasNoSymbol(
            ScriptsDir,
            "ArtifactDelta",
            "无状态文件比较直接由 FileHelper 提供，旧的独立比较类型不得保留");
        GateAssert.TreeHasNoSymbol(
            ScriptsDir,
            "ArtifactDiffer",
            "计划 T7 要求 ArtifactDiffer 删除，Diff 只比较两份摘要，不读 Git、PackageIndex、版本库或历史状态");
        GateAssert.TreeHasNoSymbol(
            ScriptsDir,
            "BuildDiffEntry",
            "无状态文件事实统一由 FileHelper.FileDigest 表达");
        GateAssert.TreeHasNoSymbol(
            ScriptsDir,
            "BuildRepositoryCLI",
            "计划 T7 要求删除 BuildRepositoryCLI，框架不引入 Git、不管理外部版本历史");
        GateAssert.TreeHasNoSymbol(
            ScriptsDir,
            "RepositoryPreviewRunner",
            "计划 T7 要求 Repository 预览入口删除，预览必须直接调用无副作用的扫描、Compose 或 Diff 服务");
    }

    /// <summary>
    /// baseline.json 是字符串字面量，净化源码会清空字符串内容，因此本子契约直接检查文件原文。
    /// </summary>
    private static void VerifyBaselineJsonLiteralRemoved()
    {
        var files = RepoSource.CsFiles(ScriptsDir);
        for (int i = 0; i < files.Count; i++)
        {
            GateAssert.NotContains(
                RepoSource.Read(files[i]),
                "baseline.json",
                "计划 T7 要求删除 baseline.json 持久化文件与其读写代码，"
                + $"{files[i]} 仍以字符串字面量引用该文件名");
        }
    }

    /// <summary>baseline 包处理器接口文件必须删除。</summary>
    private static void VerifyBaselineHandlerFileRemoved()
    {
        GateAssert.FileMissing(
            BaselineHandlerFile,
            "计划 T7 删除 baseline 物化/交付路径，对应接口文件必须删除");
    }

    /// <summary>AA/AB 的 Repository 预览面板文件必须删除。</summary>
    private static void VerifyRepositoryPreviewFilesRemoved()
    {
        GateAssert.FileMissing(
            ABRepositoryPreviewFile,
            "计划 T7 要求删除 AB Repository Preview，仓库不再提供基线预览入口");
        GateAssert.FileMissing(
            AARepositoryPreviewFile,
            "计划 T7 要求删除 AA Repository Preview，仓库不再提供基线预览入口");
    }

    /// <summary>文件摘要和比较能力集中在 FileHelper。</summary>
    private static void VerifyFileHelperComparisonDeclared()
    {
        GateAssert.TreeHasSymbol(
            ScriptsDir,
            "FileHelper.FileDigest",
            "FileHelper 应提供摘要和直接差异比较能力");
        GateAssert.TreeHasNoSymbol(
            ScriptsDir,
            "public sealed class FileDiff",
            "旧的独立差异类型不得保留");
        GateAssert.TreeHasNoSymbol(
            ScriptsDir,
            "class FileDiff",
            "旧的独立差异类型不得保留");
    }

    /// <summary>
    /// 构建摘要与发布缓存类型必须存在且职责独立；不存在独立的 Bundle 制品缓存，
    /// 历史制品复用由 Summary 承载（内容身份、输入指纹和制品摘要）。
    /// 类型位置不作为契约，复用索引归 AB 构建侧。
    /// </summary>
    private static void VerifyBuildSummaryAndCacheTypesDeclared()
    {
        GateAssert.TreeHasSymbol(
            ScriptsDir,
            "CompleteBuildSummary",
            "构建摘要应作为构建结果数据源");
        GateAssert.TreeHasSymbol(
            ScriptsDir,
            "SummaryContentFact",
            "Summary 应记录内容身份、输入指纹与制品摘要");
        GateAssert.TreeHasNoSymbol(
            ScriptsDir,
            "BundleBuildCacheEntry",
            "独立制品缓存条目类型不得再出现");
        GateAssert.TreeHasSymbol(
            ScriptsDir,
            "PublishCache",
            "发布缓存应独立存储，只作服务器事实的辅助");
    }

    /// <summary>Publisher 负责组装新隔离目录并在最后生成上传 PackageIndex。</summary>
    private static void VerifyPublisherOwnsPackageIndex()
    {
        GateAssert.FileExists(
            PublisherFile,
            "计划 T7 要求保留发布入口：Publisher 本地组装并校验新隔离目录，最后生成并上传 PackageIndex");

        string code = RepoSource.ReadCode(PublisherFile);
        GateAssert.HasSymbol(
            code,
            "PackageIndex",
            "计划 T7 规定 PackageIndex 只能由 Publisher 在所有内容上传并校验后最后生成上传，"
            + $"且发布时读取服务器 PackageIndex + Manifest 核对；{PublisherFile} 未引用 PackageIndex");
    }

    /// <summary>构建 Runner 不写 PackageIndex。</summary>
    private static void VerifyRunnerDoesNotWritePackageIndex()
    {
        GateAssert.FileExists(RunnerFile, "计划 T6 规定 Runner 只执行 Composer 结果，该文件必须存在");

        string code = RepoSource.ReadCode(RunnerFile);
        GateAssert.NoSymbol(
            code,
            "PackageIndex",
            "计划 T7 明确 BuildRunner 不写 PackageIndex，PackageIndex 是发布事务的最后一步；"
            + $"{RunnerFile} 仍引用 PackageIndex，说明构建仍然承担发布职责");
    }
}
