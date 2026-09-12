using System;

/// <summary>
/// 热更失败矩阵的纯逻辑断言。
/// 每行验证一个状态条件与决策，只调用 HotfixStateDecider，不触碰文件系统与 Unity。
/// </summary>
internal static class HotfixFailureMatrixTests
{
    public static void Run()
    {
        VerifyBuildIndexAndBuiltInPackage();
        VerifyStandaloneSkipsRemote();
        VerifyLocalPackageCandidate();
        VerifyDamagedLocalPointer();
        VerifyRemoteUnavailable();
        VerifySamePackageRepair();
        VerifyForwardTarget();
        VerifySameVersionReplacementRejected();
        VerifyRemoteMajorHigher();
        VerifyTargetFailureKeepsCurrent();
        VerifyApplyHandleGate();
        VerifyActivationFailureRollback();
    }

    /// <summary>行1：BuildIndex 无效或 BuiltInPackage 不完整 → Error 阻断。</summary>
    private static void VerifyBuildIndexAndBuiltInPackage()
    {
        HotfixStateDecision usable = HotfixStateDecider.DecideBuiltInUsable(true);
        HotfixRuntimeStateMachineTests.AssertAction(HotfixStateAction.KeepCurrent, usable, "built-in complete");
        HotfixRuntimeStateMachineTests.AssertContentState(
            HotfixContentState.BuiltIn, usable, "built-in complete content");

        HotfixStateDecision broken = HotfixStateDecider.DecideBuiltInUsable(false);
        HotfixRuntimeStateMachineTests.AssertAction(HotfixStateAction.Block, broken, "built-in incomplete");
        HotfixRuntimeStateMachineTests.AssertContentState(
            HotfixContentState.Blocked, broken, "built-in incomplete content");
    }

    /// <summary>行2：Standalone 只用内置完整包，不联网。</summary>
    private static void VerifyStandaloneSkipsRemote()
    {
        HotfixRuntimeStateMachineTests.AssertTrue(
            HotfixStateDecider.IsStandalone(RuntimeMode.Standalone), "standalone skips remote");
        HotfixRuntimeStateMachineTests.AssertFalse(
            HotfixStateDecider.IsStandalone(RuntimeMode.Online), "online checks remote");
    }

    /// <summary>行3/行4：Online 且本地完整用 LocalPackage；本地指针或目录损坏退回 BuiltInPackage。</summary>
    private static void VerifyLocalPackageCandidate()
    {
        HotfixRuntimeStateMachineTests.AssertEqual(
            HotfixContentState.Local,
            HotfixStateDecider.DecideCurrentContent(true, false),
            "trusted local pointer selects local package");
        HotfixRuntimeStateMachineTests.AssertEqual(
            HotfixContentState.BuiltIn,
            HotfixStateDecider.DecideCurrentContent(false, false),
            "missing local pointer falls back to built-in package");
        HotfixRuntimeStateMachineTests.AssertEqual(
            HotfixContentState.BuiltIn,
            HotfixStateDecider.DecideCurrentContent(true, true),
            "local pointer to the built-in identity uses the built-in package");
    }

    /// <summary>行4 续：本地目录损坏时当前内容仍必须是内置包。</summary>
    private static void VerifyDamagedLocalPointer()
    {
        HotfixContentState damaged = HotfixStateDecider.DecideCurrentContent(false, false);
        HotfixRuntimeStateMachineTests.AssertEqual(
            HotfixContentState.BuiltIn, damaged, "damaged local pointer keeps built-in content");

        // 内置包损坏时无法在持久化目录内修复，必须阻断而不是准备目标
        VersionNumber version = Version(4, 0, 0);
        HotfixRuntimeStateMachineTests.AssertAction(
            HotfixStateAction.Block,
            HotfixStateDecider.DecideTarget(
                "Build_A", version, false, false, HotfixContentState.BuiltIn, "Build_A", version),
            "damaged built-in package blocks");
    }

    /// <summary>行5：远端不可用 → 使用当前完整包；退化内置包时 Warning 且运行模式不变。</summary>
    private static void VerifyRemoteUnavailable()
    {
        HotfixFallbackDecision degraded = HotfixStateDecider.DecideRemoteFailure(
            RuntimeMode.Online, true, HotfixContentState.BuiltIn);
        HotfixRuntimeStateMachineTests.AssertFallbackAction(
            HotfixStateAction.KeepCurrent, degraded, "remote unavailable uses built-in");
        HotfixRuntimeStateMachineTests.AssertTrue(degraded.DegradedToBuiltIn, "built-in degradation warns");
        HotfixRuntimeStateMachineTests.AssertEqual(
            RuntimeMode.Online, degraded.RuntimeMode, "degradation does not change runtime mode");

        HotfixFallbackDecision blocked = HotfixStateDecider.DecideRemoteFailure(
            RuntimeMode.Online, false, HotfixContentState.Blocked);
        HotfixRuntimeStateMachineTests.AssertFallbackAction(
            HotfixStateAction.Block, blocked, "remote unavailable without complete package");
    }

    /// <summary>行6：同版本同包且本地损坏 → 准备并修复同包。</summary>
    private static void VerifySamePackageRepair()
    {
        VersionNumber version = Version(4, 0, 1);
        HotfixStateDecision decision = HotfixStateDecider.DecideTarget(
            "Build_A", version, false, true, HotfixContentState.Local, "Build_A", version);
        HotfixRuntimeStateMachineTests.AssertAction(HotfixStateAction.RepairPackage, decision, "repair same package");
        HotfixRuntimeStateMachineTests.AssertContentState(
            HotfixContentState.RemoteTarget, decision, "repair target is the remote package");
    }

    /// <summary>行7：同 Major 严格前向版本 → 准备新目标。</summary>
    private static void VerifyForwardTarget()
    {
        HotfixStateDecision decision = HotfixStateDecider.DecideTarget(
            "Build_A", Version(4, 0, 0), true, true, HotfixContentState.Local, "Build_B", Version(4, 0, 1));
        HotfixRuntimeStateMachineTests.AssertAction(HotfixStateAction.PrepareTarget, decision, "forward target");
        HotfixRuntimeStateMachineTests.AssertContentState(
            HotfixContentState.RemoteTarget, decision, "forward target content");
        HotfixRuntimeStateMachineTests.AssertFalse(decision.NotifyClientUpdate, "forward target stays in major");
    }

    /// <summary>行8：同版本换包或降级 → Warning，拒绝自动切换。</summary>
    private static void VerifySameVersionReplacementRejected()
    {
        VersionNumber version = Version(4, 0, 0);
        HotfixRuntimeStateMachineTests.AssertAction(
            HotfixStateAction.RejectRemote,
            HotfixStateDecider.DecideTarget(
                "Build_A", version, true, true, HotfixContentState.Local, "Build_B", version),
            "same version different package rejected");
        HotfixRuntimeStateMachineTests.AssertAction(
            HotfixStateAction.RejectRemote,
            HotfixStateDecider.DecideTarget(
                "Build_B", Version(4, 0, 1), true, true, HotfixContentState.Local, "Build_A", version),
            "downgrade rejected");
    }

    /// <summary>行9：远端 Major 更高 → 通知整包更新；有完整当前包则继续，否则阻断。</summary>
    private static void VerifyRemoteMajorHigher()
    {
        HotfixStateDecision keep = HotfixStateDecider.DecideMajorMismatch(
            4, 5, true, HotfixContentState.Local);
        HotfixRuntimeStateMachineTests.AssertAction(HotfixStateAction.KeepCurrent, keep, "major higher keeps current");
        HotfixRuntimeStateMachineTests.AssertTrue(keep.NotifyClientUpdate, "major higher notifies client update");

        HotfixStateDecision block = HotfixStateDecider.DecideMajorMismatch(
            4, 5, false, HotfixContentState.Blocked);
        HotfixRuntimeStateMachineTests.AssertAction(HotfixStateAction.Block, block, "major higher blocks without content");
        HotfixRuntimeStateMachineTests.AssertTrue(block.NotifyClientUpdate, "major higher still notifies");
    }

    /// <summary>行10：目标下载或校验失败 → 删除目标，保持当前完整包。</summary>
    private static void VerifyTargetFailureKeepsCurrent()
    {
        HotfixRuntimeStateMachineTests.AssertEqual(
            HotfixStateAction.KeepCurrent,
            HotfixStateDecider.DecideTargetFailure(true),
            "target failure keeps current package");
        HotfixRuntimeStateMachineTests.AssertEqual(
            HotfixStateAction.Block,
            HotfixStateDecider.DecideTargetFailure(false),
            "target failure without complete package blocks");
    }

    /// <summary>行11：Apply 时有 Handle/SceneHandle → Warning，拒绝 Apply，不强制释放。</summary>
    private static void VerifyApplyHandleGate()
    {
        HotfixApplyDecision idle = HotfixStateDecider.DecideApply(0);
        HotfixRuntimeStateMachineTests.AssertTrue(idle.CanApply, "apply allowed without handles");
        HotfixRuntimeStateMachineTests.AssertEqual(0, idle.ActiveHandleCount, "idle handle count");

        HotfixApplyDecision busy = HotfixStateDecider.DecideApply(3);
        HotfixRuntimeStateMachineTests.AssertFalse(busy.CanApply, "apply rejected while handles remain");
        HotfixRuntimeStateMachineTests.AssertEqual(3, busy.ActiveHandleCount, "reported handle count");
    }

    /// <summary>行12：激活或初始化失败 → 不写指针并恢复此前完整包；恢复失败则阻断。</summary>
    private static void VerifyActivationFailureRollback()
    {
        HotfixRuntimeStateMachineTests.AssertEqual(
            HotfixStateAction.KeepCurrent,
            HotfixStateDecider.DecideActivationRollback(true),
            "rollback success keeps previous package");
        HotfixRuntimeStateMachineTests.AssertEqual(
            HotfixStateAction.Block,
            HotfixStateDecider.DecideActivationRollback(false),
            "rollback failure blocks");

        HotfixRuntimeStateMachineTests.AssertTrue(
            HotfixStateDecider.ShouldPersistLocalPackageIndex(true, true),
            "pointer written after activation and initialization");
        HotfixRuntimeStateMachineTests.AssertFalse(
            HotfixStateDecider.ShouldPersistLocalPackageIndex(true, false),
            "pointer not written when runtime initialization failed");
        HotfixRuntimeStateMachineTests.AssertFalse(
            HotfixStateDecider.ShouldPersistLocalPackageIndex(false, true),
            "pointer not written when activation failed");
    }

    private static VersionNumber Version(int major, int minor, int patch)
    {
        return new VersionNumber { Major = major, Minor = minor, Patch = patch, Channel = string.Empty };
    }
}
