/// <summary>
/// 主干阶段槽位：一个固定阶段名与它的主干 Task 实例。
/// AA/AB 各自在 PipelineBackbone 中用本类型声明固定主干顺序，
/// BuildPipelineComposer 以槽位名作为自定义 Task 的合法插入点。
/// </summary>
/// <remarks>
/// 主干 Task 由后端直接 new 出来交给槽位，不经反射解析；反射只服务自定义 Task。
/// 声明为 readonly struct：槽位在 Composer 组装期间不变，避免被下游改写后与主干定义脱节。
/// </remarks>
public readonly struct CoreTaskSlot
{
    /// <summary>阶段名，取后端主干枚举成员名（如 AA 的 BuildAAContent）</summary>
    public readonly string Slot;

    /// <summary>该阶段的主干 Task；Composer 按槽位顺序执行它</summary>
    public readonly IBuildTask Task;

    public CoreTaskSlot(string slot, IBuildTask task)
    {
        Slot = slot;
        Task = task;
    }
}
