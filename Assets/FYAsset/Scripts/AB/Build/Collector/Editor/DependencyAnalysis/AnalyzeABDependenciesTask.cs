using System.Collections.Generic;
using UnityEditor;

/// <summary>
/// 管线 Task：依赖分析 + Bundle 依赖图构建 + 隐式依赖发现 + 共享提取决策。
/// 在管线中位于采集和 builtin 收集之后、打包 Task 之前。
/// 读取 Keys: CollectedAssets, SharePolicy（可选，回退到 AssetCollectionSetting SO）
/// 写入 Keys: CollectedAssets, BundleDependencyGraph
/// </summary>
public class AnalyzeABDependenciesTask : IBuildTask
{
    public string TaskName => "AnalyzeABDependencies";
    public BuildTaskResult Execute(BuildContext ctx)
    {
        var assets = ctx.Get<List<CollectedAssetInfo>>(ABBuildContextKeys.CollectedAssets);
        if (assets == null || assets.Count == 0)
        {
            return BuildTaskResult.Fail(BuildErrorCodes.NoCollectedAssets,
                "CollectABAssets 未产出 Asset。请检查 Collector 配置。", false);
        }

        var collectionSetting = AssetDatabase.LoadAssetAtPath<AssetCollectionSetting>(
            FYAssetABSettings.Instance.AssetCollectionSettingPath);

        // SharePolicy 优先从 BuildContext 取，不存在时回退到 AssetCollectionSetting SO
        SharePolicyConfig sharePolicy = ctx.Get<SharePolicyConfig>(ABBuildContextKeys.SharePolicy);
        if (sharePolicy == null)
            sharePolicy = collectionSetting != null ? collectionSetting.SharePolicy : null;

        // 执行依赖分析。IgnorePatterns 同时用于把忽略路径隐式依赖折叠为“随行打包”，
        // 与 Collector 的扫描忽略同一份事实来源，避免出现两套语义；
        // RawFileRules 同样必须传入，否则隐式依赖的内容类型判定会与显式采集不一致。
        var augmented = DependencyAnalyzer.Analyze(assets, sharePolicy,
            collectionSetting != null ? collectionSetting.RawFileRules : null,
            FYAssetABSettings.Instance.DependencyFilterExtensions,
            out var graph, out var messages,
            collectionSetting != null ? collectionSetting.GetEffectiveIgnorePatterns() : null);

        var warnings = new List<string>();
        bool hasFatal = false;
        foreach (var msg in messages)
        {
            warnings.Add($"[{msg.Code}] {msg.Message} ({msg.Source})");
            if (msg.Severity == BuildSeverity.Error)
                hasFatal = true;
        }

        if (hasFatal)
        {
            var result = BuildTaskResult.Fail(BuildErrorCodes.DependencyAnalysisFailed,
                $"依赖分析发现 {messages.Count} 个问题。详见 Warning 列表。", true);
            result.Warnings = warnings;
            return result;
        }

        ctx.Set(ABBuildContextKeys.CollectedAssets, augmented);
        ctx.Set(ABBuildContextKeys.BundleDependencyGraph, graph);

        return BuildTaskResult.Ok(warnings.Count > 0 ? warnings : null);
    }
}
