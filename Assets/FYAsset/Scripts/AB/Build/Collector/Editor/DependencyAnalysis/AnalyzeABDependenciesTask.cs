using System.Collections.Generic;

/// <summary>
/// 管线 Task：只消费 Collect 冻结的 ABCollectionSnapshot，执行依赖分析并写入唯一分析结果。
/// 不读取 AssetCollectionSetting、FYAssetABSettings 或其它全局配置。
/// </summary>
public class AnalyzeABDependenciesTask : IBuildTask
{
    public string TaskName => "AnalyzeABDependencies";

    public BuildTaskResult Execute(BuildRunContext ctx)
    {
        ABCollectionSnapshot snapshot;
        try
        {
            snapshot = ctx.Require<ABCollectionSnapshot>(ABBuildContextKeys.CollectionSnapshot);
        }
        catch (System.Exception ex)
        {
            return BuildTaskResult.Fail(
                BuildErrorCodes.DependencyAnalysisFailed,
                $"AnalyzeABDependencies 缺少 Collect 快照：{ex.Message}",
                false);
        }

        if (snapshot.CollectedAssets == null || snapshot.CollectedAssets.Count == 0)
        {
            return BuildTaskResult.Fail(
                BuildErrorCodes.NoCollectedAssets,
                "CollectABAssets 未产出 Asset。请检查 Collector 配置。",
                false);
        }

        var augmented = DependencyAnalyzer.Analyze(
            new List<CollectedAssetInfo>(snapshot.CollectedAssets),
            snapshot.SharePolicy,
            snapshot.RawFileRules,
            snapshot.DependencyFilterExtensions,
            out BundleDependencyGraph graph,
            out List<BuildMessage> messages,
            snapshot.IgnorePatterns);

        var warnings = new List<string>();
        bool hasFatal = false;
        foreach (BuildMessage msg in messages)
        {
            warnings.Add($"[{msg.Code}] {msg.Message} ({msg.Source})");
            if (msg.Severity == BuildSeverity.Error)
                hasFatal = true;
        }

        if (hasFatal)
        {
            BuildTaskResult result = BuildTaskResult.Fail(
                BuildErrorCodes.DependencyAnalysisFailed,
                $"依赖分析发现 {messages.Count} 个问题。详见 Warning 列表。",
                true);
            result.Warnings = warnings;
            return result;
        }

        BuildTaskResult contentValidation = ABContentAnalysisValidator.Validate(augmented);
        if (!contentValidation.Success)
            return contentValidation;

        ctx.Set(
            ABBuildContextKeys.DependencyAnalysisResult,
            new ABDependencyAnalysisResult(augmented, graph));
        return BuildTaskResult.Ok(warnings.Count > 0 ? warnings : null);
    }
}
