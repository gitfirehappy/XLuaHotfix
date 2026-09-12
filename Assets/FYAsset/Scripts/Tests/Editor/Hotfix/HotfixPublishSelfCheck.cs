#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 在临时目录中安全检查发布隔离与事务行为。
/// </summary>
/// <remarks>
/// 覆盖事实：后端目录隔离、PackageIndex 位置与写法、服务器事实缺失或损坏时的完整上传退化、
/// 未变内容的复用、发布事务回滚、Wrangler 命令参数。
/// </remarks>
public static class HotfixPublishSelfCheck
{
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), nameof(HotfixPublishSelfCheck) + "_" + Guid.NewGuid().ToString("N"));
        try
        {
            string sourceRoot = Path.Combine(root, "source");
            string serviceRoot = Path.Combine(root, "service");
            var config = new PushTargetConfig
            {
                Id = "self-check",
                Type = PushTargetType.LocalDirectory,
                Path = serviceRoot,
                PublicBaseUrl = "http://127.0.0.1:54321/"
            };

            SelfCheckPackage aaFirst = CreatePackage(
                Path.Combine(sourceRoot, "aa-v1"), "Build_20260901120000_4.0.0", "4.0.0", BackendModeNames.AA,
                ("shared.bundle", "shared-v1"), ("aa-only.bundle", "aa-only-v1"));
            SelfCheckPackage abFirst = CreatePackage(
                Path.Combine(sourceRoot, "ab-v1"), "Build_20260901120000_1.0.0", "1.0.0", BackendModeNames.AB,
                ("shared.bundle", "shared-v1"), ("ab-only.bundle", "ab-only-v1"));

            IPushTarget target = new LocalDirectoryPushTarget(config);

            // 服务器没有 PackageIndex 时无法读取服务器事实，发布必须退化为完整上传。
            AssertTrue(Push(target, config, aaFirst, root, "aa-first").DegradedToFullUpload,
                "服务器缺少 PackageIndex 时必须退化为完整上传");
            AssertTrue(Push(target, config, abFirst, root, "ab-first").DegradedToFullUpload,
                "服务器缺少 PackageIndex 时必须退化为完整上传");

            // PackageIndex 落在各后端根，包目录落在其下的包集合目录内。
            string packagesFolder = FYAssetSettings.Instance.BuildPackagesFolderName;
            string aaRoot = Path.Combine(serviceRoot, BackendModeNames.AA);
            string abRoot = Path.Combine(serviceRoot, BackendModeNames.AB);
            AssertFile(Path.Combine(aaRoot, FYAssetSettings.PACKAGE_INDEX_FILE_NAME));
            AssertFile(Path.Combine(abRoot, FYAssetSettings.PACKAGE_INDEX_FILE_NAME));
            AssertFile(Path.Combine(aaRoot, packagesFolder, aaFirst.PackageName, SelfCheckManifestReader.AAManifestFileName));
            AssertFile(Path.Combine(aaRoot, packagesFolder, aaFirst.PackageName, FYAssetSettings.ADDRESSABLES_CATALOG_FILE_NAME));
            AssertFile(Path.Combine(abRoot, packagesFolder, abFirst.PackageName, SelfCheckManifestReader.ABManifestFileName));

            // 后端隔离：一个后端根不得出现另一个后端的包目录。
            AssertMissing(Path.Combine(aaRoot, packagesFolder, abFirst.PackageName));
            AssertMissing(Path.Combine(abRoot, packagesFolder, aaFirst.PackageName));

            string aaUrl = config.GetHotfixUrl(BackendModeNames.AA);
            string abUrl = config.GetHotfixUrl(BackendModeNames.AB);
            AssertEqual("http://127.0.0.1:54321/AA/", aaUrl, "AA URL");
            AssertEqual("http://127.0.0.1:54321/AB/", abUrl, "AB URL");

            VerifyIncrementalPush(target, config, root, serviceRoot, sourceRoot);

            VerifyRollback(config, root, serviceRoot, aaFirst);

            string arguments = CloudflarePagesPushTarget.BuildDeployArguments(serviceRoot, "ProjectName1");
            if (!arguments.Contains("pages deploy") || !arguments.Contains("--project-name \"ProjectName1\"") || !arguments.Contains("--branch main"))
                throw new InvalidOperationException($"Unexpected Wrangler arguments: {arguments}");

            Debug.Log($"[{nameof(HotfixPublishSelfCheck)}] 通过 - 后端隔离、PackageIndex 位置、完整上传退化、复用、回滚与 Wrangler 命令均已验证。");
        }
        finally
        {
            FileHelper.TryDeleteDirectory(root, true);
        }
    }

    /// <summary>
    /// 服务器事实完整时必须走事务路径：只有变化文件写入服务器，未变文件复用服务器已有内容。
    /// </summary>
    private static void VerifyIncrementalPush(
        IPushTarget target,
        PushTargetConfig config,
        string root,
        string serviceRoot,
        string sourceRoot)
    {
        SelfCheckPackage next = CreatePackage(
            Path.Combine(sourceRoot, "aa-v2"), "Build_20260901120001_4.0.1", "4.0.1", BackendModeNames.AA,
            ("shared.bundle", "shared-v1"), ("aa-only.bundle", "aa-only-v2"));

        PushReceipt receipt = Push(target, config, next, root, "aa-second");
        AssertTrue(!receipt.DegradedToFullUpload, "服务器 PackageIndex 与 Manifest 可用时不得退化为完整上传");
        // 包内文件为 Manifest、catalog.json 与两个 bundle；变化只有 Manifest 与 aa-only.bundle。
        AssertEqual(2, receipt.UploadedCount, "只有变化文件需要写入服务器");
        AssertEqual(2, receipt.ReusedCount, "未变文件必须复用服务器已有内容");

        string packageDir = Path.Combine(
            serviceRoot, BackendModeNames.AA,
            FYAssetSettings.Instance.BuildPackagesFolderName, next.PackageName);
        AssertEqual("shared-v1", FileHelper.ReadAllText(Path.Combine(packageDir, SelfCheckManifestReader.BundlesDirectoryName, "shared.bundle")),
            "复用内容必须与本地包一致");
        AssertEqual("aa-only-v2", FileHelper.ReadAllText(Path.Combine(packageDir, SelfCheckManifestReader.BundlesDirectoryName, "aa-only.bundle")),
            "变化内容必须来自本地包");

        // 服务器 PackageIndex 损坏时必须退化为完整上传，而不是把损坏事实当成“没有变化”。
        FileHelper.WriteAllTextAtomic(
            Path.Combine(serviceRoot, BackendModeNames.AA, FYAssetSettings.PACKAGE_INDEX_FILE_NAME),
            "{ \"LatestPackage\": \"broken\" }");
        SelfCheckPackage afterCorrupt = CreatePackage(
            Path.Combine(sourceRoot, "aa-v3"), "Build_20260901120002_4.0.2", "4.0.2", BackendModeNames.AA,
            ("shared.bundle", "shared-v1"), ("aa-only.bundle", "aa-only-v3"));
        PushReceipt degraded = Push(target, config, afterCorrupt, root, "aa-corrupt-index");
        AssertTrue(degraded.DegradedToFullUpload, "服务器 PackageIndex 损坏时必须退化为完整上传");
        AssertEqual(0, degraded.ReusedCount, "退化发布不得复用不可信的服务器内容");
        AssertFile(Path.Combine(
            serviceRoot, BackendModeNames.AA,
            FYAssetSettings.Instance.BuildPackagesFolderName, afterCorrupt.PackageName,
            SelfCheckManifestReader.AAManifestFileName));
    }

    /// <summary>事务回滚必须恢复原包内容与原 PackageIndex，且不留下本次发布的新包目录。</summary>
    private static void VerifyRollback(
        PushTargetConfig config,
        string root,
        string serviceRoot,
        SelfCheckPackage published)
    {
        string backendRoot = Path.Combine(serviceRoot, BackendModeNames.AA);
        string indexPath = Path.Combine(backendRoot, FYAssetSettings.PACKAGE_INDEX_FILE_NAME);
        string indexBefore = FileHelper.ReadAllText(indexPath);
        string markerPath = Path.Combine(
            backendRoot,
            FYAssetSettings.Instance.BuildPackagesFolderName,
            published.PackageName,
            SelfCheckManifestReader.BundlesDirectoryName,
            "aa-only.bundle");
        string markerBefore = FileHelper.ReadAllText(markerPath);

        SelfCheckPackage rollbackPackage = CreatePackage(
            Path.Combine(root, "rollback-source"), "Build_20260901120003_9.9.9", "9.9.9", BackendModeNames.AA,
            ("shared.bundle", "shared-v1"), ("aa-only.bundle", "aa-only-v1"));

        PublishRequest request = CreateRequest(config, rollbackPackage);
        if (!PackagePublishTransaction.TryCreate(request, backendRoot, out PackagePublishTransaction transaction, out string error))
            throw new InvalidOperationException("发布事务创建失败: " + error);

        using (transaction)
        {
            transaction.CreatePlan();
            transaction.Stage();
            transaction.VerifyStaged();
            transaction.Apply();
            transaction.WritePackageIndex();
            transaction.Rollback();
        }

        AssertEqual(markerBefore, FileHelper.ReadAllText(markerPath), "回滚必须恢复原包内容");
        AssertEqual(indexBefore, FileHelper.ReadAllText(indexPath), "回滚必须恢复原 PackageIndex");
        AssertMissing(Path.Combine(
            backendRoot, FYAssetSettings.Instance.BuildPackagesFolderName, rollbackPackage.PackageName));
    }

    private static PushReceipt Push(
        IPushTarget target,
        PushTargetConfig config,
        SelfCheckPackage package,
        string root,
        string label)
    {
        PushReceipt receipt = BuildPublisher.Push(CreateRequest(config, package), target);
        FileHelper.WriteAllTextAtomic(
            Path.Combine(root, label + "-receipt.json"),
            SerializationUtility.SerializeToJson(receipt, true));
        if (receipt == null || !receipt.Success)
            throw new InvalidOperationException($"{label} push failed: {receipt?.FailureReason}");
        return receipt;
    }

    private static PublishRequest CreateRequest(PushTargetConfig config, SelfCheckPackage package)
    {
        return new PublishRequest
        {
            BackendKey = package.BackendKey,
            SourcePackageDir = package.SourceDir,
            TargetId = config.Id,
            ManifestReader = SelfCheckManifestReader.Create(package.BackendKey),
            PackagesFolderName = FYAssetSettings.Instance.BuildPackagesFolderName,
            Identity = new PackageBuildIdentity
            {
                PackageName = package.PackageName,
                Version = VersionNumber.Parse(package.Version),
                BackendId = package.BackendKey,
                BuildType = "Hotfix"
            }
        };
    }

    private static SelfCheckPackage CreatePackage(
        string sourceDir,
        string packageName,
        string version,
        string backendKey,
        params (string FileName, string Content)[] contents)
    {
        var package = new SelfCheckPackage(sourceDir, packageName, version, backendKey);
        for (int i = 0; i < contents.Length; i++)
            package.WriteContent(contents[i].FileName, contents[i].Content);
        package.Seal();
        return package;
    }

    private static void AssertTrue(bool value, string label)
    {
        if (!value)
            throw new InvalidOperationException(label);
    }

    private static void AssertFile(string path)
    {
        if (!FileHelper.Exists(path))
            throw new FileNotFoundException($"Expected file missing: {path}", path);
    }

    private static void AssertMissing(string path)
    {
        if (FileHelper.Exists(path) || FileHelper.DirectoryExists(path))
            throw new InvalidOperationException("Unexpected path exists: " + path);
    }

    private static void AssertEqual(string expected, string actual, string label)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException($"{label} mismatch. Expected: {expected}; Actual: {actual}");
    }

    private static void AssertEqual(int expected, int actual, string label)
    {
        if (expected != actual)
            throw new InvalidOperationException($"{label} mismatch. Expected: {expected}; Actual: {actual}");
    }

    /// <summary>
    /// 自检用发布包：包根写清单与构建摘要（包身份），内容文件写在 bundles 下。
    /// </summary>
    private sealed class SelfCheckPackage
    {
        private readonly List<FileDigest> _contents = new();

        public SelfCheckPackage(string sourceDir, string packageName, string version, string backendKey)
        {
            SourceDir = sourceDir;
            PackageName = packageName;
            Version = version;
            BackendKey = backendKey;
            FileHelper.EnsureDirectory(
                FYAssetPathUtility.JoinFilePath(sourceDir, SelfCheckManifestReader.BundlesDirectoryName));
        }

        public string SourceDir { get; }
        public string PackageName { get; }
        public string Version { get; }
        public string BackendKey { get; }

        /// <summary>写入一个内容文件；同名重复写入即覆盖内容与摘要。</summary>
        public void WriteContent(string fileName, string content)
        {
            string relativeName = SelfCheckManifestReader.BundlesDirectoryName + "/" + fileName;
            string path = FYAssetPathUtility.JoinFilePath(SourceDir, relativeName);
            FileHelper.WriteAllTextAtomic(path, content);
            if (!FileDigest.TryCreate(path, relativeName, out FileDigest digest))
                throw new InvalidOperationException("内容摘要计算失败: " + path);

            _contents.RemoveAll(item => string.Equals(item.Name, relativeName, StringComparison.Ordinal));
            _contents.Add(digest);
        }

        /// <summary>写出声明全部内容的清单（包目录只含发布内容）。</summary>
        public void Seal()
        {
            SelfCheckManifestReader.WriteManifest(SourceDir, BackendKey, _contents);
        }
    }

    /// <summary>
    /// 自检用清单读取器：包根清单声明 bundles 目录下的内容摘要，名称为包根相对路径。
    /// </summary>
    /// <remarks>
    /// 自检只验证发布事务与目标隔离，因此不依赖后端真实清单格式与 ManifestOutputFormat 设置；
    /// 清单文件名仍使用后端真实文件名，避免自检事实与生产布局脱节。
    /// </remarks>
    private sealed class SelfCheckManifestReader : IPackageManifestReader
    {
        public const string BundlesDirectoryName = FYAssetSettings.BUNDLES_DIRECTORY_NAME;
        public const string AAManifestFileName = FYAssetSettings.AA_MANIFEST_FILE_NAME;
        public const string ABManifestFileName = FYAssetSettings.MANIFEST_FILE_NAME;

        private readonly string _manifestFileName;

        private SelfCheckManifestReader(string backendKey)
        {
            _manifestFileName = string.Equals(backendKey, BackendModeNames.AA, StringComparison.OrdinalIgnoreCase)
                ? AAManifestFileName
                : ABManifestFileName;
        }

        public static SelfCheckManifestReader Create(string backendKey) => new(backendKey);

        public IReadOnlyList<string> RequiredPackageFileNames => new[] { _manifestFileName };

        public string ContentDirectoryName => BundlesDirectoryName;

        public bool TryReadContentDigests(string packageDir, out IReadOnlyList<FileDigest> contents, out string error)
        {
            var result = new List<FileDigest>();
            contents = result;
            error = string.Empty;

            string manifestPath = FYAssetPathUtility.JoinFilePath(packageDir, _manifestFileName);
            if (!FileHelper.Exists(manifestPath))
            {
                error = $"清单不存在: {manifestPath}";
                return false;
            }

            SelfCheckManifestDocument document;
            try
            {
                document = SerializationUtility.DeserializeJson<SelfCheckManifestDocument>(FileHelper.ReadAllText(manifestPath));
            }
            catch (Exception ex)
            {
                error = $"清单解析失败: {manifestPath} — {ex.Message}";
                return false;
            }

            if (document?.Files == null)
            {
                error = $"清单缺少 files: {manifestPath}";
                return false;
            }

            for (int i = 0; i < document.Files.Count; i++)
            {
                SelfCheckManifestEntry entry = document.Files[i];
                if (entry == null || string.IsNullOrEmpty(entry.Name))
                    continue;
                result.Add(new FileDigest(entry.Name, entry.Hash, entry.Crc, entry.Size));
            }

            return true;
        }

        /// <summary>按自检包内实际内容写出清单（名称为包根相对路径）。</summary>
        public static void WriteManifest(string packageDir, string backendKey, IReadOnlyList<FileDigest> contents)
        {
            var document = new SelfCheckManifestDocument { Files = new List<SelfCheckManifestEntry>() };
            for (int i = 0; i < (contents?.Count ?? 0); i++)
            {
                FileDigest digest = contents[i];
                document.Files.Add(new SelfCheckManifestEntry
                {
                    Name = digest.Name,
                    Hash = digest.Hash,
                    Crc = digest.CRC,
                    Size = digest.Size
                });
            }

            string manifestFileName = string.Equals(backendKey, BackendModeNames.AA, StringComparison.OrdinalIgnoreCase)
                ? AAManifestFileName
                : ABManifestFileName;
            FileHelper.WriteAllTextAtomic(
                FYAssetPathUtility.JoinFilePath(packageDir, manifestFileName),
                SerializationUtility.SerializeToJson(document, true));
        }

        [Serializable]
        private sealed class SelfCheckManifestDocument
        {
            public List<SelfCheckManifestEntry> Files = new();
        }

        [Serializable]
        private sealed class SelfCheckManifestEntry
        {
            public string Name;
            public string Hash;
            public uint Crc;
            public long Size;
        }
    }
}
#endif
