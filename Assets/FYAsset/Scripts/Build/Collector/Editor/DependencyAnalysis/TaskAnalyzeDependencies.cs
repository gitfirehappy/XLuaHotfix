using System.Collections.Generic;
using UnityEditor;

/// <summary>
/// 管线 Task：依赖分析 + Bundle 依赖图构建 + 隐式依赖发现 + 共享提取决策。
/// 在管线中位于采集 Task 之后、打包 Task 之前。
/// ReadKeys: CollectedAssets, SharePolicies（可选，回退到 CollectorSetting SO）
/// WriteKeys: CollectedAssets（增强后）, BundleDependencyGraph
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
                "TaskCollectAssets 未产出 Asset。请检查 Collector 配置。", false);
        }

        // 读取 Per-Package SharePolicy：优先从 BuildContext 取（显式数据流），
        // 不存在时回退到 AssetDatabase 加载 CollectorSetting SO
        var policies = ctx.Get<Dictionary<string, SharePolicyConfig>>(BuildContextKeys.SharePolicies);
        if (policies == null)
        {
            policies = new Dictionary<string, SharePolicyConfig>();
            var setting = AssetDatabase.LoadAssetAtPath<CollectorSetting>(
                FYAssetSettings.Instance.CollectorSettingPath);
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

        // 汇总消息：统一收集，再根据是否有 Error 决定返回 Ok 或 Fail
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
            var result = BuildTaskResult.Fail("DEPENDENCY_ANALYSIS_FAILED",
                $"依赖分析发现 {messages.Count} 个问题。详见 Warning 列表。", true);
            result.Warnings = warnings;
            return result;
        }

        // 写回 BuildContext
        ctx.Set(BuildContextKeys.CollectedAssets, augmented);
        ctx.Set(BuildContextKeys.BundleDependencyGraph, graph);

        return BuildTaskResult.Ok(warnings.Count > 0 ? warnings : null);
    }
}
