using System;

/// <summary>
/// 热更状态机在准备后端特定步骤之前选定的高层动作。
/// </summary>
public enum HotfixStateAction
{
    /// <summary>保持当前完整包，不做任何下载或修复。</summary>
    KeepCurrent = 0,

    /// <summary>当前完整包可用但本地 PackageIndex 缺失或损坏：补写本地指针。</summary>
    RepairPointer = 1,

    /// <summary>远端与当前是同版本同包但本地内容损坏：在隔离目录内准备并修复同一包。</summary>
    RepairPackage = 2,

    /// <summary>远端是同 Major 严格前向新包：准备新目标。</summary>
    PrepareTarget = 3,

    /// <summary>远端是同版本换包或降级：拒绝自动切换，保持当前完整包。</summary>
    RejectRemote = 4,

    /// <summary>没有可继续使用的完整包：阻断启动。</summary>
    Block = 5
}

/// <summary>
/// <see cref="HotfixStateDecider"/> 返回的不可变决策。
/// </summary>
public readonly struct HotfixStateDecision
{
    public HotfixStateAction Action { get; }

    /// <summary>决策后本次启动实际使用的内容归属。</summary>
    public HotfixContentState ContentState { get; }

    /// <summary>是否需要通知业务更新整个客户端（远端 Major 更高）。</summary>
    public bool NotifyClientUpdate { get; }

    public HotfixStateDecision(
        HotfixStateAction action,
        HotfixContentState contentState,
        bool notifyClientUpdate = false)
    {
        Action = action;
        ContentState = contentState;
        NotifyClientUpdate = notifyClientUpdate;
    }
}

/// <summary>
/// 远端不可用时的退化决策。
/// </summary>
public readonly struct HotfixFallbackDecision
{
    public HotfixStateAction Action { get; }

    /// <summary>退化后使用的内容归属：当前完整包，或本地不可用时的内置完整包。</summary>
    public HotfixContentState ContentState { get; }

    /// <summary>本次启动的运行模式；退化不改变它，原样带出供调用方继续使用。</summary>
    public RuntimeMode RuntimeMode { get; }

    /// <summary>true 表示本次退化到内置完整包，调用方必须给出 Warning。</summary>
    public bool DegradedToBuiltIn { get; }

    public HotfixFallbackDecision(
        HotfixStateAction action,
        HotfixContentState contentState,
        RuntimeMode runtimeMode,
        bool degradedToBuiltIn)
    {
        Action = action;
        ContentState = contentState;
        RuntimeMode = runtimeMode;
        DegradedToBuiltIn = degradedToBuiltIn;
    }
}

/// <summary>
/// Apply 门禁结果：仍有活跃 Handle 时拒绝切换包根。
/// </summary>
public readonly struct HotfixApplyDecision
{
    public bool CanApply { get; }

    /// <summary>拒绝时未释放的活跃 Handle 总数（Asset + Scene）。</summary>
    public int ActiveHandleCount { get; }

    public HotfixApplyDecision(bool canApply, int activeHandleCount)
    {
        CanApply = canApply;
        ActiveHandleCount = activeHandleCount;
    }
}

/// <summary>
/// AA 与 AB 热更流程共用的纯状态决策器。
/// </summary>
/// <remarks>
/// 只做取值判断，不读文件、不联网、不改路径；所有副作用由 HotfixFlowBase 执行，
/// 因此失败矩阵的每一行都可以脱离 Unity 断言。
/// </remarks>
public static class HotfixStateDecider
{
    /// <summary>
    /// 目标包激活后资源管理器初始化失败时，是否允许删除该目标目录。
    /// </summary>
    public static bool ShouldDeleteFailedTarget(bool packageManagerInitialized)
    {
        return !packageManagerInitialized;
    }

    /// <summary>
    /// Standalone 只用内置完整包，不联网；Online 才继续远端检查。
    /// </summary>
    public static bool IsStandalone(RuntimeMode runtimeMode)
    {
        return runtimeMode == RuntimeMode.Standalone;
    }

    /// <summary>
    /// 内置包不完整时没有可用的最终完整包，必须阻断启动。
    /// </summary>
    public static HotfixStateDecision DecideBuiltInUsable(bool builtInPackageComplete)
    {
        return builtInPackageComplete
            ? new HotfixStateDecision(HotfixStateAction.KeepCurrent, HotfixContentState.BuiltIn)
            : new HotfixStateDecision(HotfixStateAction.Block, HotfixContentState.Blocked);
    }

    /// <summary>
    /// 决定当前候选内容：本地 PackageIndex 可信、不是内置包身份、且指向的本地包精确检查通过时使用 LocalPackage，
    /// 否则退回 BuiltInPackage 继续远端检查。
    /// </summary>
    /// <param name="localPointerTrusted">本地 PackageIndex 是否可信（存在、字段有效、Major 与内置包一致）。</param>
    /// <param name="localIsBuiltInIdentity">本地指针是否指向内置包身份。</param>
    /// <param name="localPackageComplete">
    /// 本地指针指向的包根精确检查是否通过；损坏包不得作为当前内容候选。
    /// 省略时按完整处理，只服务不表达完整性的两参数调用点；流程实现必须传入精确检查结果。
    /// </param>
    public static HotfixContentState DecideCurrentContent(
        bool localPointerTrusted,
        bool localIsBuiltInIdentity,
        bool localPackageComplete = true)
    {
        return localPointerTrusted && !localIsBuiltInIdentity && localPackageComplete
            ? HotfixContentState.Local
            : HotfixContentState.BuiltIn;
    }

    /// <summary>
    /// 比较当前内容与远端目标，决定保持、修复、准备新目标、拒绝还是阻断。
    /// </summary>
    /// <param name="currentPointerTrusted">
    /// 本地 PackageIndex 是否可信；不可信时即使内容可用也要补写指针。
    /// </param>
    /// <remarks>
    /// 同 Major 严格前向：远端版本更高且包名不同才准备新目标；
    /// 同版本换包、降级、以及同名目录内的前向发布都属于发布异常，拒绝自动切换。
    /// </remarks>
    public static HotfixStateDecision DecideTarget(
        string currentPackageName,
        VersionNumber currentVersion,
        bool currentPackageComplete,
        bool currentPointerTrusted,
        HotfixContentState currentState,
        string remotePackageName,
        VersionNumber remoteVersion)
    {
        bool sameTarget = string.Equals(currentPackageName, remotePackageName, StringComparison.Ordinal)
                          && currentVersion == remoteVersion;
        if (sameTarget && currentPackageComplete && currentPointerTrusted)
            return new HotfixStateDecision(HotfixStateAction.KeepCurrent, currentState);
        if (sameTarget && currentPackageComplete)
            return new HotfixStateDecision(HotfixStateAction.RepairPointer, currentState);
        if (sameTarget && currentState == HotfixContentState.BuiltIn)
            // 内置包属于安装包内容，损坏后无法在持久化目录内修复，只能整包重装
            return new HotfixStateDecision(HotfixStateAction.Block, HotfixContentState.Blocked);
        if (sameTarget)
            return new HotfixStateDecision(
                HotfixStateAction.RepairPackage,
                HotfixContentState.RemoteTarget);

        if (remoteVersion > currentVersion
            && !string.Equals(currentPackageName, remotePackageName, StringComparison.Ordinal))
            return new HotfixStateDecision(
                HotfixStateAction.PrepareTarget,
                HotfixContentState.RemoteTarget);

        return new HotfixStateDecision(
            currentPackageComplete ? HotfixStateAction.RejectRemote : HotfixStateAction.Block,
            currentPackageComplete ? currentState : HotfixContentState.Blocked);
    }

    /// <summary>
    /// 远端 PackageIndex 不可用：使用当前完整包；没有完整包则阻断。
    /// </summary>
    /// <remarks>
    /// 退化到内置包时 DegradedToBuiltIn 为 true，调用方必须给出 Warning；
    /// 返回的 RuntimeMode 与传入值一致，退化不改变运行模式，下次启动照常 Check。
    /// </remarks>
    public static HotfixFallbackDecision DecideRemoteFailure(
        RuntimeMode runtimeMode,
        bool currentPackageComplete,
        HotfixContentState currentState)
    {
        if (!currentPackageComplete)
        {
            return new HotfixFallbackDecision(
                HotfixStateAction.Block,
                HotfixContentState.Blocked,
                runtimeMode,
                false);
        }

        return new HotfixFallbackDecision(
            HotfixStateAction.KeepCurrent,
            currentState,
            runtimeMode,
            currentState == HotfixContentState.BuiltIn);
    }

    /// <summary>
    /// 客户端与远端包 Major 不一致：远端更高时通知整包更新，其余按当前完整包继续。
    /// </summary>
    public static HotfixStateDecision DecideMajorMismatch(
        int clientMajor,
        int remoteMajor,
        bool currentPackageComplete,
        HotfixContentState currentState)
    {
        return new HotfixStateDecision(
            currentPackageComplete ? HotfixStateAction.KeepCurrent : HotfixStateAction.Block,
            currentPackageComplete ? currentState : HotfixContentState.Blocked,
            remoteMajor > clientMajor);
    }

    /// <summary>
    /// 目标下载或校验失败：删除目标后按当前完整包继续，没有完整包则阻断。
    /// </summary>
    public static HotfixStateAction DecideTargetFailure(bool currentPackageComplete)
    {
        return currentPackageComplete ? HotfixStateAction.KeepCurrent : HotfixStateAction.Block;
    }

    /// <summary>
    /// Apply 门禁：存在未释放的 Asset 或 Scene Handle 时拒绝切换包根，由业务自行释放后重试。
    /// </summary>
    public static HotfixApplyDecision DecideApply(int activeHandleCount)
    {
        return new HotfixApplyDecision(activeHandleCount <= 0, activeHandleCount);
    }

    /// <summary>
    /// 激活或资源管理器初始化失败：恢复此前完整包成功则继续，恢复失败则阻断。
    /// </summary>
    public static HotfixStateAction DecideActivationRollback(bool rollbackSucceeded)
    {
        return rollbackSucceeded ? HotfixStateAction.KeepCurrent : HotfixStateAction.Block;
    }

    /// <summary>
    /// 本地 PackageIndex 只允许在包已激活且资源管理器初始化成功之后写入。
    /// </summary>
    public static bool ShouldPersistLocalPackageIndex(bool packageActivated, bool runtimeInitialized)
    {
        return packageActivated && runtimeInitialized;
    }
}
