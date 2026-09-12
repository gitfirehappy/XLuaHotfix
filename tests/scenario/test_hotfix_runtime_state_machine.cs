using System;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
internal sealed class BinarySerializableAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Field)]
internal sealed class BinaryFieldAttribute : Attribute
{
    public BinaryFieldAttribute(int index) { }
}

internal static class HotfixRuntimeStateMachineTests
{
    private static int Main()
    {
        VerifyTargetDecisions();
        VerifyRemoteFailure();
        VerifyMajorDirections();
        VerifyPackageMetadataValidation();
        HotfixFailureMatrixTests.Run();
        HotfixReviewHardeningTests.Run();
        Console.WriteLine("PASS - Windows hotfix state decisions verified.");
        return 0;
    }

    /// <summary>
    /// 当前内容与远端目标的比较决策。
    /// 旧语义映射：ActivateLocal→KeepCurrent、RepairBaselinePointer→RepairPointer、
    /// RepairTarget→RepairPackage、UpdateTarget→PrepareTarget、FailStartup→Block。
    /// </summary>
    private static void VerifyTargetDecisions()
    {
        VersionNumber v400 = Version(4, 0, 0);
        VersionNumber v401 = Version(4, 0, 1);

        AssertAction(HotfixStateAction.KeepCurrent,
            HotfixStateDecider.DecideTarget("Build_A", v400, true, true, HotfixContentState.Local, "Build_A", v400),
            "same complete");
        AssertAction(HotfixStateAction.RepairPackage,
            HotfixStateDecider.DecideTarget("Build_A", v400, false, true, HotfixContentState.Local, "Build_A", v400),
            "same hotfix incomplete");
        AssertAction(HotfixStateAction.RepairPointer,
            HotfixStateDecider.DecideTarget("Build_A", v400, true, false, HotfixContentState.BuiltIn, "Build_A", v400),
            "usable content with damaged local pointer");
        AssertAction(HotfixStateAction.PrepareTarget,
            HotfixStateDecider.DecideTarget("Build_A", v400, true, true, HotfixContentState.Local, "Build_B", v401),
            "forward update");
        AssertAction(HotfixStateAction.RejectRemote,
            HotfixStateDecider.DecideTarget("Build_A", v400, true, true, HotfixContentState.Local, "Build_A", v401),
            "same-directory forward publication rejection");
        AssertAction(HotfixStateAction.RejectRemote,
            HotfixStateDecider.DecideTarget("Build_B", v401, true, true, HotfixContentState.Local, "Build_A", v400),
            "rollback rejection");
        AssertAction(HotfixStateAction.RejectRemote,
            HotfixStateDecider.DecideTarget("Build_A", v400, true, true, HotfixContentState.Local, "Build_B", v400),
            "same-version replacement rejection");
        AssertAction(HotfixStateAction.Block,
            HotfixStateDecider.DecideTarget("Build_A", v400, false, true, HotfixContentState.Local, "Build_B", v400),
            "invalid local cannot reject remote safely");
    }

    /// <summary>
    /// 远端不可用的退化决策：保持当前完整包；退化到内置包时运行模式不变。
    /// </summary>
    private static void VerifyRemoteFailure()
    {
        HotfixFallbackDecision builtInFallback = HotfixStateDecider.DecideRemoteFailure(
            RuntimeMode.Online, true, HotfixContentState.BuiltIn);
        AssertFallbackAction(HotfixStateAction.KeepCurrent, builtInFallback, "remote failure built-in fallback");
        AssertTrue(builtInFallback.DegradedToBuiltIn, "built-in fallback must warn");
        AssertEqual(RuntimeMode.Online, builtInFallback.RuntimeMode, "built-in fallback keeps Online mode");
        AssertEqual(HotfixContentState.BuiltIn, builtInFallback.ContentState, "built-in fallback content");

        HotfixFallbackDecision localFallback = HotfixStateDecider.DecideRemoteFailure(
            RuntimeMode.Online, true, HotfixContentState.Local);
        AssertFallbackAction(HotfixStateAction.KeepCurrent, localFallback, "remote failure local fallback");
        AssertFalse(localFallback.DegradedToBuiltIn, "local fallback is not a degradation");

        HotfixFallbackDecision blocked = HotfixStateDecider.DecideRemoteFailure(
            RuntimeMode.Standalone, false, HotfixContentState.Blocked);
        AssertFallbackAction(HotfixStateAction.Block, blocked, "remote failure without complete current package");
        AssertEqual(RuntimeMode.Standalone, blocked.RuntimeMode, "blocked fallback keeps the runtime mode");
    }

    private static void VerifyMajorDirections()
    {
        HotfixStateDecision newerLocal = HotfixStateDecider.DecideMajorMismatch(
            4, 5, true, HotfixContentState.Local);
        AssertAction(HotfixStateAction.KeepCurrent, newerLocal, "remote newer local");
        AssertTrue(newerLocal.NotifyClientUpdate, "remote newer notification");
        AssertContentState(HotfixContentState.Local, newerLocal, "remote newer keeps local content");

        HotfixStateDecision newerInvalid = HotfixStateDecider.DecideMajorMismatch(
            4, 5, false, HotfixContentState.Blocked);
        AssertAction(HotfixStateAction.Block, newerInvalid, "remote newer invalid local");
        AssertTrue(newerInvalid.NotifyClientUpdate, "remote newer invalid notification");

        HotfixStateDecision olderLocal = HotfixStateDecider.DecideMajorMismatch(
            5, 4, true, HotfixContentState.BuiltIn);
        AssertAction(HotfixStateAction.KeepCurrent, olderLocal, "remote older local");
        AssertFalse(olderLocal.NotifyClientUpdate, "remote older notification");
        AssertContentState(HotfixContentState.BuiltIn, olderLocal, "remote older keeps built-in content");

        AssertAction(HotfixStateAction.Block,
            HotfixStateDecider.DecideMajorMismatch(5, 4, false, HotfixContentState.Blocked),
            "remote older invalid local");
    }

    private static void VerifyPackageMetadataValidation()
    {
        AssertTrue(HotfixPackageValidator.IsSafePathSegment("Build_20260713_4.0.1"), "safe package name");
        AssertTrue(HotfixPackageValidator.IsPackageName("Build_20260713_4.0.1"), "valid package name");
        AssertFalse(HotfixPackageValidator.IsPackageName("package"), "package prefix");
        AssertFalse(HotfixPackageValidator.IsSafePathSegment("../Build_A"), "parent traversal");
        AssertFalse(HotfixPackageValidator.IsSafePathSegment("nested/Build_A"), "nested path");
        AssertFalse(HotfixPackageValidator.IsSafePathSegment("C:\\Build_A"), "rooted path");

        AssertTrue(HotfixPackageValidator.IsBundleMetadataValid("bundle_a.bundle", 10, 123), "valid bundle metadata");
        AssertFalse(HotfixPackageValidator.IsBundleMetadataValid("../bundle", 10, 123), "unsafe bundle name");
        AssertFalse(HotfixPackageValidator.IsBundleMetadataValid("bundle", -1, 123), "negative bundle size");
        AssertFalse(HotfixPackageValidator.IsBundleMetadataValid("bundle", 10, 0), "zero bundle CRC");
    }

    private static VersionNumber Version(int major, int minor, int patch)
    {
        return new VersionNumber { Major = major, Minor = minor, Patch = patch, Channel = string.Empty };
    }

    internal static void AssertAction(HotfixStateAction expected, HotfixStateDecision actual, string label)
    {
        if (actual.Action != expected)
            throw new InvalidOperationException($"{label}: expected {expected}, actual {actual.Action}");
    }

    internal static void AssertContentState(
        HotfixContentState expected,
        HotfixStateDecision actual,
        string label)
    {
        if (actual.ContentState != expected)
            throw new InvalidOperationException($"{label}: expected {expected}, actual {actual.ContentState}");
    }

    internal static void AssertFallbackAction(
        HotfixStateAction expected,
        HotfixFallbackDecision actual,
        string label)
    {
        if (actual.Action != expected)
            throw new InvalidOperationException($"{label}: expected {expected}, actual {actual.Action}");
    }

    internal static void AssertTrue(bool value, string label)
    {
        if (!value)
            throw new InvalidOperationException($"Expected true: {label}");
    }

    internal static void AssertFalse(bool value, string label)
    {
        if (value)
            throw new InvalidOperationException($"Expected false: {label}");
    }

    internal static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}");
    }
}
