using System.Collections.Generic;
using UnityEditor;

/// <summary>
/// 管线 Task：依赖分析 + Bundle 依赖图构建 + 隐式依赖发现 + 共享提取决策。
/// 在管线中位于采集 Task 之后、打包 Task 之前。
/// ReadKeys: CollectedAssets, SharePolicies (optional, falls back to CollectorSetting SO)
/// WriteKeys: CollectedAssets (augmented), BundleDependencyGraph
/// </summary>
public class TaskAnalyzeDependencies : IBuildTask
{
    public string TaskName => "TaskAnalyzeDependencies";
    public string[] DependsOn => new[] { "TaskCollectAssets" };
    public string[] ReadKeys => new[] { BuildContextKeys.CollectedAssets, BuildContextKeys.SharePolicies };
    public string[] WriteKeys => new[] { BuildContextKeys.CollectedAssets, BuildContextKeys.BundleDependencyGraph };

    public BuildTaskResult Execute(BuildContext ctx)
    {
        // 读取收集扫描产出的资产列表
        var assets = ctx.Get<List<CollectedAssetInfo>>(BuildContextKeys.CollectedAssets);
        if (assets == null || assets.Count == 0)
        {
            return BuildTaskResult.Fail("NO_COLLECTED_ASSETS",
                "TaskCollectAssets produced no assets. Check Collector configuration.", false);
        }

        // 读取 Per-Package SharePolicy：优先从 BuildContext 取（显式数据流），
        // 不存在时回退到 AssetDatabase 加载 CollectorSetting SO
        var policies = ctx.Get<Dictionary<string, SharePolicyConfig>>(BuildContextKeys.SharePolicies);
        if (policies == null)
        {
            policies = new Dictionary<string, SharePolicyConfig>();
            var setting = AssetDatabase.LoadAssetAtPath<CollectorSetting>(
                FYAssetConstants.COLLECTOR_SETTING_ASSET_PATH);
            if (setting != null)
            {
                foreach (var pkg in setting.Packages)
                {
                    if (!string.IsNullOrEmpty(pkg.PackageName) && pkg.SharePolicy != null)
                        policies[pkg.PackageName] = pkg.SharePolicy;
                }
            }
        }

        // 执行依赖分析
        var augmented = DependencyAnalyzer.Analyze(assets, policies,
            out var graph, out var messages);

        // 汇总消息
        var warnings = new List<string>();
        bool hasFatal = false;
        foreach (var msg in messages)
        {
            if (msg.Severity == BuildSeverity.Error)
            {
                hasFatal = true;
                warnings.Add($"[{msg.Code}] {msg.Message} ({msg.Source})");
            }
            else
            {
                warnings.Add($"[{msg.Code}] {msg.Message} ({msg.Source})");
            }
        }

        if (hasFatal)
        {
            var result = BuildTaskResult.Fail("DEPENDENCY_ANALYSIS_FAILED",
                $"Dependency analysis found {messages.Count} issue(s). See warnings for details.", true);
            result.Warnings = warnings;
            return result;
        }

        // 写回 BuildContext
        ctx.Set(BuildContextKeys.CollectedAssets, augmented);
        ctx.Set(BuildContextKeys.BundleDependencyGraph, graph);

        return BuildTaskResult.Ok(warnings.Count > 0 ? warnings : null);
    }
}
