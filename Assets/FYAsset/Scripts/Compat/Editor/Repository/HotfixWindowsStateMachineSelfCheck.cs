#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 在临时目录中检查 Windows 热更包完整性规则。
/// </summary>
public static class HotfixWindowsStateMachineSelfCheck
{
    public static void Run()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            nameof(HotfixWindowsStateMachineSelfCheck) + "_" + Guid.NewGuid().ToString("N"));
        try
        {
            byte[] content = { 1, 2, 3, 4, 5 };
            uint crc = WriteBundle(root, "Build_Valid", "valid.bundle", content);

            AssertComplete(Inspect(root, "Build_Valid", "valid.bundle", content.Length, crc), "valid package");
            AssertIncomplete(Inspect(root, "Build_Missing", "missing.bundle", content.Length, crc), "missing bundle");

            WriteBundle(root, "Build_Size", "size.bundle", content);
            AssertIncomplete(Inspect(root, "Build_Size", "size.bundle", content.Length + 1, crc), "size mismatch");

            WriteBundle(root, "Build_Crc", "crc.bundle", content);
            AssertIncomplete(Inspect(root, "Build_Crc", "crc.bundle", content.Length, crc ^ 1u), "CRC mismatch");

            WriteBundle(root, "Build_ZeroCrc", "zero.bundle", content);
            AssertIncomplete(Inspect(root, "Build_ZeroCrc", "zero.bundle", content.Length, 0), "zero CRC");

            WriteBundle(root, "Build_Unsafe", "safe.bundle", content);
            AssertIncomplete(Inspect(root, "Build_Unsafe", "../unsafe.bundle", content.Length, crc), "unsafe bundle name");

            WriteBundle(root, "Build_Duplicate", "duplicate.bundle", content);
            AssertIncomplete(
                Inspect(root, "Build_Duplicate", "duplicate.bundle", content.Length, crc, duplicate: true),
                "duplicate bundle name");

            WriteBundle(root, "Build_Version", "version.bundle", content);
            AssertIncomplete(
                Inspect(root, "Build_Version", "version.bundle", content.Length, crc, manifestPatch: 1),
                "manifest version mismatch");

            string baselineRoot = Path.Combine(root, "StreamingAssets");
            FileHelper.EnsureDirectory(Path.Combine(baselineRoot, FYAssetSettings.BUNDLES_DIRECTORY_NAME));
            FileHelper.WriteAllBytesAtomic(
                Path.Combine(baselineRoot, FYAssetSettings.BUNDLES_DIRECTORY_NAME, "baseline.bundle"),
                content);
            AssertComplete(
                InspectExactRoot(
                    baselineRoot,
                    "Build_Baseline",
                    "baseline.bundle",
                    content.Length,
                    HashGenerator.GenerateFileCRC(Path.Combine(
                        baselineRoot,
                        FYAssetSettings.BUNDLES_DIRECTORY_NAME,
                        "baseline.bundle")),
                    false),
                "baseline directory-name bypass");

            Debug.Log($"[{nameof(HotfixWindowsStateMachineSelfCheck)}] 通过。");
        }
        finally
        {
            FileHelper.TryDeleteDirectory(root, true);
        }
    }

    private static uint WriteBundle(string root, string packageName, string bundleName, byte[] content)
    {
        string path = Path.Combine(root, packageName, FYAssetSettings.BUNDLES_DIRECTORY_NAME, bundleName);
        FileHelper.WriteAllBytesAtomic(path, content);
        return HashGenerator.GenerateFileCRC(path);
    }

    private static HotfixPackageInspection Inspect(
        string root,
        string packageName,
        string bundleName,
        long fileSize,
        uint fileCrc,
        bool duplicate = false,
        int manifestPatch = 0)
    {
        return InspectExactRoot(
            Path.Combine(root, packageName),
            packageName,
            bundleName,
            fileSize,
            fileCrc,
            true,
            duplicate,
            manifestPatch);
    }

    private static HotfixPackageInspection InspectExactRoot(
        string packageRoot,
        string packageName,
        string bundleName,
        long fileSize,
        uint fileCrc,
        bool requireDirectoryMatch,
        bool duplicate = false,
        int manifestPatch = 0)
    {
        var version = new VersionNumber { Major = 4, Minor = 0, Patch = 0, Channel = string.Empty };
        var bundle = new BundleDownloadItem
        {
            BundleName = bundleName,
            FileHash = "hash",
            FileCRC = fileCrc,
            FileSize = fileSize
        };
        var bundles = new List<BundleDownloadItem> { bundle };
        if (duplicate)
            bundles.Add(bundle);

        return HotfixPackageInspection.Inspect(
            packageRoot,
            new PackageIndex
            {
                LatestPackage = packageName,
                LatestVersion = version,
                BackendMode = BackendModeNames.AA
            },
            new HotfixVersionInfo
            {
                Version = new VersionNumber
                {
                    Major = 4,
                    Minor = 0,
                    Patch = manifestPatch,
                    Channel = string.Empty
                },
                Bundles = bundles
            },
            true,
            string.Empty,
            requireDirectoryMatch);
    }

    private static void AssertComplete(HotfixPackageInspection inspection, string label)
    {
        if (inspection == null || !inspection.IsComplete)
            throw new InvalidOperationException($"{label}: expected complete, actual={inspection?.FailureReason}");
    }

    private static void AssertIncomplete(HotfixPackageInspection inspection, string label)
    {
        if (inspection == null || inspection.IsComplete)
            throw new InvalidOperationException($"{label}: expected incomplete");
    }
}
#endif
