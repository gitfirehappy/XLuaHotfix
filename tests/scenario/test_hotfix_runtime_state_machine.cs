using System;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
internal sealed class BinarySerializableAttribute : Attribute { }
[AttributeUsage(AttributeTargets.Field)]
internal sealed class BinaryFieldAttribute : Attribute { public BinaryFieldAttribute(int index) { } }

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

    private static void VerifyTargetDecisions()
    {
        VersionNumber v400 = Version(4, 0, 0);
        VersionNumber v401 = Version(4, 0, 1);
        AssertAction(HotfixStateAction.KeepCurrent, HotfixStateDecider.DecideTarget("Build_A", v400, true, true, false, "Build_A", v400), "same complete");
        AssertAction(HotfixStateAction.RepairPackage, HotfixStateDecider.DecideTarget("Build_A", v400, false, true, false, "Build_A", v400), "same local incomplete");
        AssertAction(HotfixStateAction.Block, HotfixStateDecider.DecideTarget("Build_A", v400, false, true, true, "Build_A", v400), "damaged built-in");
        AssertAction(HotfixStateAction.RepairPointer, HotfixStateDecider.DecideTarget("Build_A", v400, true, false, true, "Build_A", v400), "usable content with damaged pointer");
        AssertAction(HotfixStateAction.PrepareTarget, HotfixStateDecider.DecideTarget("Build_A", v400, true, true, false, "Build_B", v401), "forward update");
        AssertAction(HotfixStateAction.RejectRemote, HotfixStateDecider.DecideTarget("Build_A", v400, true, true, false, "Build_A", v401), "same-directory forward publication rejection");
        AssertAction(HotfixStateAction.RejectRemote, HotfixStateDecider.DecideTarget("Build_B", v401, true, true, false, "Build_A", v400), "rollback rejection");
        AssertAction(HotfixStateAction.RejectRemote, HotfixStateDecider.DecideTarget("Build_A", v400, true, true, false, "Build_B", v400), "same-version replacement rejection");
        AssertAction(HotfixStateAction.Block, HotfixStateDecider.DecideTarget("Build_A", v400, false, true, false, "Build_B", v400), "invalid local cannot reject remote safely");
    }

    private static void VerifyRemoteFailure()
    {
        HotfixFallbackDecision builtIn = HotfixStateDecider.DecideRemoteFailure(RuntimeMode.Online, true, true);
        AssertFallbackAction(HotfixStateAction.KeepCurrent, builtIn, "built-in fallback");
        AssertTrue(builtIn.DegradedToBuiltIn, "built-in fallback warns");
        AssertEqual(RuntimeMode.Online, builtIn.RuntimeMode, "fallback keeps Online");
        HotfixFallbackDecision local = HotfixStateDecider.DecideRemoteFailure(RuntimeMode.Online, true, false);
        AssertFalse(local.DegradedToBuiltIn, "local fallback is not degradation");
        AssertFallbackAction(HotfixStateAction.Block, HotfixStateDecider.DecideRemoteFailure(RuntimeMode.Standalone, false, true), "incomplete fallback blocks");
    }

    private static void VerifyMajorDirections()
    {
        HotfixStateDecision newer = HotfixStateDecider.DecideMajorMismatch(4, 5, true);
        AssertAction(HotfixStateAction.KeepCurrent, newer, "remote newer keeps current");
        AssertTrue(newer.NotifyClientUpdate, "remote newer notifies");
        AssertAction(HotfixStateAction.Block, HotfixStateDecider.DecideMajorMismatch(4, 5, false), "invalid current blocks");
        AssertFalse(HotfixStateDecider.DecideMajorMismatch(5, 4, true).NotifyClientUpdate, "remote older does not notify");
    }

    private static void VerifyPackageMetadataValidation()
    {
        AssertTrue(HotfixPackageValidator.IsSafePathSegment("Build_20260713_4.0.1"), "safe package name");
        AssertTrue(HotfixPackageValidator.IsPackageName("Build_20260713_4.0.1"), "valid package name");
        AssertFalse(HotfixPackageValidator.IsPackageName("package"), "package prefix");
        AssertFalse(HotfixPackageValidator.IsSafePathSegment("../Build_A"), "parent traversal");
        AssertFalse(HotfixPackageValidator.IsSafePathSegment("nested/Build_A"), "nested path");
        AssertFalse(HotfixPackageValidator.IsSafePathSegment("C:\\Build_A"), "rooted path");
        AssertTrue(HotfixPackageValidator.IsBundleMetadataValid("bundle_a.bundle", 10, 123), "valid metadata");
        AssertFalse(HotfixPackageValidator.IsBundleMetadataValid("../bundle", 10, 123), "unsafe bundle name");
        AssertFalse(HotfixPackageValidator.IsBundleMetadataValid("bundle", -1, 123), "negative size");
        AssertFalse(HotfixPackageValidator.IsBundleMetadataValid("bundle", 10, 0), "zero CRC");
    }

    private static VersionNumber Version(int major, int minor, int patch) => new VersionNumber { Major = major, Minor = minor, Patch = patch, Channel = string.Empty };
    internal static void AssertAction(HotfixStateAction expected, HotfixStateDecision actual, string label) { if (actual.Action != expected) throw new InvalidOperationException($"{label}: expected {expected}, actual {actual.Action}"); }
    internal static void AssertFallbackAction(HotfixStateAction expected, HotfixFallbackDecision actual, string label) => AssertAction(expected, new HotfixStateDecision(actual.Action), label);
    internal static void AssertTrue(bool value, string label) { if (!value) throw new InvalidOperationException($"Expected true: {label}"); }
    internal static void AssertFalse(bool value, string label) { if (value) throw new InvalidOperationException($"Expected false: {label}"); }
    internal static void AssertEqual<T>(T expected, T actual, string label) { if (!Equals(expected, actual)) throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}"); }
}
