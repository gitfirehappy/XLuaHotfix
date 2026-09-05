#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
// HashSet

/// <summary>
/// 独立磁盘验收：从磁盘重载 package / manifest / repository / StreamingAssets。
/// </summary>
public static class BuildTestAcceptance
{
    public sealed class AcceptanceContext
    {
        public BuildTestBackend Backend;
        public bool IsHotfix;
        public string ExpectedVersion;
        public string ExpectedParentVersion;
        public string FixturePhysicalHint;
        public string FullFixtureHash;
        public string HotfixFixtureHash;
    }

    public static void AcceptFull(AcceptanceContext ctx, BuildTestResult result)
    {
        BackendMode mode = BuildTestState.ToBackendMode(ctx.Backend);
        string channelKey = BuildBaselineStore.GetChannelKey(
            string.Empty, BackendModeNames.FromBackendMode(mode));
        BuildBaseline head = BuildBaselineStore.LoadLatest(channelKey);
        if (head == null || head.Version == null)
            throw new InvalidOperationException("Latest baseline missing after Full build.");
        if (!string.IsNullOrEmpty(head.ParentVersion))
            throw new InvalidOperationException("Full HEAD must have no Parent. Actual=" + head.ParentVersion);

        string version = head.Version.GetReleaseVersionString();
        if (!string.Equals(version, ctx.ExpectedVersion, StringComparison.Ordinal))
            throw new InvalidOperationException($"Full version mismatch. Expected={ctx.ExpectedVersion}, Actual={version}");

        VersionRecord versionData = AssetDatabase.LoadAssetAtPath<VersionRecord>(
            FYAssetSettings.Instance.VersionRecordPath);
        if (versionData?.CurrentVersion == null
            || !string.Equals(versionData.CurrentVersion.GetReleaseVersionString(), ctx.ExpectedVersion, StringComparison.Ordinal))
            throw new InvalidOperationException("VersionRecord not advanced to Full version.");

        ValidatePackageOnDisk(head, ctx, result);
        ValidatePackageIndex(head, result);
        ValidateStreamingAssetsBaseline(head, result);
        ValidatePermanentAddressesInManifest(head, ctx.Backend, false);

        result.RepositoryHead = version;
        result.RepositoryParent = head.ParentVersion ?? string.Empty;
        result.ExpectedVersion = ctx.ExpectedVersion;
        result.ActualVersion = version;
    }

    public static void AcceptHotfix(AcceptanceContext ctx, BuildTestResult result)
    {
        BackendMode mode = BuildTestState.ToBackendMode(ctx.Backend);
        string channelKey = BuildBaselineStore.GetChannelKey(
            string.Empty, BackendModeNames.FromBackendMode(mode));
        BuildBaseline head = BuildBaselineStore.LoadLatest(channelKey);
        if (head == null || head.Version == null)
            throw new InvalidOperationException("Latest baseline missing after Hotfix build.");
        if (!string.Equals(head.ParentVersion, ctx.ExpectedParentVersion, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Hotfix parent mismatch. Expected={ctx.ExpectedParentVersion}, Actual={head.ParentVersion}");

        string version = head.Version.GetReleaseVersionString();
        if (!string.Equals(version, ctx.ExpectedVersion, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Hotfix version mismatch. Expected={ctx.ExpectedVersion}, Actual={version}");

        ValidatePackageOnDisk(head, ctx, result);
        ValidatePackageIndex(head, result);
        ValidateStreamingAssetsUnchangedFromFull(result);
        ValidateHotfixDelta(head, ctx);
        ValidatePermanentAddressesInManifest(head, ctx.Backend, true);

        result.RepositoryHead = version;
        result.RepositoryParent = head.ParentVersion ?? string.Empty;
        result.ExpectedVersion = ctx.ExpectedVersion;
        result.ActualVersion = version;
        result.FixturePhysicalArtifact = ctx.FixturePhysicalHint;
    }

    public static void RequireLocalFullIdentity(BuildTestBackend backend, out BuildBaseline head)
    {
        BackendMode mode = BuildTestState.ToBackendMode(backend);
        string channelKey = BuildBaselineStore.GetChannelKey(
            string.Empty, BackendModeNames.FromBackendMode(mode));
        // Standalone Hotfix 的累积基准是 LatestFull（Latest 可能已被任何一次 hotfix 覆写）。
        head = BuildBaselineStore.LoadLatestFull(channelKey);
        if (head == null || head.Version == null || string.IsNullOrEmpty(head.PackageName))
            throw new InvalidOperationException("Local Full baseline missing for Hotfix mode.");
        if (!string.IsNullOrEmpty(head.ParentVersion))
            throw new InvalidOperationException("Standalone Hotfix requires Full HEAD without Parent.");
        if (!FileHelper.DirectoryExists(head.PackageRootDir))
            throw new InvalidOperationException("Local Full package dir missing: " + head.PackageRootDir);

        PackageIndex index = null;
        if (FileHelper.Exists(BuildPathManager.PackageIndexPath))
            index = SerializationUtility.DeserializeJson<PackageIndex>(
                File.ReadAllText(BuildPathManager.PackageIndexPath, Encoding.UTF8));
        if (index == null
            || !string.Equals(index.LatestPackage, head.PackageName, StringComparison.Ordinal)
            || !string.Equals(index.BackendMode, BuildTestPaths.BackendSegment(backend), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Project PackageIndex does not match local Full HEAD.");
        }
    }

    public static void RequireTargetFullIdentity(
        BuildTestTargetSnapshot target,
        BuildTestBackend backend,
        BuildBaseline fullHead)
    {
        string expectedVersion = fullHead.Version.GetReleaseVersionString();
        BuildTestState.ProbeTargetIdentity(
            target,
            BuildTestPaths.BackendSegment(backend),
            fullHead.PackageName,
            expectedVersion,
            true,
            null);
    }

    private static void ValidatePackageOnDisk(
        BuildBaseline head,
        AcceptanceContext ctx,
        BuildTestResult result)
    {
        if (!FileHelper.DirectoryExists(head.PackageRootDir))
            throw new InvalidOperationException("Package root missing: " + head.PackageRootDir);

        BuildTestBackend backend = ctx.Backend;
        string jsonName = backend == BuildTestBackend.AB
            ? FYAssetSettings.MANIFEST_FILE_NAME
            : FYAssetSettings.AA_MANIFEST_FILE_NAME;
        string binName = backend == BuildTestBackend.AB
            ? FYAssetSettings.MANIFEST_FILE_NAME_BIN
            : FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN;
        string jsonPath = FYAssetPathUtility.JoinFilePath(head.PackageRootDir, jsonName);
        string binPath = FYAssetPathUtility.JoinFilePath(head.PackageRootDir, binName);
        if (!FileHelper.Exists(jsonPath) && !FileHelper.Exists(binPath))
            throw new InvalidOperationException("Manifest missing in package: " + head.PackageRootDir);

        // Cross-backend residue checks.
        if (backend == BuildTestBackend.AA)
        {
            if (FileHelper.Exists(FYAssetPathUtility.JoinFilePath(head.PackageRootDir, FYAssetSettings.MANIFEST_FILE_NAME))
                || FileHelper.Exists(FYAssetPathUtility.JoinFilePath(head.PackageRootDir, FYAssetSettings.MANIFEST_FILE_NAME_BIN)))
                throw new InvalidOperationException("AA package contains AB manifest residue.");
        }
        else
        {
            if (FileHelper.Exists(FYAssetPathUtility.JoinFilePath(head.PackageRootDir, FYAssetSettings.AA_MANIFEST_FILE_NAME))
                || FileHelper.Exists(FYAssetPathUtility.JoinFilePath(head.PackageRootDir, FYAssetSettings.AA_MANIFEST_FILE_NAME_BIN))
                || FileHelper.Exists(FYAssetPathUtility.JoinFilePath(head.PackageRootDir, FYAssetSettings.ADDRESSABLES_CATALOG_FILE_NAME)))
                throw new InvalidOperationException("AB package contains AA residue.");
        }

        // AA repository artifacts use Asset GUID as Name (not package file paths).
        // AB repository artifacts use logical BundleName; Full packages ship the full set while Hotfix
        // packages may only ship delivery deltas from CommitDelta.
        long bytes = 0;
        int checkedCount = 0;
        if (backend == BuildTestBackend.AA)
        {
            string bundlesDir = FYAssetPathUtility.JoinFilePath(
                head.PackageRootDir,
                FYAssetSettings.BUNDLES_DIRECTORY_NAME);
            if (!FileHelper.DirectoryExists(bundlesDir)
                || FileHelper.GetFiles(bundlesDir, "*", SearchOption.TopDirectoryOnly).Length == 0)
            {
                throw new InvalidOperationException("AA package bundles directory missing or empty.");
            }

            string catalog = FYAssetPathUtility.JoinFilePath(
                head.PackageRootDir,
                FYAssetSettings.ADDRESSABLES_CATALOG_FILE_NAME);
            if (!FileHelper.Exists(catalog))
                throw new InvalidOperationException("AA catalog.json missing from package.");

            string[] bundleFiles = FileHelper.GetFiles(bundlesDir, "*", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < bundleFiles.Length; i++)
                bytes += new FileInfo(bundleFiles[i]).Length;
            checkedCount = bundleFiles.Length;
        }
        else
        {
            IList<ArtifactDigest> expectedPhysical = head.Artifacts;
            if (ctx.IsHotfix && head.CommitDelta != null && !head.CommitDelta.IsEmpty)
            {
                var delivery = new List<ArtifactDigest>();
                CollectDigests(head.CommitDelta.Added, delivery);
                CollectDigests(head.CommitDelta.Modified, delivery);
                expectedPhysical = delivery;
            }

            if (expectedPhysical != null)
            {
                for (int i = 0; i < expectedPhysical.Count; i++)
                {
                    ArtifactDigest artifact = expectedPhysical[i];
                    if (artifact == null || string.IsNullOrEmpty(artifact.Name))
                        continue;
                    string path = ResolveArtifactPath(head.PackageRootDir, artifact.Name);
                    if (string.IsNullOrEmpty(path))
                        throw new InvalidOperationException("Declared artifact missing: " + artifact.Name);

                    if (!string.IsNullOrEmpty(artifact.Hash))
                    {
                        string hash = HashGenerator.GenerateFileHash(path);
                        if (!string.Equals(hash, artifact.Hash, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Artifact hash mismatch: " + artifact.Name);
                    }
                    if (artifact.CRC != 0)
                    {
                        uint crc = HashGenerator.GenerateFileCRC(path);
                        if (crc != artifact.CRC)
                            throw new InvalidOperationException("Artifact CRC mismatch: " + artifact.Name);
                    }
                    bytes += new FileInfo(path).Length;
                    checkedCount++;
                }
            }
        }

        result.ArtifactCount = head.Artifacts != null ? head.Artifacts.Count : checkedCount;
        result.ArtifactBytes = bytes;

        string manifestPath = FileHelper.Exists(binPath) ? binPath : jsonPath;
        result.ManifestHash = HashGenerator.GenerateFileHash(manifestPath);
        result.PackagePath = head.PackageRootDir;
    }

    private static void ValidatePackageIndex(BuildBaseline head, BuildTestResult result)
    {
        if (!FileHelper.Exists(BuildPathManager.PackageIndexPath))
            throw new InvalidOperationException("Project PackageIndex missing.");
        PackageIndex index = SerializationUtility.DeserializeJson<PackageIndex>(
            File.ReadAllText(BuildPathManager.PackageIndexPath, Encoding.UTF8));
        if (index == null)
            throw new InvalidOperationException("Project PackageIndex invalid.");
        if (!string.Equals(index.LatestPackage, head.PackageName, StringComparison.Ordinal))
            throw new InvalidOperationException("PackageIndex package mismatch.");
        string version = index.LatestVersion != null ? index.LatestVersion.GetReleaseVersionString() : string.Empty;
        string headVersion = head.Version.GetReleaseVersionString();
        if (!string.Equals(version, headVersion, StringComparison.Ordinal))
            throw new InvalidOperationException("PackageIndex version mismatch.");
        if (!string.Equals(index.BackendMode, head.BackendMode, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("PackageIndex backend mismatch.");
        result.PackageIndexIdentity = $"{index.BackendMode}/{index.LatestPackage}/{version}";
    }

    private static void ValidateStreamingAssetsBaseline(BuildBaseline head, BuildTestResult result)
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
        if (!string.IsNullOrEmpty(result.StreamingAssetsBaselineHash)
            && !string.Equals(hash, result.StreamingAssetsBaselineHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("StreamingAssets changed during Hotfix; must remain Full baseline.");
        }
    }

    private static void ValidateHotfixDelta(BuildBaseline head, AcceptanceContext ctx)
    {
        if (head.CommitDelta == null || head.CommitDelta.IsEmpty)
            throw new InvalidOperationException("Hotfix CommitDelta is empty.");

        string fixturePath = BuildTestFixtures.GetHotfixFixturePath(ctx.Backend);
        string fixtureGuid = AssetDatabase.AssetPathToGUID(fixturePath);
        string fixtureToken = ctx.Backend == BuildTestBackend.AB
            ? BuildTestConstants.AddressRaw
            : BuildTestConstants.AddressSync;
        string fixtureFileToken = Path.GetFileNameWithoutExtension(fixturePath);

        bool fixtureTouched = false;
        var changedNames = new List<string>();
        CollectNames(head.CommitDelta.Added, changedNames);
        CollectNames(head.CommitDelta.Modified, changedNames);
        for (int i = 0; i < changedNames.Count; i++)
        {
            string name = changedNames[i] ?? string.Empty;
            bool hit = false;
            if (!string.IsNullOrEmpty(fixtureGuid)
                && string.Equals(name, fixtureGuid, StringComparison.OrdinalIgnoreCase))
            {
                hit = true;
            }
            else if (name.IndexOf(fixtureToken, StringComparison.OrdinalIgnoreCase) >= 0
                     || name.IndexOf(fixtureFileToken, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                hit = true;
            }
            else if (ctx.Backend == BuildTestBackend.AA
                     && name.Length == 32
                     && System.Text.RegularExpressions.Regex.IsMatch(name, "^[0-9a-fA-F]{32}$"))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(name);
                if (!string.IsNullOrEmpty(assetPath)
                    && string.Equals(assetPath, fixturePath, StringComparison.OrdinalIgnoreCase))
                    hit = true;
            }

            if (hit)
            {
                fixtureTouched = true;
                ctx.FixturePhysicalHint = name;
            }
        }

        if (!fixtureTouched)
            throw new InvalidOperationException(
                "Hotfix delta does not include fixture physical artifact for " + fixtureToken);

        // Unrelated business payload changes: allow metadata/catalog/manifest noise.
        int nonFixturePayload = 0;
        for (int i = 0; i < changedNames.Count; i++)
        {
            string name = changedNames[i] ?? string.Empty;
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

            // AA GUID-named assets that are not the fixture still count as payload changes.
            if (ctx.Backend == BuildTestBackend.AA
                && name.Length == 32
                && System.Text.RegularExpressions.Regex.IsMatch(name, "^[0-9a-fA-F]{32}$"))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(name);
                if (!string.IsNullOrEmpty(assetPath)
                    && !string.Equals(assetPath, fixturePath, StringComparison.OrdinalIgnoreCase)
                    && assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                {
                    nonFixturePayload++;
                }
                continue;
            }

            nonFixturePayload++;
        }

        if (nonFixturePayload > 0)
            throw new InvalidOperationException(
                "Hotfix delta contains unrelated payload artifact changes. Count=" + nonFixturePayload);
    }

    private static void CollectNames(List<ArtifactDigest> list, List<string> names)
    {
        if (list == null)
            return;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] != null && !string.IsNullOrEmpty(list[i].Name))
                names.Add(list[i].Name);
        }
    }

    private static void CollectDigests(List<ArtifactDigest> list, List<ArtifactDigest> target)
    {
        if (list == null)
            return;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] != null && !string.IsNullOrEmpty(list[i].Name))
                target.Add(list[i]);
        }
    }

    private static string ResolveArtifactPath(string packageRoot, string artifactName)
    {
        if (string.IsNullOrEmpty(packageRoot) || string.IsNullOrEmpty(artifactName))
            return null;

        string[] candidates =
        {
            FYAssetPathUtility.JoinFilePath(packageRoot, artifactName),
            FYAssetPathUtility.JoinFilePath(packageRoot, FYAssetSettings.BUNDLES_DIRECTORY_NAME, artifactName),
            FYAssetPathUtility.JoinFilePath(packageRoot, FYAssetSettings.BUNDLES_DIRECTORY_NAME, artifactName + ".bundle"),
            FYAssetPathUtility.JoinFilePath(packageRoot, artifactName + ".bundle"),
            FYAssetPathUtility.JoinFilePath(
                packageRoot,
                FYAssetSettings.BUNDLES_DIRECTORY_NAME,
                Path.GetFileName(artifactName))
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            if (FileHelper.Exists(candidates[i]))
                return candidates[i];
        }

        return null;
    }

    private static void ValidatePermanentAddressesInManifest(
        BuildBaseline head,
        BuildTestBackend backend,
        bool hotfix)
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
            CollectAAAddresses(head.PackageRootDir, found);
        }
        else
        {
            CollectABAddresses(head.PackageRootDir, found);
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

        // AB binary is not UTF-8 text; prefer JSON. For binary-only, fall back to package path scan of known fixture names.
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

        // Also accept presence via on-disk fixture-named bundles when JSON is unavailable.
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
