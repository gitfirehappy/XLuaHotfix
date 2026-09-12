using System.Collections.Generic;

/// <summary>
/// 整条构建管线的聚合执行结果。
/// 由 BuildPipelineRunner.Run 返回，包含所有 Task 的逐个结果、跳过计数、总计、
/// 本次运行的 Context（供后端读回产物）与交付结算 token。
/// </summary>
public class BuildRunResult
{
    /// <summary>管线整体是否成功（所有 Task 均成功，且成功时 attempt 提升无失败）</summary>
    public bool Success;

    /// <summary>参与执行的 Task 总数</summary>
    public int TotalTasks;

    /// <summary>成功完成的 Task 数量</summary>
    public int CompletedTasks;

    /// <summary>因首个失败而被跳过的 Task 数量</summary>
    public int SkippedTasks;

    /// <summary>每个 Task 的独立执行结果，按执行顺序排列</summary>
    public List<BuildTaskResult> TaskResults = new();

    /// <summary>本次运行使用的 Context；Runner 未开始执行时为 null</summary>
    public BuildContext Context;

    /// <summary>
    /// 交付结算 token：Runner 提升成功后才非空，由调用方在自身事务边界 Commit 或 Rollback。
    /// 非 attempt 布局不产生 token。
    /// </summary>
    public IBuildDeliveryToken DeliveryToken;
}
