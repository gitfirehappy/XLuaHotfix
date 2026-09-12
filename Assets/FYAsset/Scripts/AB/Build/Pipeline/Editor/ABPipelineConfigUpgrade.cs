using System;
using System.Collections.Generic;

/// <summary>
/// AB 配置升级：把配置中的自定义 Task 映射到主干插入槽位。
/// </summary>
/// <remarks>
/// 固定主干由 ABPipelineBackbone 定义，本类只负责配置转换和已知项目 Task 的默认槽位。
/// </remarks>
public static class ABPipelineConfigUpgrade
{
    /// <summary>
    /// 项目胶水层的构建索引 Task 名（Compat 提供）。命名不携带 Lua 语义：
    /// Lua 索引只是当前项目对该 Task 的实现，框架侧只把它当普通自定义 Task 处理。
    /// </summary>
    private const string ProjectBuildIndexTaskName = "LuaScriptsIndexBuildTask";

    /// <summary>按 AB 主干升级配置；返回 true 表示本次改动了配置并已保存。</summary>
    public static bool TryUpgrade(BuildPipelineConfig config)
    {
        var policy = new BuildPipelineConfigUpgradePolicy
        {
            DefaultSlot = BuildPipelineComposer.InputSlot,
            KnownCustomTaskSlots = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { ProjectBuildIndexTaskName, nameof(ABPipelineSlot.BuildABContent) }
            }
        };

        return BuildPipelineConfigUpgrader.Upgrade(config, ABPipelineBackbone.CreateCoreSlots(), policy);
    }
}
