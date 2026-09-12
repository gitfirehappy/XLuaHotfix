using System.Collections.Generic;

/// <summary>
/// AB 构建管线的固定主干阶段。顺序即执行顺序，成员名同时用作自定义 Task 的插入槽位名。
/// </summary>
public enum ABPipelineSlot
{
    /// <summary>扫描 Group/Collector，应用排除与 RawFile 规则，补框架内置内容</summary>
    CollectABAssets = 0,

    /// <summary>分析 Asset 依赖、显式/隐式来源与共享策略，生成依赖预期图</summary>
    AnalyzeABDependencies = 1,

    /// <summary>按输入指纹复用旧内容并构建本次内容（Serialized/Scene 走 Unity，RawFile 直接复制）</summary>
    BuildABContent = 2,

    /// <summary>读取 Unity AssetBundleManifest 的实际依赖，生成完整 Asset/Content 映射</summary>
    GenerateABManifest = 3,

    /// <summary>校验 Address 唯一、公共边界、成员关系、Content 类型、依赖、文件集合与摘要</summary>
    VerifyABContent = 4,

    /// <summary>计算交付集合、写模式输出与构建摘要</summary>
    ExportABOutput = 5
}

/// <summary>
/// AB 固定主干定义；成员顺序即执行顺序，成员名同时用作自定义 Task 的插入槽位。
/// 主干 Task 直接在这里创建，配置只能声明自定义 Task 的插入槽位。
/// </summary>
public static class ABPipelineBackbone
{
    /// <summary>按执行顺序创建 AB 主干槽位；配置不能增删主干 Task。</summary>
    public static IReadOnlyList<CoreTaskSlot> CreateCoreSlots()
    {
        return new[]
        {
            new CoreTaskSlot(nameof(ABPipelineSlot.CollectABAssets), new CollectABAssetsTask()),
            new CoreTaskSlot(nameof(ABPipelineSlot.AnalyzeABDependencies), new AnalyzeABDependenciesTask()),
            new CoreTaskSlot(nameof(ABPipelineSlot.BuildABContent), new BuildABContentTask()),
            new CoreTaskSlot(nameof(ABPipelineSlot.GenerateABManifest), new GenerateABManifestTask()),
            new CoreTaskSlot(nameof(ABPipelineSlot.VerifyABContent), new VerifyABContentTask()),
            new CoreTaskSlot(nameof(ABPipelineSlot.ExportABOutput), new ExportABOutputTask())
        };
    }

    /// <summary>用固定主干与配置里的自定义 Task 组装本次执行的 Task 序列。</summary>
    public static IReadOnlyList<IBuildTask> ComposeTasks(BuildPipelineConfig config)
    {
        return BuildPipelineComposer.Compose(CreateCoreSlots(), config?.Tasks);
    }
}
