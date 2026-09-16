using System;

internal static class HotfixFailureMatrixTests
{
    internal static void Run()
    {
        HotfixRuntimeStateMachineTests.AssertAction(HotfixStateAction.KeepCurrent, HotfixStateDecider.DecideBuiltInUsable(true), "built-in complete");
        HotfixRuntimeStateMachineTests.AssertAction(HotfixStateAction.Block, HotfixStateDecider.DecideBuiltInUsable(false), "built-in incomplete");
        HotfixRuntimeStateMachineTests.AssertTrue(HotfixStateDecider.IsStandalone(RuntimeMode.Standalone), "standalone skips remote");
        HotfixRuntimeStateMachineTests.AssertFalse(HotfixStateDecider.IsStandalone(RuntimeMode.Online), "online checks remote");
        HotfixRuntimeStateMachineTests.AssertTrue(HotfixStateDecider.ShouldUseLocalPackage(true, false, true), "complete trusted local package is usable");
        HotfixRuntimeStateMachineTests.AssertFalse(HotfixStateDecider.ShouldUseLocalPackage(true, false, false), "damaged local package is not usable");
        HotfixRuntimeStateMachineTests.AssertFalse(HotfixStateDecider.ShouldUseLocalPackage(true, true, true), "built-in identity is not local package");
        VersionNumber version = new VersionNumber { Major = 4, Minor = 0, Patch = 0, Channel = string.Empty };
        HotfixRuntimeStateMachineTests.AssertAction(HotfixStateAction.Block,
            HotfixStateDecider.DecideTarget("Build_A", version, false, false, true, "Build_A", version), "damaged built-in blocks");
        HotfixRuntimeStateMachineTests.AssertAction(HotfixStateAction.RepairPackage,
            HotfixStateDecider.DecideTarget("Build_A", version, false, true, false, "Build_A", version), "same package repairs");
        HotfixRuntimeStateMachineTests.AssertAction(HotfixStateAction.PrepareTarget,
            HotfixStateDecider.DecideTarget("Build_A", version, true, true, false, "Build_B", new VersionNumber { Major = 4, Minor = 0, Patch = 1 }), "forward target");
        HotfixRuntimeStateMachineTests.AssertAction(HotfixStateAction.RejectRemote,
            HotfixStateDecider.DecideTarget("Build_A", version, true, true, false, "Build_B", version), "same version replacement rejected");
        HotfixRuntimeStateMachineTests.AssertTrue(HotfixStateDecider.DecideRemoteFailure(RuntimeMode.Online, true, true).DegradedToBuiltIn, "built-in degradation warns");
        HotfixRuntimeStateMachineTests.AssertFalse(HotfixStateDecider.DecideRemoteFailure(RuntimeMode.Online, true, false).DegradedToBuiltIn, "local fallback is not degradation");
        HotfixRuntimeStateMachineTests.AssertAction(HotfixStateAction.Block, HotfixStateDecider.DecideTargetFailure(false) == HotfixStateAction.Block ? new HotfixStateDecision(HotfixStateAction.Block) : new HotfixStateDecision(HotfixStateAction.KeepCurrent), "target failure blocks without current package");
        HotfixRuntimeStateMachineTests.AssertTrue(HotfixStateDecider.ShouldPersistLocalPackageIndex(true, true), "pointer written after successful initialization");
        HotfixRuntimeStateMachineTests.AssertFalse(HotfixStateDecider.ShouldPersistLocalPackageIndex(true, false), "pointer not written after failed initialization");
    }
}
