using System.Collections.Generic;

/// <summary>
/// Hotfix 交付集合由本次构建内容与作用域最近成功 Full 直接比较得到。
/// </summary>
/// <remarks>
/// H1 与 H2 都直接对 Full 求差，因此 H2 不依赖 H1 是否仍存在；内容恢复到 Full 后不再进入交付集合。
/// </remarks>
internal static class HotfixDeliveryTests
{
    public static void Run()
    {
        GateChecks.RunAll(
            ("DeliveryIsDirectDiffAgainstFullBaseline", VerifyDirectDiffAgainstFullBaseline),
            ("BaselineIsScopeLatestSuccessfulFull", VerifyBaselineSource),
            ("HotfixExportDoesNotReadPreviousHotfixPackage", VerifyNoPreviousHotfixDependency));
    }

    /// <summary>
    /// 交付集合语义：Full→H1 得 A；Full→H2 得 A+B；把 A 恢复成 Full 内容后只剩 B。
    /// </summary>
    private static void VerifyDirectDiffAgainstFullBaseline()
    {
        List<FileHelper.FileDigest> full = Contents(("a.content", "a0"), ("b.content", "b0"), ("c.content", "c0"));
        List<FileHelper.FileDigest> h1 = Contents(("a.content", "a1"), ("b.content", "b0"), ("c.content", "c0"));
        List<FileHelper.FileDigest> h2 = Contents(("a.content", "a1"), ("b.content", "b1"), ("c.content", "c0"));
        List<FileHelper.FileDigest> restored = Contents(("a.content", "a0"), ("b.content", "b1"), ("c.content", "c0"));

        HashSet<string> h1Delivery = Changed(full, h1);
        GateAssert.Equal(1, h1Delivery.Count, "H1 相对 Full 只交付被修改的内容");
        GateAssert.True(h1Delivery.Contains("a.content"), "H1 必须交付被修改的 a");

        HashSet<string> h2Delivery = Changed(full, h2);
        GateAssert.Equal(2, h2Delivery.Count, "H2 相对 Full 必须得到累计的两个变化内容");
        GateAssert.True(
            h2Delivery.Contains("a.content") && h2Delivery.Contains("b.content"),
            "H2 必须得到 A+B 的累计交付集合");

        HashSet<string> restoredDelivery = Changed(full, restored);
        GateAssert.Equal(1, restoredDelivery.Count, "内容恢复为 Full 版本后交付集合只剩余一个变化内容");
        GateAssert.True(restoredDelivery.Contains("b.content"), "恢复后的交付集合必须只剩下 B");
        GateAssert.False(restoredDelivery.Contains("a.content"), "恢复为 Full 内容后不得再交付该内容");

        // 基准是 Full 而不是前一个 Hotfix：删除 H1 不改变 H2 的求差输入。
        GateAssert.Equal(
            2, Changed(full, h2).Count,
            "删除 H1 后 H2 仍必须能相对 Full 得到完整累计集合");
    }

    /// <summary>基准来源：作用域索引里的最近成功 Full，不允许指向历史 Hotfix。</summary>
    private static void VerifyBaselineSource()
    {
        string resolver = RepoSource.ReadCode(
            "Assets/FYAsset/Scripts/Shared/Build/Summary/Editor/HotfixBaselineResolver.cs");
        GateAssert.Contains(
            resolver, "scope.LatestFullSummaryId",
            "Hotfix 基准必须来自作用域最近成功 Full");
        GateAssert.NotContains(
            resolver, "LatestHotfix",
            "Hotfix 基准不得指向历史 Hotfix 包");

        string export = RepoSource.ReadCode(
            "Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/Tasks/ExportABOutputTask.cs");
        GateAssert.Contains(
            export, "HotfixBaselineResolver.TryResolve",
            "Hotfix Export 必须通过 HotfixBaselineResolver 取基准");
    }

    /// <summary>Hotfix 交付不得回退到固定累计目录或历史累计计划。</summary>
    private static void VerifyNoPreviousHotfixDependency()
    {
        string export = RepoSource.ReadCode(
            "Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/Tasks/ExportABOutputTask.cs");
        GateAssert.NotContains(
            export, "HotfixAccumulation",
            "Hotfix 交付不得依赖历史累计目录");
        GateAssert.NotContains(
            export, "HOTFIX_OUTPUT_FOLDER_NAME",
            "Hotfix 交付不得依赖固定累计输出目录");
    }

    private static HashSet<string> Changed(
        IReadOnlyList<FileHelper.FileDigest> previous,
        IReadOnlyList<FileHelper.FileDigest> current)
    {
        FileHelper.ComputeDiff(previous, current,
            out List<FileHelper.FileDigest> added,
            out List<FileHelper.FileDigest> modified,
            out _,
            out _);
        return FileHelper.CollectChangedNames(added, modified);
    }

    private static List<FileHelper.FileDigest> Contents(params (string Name, string Hash)[] entries)
    {
        var contents = new List<FileHelper.FileDigest>();
        for (int i = 0; i < entries.Length; i++)
        {
            contents.Add(new FileHelper.FileDigest(entries[i].Name, entries[i].Hash, 0u, entries[i].Hash.Length));
        }

        return contents;
    }
}
