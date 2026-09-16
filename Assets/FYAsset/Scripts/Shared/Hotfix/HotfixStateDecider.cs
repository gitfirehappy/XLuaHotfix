using System;

/// <summary>热更状态机在执行后端步骤前选定的高层动作。</summary>
public enum HotfixStateAction
{
    KeepCurrent = 0,
    RepairPointer = 1,
    RepairPackage = 2,
    PrepareTarget = 3,
    RejectRemote = 4,
    Block = 5
}

/// <summary>热更决策；当前来源由包根事实表达，不在结果中复制状态。</summary>
public readonly struct HotfixStateDecision
{
    public HotfixStateAction Action { get; }
    public bool NotifyClientUpdate { get; }

    public HotfixStateDecision(HotfixStateAction action, bool notifyClientUpdate = false)
    {
        Action = action;
        NotifyClientUpdate = notifyClientUpdate;
    }
}

/// <summary>远端不可用时的退化决策。</summary>
public readonly struct HotfixFallbackDecision
{
    public HotfixStateAction Action { get; }
    public RuntimeMode RuntimeMode { get; }
    public bool DegradedToBuiltIn { get; }

    public HotfixFallbackDecision(HotfixStateAction action, RuntimeMode runtimeMode, bool degradedToBuiltIn)
    {
        Action = action;
        RuntimeMode = runtimeMode;
        DegradedToBuiltIn = degradedToBuiltIn;
    }
}

/// <summary>Apply 门禁结果：仍有活跃 Handle 时拒绝切换包根。</summary>
public readonly struct HotfixApplyDecision
{
    public bool CanApply { get; }
    public int ActiveHandleCount { get; }

    public HotfixApplyDecision(bool canApply, int activeHandleCount)
    {
        CanApply = canApply;
        ActiveHandleCount = activeHandleCount;
    }
}

/// <summary>AA 与 AB 热更流程共用的纯动作决策器。</summary>
public static class HotfixStateDecider
{
    public static bool ShouldDeleteFailedTarget(bool packageManagerInitialized) => !packageManagerInitialized;
    public static bool IsStandalone(RuntimeMode runtimeMode) => runtimeMode == RuntimeMode.Standalone;

    public static HotfixStateDecision DecideBuiltInUsable(bool builtInPackageComplete)
        => new HotfixStateDecision(builtInPackageComplete ? HotfixStateAction.KeepCurrent : HotfixStateAction.Block);

    public static bool ShouldUseLocalPackage(bool localPointerTrusted, bool localIsBuiltInIdentity, bool localPackageComplete)
        => localPointerTrusted && !localIsBuiltInIdentity && localPackageComplete;

    public static HotfixStateDecision DecideTarget(
        string currentPackageName,
        VersionNumber currentVersion,
        bool currentPackageComplete,
        bool currentPointerTrusted,
        bool currentIsBuiltIn,
        string remotePackageName,
        VersionNumber remoteVersion)
    {
        bool sameTarget = string.Equals(currentPackageName, remotePackageName, StringComparison.Ordinal)
                          && currentVersion == remoteVersion;
        if (sameTarget && currentPackageComplete && currentPointerTrusted)
            return new HotfixStateDecision(HotfixStateAction.KeepCurrent);
        if (sameTarget && currentPackageComplete)
            return new HotfixStateDecision(HotfixStateAction.RepairPointer);
        if (sameTarget && currentIsBuiltIn)
            return new HotfixStateDecision(HotfixStateAction.Block);
        if (sameTarget)
            return new HotfixStateDecision(HotfixStateAction.RepairPackage);
        if (remoteVersion > currentVersion && !string.Equals(currentPackageName, remotePackageName, StringComparison.Ordinal))
            return new HotfixStateDecision(HotfixStateAction.PrepareTarget);
        return new HotfixStateDecision(currentPackageComplete ? HotfixStateAction.RejectRemote : HotfixStateAction.Block);
    }

    public static HotfixFallbackDecision DecideRemoteFailure(RuntimeMode runtimeMode, bool currentPackageComplete, bool currentIsBuiltIn)
        => currentPackageComplete
            ? new HotfixFallbackDecision(HotfixStateAction.KeepCurrent, runtimeMode, currentIsBuiltIn)
            : new HotfixFallbackDecision(HotfixStateAction.Block, runtimeMode, false);

    public static HotfixStateDecision DecideMajorMismatch(int clientMajor, int remoteMajor, bool currentPackageComplete)
        => new HotfixStateDecision(currentPackageComplete ? HotfixStateAction.KeepCurrent : HotfixStateAction.Block,
            remoteMajor > clientMajor);

    public static HotfixStateAction DecideTargetFailure(bool currentPackageComplete)
        => currentPackageComplete ? HotfixStateAction.KeepCurrent : HotfixStateAction.Block;

    public static HotfixApplyDecision DecideApply(int activeHandleCount)
        => new HotfixApplyDecision(activeHandleCount <= 0, activeHandleCount);

    public static HotfixStateAction DecideActivationRollback(bool rollbackSucceeded)
        => rollbackSucceeded ? HotfixStateAction.KeepCurrent : HotfixStateAction.Block;

    public static bool ShouldPersistLocalPackageIndex(bool packageActivated, bool runtimeInitialized)
        => packageActivated && runtimeInitialized;
}
