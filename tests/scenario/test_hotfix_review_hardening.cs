using System;
using System.Collections.Generic;
using System.IO;

internal static class HotfixReviewHardeningTests
{
    public static void Run()
    {
        VerifyWindowsNames();
        VerifyExactBundleFiles();
        VerifyCleanupPhase();
    }

    private static void VerifyWindowsNames()
    {
        AssertTrue(HotfixPackageValidator.IsSafePathSegment("valid.bundle"), "valid filename");
        AssertFalse(HotfixPackageValidator.IsSafePathSegment("CON"), "device name");
        AssertFalse(HotfixPackageValidator.IsSafePathSegment("nul.txt"), "device name with extension");
        AssertFalse(HotfixPackageValidator.IsSafePathSegment("CON.data.bundle"), "device name with multiple extensions");
        AssertFalse(HotfixPackageValidator.IsSafePathSegment("bad*name"), "invalid character");
        AssertFalse(HotfixPackageValidator.IsSafePathSegment("bad?name"), "invalid character question mark");
        AssertFalse(HotfixPackageValidator.IsSafePathSegment("bad."), "trailing dot");
        AssertFalse(HotfixPackageValidator.IsSafePathSegment("bad "), "trailing space");
    }

    private static void VerifyExactBundleFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), nameof(HotfixReviewHardeningTests) + "_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "valid.bundle");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
            uint crc = HashGenerator.GenerateFileCRC(path);
            var valid = new List<BundleDownloadItem>
            {
                Bundle("valid.bundle", 4, crc)
            };

            AssertTrue(HotfixPackageValidator.TryValidateBundleFiles(root, valid, out _), "exact bundle set");
            AssertFalse(HotfixPackageValidator.TryValidateBundleFiles(root,
                new List<BundleDownloadItem> { Bundle("missing.bundle", 4, crc) }, out _), "missing file");
            AssertFalse(HotfixPackageValidator.TryValidateBundleFiles(root,
                new List<BundleDownloadItem> { Bundle("valid.bundle", 5, crc) }, out _), "size mismatch");
            AssertFalse(HotfixPackageValidator.TryValidateBundleFiles(root,
                new List<BundleDownloadItem> { Bundle("valid.bundle", 4, crc ^ 1u) }, out _), "CRC mismatch");
            AssertFalse(HotfixPackageValidator.TryValidateBundleFiles(root,
                new List<BundleDownloadItem> { valid[0], Bundle("VALID.BUNDLE", 4, crc) }, out _), "case duplicate");

            File.WriteAllBytes(Path.Combine(root, "extra.bundle"), new byte[] { 5 });
            AssertFalse(HotfixPackageValidator.TryValidateBundleFiles(root, valid, out _), "unexpected file");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void VerifyCleanupPhase()
    {
        AssertTrue(HotfixStateDecider.ShouldDeleteFailedTarget(false), "delete before PackageManager initialization");
        AssertFalse(HotfixStateDecider.ShouldDeleteFailedTarget(true), "keep live target after initialization");
    }

    private static BundleDownloadItem Bundle(string name, long size, uint crc)
    {
        return new BundleDownloadItem { BundleName = name, FileSize = size, FileCRC = crc, FileHash = "hash" };
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
