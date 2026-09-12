using System;

/// <summary>
/// 构建运行环境 seam：Runner 需要的一切“只有编辑器/具体后端才知道”的动作都从这里取，
/// 因此 <see cref="BuildPipelineRunner"/> 与 Composer 本身不依赖 UnityEngine/UnityEditor，
/// 可以被纯 .NET 场景直接链接执行。
/// </summary>
/// <remarks>
/// 实现方负责：写入 Context 标准键（后端请求、构建类型、BuildConfig 等），
/// 以及在 attempt 布局下建立产物中间目录。Runner 只负责顺序执行与失败即停。
/// </remarks>
public interface IBuildRunEnvironment
{
    /// <summary>在任务执行前建立 Context 标准键；实现方是唯一知道后端请求结构的角色。</summary>
    void PrepareContext(BuildContext context, BuildRequest request);

    /// <summary>
    /// 建立本次运行的产物事务。返回 null 表示本次运行不需要 attempt 中间目录
    /// （产物直接写在正式输出位置，失败清理由调用方负责）。
    /// </summary>
    IBuildAttempt BeginAttempt(BuildContext context, BuildRequest request);
}

/// <summary>
/// 一次运行的产物事务：成功时把中间产物提升到正式输出，失败时只回收中间产物。
/// </summary>
public interface IBuildAttempt
{
    /// <summary>
    /// 提升 attempt 产物并把结算 token 交给调用方。
    /// 返回 false 表示提升失败（<paramref name="error"/> 说明原因），此时正式输出必须保持运行前状态。
    /// </summary>
    bool TryPromote(out IBuildDeliveryToken token, out string error);

    /// <summary>任务失败后的清理：删除本次运行的中间产物，不触碰正式输出。</summary>
    void Discard();
}

/// <summary>
/// 交付结算 token：Runner 提升成功后交给调用方，由调用方在自身事务边界 Commit 或 Rollback。
/// </summary>
/// <remarks>
/// Runner 不提交、不回滚交付，也不写本地启动数据、PackageIndex、baseline 或版本记录；
/// 这些跨步骤事务仍由 BuildProjectRunner 编排，保持“Runner 提升 → 调用方事务”的分段。
/// </remarks>
public interface IBuildDeliveryToken
{
    /// <summary>交付成功：释放提升过程保留的旧内容备份。</summary>
    void Commit();

    /// <summary>交付失败：把正式输出恢复回提升前的状态。</summary>
    void Rollback();
}
