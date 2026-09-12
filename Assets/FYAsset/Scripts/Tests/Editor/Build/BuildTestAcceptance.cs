#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 构建交付验收：从正式 Summary 定位的独立包目录与 StreamingAssets 读取事实并断言构建结果。
/// </summary>
/// <remarks>
/// 包身份来自 BuildData/Summaries 的正式摘要；Hotfix 基于最近成功 Full，包内包含完整目标 Manifest 和变化内容。
/// </remarks>
public static class BuildTestAcceptance
{
    public sealed class AcceptanceContext
    {
        public BuildTestBackend Backend;
        public bool IsHotfix;
        public string ExpectedVersion;

        /// <summary>Hotfix 的基准 Full 版本：作用域最近成功 Full 的交付版本。</summary>
        public string ExpectedCumulativeBaseVersion;

        /// <summary>Hotfix 构建前的基准 Full 事实；IsHotfix 为 true 时必填。</summary>
        public HotfixBaselineFacts CumulativeBase;

        /// <summary>变化集合中命中被改动夹具的物理内容名；由 <see cref="AcceptHotfix"/> 写入。</summary>
        public string FixturePhysicalHint;
    }

    /// <summary>
    /// Hotfix 构建前的本地事实：基准 Full 包的完整 Manifest、内容集合与上一次 Full 交付身份。
    /// </summary>
    /// <remarks>
    /// 必须在 Hotfix 构建之前 Capture：构建成功后作用域的最近成功交付已变为本次 Hotfix，基准事实无法再按构建前状态读取。
    /// </remarks>
    public sealed class HotfixBaselineFacts
    {
        public PackageBuildIdentity FullIdentity;

        /// <summary>基准 Full 包目录（构建前解析，构建后仍指向那次 Full 交付）。</summary>
        public string BaseFullPackageDir;

        public List<FileHelper.FileDigest> PreviousManifestContents = new();
        public List<FileHelper.FileDigest> AccumulatedContents = new();

        /// <summary>AA 上次成功构建的源快照（名称为 Asset GUID）；AB 恒为空。</summary>
        public List<FileHelper.FileDigest> PreviousSourceScan = new();

        /// <summary>读取基准事实；缺少上一次成功 Full 交付或其制品时失败。</summary>
        public static HotfixBaselineFacts Capture(BuildTestBackend backend)
        {
            RequireLocalFullIdentity(backend, out PackageBuildIdentity fullIdentity);
            string fullPackageDir = BuildPathManager.GetPackageDir(fullIdentity.PackageName);
            IPackageManifestReader reader = ResolveManifestReader(backend);

            var facts = new HotfixBaselineFacts
            {
                FullIdentity = fullIdentity,
                BaseFullPackageDir = fullPackageDir
            };

            if (!reader.TryReadContentDigests(fullPackageDir, out IReadOnlyList<FileHelper.FileDigest> previous, out string manifestError))
                throw new InvalidOperationException("基准 Full 包的 Manifest 不可用: " + manifestError);
            if (previous.Count == 0)
                throw new InvalidOperationException("基准 Full 包的 Manifest 未声明任何内容: " + fullPackageDir);
            facts.PreviousManifestContents.AddRange(previous);

            ScanPackageContents(fullPackageDir, reader.ContentDirectoryName, facts.AccumulatedContents);

            // AA 的变化资源判定依赖基准 Full 包内的源快照；缺失时无法计算本次变化集合。
            if (backend == BuildTestBackend.AA)
            {
                if (!AASourceScanFile.TryRead(fullPackageDir, out List<FileHelper.FileDigest> scan, out string scanReadError))
                    throw new InvalidOperationException("AA 基准 Full 包的源快照不可用: " + scanReadError);
                if (scan.Count == 0)
                    throw new InvalidOperationException("AA 基准 Full 包的源快照没有资源条目: " + fullPackageDir);
                facts.PreviousSourceScan.AddRange(scan);
            }

            return facts;
        }
    }

    /// <summary>后端清单读取器：发布与验收共用同一份内容集合口径。</summary>
    public static IPackageManifestReader ResolveManifestReader(BuildTestBackend backend)
    {
        return backend == BuildTestBackend.AB
            ? ABPackageManifestReader.Instance
            : (IPackageManifestReader)AAPackageManifestReader.Instance;
    }

    /// <summary>从正式 Summary 读取包身份，并校验后端与运行后端一致。</summary>
    public static PackageBuildIdentity RequireDeliveryIdentity(BuildTestBackend backend, string packageName)
    {
        string backendKey = BuildTestPaths.BackendSegment(backend);
        BuildSummaryStore store = BuildSummaryStore.CreateDefault();
        if (!store.TryReadSummaryDocument(backendKey, packageName, out CompleteBuildSummary.SummaryDocument document, out string error))
            throw new InvalidOperationException("正式 Summary 中缺少包身份: " + error);

        if (!VersionNumber.TryParse(document.Version, out VersionNumber version))
            throw new InvalidOperationException("正式 Summary 版本无法解析: " + document.Version);

        var identity = new PackageBuildIdentity
        {
            PackageName = document.BuildId,
            Version = version,
            BackendId = document.BackendId,
            BuildType = document.BuildType
        };

        if (!identity.MatchesBackend(backendKey))
            throw new InvalidOperationException(
                $"正式 Summary 包身份后端不匹配。Expected={backendKey}, Actual={identity.BackendId}");
        return identity;
    }

    /// <summary>按正式 Summary 解析最近一次成功交付的包身份与包目录。</summary>
    public static PackageBuildIdentity RequireLatestSuccessfulDelivery(BuildTestBackend backend, out string packageDir)
    {
        string backendKey = BuildTestPaths.BackendSegment(backend);
        BuildSummaryStore store = BuildSummaryStore.CreateDefault();
        if (!store.TryReadIndex(out BuildSummaryIndex index, out string indexError))
            throw new InvalidOperationException("Summary Index 不可用: " + indexError);

        BuildSummaryScope scope = null;
        for (int i = 0; i < index.Scopes.Count; i++)
        {
            BuildSummaryScope candidate = index.Scopes[i];
            if (candidate != null
                && string.Equals(candidate.Backend, backendKey, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(candidate.LatestSuccessfulSummaryId))
            {
                scope = candidate;
                break;
            }
        }

        if (scope == null)
            throw new InvalidOperationException("Summary Index 没有该后端的成功记录: " + backendKey);

        if (!store.TryReadSummaryDocument(backendKey, scope.LatestSuccessfulSummaryId,
                out CompleteBuildSummary.SummaryDocument document, out string readError))
            throw new InvalidOperationException("最新成功摘要不可读: " + readError);

        PackageBuildIdentity identity = RequireDeliveryIdentity(backend, scope.LatestSuccessfulSummaryId);
        packageDir = ResolveArtifactDir(document.ArtifactRelativePath);
        if (!FileHelper.DirectoryExists(packageDir))
            throw new InvalidOperationException("最新成功包目录不存在: " + packageDir);
        return identity;
    }

    private static string ResolveArtifactDir(string artifactRelativePath)
    {
        string root = FYAssetPathUtility.NormalizePath(BuildPathManager.ProjectRoot);
        return FYAssetPathUtility.NormalizePath(
            Path.Combine(root, (artifactRelativePath ?? string.Empty).Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>扫描包内内容目录，结果名称为包根相对路径。</summary>
    private static void ScanPackageContents(string packageDir, string contentDirectoryName, List<FileHelper.FileDigest> result)
    {
        string contentDir = FYAssetPathUtility.JoinFilePath(packageDir, contentDirectoryName);
        if (!FileHelper.DirectoryExists(contentDir))
            return;

        string[] files = FileHelper.GetFiles(contentDir, "*", SearchOption.TopDirectoryOnly);
        for (int i = 0; i < files.Length; i++)
        {
            string name = string.Concat(contentDirectoryName, "/", Path.GetFileName(files[i]));
            if (FileHelper.TryCreateDigest(files[i], name, out FileHelper.FileDigest digest))
                result.Add(digest);
        }
    }

    /// <summary>
    /// 隔离 Full 项目会重置全部构建事实，因此首个成功 Full 的版本恒为 1.0.0。
    /// </summary>
    /// <remarks>版本规则由 BuildVersionPlanner 持有（首个 Full=1.0.0、后续 Full=Major+1、Hotfix=Patch+1），
    /// 纯 .NET 门禁覆盖规则本身；这里用确定值断言磁盘交付遵守规则。</remarks>
    public const string FirstFullVersionAfterReset = "1.0.0";

    public static void AcceptFull(AcceptanceContext ctx, BuildTestResult result)
    {
        RequireLocalFullIdentity(ctx.Backend, out PackageBuildIdentity identity);

        string version = identity.Version.GetReleaseVersionString();
        if (!string.Equals(version, ctx.ExpectedVersion, StringComparison.Ordinal))
            throw new InvalidOperationException($"Full version mismatch. Expected={ctx.ExpectedVersion}, Actual={version}");

        if (!string.Equals(
                BuildSummaryStore.CreateDefault().ReadCurrentVersionText(),
                ctx.ExpectedVersion,
                StringComparison.Ordinal))
            throw new InvalidOperationException("Summary Index not advanced to Full version.");

        IPackageManifestReader reader = ResolveManifestReader(ctx.Backend);
        string packageDir = BuildPathManager.GetPackageDir(identity.PackageName);
        RequireNoLocalPackageIndex(identity.PackageName);
        RequireNoPackageIndex(packageDir, "Full 包目录");
        List<FileHelper.FileDigest> manifestContents = RequireManifestContents(reader, packageDir, "Full 包目录");

        ValidatePackageOnDisk(ctx, packageDir, manifestContents, result);
        ValidateStreamingAssetsBaseline(result);
        ValidatePermanentAddressesInManifest(packageDir, ctx.Backend);

        result.DeliveredPackageName = identity.PackageName;
        result.CumulativeBaseVersion = string.Empty;
        result.ExpectedVersion = ctx.ExpectedVersion;
        result.ActualVersion = version;
    }

    public static void AcceptHotfix(AcceptanceContext ctx, BuildTestResult result)
    {
        if (ctx.CumulativeBase == null)
            throw new InvalidOperationException("Hotfix 验收缺少构建前的基准事实。");

        PackageBuildIdentity identity = RequireLatestSuccessfulDelivery(ctx.Backend, out string packageDir);

        string version = identity.Version.GetReleaseVersionString();
        if (!string.Equals(version, ctx.ExpectedVersion, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Hotfix version mismatch. Expected={ctx.ExpectedVersion}, Actual={version}");

        string baseVersion = ctx.CumulativeBase.FullIdentity.Version.GetReleaseVersionString();
        if (!string.Equals(baseVersion, ctx.ExpectedCumulativeBaseVersion, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Hotfix 基准版本不匹配。Expected={ctx.ExpectedCumulativeBaseVersion}, Actual={baseVersion}");

        IPackageManifestReader reader = ResolveManifestReader(ctx.Backend);
        List<FileHelper.FileDigest> currentContents = RequireManifestContents(reader, packageDir, "Hotfix 包目录");
        FileHelper.ComputeDiff(
            ctx.CumulativeBase.PreviousManifestContents,
            currentContents,
            out List<FileHelper.FileDigest> added,
            out List<FileHelper.FileDigest> modified,
            out _,
            out _);

        RequireNoLocalPackageIndex(identity.PackageName);
        RequireNoPackageIndex(packageDir, "Hotfix 包目录");
        ValidateHotfixDeliveryContents(packageDir, reader, added, modified, result);
        ValidateHotfixDelta(added, modified, ctx);
        if (ctx.Backend == BuildTestBackend.AA)
            ValidateAASourceScanDelta(ctx.CumulativeBase, packageDir);
        ValidateStreamingAssetsUnchangedFromFull(result);
        ValidatePermanentAddressesInManifest(packageDir, ctx.Backend);

        result.DeliveredPackageName = identity.PackageName;
        result.CumulativeBaseVersion = baseVersion;
        result.ExpectedVersion = ctx.ExpectedVersion;
        result.ActualVersion = version;
        result.FixturePhysicalArtifact = ctx.FixturePhysicalHint;
    }

    /// <summary>
    /// 要求本地存在一次可作 Hotfix 基准的完整 Full 交付，并返回该交付的包身份。
    /// </summary>
    /// <remarks>
    /// 判定事实：StreamingAssets BuildIndex 指向的包目录必须存在且包身份一致；
    /// 本地基准 Full 交付必须携带可解析的完整 Manifest（Hotfix 的求差基准），否则本次 Hotfix 无法进行。
    /// </remarks>
    public static void RequireLocalFullIdentity(BuildTestBackend backend, out PackageBuildIdentity identity)
    {
        string backendKey = BuildTestPaths.BackendSegment(backend);
        string buildIndexPath = FYAssetPathUtility.JoinFilePath(
            Application.streamingAssetsPath,
            FYAssetSettings.BUILD_INDEX_FILENAME);
        if (!FileHelper.Exists(buildIndexPath))
            throw new InvalidOperationException("本地缺少 StreamingAssets BuildIndex，无法确定上次 Full 交付: " + buildIndexPath);

        BuildIndexData buildIndex = SerializationUtility.DeserializeJson<BuildIndexData>(
            File.ReadAllText(buildIndexPath, Encoding.UTF8));
        if (buildIndex == null || string.IsNullOrEmpty(buildIndex.BuildGUID))
            throw new InvalidOperationException("本地 BuildIndex 无效或缺少 BuildGUID: " + buildIndexPath);
        if (!string.Equals(buildIndex.BackendMode, backendKey, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"本地 BuildIndex 后端不匹配。Expected={backendKey}, Actual={buildIndex.BackendMode}");

        string packageDir = BuildPathManager.GetPackageDir(buildIndex.BuildGUID);
        if (!FileHelper.DirectoryExists(packageDir))
            throw new InvalidOperationException("本地 Full 包目录不存在: " + packageDir);

        identity = RequireDeliveryIdentity(backend, buildIndex.BuildGUID);
        if (!string.Equals(identity.PackageName, buildIndex.BuildGUID, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Full 包目录身份与 BuildIndex 不一致。BuildIndex={buildIndex.BuildGUID}, 摘要={identity.PackageName}");
        if (buildIndex.Version == null
            || !string.Equals(
                identity.Version.GetReleaseVersionString(),
                buildIndex.Version.GetReleaseVersionString(),
                StringComparison.Ordinal))
            throw new InvalidOperationException("Full 包目录版本与 BuildIndex 不一致: " + packageDir);

        IPackageManifestReader reader = ResolveManifestReader(backend);
        RequireManifestContents(reader, packageDir, "本地 Full 包目录");
    }

    public static void RequireTargetFullIdentity(
        BuildTestTargetSnapshot target,
        BuildTestBackend backend,
        PackageBuildIdentity fullIdentity)
    {
        BuildTestState.ProbeTargetIdentity(
            target,
            BuildTestPaths.BackendSegment(backend),
            fullIdentity.PackageName,
            fullIdentity.Version.GetReleaseVersionString(),
            true,
            null);
    }

    /// <summary>交付目录必须携带完整 Manifest，且内容集合非空。</summary>
    private static List<FileHelper.FileDigest> RequireManifestContents(
        IPackageManifestReader reader,
        string packageDir,
        string label)
    {
        if (!reader.TryReadContentDigests(packageDir, out IReadOnlyList<FileHelper.FileDigest> contents, out string error))
            throw new InvalidOperationException($"{label}的 Manifest 不可用: {error}");
        if (contents.Count == 0)
            throw new InvalidOperationException($"{label}的 Manifest 未声明任何内容: " + packageDir);
        return new List<FileHelper.FileDigest>(contents);
    }

    /// <summary>交付目录不得携带 PackageIndex；该文件只由发布器在发布事务最后写到服务器目标根。</summary>
    private static void RequireNoPackageIndex(string packageDir, string label)
    {
        string path = FYAssetPathUtility.JoinFilePath(packageDir, FYAssetSettings.PACKAGE_INDEX_FILE_NAME);
        if (FileHelper.Exists(path))
            throw new InvalidOperationException($"{label}不得包含 PackageIndex（构建不产出该文件）: " + path);
    }

    /// <summary>
    /// 本地输出根不得被本次构建写成本次交付包的指针。
    /// 该文件只由发布器写到服务器目标根；机器上可能残留历史构建写的文件，因此按“是否指向本次包”判断。
    /// </summary>
    private static void RequireNoLocalPackageIndex(string deliveredPackageName)
    {
        string path = FYAssetPathUtility.JoinFilePath(
            BuildPathManager.OutputRoot,
            FYAssetSettings.PACKAGE_INDEX_FILE_NAME);
        if (!FileHelper.Exists(path))
            return;

        PackageIndex index;
        try
        {
            index = SerializationUtility.ReadFromFile<PackageIndex>(path);
        }
        catch (Exception)
        {
            // 残留的损坏文件无法证明本次构建写过指针。
            return;
        }

        if (index != null && string.Equals(index.LatestPackage, deliveredPackageName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "构建不得写本地 PackageIndex（该文件只由发布器写到服务器目标根）: " + path);
        }
    }

    /// <summary>
    /// Hotfix 包内内容必须恰好等于“相对基准 Full 的新增与修改内容”。
    /// </summary>
    private static void ValidateHotfixDeliveryContents(
        string packageDir,
        IPackageManifestReader reader,
        IReadOnlyList<FileHelper.FileDigest> added,
        IReadOnlyList<FileHelper.FileDigest> modified,
        BuildTestResult result)
    {
        var expectedContents = new List<FileHelper.FileDigest>(added.Count + modified.Count);
        expectedContents.AddRange(added);
        expectedContents.AddRange(modified);
        Dictionary<string, FileHelper.FileDigest> expected = FileHelper.IndexByName(expectedContents);

        var actualContents = new List<FileHelper.FileDigest>();
        ScanPackageContents(packageDir, reader.ContentDirectoryName, actualContents);
        Dictionary<string, FileHelper.FileDigest> actual = FileHelper.IndexByName(actualContents);

        if (actual.Count != expected.Count)
            throw new InvalidOperationException(
                $"Hotfix 包内容数量与相对 Full 的差异集合不一致: 实际={actual.Count}, 期望={expected.Count}");

        long bytes = 0;
        foreach (KeyValuePair<string, FileHelper.FileDigest> pair in expected)
        {
            if (!actual.TryGetValue(pair.Key, out FileHelper.FileDigest delivered))
                throw new InvalidOperationException("Hotfix 包缺少差异集合内的内容: " + pair.Key);
            if (!delivered.Matches(pair.Value))
                throw new InvalidOperationException("Hotfix 包内容摘要与差异集合不一致: " + pair.Key);
            bytes += delivered.Size;
        }

        result.ArtifactCount = expected.Count;
        result.ArtifactBytes = bytes;
        result.PackagePath = packageDir;
        result.ManifestHash = HashGenerator.GenerateFileHash(RequireManifestFile(packageDir));
    }

    private static void ValidatePackageOnDisk(
        AcceptanceContext ctx,
        string packageDir,
        List<FileHelper.FileDigest> manifestContents,
        BuildTestResult result)
    {
        if (!FileHelper.DirectoryExists(packageDir))
            throw new InvalidOperationException("Package root missing: " + packageDir);

        string summaryPath = FYAssetPathUtility.JoinFilePath(packageDir, "build_summary.json");
        if (FileHelper.Exists(summaryPath))
            throw new InvalidOperationException("包目录不得包含构建摘要（身份来自 BuildData/Summaries）: " + summaryPath);

        BuildTestBackend backend = ctx.Backend;
        string jsonName = backend == BuildTestBackend.AB
            ? FYAssetSettings.MANIFEST_FILE_NAME
            : FYAssetSettings.AA_MANIFEST_FILE_NAME;
        string binName = backend == BuildTestBackend.AB
            ? FYAssetSettings.MANIFEST_FILE_NAME_BIN
            : FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN;
        string jsonPath = FYAssetPathUtility.JoinFilePath(packageDir, jsonName);
        string binPath = FYAssetPathUtility.JoinFilePath(packageDir, binName);
        if (!FileHelper.Exists(jsonPath) && !FileHelper.Exists(binPath))
            throw new InvalidOperationException("Manifest missing in package: " + packageDir);

        // 跨后端残留检查。
        if (backend == BuildTestBackend.AA)
        {
            if (FileHelper.Exists(FYAssetPathUtility.JoinFilePath(packageDir, FYAssetSettings.MANIFEST_FILE_NAME))
                || FileHelper.Exists(FYAssetPathUtility.JoinFilePath(packageDir, FYAssetSettings.MANIFEST_FILE_NAME_BIN)))
                throw new InvalidOperationException("AA package contains AB manifest residue.");
        }
        else
        {
            if (FileHelper.Exists(FYAssetPathUtility.JoinFilePath(packageDir, FYAssetSettings.AA_MANIFEST_FILE_NAME))
                || FileHelper.Exists(FYAssetPathUtility.JoinFilePath(packageDir, FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN))
                || FileHelper.Exists(FYAssetPathUtility.JoinFilePath(packageDir, FYAssetSettings.ADDRESSABLES_CATALOG_FILE_NAME)))
                throw new InvalidOperationException("AB package contains AA residue.");
        }

        if (backend == BuildTestBackend.AA)
        {
            string bundlesDir = FYAssetPathUtility.JoinFilePath(
                packageDir,
                FYAssetSettings.BUNDLES_DIRECTORY_NAME);
            if (!FileHelper.DirectoryExists(bundlesDir)
                || FileHelper.GetFiles(bundlesDir, "*", SearchOption.TopDirectoryOnly).Length == 0)
            {
                throw new InvalidOperationException("AA package bundles directory missing or empty.");
            }

            string catalog = FYAssetPathUtility.JoinFilePath(
                packageDir,
                FYAssetSettings.ADDRESSABLES_CATALOG_FILE_NAME);
            if (!FileHelper.Exists(catalog))
                throw new InvalidOperationException("AA catalog.json missing from package.");
        }

        // 完整交付必须与自己的 Manifest 一致：声明的内容都要在磁盘上且摘要一致。
        long bytes = 0;
        for (int i = 0; i < manifestContents.Count; i++)
        {
            FileHelper.FileDigest declared = manifestContents[i];
            string path = FYAssetPathUtility.JoinFilePath(packageDir, declared.Name);
            if (!FileHelper.TryCreateDigest(path, declared.Name, out FileHelper.FileDigest actual))
                throw new InvalidOperationException("包目录缺少 Manifest 声明的内容: " + declared.Name);
            if (!actual.Matches(declared))
                throw new InvalidOperationException("包目录内容与 Manifest 声明不一致: " + declared.Name);
            bytes += actual.Size;
        }

        result.ArtifactCount = manifestContents.Count;
        result.ArtifactBytes = bytes;
        result.ManifestHash = HashGenerator.GenerateFileHash(FileHelper.Exists(binPath) ? binPath : jsonPath);
        result.PackagePath = packageDir;
    }

    /// <summary>交付目录内实际存在的清单文件路径；发布与验收都要求至少存在一种格式。</summary>
    private static string RequireManifestFile(string packageDir)
    {
        string[] candidates =
        {
            FYAssetSettings.MANIFEST_FILE_NAME_BIN,
            FYAssetSettings.MANIFEST_FILE_NAME,
            FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN,
            FYAssetSettings.AA_MANIFEST_FILE_NAME
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            string path = FYAssetPathUtility.JoinFilePath(packageDir, candidates[i]);
            if (FileHelper.Exists(path))
                return path;
        }

        throw new InvalidOperationException("交付目录缺少 Manifest 文件: " + packageDir);
    }

    private static void ValidateStreamingAssetsBaseline(BuildTestResult result)
    {
        string buildIndexPath = FYAssetPathUtility.JoinFilePath(
            Application.streamingAssetsPath,
            FYAssetSettings.BUILD_INDEX_FILENAME);
        if (!FileHelper.Exists(buildIndexPath))
            throw new InvalidOperationException("StreamingAssets BuildIndex missing after Full.");
        result.StreamingAssetsBaselineHash = HashGenerator.GenerateFileHash(buildIndexPath);
    }

    private static void ValidateStreamingAssetsUnchangedFromFull(BuildTestResult result)
    {
        string buildIndexPath = FYAssetPathUtility.JoinFilePath(
            Application.streamingAssetsPath,
            FYAssetSettings.BUILD_INDEX_FILENAME);
        if (!FileHelper.Exists(buildIndexPath))
            throw new InvalidOperationException("StreamingAssets BuildIndex missing after Hotfix.");
        string hash = HashGenerator.GenerateFileHash(buildIndexPath);
        if (string.IsNullOrEmpty(result.StreamingAssetsBaselineHash))
            throw new InvalidOperationException("Hotfix 验收缺少 Full 交付的 StreamingAssets 基准摘要。");
        if (!string.Equals(hash, result.StreamingAssetsBaselineHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("StreamingAssets changed during Hotfix; must remain Full baseline.");
    }

    /// <summary>
    /// Hotfix 内容变化必须由被改动夹具引起，且不得包含无关业务 payload 变化。
    /// </summary>
    /// <remarks>
    /// AB 的内容文件名来自 BundleName，包含资源文件名，可直接按夹具标记匹配；
    /// AA 的 Bundle 名来自 Addressables 分组名，被改动资源会迁入 HotfixGroup，
    /// 因此 AA 的 payload 级事实由 <see cref="ValidateAASourceScanDelta"/> 单独断言。
    /// </remarks>
    private static void ValidateHotfixDelta(
        IReadOnlyList<FileHelper.FileDigest> added,
        IReadOnlyList<FileHelper.FileDigest> modified,
        AcceptanceContext ctx)
    {
        var changed = new List<string>();
        CollectNames(added, changed);
        CollectNames(modified, changed);
        if (changed.Count == 0)
            throw new InvalidOperationException("Hotfix 相对基准 Full 没有任何内容变化。");

        if (ctx.Backend == BuildTestBackend.AA)
        {
            ValidateAAContentDelta(changed, ctx);
            return;
        }

        ValidateABContentDelta(changed, ctx);
    }

    /// <summary>
    /// AA 的 payload 变化事实是基准 Full 包内的源快照：本次变化必须恰好是被改动夹具的资源。
    /// </summary>
    private static void ValidateAASourceScanDelta(HotfixBaselineFacts baseFacts, string packageDir)
    {
        if (!AASourceScanFile.TryRead(packageDir, out List<FileHelper.FileDigest> current, out string error))
            throw new InvalidOperationException("AA Hotfix 包缺少本次构建的源快照: " + error);

        string fixturePath = BuildTestFixtures.GetHotfixFixturePath(BuildTestBackend.AA);
        string fixtureGuid = AssetDatabase.AssetPathToGUID(fixturePath);
        if (string.IsNullOrEmpty(fixtureGuid))
            throw new InvalidOperationException("AA 夹具缺少 Asset GUID: " + fixturePath);

        FileHelper.ComputeDiff(
            baseFacts.PreviousSourceScan,
            current,
            out List<FileHelper.FileDigest> added,
            out List<FileHelper.FileDigest> modified,
            out _,
            out List<string> removed);
        var changed = new List<string>();
        CollectNames(added, changed);
        CollectNames(modified, changed);

        if (changed.Count != 1 || !string.Equals(changed[0], fixtureGuid, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "AA Hotfix 源快照变化集合必须恰好是被改动夹具。Expected=" + fixtureGuid
                + ", Actual=" + string.Join(",", changed));
        if (removed.Count > 0)
            throw new InvalidOperationException(
                "AA Hotfix 源快照出现被移除的资源: " + string.Join(",", removed));
    }

    /// <summary>AA 的内容变化必须包含被改动资源迁入 HotfixGroup 后产出的 Bundle。</summary>
    private static void ValidateAAContentDelta(List<string> changed, AcceptanceContext ctx)
    {
        for (int i = 0; i < changed.Count; i++)
        {
            if (changed[i].IndexOf(FYAssetSettings.HOTFIX_GROUP_NAME, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                ctx.FixturePhysicalHint = changed[i];
                return;
            }
        }

        throw new InvalidOperationException(
            "AA Hotfix 变化集合缺少 HotfixGroup Bundle，被改动资源没有进入本次交付: "
            + FYAssetSettings.HOTFIX_GROUP_NAME);
    }

    /// <summary>AB 的内容变化必须来自夹具 Bundle，且不得出现无关 payload 变化。</summary>
    private static void ValidateABContentDelta(List<string> changed, AcceptanceContext ctx)
    {
        string fixturePath = BuildTestFixtures.GetHotfixFixturePath(ctx.Backend);
        string fixtureToken = BuildTestConstants.AddressRaw;
        string fixtureFileToken = Path.GetFileNameWithoutExtension(fixturePath);

        bool fixtureTouched = false;
        for (int i = 0; i < changed.Count; i++)
        {
            string name = changed[i] ?? string.Empty;
            if (name.IndexOf(fixtureToken, StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf(fixtureFileToken, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                fixtureTouched = true;
                ctx.FixturePhysicalHint = name;
            }
        }

        if (!fixtureTouched)
            throw new InvalidOperationException(
                "AB Hotfix delta does not include fixture physical artifact for " + fixtureToken);

        // 非夹具的业务 payload 变更；metadata/catalog/manifest 噪音允许。
        int nonFixturePayload = 0;
        for (int i = 0; i < changed.Count; i++)
        {
            string name = changed[i] ?? string.Empty;
            if (!string.IsNullOrEmpty(ctx.FixturePhysicalHint)
                && string.Equals(name, ctx.FixturePhysicalHint, StringComparison.OrdinalIgnoreCase))
                continue;
            if (name.IndexOf(fixtureToken, StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf(fixtureFileToken, StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)
                || name.IndexOf("manifest", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("catalog", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("hash", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            nonFixturePayload++;
        }

        if (nonFixturePayload > 0)
            throw new InvalidOperationException(
                "AB Hotfix delta contains unrelated payload artifact changes. Count=" + nonFixturePayload);
    }

    private static void CollectNames(IReadOnlyList<FileHelper.FileDigest> list, List<string> names)
    {
        if (list == null)
            return;
        for (int i = 0; i < list.Count; i++)
        {
            if (!string.IsNullOrEmpty(list[i].Name))
                names.Add(list[i].Name);
        }
    }

    private static void ValidatePermanentAddressesInManifest(string packageRoot, BuildTestBackend backend)
    {
        string[] required =
        {
            BuildTestConstants.AddressAsync,
            BuildTestConstants.AddressSync,
            BuildTestConstants.AddressLua
        };

        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (backend == BuildTestBackend.AA)
        {
            CollectAAAddresses(packageRoot, found);
        }
        else
        {
            CollectABAddresses(packageRoot, found);
        }

        for (int i = 0; i < required.Length; i++)
        {
            if (!found.Contains(required[i]))
                throw new InvalidOperationException("Permanent address missing from package/manifest: " + required[i]);
        }

        if (backend == BuildTestBackend.AB && !found.Contains(BuildTestConstants.AddressRaw))
            throw new InvalidOperationException("AB Raw address missing from package/manifest.");
    }

    private static void CollectAAAddresses(string packageRoot, HashSet<string> found)
    {
        string jsonPath = FYAssetPathUtility.JoinFilePath(packageRoot, FYAssetSettings.AA_MANIFEST_FILE_NAME);
        string binPath = FYAssetPathUtility.JoinFilePath(packageRoot, FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN);
        AAManifest manifest = null;
        if (FileHelper.Exists(jsonPath))
            manifest = SerializationUtility.DeserializeJson<AAManifest>(File.ReadAllText(jsonPath, Encoding.UTF8));
        else if (FileHelper.Exists(binPath))
            manifest = SerializationUtility.Deserialize<AAManifest>(File.ReadAllBytes(binPath));

        if (manifest?.AssetEntries == null)
            throw new InvalidOperationException("AAManifest missing or has no AssetEntries.");

        for (int i = 0; i < manifest.AssetEntries.Count; i++)
        {
            PackageEntry entry = manifest.AssetEntries[i];
            if (entry != null && !string.IsNullOrEmpty(entry.key))
                found.Add(entry.key);
        }
    }

    private static void CollectABAddresses(string packageRoot, HashSet<string> found)
    {
        string jsonPath = FYAssetPathUtility.JoinFilePath(packageRoot, FYAssetSettings.MANIFEST_FILE_NAME);
        string binPath = FYAssetPathUtility.JoinFilePath(packageRoot, FYAssetSettings.MANIFEST_FILE_NAME_BIN);
        string content = string.Empty;
        if (FileHelper.Exists(jsonPath))
            content = File.ReadAllText(jsonPath, Encoding.UTF8);
        else if (FileHelper.Exists(binPath))
            content = Encoding.UTF8.GetString(File.ReadAllBytes(binPath));
        else
            throw new InvalidOperationException("ABManifest missing from package.");

        // 这里只做字符串匹配，未反序列化 ABManifest；二进制分支也按 UTF-8 解码后查找。
        if (!string.IsNullOrEmpty(content))
        {
            string[] needles =
            {
                BuildTestConstants.AddressAsync,
                BuildTestConstants.AddressSync,
                BuildTestConstants.AddressLua,
                BuildTestConstants.AddressRaw
            };
            for (int i = 0; i < needles.Length; i++)
            {
                if (content.IndexOf(needles[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    found.Add(needles[i]);
            }
        }

        // 无论是否读到 JSON，文件名命中也会补充地址集合；不能据此证明运行时可解析该 Address。
        string[] files = FileHelper.GetFiles(packageRoot, "*", SearchOption.AllDirectories);
        for (int i = 0; i < files.Length; i++)
        {
            string name = Path.GetFileName(files[i]);
            if (name.IndexOf("fyassetpipelineasync", StringComparison.OrdinalIgnoreCase) >= 0)
                found.Add(BuildTestConstants.AddressAsync);
            if (name.IndexOf("fyassetpipelinesync", StringComparison.OrdinalIgnoreCase) >= 0)
                found.Add(BuildTestConstants.AddressSync);
            if (name.IndexOf("fyassetpipelinelua", StringComparison.OrdinalIgnoreCase) >= 0)
                found.Add(BuildTestConstants.AddressLua);
            if (name.IndexOf("fyassetpipelineraw", StringComparison.OrdinalIgnoreCase) >= 0)
                found.Add(BuildTestConstants.AddressRaw);
        }
    }
}
#endif
