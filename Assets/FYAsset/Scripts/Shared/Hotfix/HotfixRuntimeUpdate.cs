/// <summary>
/// 运行中热更的三个阶段。
/// </summary>
/// <remarks>
/// Check 只检查是否存在可接受更新，不下载不切换；
/// Prepare 在当前包继续运行的前提下隔离下载、复制并完整校验目标；
/// Apply 在业务回到安全入口并释放 Handle 后关闭旧管理器、切换包根并重新初始化。
/// 框架不负责弹窗、UI、场景跳转、停止业务协程或强制释放 Handle。
/// </remarks>
public enum HotfixPhase
{
    /// <summary>只检查远端是否存在可接受更新。</summary>
    Check = 0,

    /// <summary>在当前包继续运行的前提下准备并校验目标包。</summary>
    Prepare = 1,

    /// <summary>切换到已准备的目标包并重新初始化资源管理器。</summary>
    Apply = 2
}

/// <summary>
/// 运行中 Check 的结果：只描述是否存在可接受更新，不含任何下载或切换副作用。
/// </summary>
public sealed class HotfixCheckResult
{
    /// <summary>决策动作：PrepareTarget / RepairPackage 表示存在可准备更新。</summary>
    public HotfixStateAction Action { get; }

    /// <summary>检查后当前使用的内容归属。</summary>
    public HotfixContentState ContentState { get; }

    /// <summary>远端目标包名；无可用远端信息时为空。</summary>
    public string TargetPackageName { get; }

    /// <summary>远端目标版本；无可用远端信息时为零值。</summary>
    public VersionNumber TargetVersion { get; }

    /// <summary>诊断信息，用于业务提示或日志。</summary>
    public string Message { get; }

    /// <summary>true 表示存在可准备的目标包，Prepare 才有意义。</summary>
    public bool HasUpdate =>
        Action == HotfixStateAction.PrepareTarget || Action == HotfixStateAction.RepairPackage;

    /// <summary>true 表示远端要求更新整个客户端。</summary>
    public bool ClientUpdateRequired { get; }

    public HotfixCheckResult(
        HotfixStateAction action,
        HotfixContentState contentState,
        string targetPackageName,
        VersionNumber targetVersion,
        string message,
        bool clientUpdateRequired = false)
    {
        Action = action;
        ContentState = contentState;
        TargetPackageName = targetPackageName ?? string.Empty;
        TargetVersion = targetVersion;
        Message = message ?? string.Empty;
        ClientUpdateRequired = clientUpdateRequired;
    }
}
