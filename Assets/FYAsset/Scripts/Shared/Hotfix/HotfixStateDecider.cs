using System;

/// <summary>
/// 执行后端特定热更步骤前选定的高层动作。
/// </summary>
public enum HotfixStateAction
{
    ActivateLocal = 0,
    RepairBaselinePointer = 1,
    RepairTarget = 2,
    UpdateTarget = 3,
    RejectRemote = 4,
    FailStartup = 5
}

/// <summary>
/// <see cref="HotfixStateDecider"/> 返回的不可变决策。
/// </summary>
public readonly struct HotfixStateDecision
{
    public HotfixStateAction Action { get; }
    public bool NotifyClientUpdate { get; }

    public HotfixStateDecision(
        HotfixStateAction action,
        bool notifyClientUpdate = false)
    {
        Action = action;
        NotifyClientUpdate = notifyClientUpdate;
    }
}

/// <summary>
/// AA 与 AB 热更流程共用的纯包指针状态决策器。
/// </summary>
public static class HotfixStateDecider
{
    public static bool ShouldDeleteFailedTarget(bool packageManagerInitialized)
    {
        return !packageManagerInitialized;
    }

    public static HotfixStateDecision DecideTarget(
        string localPackageName,
        VersionNumber localVersion,
        bool localPackageComplete,
        bool localIsBaseline,
        string remotePackageName,
        VersionNumber remoteVersion)
    {
        bool sameTarget = string.Equals(localPackageName, remotePackageName, StringComparison.Ordinal)
                          && localVersion == remoteVersion;
        if (sameTarget && localPackageComplete)
            return new HotfixStateDecision(HotfixStateAction.ActivateLocal);
        if (sameTarget && localIsBaseline)
            return new HotfixStateDecision(HotfixStateAction.RepairBaselinePointer);
        if (sameTarget)
            return new HotfixStateDecision(HotfixStateAction.RepairTarget);

        if (remoteVersion > localVersion
            && !string.Equals(localPackageName, remotePackageName, StringComparison.Ordinal))
            return new HotfixStateDecision(HotfixStateAction.UpdateTarget);

        return new HotfixStateDecision(localPackageComplete
            ? HotfixStateAction.RejectRemote
            : HotfixStateAction.FailStartup);
    }

    public static HotfixStateDecision DecideRemoteFailure(bool localPackageComplete)
    {
        return new HotfixStateDecision(localPackageComplete
            ? HotfixStateAction.ActivateLocal
            : HotfixStateAction.FailStartup);
    }

    public static HotfixStateDecision DecideMajorMismatch(
        int clientMajor,
        int remoteMajor,
        bool localPackageComplete)
    {
        return new HotfixStateDecision(
            localPackageComplete ? HotfixStateAction.ActivateLocal : HotfixStateAction.FailStartup,
            remoteMajor > clientMajor);
    }
}
