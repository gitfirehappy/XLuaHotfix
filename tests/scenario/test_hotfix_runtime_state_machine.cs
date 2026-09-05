using System;

[AttributeUsage(AttributeTargets.Class)]
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
        HotfixReviewHardeningTests.Run();
        Console.WriteLine("PASS - Windows hotfix state decisions verified.");
        return 0;
    }

    private static void VerifyTargetDecisions()
    {
        VersionNumber v400 = Version(4, 0, 0);
        VersionNumber v401 = Version(4, 0, 1);

        AssertAction(HotfixStateAction.ActivateLocal,
            HotfixStateDecider.DecideTarget("Build_A", v400, true, false, "Build_A", v400),
            "same complete");
        AssertAction(HotfixStateAction.RepairTarget,
            HotfixStateDecider.DecideTarget("Build_A", v400, false, false, "Build_A", v400),
            "same hotfix incomplete");
        AssertAction(HotfixStateAction.RepairBaselinePointer,
            HotfixStateDecider.DecideTarget("Build_A", v400, false, true, "Build_A", v400),
            "baseline pointer repair");
        AssertAction(HotfixStateAction.UpdateTarget,
            HotfixStateDecider.DecideTarget("Build_A", v400, true, true, "Build_B", v401),
            "forward update");
        AssertAction(HotfixStateAction.RejectRemote,
            HotfixStateDecider.DecideTarget("Build_A", v400, true, true, "Build_A", v401),
            "same-directory forward publication rejection");
        AssertAction(HotfixStateAction.RejectRemote,
            HotfixStateDecider.DecideTarget("Build_B", v401, true, false, "Build_A", v400),
            "rollback rejection");
        AssertAction(HotfixStateAction.RejectRemote,
            HotfixStateDecider.DecideTarget("Build_A", v400, true, false, "Build_B", v400),
            "same-version replacement rejection");
        AssertAction(HotfixStateAction.FailStartup,
            HotfixStateDecider.DecideTarget("Build_A", v400, false, true, "Build_B", v400),
            "invalid local cannot reject remote safely");
    }

    private static void VerifyRemoteFailure()
    {
        AssertAction(HotfixStateAction.ActivateLocal,
            HotfixStateDecider.DecideRemoteFailure(true), "remote failure local fallback");
        AssertAction(HotfixStateAction.FailStartup,
            HotfixStateDecider.DecideRemoteFailure(false), "remote failure without local");
    }

    private static void VerifyMajorDirections()
    {
        HotfixStateDecision newerLocal = HotfixStateDecider.DecideMajorMismatch(4, 5, true);
        AssertAction(HotfixStateAction.ActivateLocal, newerLocal, "remote newer local");
        AssertTrue(newerLocal.NotifyClientUpdate, "remote newer notification");

        HotfixStateDecision newerInvalid = HotfixStateDecider.DecideMajorMismatch(4, 5, false);
        AssertAction(HotfixStateAction.FailStartup, newerInvalid, "remote newer invalid local");
        AssertTrue(newerInvalid.NotifyClientUpdate, "remote newer invalid notification");

        HotfixStateDecision olderLocal = HotfixStateDecider.DecideMajorMismatch(5, 4, true);
        AssertAction(HotfixStateAction.ActivateLocal, olderLocal, "remote older local");
        AssertFalse(olderLocal.NotifyClientUpdate, "remote older notification");

        AssertAction(HotfixStateAction.FailStartup,
            HotfixStateDecider.DecideMajorMismatch(5, 4, false), "remote older invalid local");
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

    private static void AssertAction(HotfixStateAction expected, HotfixStateDecision actual, string label)
    {
        if (actual.Action != expected)
            throw new InvalidOperationException($"{label}: expected {expected}, actual {actual.Action}");
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
}
