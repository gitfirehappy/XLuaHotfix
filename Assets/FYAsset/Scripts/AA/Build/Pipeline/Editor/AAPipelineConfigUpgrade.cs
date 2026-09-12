using System;
using System.Collections.Generic;

/// <summary>
/// AA 侧配置升级策略：把旧的 AA 主干 Task 顺序列表迁移成自定义 Task 插入槽位。
/// </summary>
/// <remarks>
/// 放在独立文件而不是 AAPipelineBackbone：主干定义只描述固定阶段，
/// 历史 Task 名称属于一次性迁移知识，不应反向污染主干声明。
/// </remarks>
public static class AAPipelineConfigUpgrade
{
    /// <summary>
    /// 项目胶水层的构建索引 Task 名（Compat 提供）。命名不携带 Lua 语义：
    /// Lua 索引只是当前项目对该 Task 的实现，框架侧只把它当普通自定义 Task 处理。
    /// </summary>
    private const string ProjectBuildIndexTaskName = "LuaScriptsIndexBuildTask";

    /// <summary>按 AA 主干升级配置；返回 true 表示本次改动了配置并已保存。</summary>
    public static bool TryUpgrade(BuildPipelineConfig config)
    {
        var policy = new BuildPipelineConfigUpgradePolicy
        {
            DefaultSlot = BuildPipelineComposer.InputSlot,
            KnownCustomTaskSlots = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { ProjectBuildIndexTaskName, BuildPipelineComposer.InputSlot }
            }
        };

        return BuildPipelineConfigUpgrader.Upgrade(config, AAPipelineBackbone.CreateCoreSlots(), policy);
    }
}
