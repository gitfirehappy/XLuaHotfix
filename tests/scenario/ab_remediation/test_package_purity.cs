using System;

/// <summary>
/// Full/Hotfix 输出独立不可变包，结果面板读取构建事实，包目录不承担累计状态。
/// </summary>
internal static class PackagePurityTests
{
    public static void Run()
    {
        GateChecks.RunAll(
            ("FixedCumulativeDirectoryRetired", VerifyFixedCumulativeDirectoryRetired),
            ("HotfixDeliveryOutputIsIndependent", VerifyHotfixDeliveryOutputIsIndependent),
            ("ResultsViewReadsBuildFacts", VerifyResultsViewReadsBuildFacts));
    }

    /// <summary>固定累计目录 {OutputRoot}/Hotfix/{AA|AB} 及其实现类型、常量必须全部退出。</summary>
    private static void VerifyFixedCumulativeDirectoryRetired()
    {
        GateAssert.FileMissing(
            "Assets/FYAsset/Scripts/Shared/Build/Release/Editor/HotfixAccumulationDirectory.cs",
            "固定累计目录实现类型必须删除");
        GateAssert.NoSymbol(
            RepoSource.ReadCode("Assets/FYAsset/Scripts/Shared/Settings/FYAssetSettings.cs"),
            "HOTFIX_OUTPUT_FOLDER_NAME",
            "FYAssetSettings 不得再声明固定累计目录常量");
        GateAssert.NoSymbol(
            RepoSource.ReadCode("Assets/FYAsset/Scripts/Shared/Build/BuildPathManager.cs"),
            "HotfixOutputRoot",
            "BuildPathManager 不得再暴露固定累计根");
    }

    /// <summary>Hotfix 交付出口必须是独立 Build_* 包目录，不得再解析到固定累计目录。</summary>
    private static void VerifyHotfixDeliveryOutputIsIndependent()
    {
        GateAssert.TreeHasNoSymbol(
            "Assets/FYAsset/Scripts",
            "GetHotfixOutputDir",
            "任何构建代码都不得再把固定累计目录当作输出根");
        GateAssert.NotContains(
            RepoSource.ReadCode("Assets/FYAsset/Scripts/Shared/Build/Release/Editor/BuildExportWriter.cs"),
            "GetHotfixOutputDir",
            "Hotfix 交付不得再解析固定累计目录");
    }

    /// <summary>包管理 UI 必须从构建事实（Summary 记录与制品状态）展示历史包，而不是只扫描当前目录。</summary>
    private static void VerifyResultsViewReadsBuildFacts()
    {
        // API 契约：构建事实读取入口为 BuildSummaryStore，面板必须经它取得历史包与制品状态。
        GateAssert.Contains(
            RepoSource.ReadCode("Assets/FYAsset/Scripts/Shared/Build/Manage/Editor/BuildPackageResultsView.cs"),
            "BuildSummaryStore",
            "包管理面板必须从 BuildSummaryStore 读取历史包与制品状态");
    }
}
