#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 在临时目录中安全检查发布隔离与事务行为。
/// </summary>
public static class HotfixPublishSelfCheck
{
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), nameof(HotfixPublishSelfCheck) + "_" + Guid.NewGuid().ToString("N"));
        try
        {
            string sourceRoot = Path.Combine(root, "source");
            string serviceRoot = Path.Combine(root, "service");
            BuildBaseline aaCommit = CreatePackage(sourceRoot, "Build_AA_4.0.0", BackendModeNames.AA, "aa-v1");
            BuildBaseline abCommit = CreatePackage(sourceRoot, "Build_AB_1.0.0", BackendModeNames.AB, "ab-v1");
            var config = new PushTargetConfig
            {
                Id = "self-check",
                Type = PushTargetType.LocalDirectory,
                Path = serviceRoot,
                PublicBaseUrl = "http://127.0.0.1:54321/"
            };

            AssertPush(new LocalDirectoryPushTarget(config).Push(CreatePayload(aaCommit)), BackendModeNames.AA);
            AssertPush(new LocalDirectoryPushTarget(config).Push(CreatePayload(abCommit)), BackendModeNames.AB);
            AssertFile(Path.Combine(serviceRoot, "AA", FYAssetSettings.PACKAGE_INDEX_FILE_NAME));
            AssertFile(Path.Combine(serviceRoot, "AB", FYAssetSettings.PACKAGE_INDEX_FILE_NAME));
            AssertFile(Path.Combine(serviceRoot, "AA", FYAssetSettings.Instance.BuildPackagesFolderName, aaCommit.PackageName, FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN));
            AssertFile(Path.Combine(serviceRoot, "AB", FYAssetSettings.Instance.BuildPackagesFolderName, abCommit.PackageName, FYAssetSettings.MANIFEST_FILE_NAME_BIN));

            string aaUrl = PushTargetUtility.GetBackendHotfixUrl(config, BackendMode.AA);
            string abUrl = PushTargetUtility.GetBackendHotfixUrl(config, BackendMode.ABManifest);
            AssertEqual("http://127.0.0.1:54321/AA/", aaUrl, "AA URL");
            AssertEqual("http://127.0.0.1:54321/AB/", abUrl, "AB URL");

            VerifyRollback(root, serviceRoot);

            string arguments = PushTargetUtility.BuildWranglerDeployArguments(serviceRoot, "ProjectName1");
            if (!arguments.Contains("pages deploy") || !arguments.Contains("--project-name \"ProjectName1\"") || !arguments.Contains("--branch main"))
                throw new InvalidOperationException($"Unexpected Wrangler arguments: {arguments}");

            Debug.Log($"[{nameof(HotfixPublishSelfCheck)}] 通过 - 后端隔离、URL、回滚与 Wrangler 命令均已验证。");
        }
        finally
        {
            FileHelper.TryDeleteDirectory(root, true);
        }
    }

    private static void VerifyRollback(string root, string serviceRoot)
    {
        string backendRoot = Path.Combine(serviceRoot, "AA");
        string oldPackageName = "Build_AA_4.0.0";
        string oldIndex = FileHelper.ReadAllText(Path.Combine(backendRoot, FYAssetSettings.PACKAGE_INDEX_FILE_NAME));
        BuildBaseline newCommit = CreatePackage(Path.Combine(root, "rollback-source"), "Build_AA_4.0.1", BackendModeNames.AA, "aa-v2");

        using var transaction = new PackagePublishTransaction(newCommit, newCommit.PackageRootDir, backendRoot);
        transaction.Apply();
        AssertFile(Path.Combine(backendRoot, FYAssetSettings.Instance.BuildPackagesFolderName, newCommit.PackageName, FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN));
        transaction.Rollback();

        AssertFile(Path.Combine(backendRoot, FYAssetSettings.Instance.BuildPackagesFolderName, oldPackageName, FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN));
        string restoredIndex = FileHelper.ReadAllText(Path.Combine(backendRoot, FYAssetSettings.PACKAGE_INDEX_FILE_NAME));
        AssertEqual(oldIndex, restoredIndex, "PackageIndex rollback");
    }

    private static BuildBaseline CreatePackage(string sourceRoot, string packageName, string backendMode, string marker)
    {
        string packageRoot = Path.Combine(sourceRoot, packageName);
        FileHelper.EnsureDirectory(Path.Combine(packageRoot, FYAssetSettings.BUNDLES_DIRECTORY_NAME));
        string manifestName = string.Equals(backendMode, BackendModeNames.AB, StringComparison.OrdinalIgnoreCase)
            ? FYAssetSettings.MANIFEST_FILE_NAME_BIN
            : FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN;
        FileHelper.WriteAllTextAtomic(Path.Combine(packageRoot, manifestName), marker);
        FileHelper.WriteAllTextAtomic(Path.Combine(packageRoot, FYAssetSettings.BUNDLES_DIRECTORY_NAME, marker + ".bundle"), marker);
        if (string.Equals(backendMode, BackendModeNames.AA, StringComparison.OrdinalIgnoreCase))
            FileHelper.WriteAllTextAtomic(Path.Combine(packageRoot, FYAssetSettings.ADDRESSABLES_CATALOG_FILE_NAME), "{}");

        return new BuildBaseline
        {
            Version = new VersionNumber { Major = 1, Minor = 0, Patch = 0 },
            BackendMode = backendMode,
            PackageName = packageName,
            PackageRootDir = packageRoot
        };
    }

    private static PushPayload CreatePayload(BuildBaseline release)
    {
        return new PushPayload { Release = release };
    }

    private static void AssertPush(PushReceipt receipt, string backend)
    {
        if (receipt == null || !receipt.Success)
            throw new InvalidOperationException($"{backend} push failed: {receipt?.FailureReason}");
    }

    private static void AssertFile(string path)
    {
        if (!FileHelper.Exists(path))
            throw new FileNotFoundException($"Expected file missing: {path}", path);
    }

    private static void AssertEqual(string expected, string actual, string label)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException($"{label} mismatch. Expected: {expected}; Actual: {actual}");
    }
}
#endif
