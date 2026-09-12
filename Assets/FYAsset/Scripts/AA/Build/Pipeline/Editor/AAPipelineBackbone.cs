using System.Collections.Generic;

/// <summary>
/// AA 构建管线的固定主干阶段。顺序即执行顺序，成员名同时用作自定义 Task 的插入槽位名。
/// </summary>
public enum AAPipelineSlot
{
    /// <summary>准备输入：记录 Addressables source 快照，Hotfix 时计算差异并迁移热更分组</summary>
    PrepareAAInput = 0,

    /// <summary>调用 Addressables 原生构建产出内容</summary>
    BuildAAContent = 1,

    /// <summary>规范化 catalog 并从实际输出生成完整 AA 清单与哈希</summary>
    GenerateAAManifest = 2,

    /// <summary>校验 catalog、清单与内容文件集合</summary>
    VerifyAAContent = 3,

    /// <summary>形成完整构建结果、模式输出与 BuildIndex</summary>
    ExportAAOutput = 4
}

/// <summary>
/// AA 固定主干定义：5 个阶段，每个阶段之间允许 0..N 个自定义 Task。
/// 主干 Task 直接在这里 new 出来，不经反射；配置只能声明自定义 Task 的插入槽位。
/// </summary>
public static class AAPipelineBackbone
{
    /// <summary>按执行顺序创建 AA 主干槽位；主干顺序是唯一事实来源，配置不能增删阶段。</summary>
    public static IReadOnlyList<CoreTaskSlot> CreateCoreSlots()
    {
        return new[]
        {
            new CoreTaskSlot(nameof(AAPipelineSlot.PrepareAAInput), new PrepareAAInputTask()),
            new CoreTaskSlot(nameof(AAPipelineSlot.BuildAAContent), new BuildAAContentTask()),
            new CoreTaskSlot(nameof(AAPipelineSlot.GenerateAAManifest), new GenerateAAManifestTask()),
            new CoreTaskSlot(nameof(AAPipelineSlot.VerifyAAContent), new VerifyAAContentTask()),
            new CoreTaskSlot(nameof(AAPipelineSlot.ExportAAOutput), new ExportAAOutputTask())
        };
    }

    /// <summary>用固定主干与配置里的自定义 Task 组装本次执行的 Task 序列。</summary>
    public static IReadOnlyList<IBuildTask> ComposeTasks(BuildPipelineConfig config)
    {
        return BuildPipelineComposer.Compose(CreateCoreSlots(), config?.Tasks);
    }
}
