using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// 版本候选、摘要不可变、索引重建与损坏恢复契约。
/// </summary>
internal static class SummaryTransactionTests
{
    public static void Run()
    {
        GateChecks.RunAll(
            ("VersionPlanFullAdvancesMajor", VerifyFullAdvancesMajor),
            ("VersionPlanHotfixAdvancesPatch", VerifyHotfixAdvancesPatch),
            ("VersionPlanInheritsChannel", VerifyChannelInheritance),
            ("VersionPlanRejectsChannelDowngrade", VerifyChannelDowngradeRejected),
            ("VersionPlanRequiresFullBaselineForHotfix", VerifyHotfixRequiresBaseline),
            ("VersionPlanStartsAtOneZeroZero", VerifyFirstBuildVersion),
            ("SummaryIndexRebuildsLocators", VerifyIndexRebuild),
            ("SummaryStoreRejectsImmutableOverwrite", VerifyImmutableSummary),
            ("SummaryIndexRebuildsAfterCorruption", VerifyIndexCorruptionRecovery));
    }

    private static void VerifyFullAdvancesMajor()
    {
        BuildVersionPlan plan = BuildVersionPlanner.Plan("1.2.3", BuildType.Full, null);
        GateAssert.True(plan.Success, $"Full 版本计算必须成功: {plan.Error}");
        GateAssert.Equal("2.0.0", plan.Version.GetReleaseVersionString(), "Full 默认推进 Major 并清零 Minor/Patch");
        GateAssert.True(plan.Version.CompareTo(VersionNumber.Parse("1.2.3")) > 0, "候选版本必须严格更高");
    }

    private static void VerifyHotfixAdvancesPatch()
    {
        BuildVersionPlan plan = BuildVersionPlanner.Plan("1.2.3", BuildType.Hotfix, null);
        GateAssert.True(plan.Success, $"Hotfix 版本计算必须成功: {plan.Error}");
        GateAssert.Equal("1.2.4", plan.Version.GetReleaseVersionString(), "Hotfix 默认推进 Patch");
    }

    private static void VerifyChannelInheritance()
    {
        BuildVersionPlan inherited = BuildVersionPlanner.Plan("1.2.3-beta", BuildType.Hotfix, null);
        GateAssert.Equal("1.2.4-beta", inherited.Version.GetReleaseVersionString(), "未提供选择时必须继承当前通道");

        BuildVersionPlan promoted = BuildVersionPlanner.Plan("1.2.3-beta", BuildType.Hotfix, "release");
        GateAssert.True(promoted.Success, $"beta → release 必须允许: {promoted.Error}");
        GateAssert.Equal("1.2.4", promoted.Version.GetReleaseVersionString(), "release 映射为无后缀正式版");
    }

    private static void VerifyChannelDowngradeRejected()
    {
        BuildVersionPlan downgraded = BuildVersionPlanner.Plan("1.2.3", BuildType.Hotfix, "beta");
        GateAssert.True(!downgraded.Success, "正式版不得降级到 beta");
        GateAssert.True(!string.IsNullOrEmpty(downgraded.Error), "拒绝必须给出原因");

        BuildVersionPlan invalid = BuildVersionPlanner.Plan("1.2.3", BuildType.Hotfix, "nightly");
        GateAssert.True(!invalid.Success, "未知通道必须被拒绝而不是静默清空");
    }

    private static void VerifyHotfixRequiresBaseline()
    {
        BuildVersionPlan hotfix = BuildVersionPlanner.Plan(string.Empty, BuildType.Hotfix, null);
        GateAssert.True(!hotfix.Success, "没有成功 Full 基准时 Hotfix 必须拒绝");
    }

    private static void VerifyFirstBuildVersion()
    {
        BuildVersionPlan full = BuildVersionPlanner.Plan(string.Empty, BuildType.Full, null);
        GateAssert.True(full.Success, $"首个 Full 必须允许: {full.Error}");
        GateAssert.Equal("1.0.0", full.Version.GetReleaseVersionString(), "无历史时从 1.0.0 开始");
    }

    private static void VerifyIndexRebuild()
    {
        var documents = new List<CompleteBuildSummary.SummaryDocument>
        {
            Document("Build_20260911120000_1.0.0", "AB", "1.0.0", "Full", "2026-09-11T12:00:00Z"),
            Document("Build_20260911130000_1.0.1", "AB", "1.0.1", "Hotfix", "2026-09-11T13:00:00Z"),
            Document("Build_20260911140000_1.0.0", "AA", "1.0.0", "Full", "2026-09-11T14:00:00Z")
        };

        BuildSummaryIndex index = BuildSummaryIndex.Rebuild(documents);

        BuildSummaryScope ab = index.FindScope("AB", "Windows", string.Empty);
        GateAssert.True(ab != null, "重建必须生成 AB 作用域记录");
        GateAssert.Equal("Build_20260911130000_1.0.1", ab.LatestSuccessfulSummaryId, "最新成功事实按开始时间选择");
        GateAssert.Equal("Build_20260911120000_1.0.0", ab.LatestFullSummaryId, "LatestFullSummaryId 只指向 Full");
        GateAssert.Equal("1.0.1", index.ProjectVersion.CurrentSuccessfulVersion, "项目版本取全局最高成功版本");
    }

    private static void VerifyImmutableSummary()
    {
        using var workspace = new TempWorkspace(nameof(VerifyImmutableSummary));
        var store = new BuildSummaryStore(workspace.Root);

        CompleteBuildSummary summary = Summary("Build_20260911120000_1.0.0");
        GateAssert.True(store.TryWriteSummary(summary, out string error), $"首次写入必须成功: {error}");
        GateAssert.True(store.TryWriteSummary(summary, out error), "内容一致时重复写入必须幂等");

        summary.Version = VersionNumber.Parse("1.1.0");
        GateAssert.False(store.TryWriteSummary(summary, out error), "正式摘要不可变，内容不同时拒绝覆盖");
        GateAssert.True(!string.IsNullOrEmpty(error), "拒绝必须给出原因");
    }

    private static void VerifyIndexCorruptionRecovery()
    {
        using var workspace = new TempWorkspace(nameof(VerifyIndexCorruptionRecovery));
        var store = new BuildSummaryStore(workspace.Root);

        GateAssert.True(store.TryWriteSummary(Summary("Build_20260911120000_1.0.0"), out string error),
            $"写入摘要必须成功: {error}");

        Directory.CreateDirectory(store.RootDir);
        File.WriteAllText(store.IndexPath, "{ this is not json");
        GateAssert.False(store.TryReadIndex(out _, out _), "损坏索引必须报告无法读取");

        BuildSummaryIndex rebuilt = store.RebuildIndex();
        GateAssert.True(store.TryWriteIndex(rebuilt, out error), $"重建索引必须可写回: {error}");
        GateAssert.True(store.TryReadIndex(out BuildSummaryIndex index, out error), $"写回后必须可读: {error}");

        BuildSummaryScope scope = index.FindScope("AB", "Windows", string.Empty);
        GateAssert.True(scope != null, "重建后必须恢复作用域记录");
        GateAssert.Equal("Build_20260911120000_1.0.0", scope.LatestSuccessfulSummaryId, "重建必须恢复最新成功事实");
        GateAssert.Equal("1.0.0", index.ProjectVersion.CurrentSuccessfulVersion, "重建必须恢复项目版本");
    }

    private static CompleteBuildSummary Summary(string buildId) => new()
    {
        BuildId = buildId,
        BackendId = "AB",
        BuildType = BuildType.Full,
        RuntimeMode = RuntimeMode.Online,
        Version = VersionNumber.Parse("1.0.0"),
        Platform = "Windows",
        StartedAt = DateTime.UtcNow,
        Success = true
    };

    private static CompleteBuildSummary.SummaryDocument Document(
        string buildId, string backend, string version, string buildType, string startedAtUtc) => new()
    {
        BuildId = buildId,
        BackendId = backend,
        BuildType = buildType,
        RuntimeMode = "Online",
        Version = version,
        Platform = "Windows",
        StartedAtUtc = startedAtUtc,
        Success = true,
        Statistics = new BuildStatistics(),
        Files = new List<CompleteBuildSummary.SummaryFile>(),
        Messages = new List<CompleteBuildSummary.SummaryMessage>()
    };
}

/// <summary>场景临时目录：每个用例独立根，结束后删除。</summary>
internal sealed class TempWorkspace : IDisposable
{
    public TempWorkspace(string name)
    {
        Root = Path.Combine(Path.GetTempPath(), "fyasset_ab_remediation", name + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, true);
        }
        catch (Exception)
        {
            // 临时目录清理失败不影响场景结论。
        }
    }
}
