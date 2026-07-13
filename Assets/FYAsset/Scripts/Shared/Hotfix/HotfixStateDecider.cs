using System;

/// <summary>
/// 控制远端热更元数据不可用时的启动策略。
/// </summary>
public enum HotfixRemoteFailurePolicy
{
    ContinueWithLocal = 0,
    FailStartup = 1
}

/// <summary>
/// 执行后端特定热更步骤前选定的高层动作。
/// </summary>
public enum HotfixStateAction
{
    ActivateLocal = 0,
    ActivateBaseline = 1,
    RepairTarget = 2,
    UpdateTarget = 3,
    FailStartup = 4
}

/// <summary>
/// <see cref="HotfixStateDecider"/> 返回的不可变决策。
/// </summary>
public readonly struct HotfixStateDecision
{
    public HotfixStateAction Action { get; }
    public bool RequiresRemoteManifest { get; }
    public bool NotifyClientUpdate { get; }

    public HotfixStateDecision(
        HotfixStateAction action,
        bool requiresRemoteManifest = false,
        bool notifyClientUpdate = false)
    {
        Action = action;
        RequiresRemoteManifest = requiresRemoteManifest;
        NotifyClientUpdate = notifyClientUpdate;
    }
}

/// <summary>
/// AA 与 AB 热更流程共用的纯包指针状态决策器。
/// </summary>
public static class HotfixStateDecider
{
    public static HotfixStateDecision DecideTarget(
        string localPackageName,
        bool localPackageComplete,
        string remotePackageName)
    {
        bool samePackage = !string.IsNullOrEmpty(localPackageName)
                           && string.Equals(localPackageName, remotePackageName, StringComparison.Ordinal);
        if (samePackage && localPackageComplete)
            return new HotfixStateDecision(HotfixStateAction.ActivateLocal);
        if (samePackage)
            return new HotfixStateDecision(HotfixStateAction.RepairTarget, true);

        return new HotfixStateDecision(HotfixStateAction.UpdateTarget, true);
    }

    public static HotfixStateDecision DecideRemoteFailure(
        HotfixRemoteFailurePolicy policy,
        bool localPackageComplete)
    {
        if (policy == HotfixRemoteFailurePolicy.FailStartup)
            return new HotfixStateDecision(HotfixStateAction.FailStartup);

        return new HotfixStateDecision(localPackageComplete
            ? HotfixStateAction.ActivateLocal
            : HotfixStateAction.ActivateBaseline);
    }

    public static HotfixStateDecision DecideMajorMismatch(
        int clientMajor,
        int remoteMajor,
        bool localPackageComplete)
    {
        return new HotfixStateDecision(
            localPackageComplete ? HotfixStateAction.ActivateLocal : HotfixStateAction.ActivateBaseline,
            notifyClientUpdate: remoteMajor > clientMajor);
    }
}
