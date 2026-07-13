using System;

internal static class HotfixRuntimeStateMachineTests
{
    private static int Main()
    {
        VerifyTargetDecisions();
        VerifyRemoteFailurePolicies();
        VerifyMajorMismatchPolicies();
        Console.WriteLine("PASS - hotfix runtime state decisions verified.");
        return 0;
    }

    private static void VerifyTargetDecisions()
    {
        HotfixStateDecision sameComplete = HotfixStateDecider.DecideTarget(
            "Build_4.0.0", true, "Build_4.0.0");
        AssertEqual(HotfixStateAction.ActivateLocal, sameComplete.Action, "same complete action");
        AssertFalse(sameComplete.RequiresRemoteManifest, "same complete manifest request");

        HotfixStateDecision sameIncomplete = HotfixStateDecider.DecideTarget(
            "Build_4.0.0", false, "Build_4.0.0");
        AssertEqual(HotfixStateAction.RepairTarget, sameIncomplete.Action, "same incomplete action");
        AssertTrue(sameIncomplete.RequiresRemoteManifest, "same incomplete manifest request");

        HotfixStateDecision rollback = HotfixStateDecider.DecideTarget(
            "Build_4.0.1", true, "Build_4.0.0");
        AssertEqual(HotfixStateAction.UpdateTarget, rollback.Action, "same-major rollback action");
        AssertTrue(rollback.RequiresRemoteManifest, "rollback manifest request");
    }

    private static void VerifyRemoteFailurePolicies()
    {
        HotfixStateDecision local = HotfixStateDecider.DecideRemoteFailure(
            HotfixRemoteFailurePolicy.ContinueWithLocal, true);
        AssertEqual(HotfixStateAction.ActivateLocal, local.Action, "remote failure local fallback");

        HotfixStateDecision baseline = HotfixStateDecider.DecideRemoteFailure(
            HotfixRemoteFailurePolicy.ContinueWithLocal, false);
        AssertEqual(HotfixStateAction.ActivateBaseline, baseline.Action, "remote failure baseline fallback");

        HotfixStateDecision fatal = HotfixStateDecider.DecideRemoteFailure(
            HotfixRemoteFailurePolicy.FailStartup, true);
        AssertEqual(HotfixStateAction.FailStartup, fatal.Action, "remote failure fatal policy");
    }

    private static void VerifyMajorMismatchPolicies()
    {
        HotfixStateDecision continueLocal = HotfixStateDecider.DecideMajorMismatch(
            HotfixMajorVersionMismatchPolicy.ContinueWithLocal, true);
        AssertEqual(HotfixStateAction.ActivateLocal, continueLocal.Action, "major mismatch local fallback");
        AssertTrue(continueLocal.NotifyClientUpdate, "major mismatch notification");

        HotfixStateDecision requireUpdate = HotfixStateDecider.DecideMajorMismatch(
            HotfixMajorVersionMismatchPolicy.RequireClientUpdate, false);
        AssertEqual(HotfixStateAction.FailStartup, requireUpdate.Action, "major mismatch fatal policy");
        AssertTrue(requireUpdate.NotifyClientUpdate, "required update notification");
    }

    private static void AssertTrue(bool value, string label)
    {
        if (!value)
            throw new InvalidOperationException($"Expected true: {label}");
    }

    private static void AssertFalse(bool value, string label)
    {
        if (value)
            throw new InvalidOperationException($"Expected false: {label}");
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}");
    }
}
