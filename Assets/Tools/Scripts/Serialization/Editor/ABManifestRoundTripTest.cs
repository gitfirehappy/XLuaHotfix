using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// ABManifest 二进制序列化 Round-trip 验证。
/// </summary>
public static class ABManifestRoundTripTest
{
    [MenuItem("Tools/Serialization/Test ABManifest Round-Trip", false, 32)]
    public static void Run()
    {
        BinarySerializerInitializer.Initialize();

        var source = CreateTestManifest();
        byte[] data = SerializeManifest(source);
        var target = DeserializeManifest(data);
        VerifyRoundTrip(source, target);

        Debug.Log("[ABManifestRoundTripTest] PASS - Binary serialization round-trip verified");
    }

    private static ABManifest CreateTestManifest()
    {
        return new ABManifest
        {
            PackageName = "TestPackage",
            PackageVersion = new VersionNumber { Major = 1, Minor = 2, Patch = 3 },
            BuildTimestamp = "2026-04-19T12:00:00Z",
            AssetEntries = new List<ManifestAssetEntry>
            {
                new ManifestAssetEntry
                {
                    EntryId = "guid-001",
                    Address = "Assets/Prefabs/Player.prefab",
                    PrimaryType = "GameObject",
                    Labels = new List<string> { "Player", "Main" },
                    SourcePath = "Assets/Prefabs/Player.prefab",
                    Group = "Characters",
                    AutoAddress = true,
                    BundleIndex = 0,
                    PayloadKind = EPayloadKind.Serialized
                },
                new ManifestAssetEntry
                {
                    EntryId = "guid-002",
                    Address = "Assets/Textures/Icon.png",
                    PrimaryType = "Texture2D",
                    Labels = new List<string> { "UI" },
                    SourcePath = "Assets/Textures/Icon.png",
                    Group = "UI",
                    AutoAddress = true,
                    BundleIndex = 1,
                    PayloadKind = EPayloadKind.RawFile
                }
            },
            BundleEntries = new List<ManifestBundleEntry>
            {
                new ManifestBundleEntry
                {
                    BundleName = "characters_bundle.bundle",
                    FileHash = "abc123def456",
                    FileCRC = 12345678u,
                    FileSize = 1024000L,
                    Encrypted = false,
                    BundleType = "Prefab",
                    Tags = new List<string> { "Required" },
                    DependBundleIndices = new int[0]
                },
                new ManifestBundleEntry
                {
                    BundleName = "ui_bundle.bundle",
                    FileHash = "def789ghi012",
                    FileCRC = 87654321u,
                    FileSize = 512000L,
                    Encrypted = true,
                    BundleType = "Texture",
                    Tags = new List<string>(),
                    DependBundleIndices = new int[] { 0 }
                }
            },
            DeliveryBundles = new List<ManifestBundleEntry>
            {
                new ManifestBundleEntry
                {
                    BundleName = "ui_bundle.bundle",
                    FileHash = "def789ghi012",
                    FileCRC = 87654321u,
                    FileSize = 512000L,
                    Encrypted = true,
                    BundleType = "Texture",
                    Tags = new List<string>(),
                    DependBundleIndices = new int[] { 0 }
                }
            }
        };
    }

    private static byte[] SerializeManifest(ABManifest manifest)
    {
        var codec = SerializationUtility.GetBinaryCodec();
        return codec.Serialize(manifest);
    }

    private static ABManifest DeserializeManifest(byte[] data)
    {
        var manifest = SerializationUtility.Deserialize<ABManifest>(data);
        manifest.Initialize();
        return manifest;
    }

    private static void VerifyRoundTrip(ABManifest source, ABManifest target)
    {
        if (target == null)
            throw new InvalidOperationException("Deserialized manifest is null");

        if (source.PackageName != target.PackageName)
            throw new InvalidOperationException($"PackageName mismatch: {source.PackageName} vs {target.PackageName}");

        if (source.PackageVersion.Major != target.PackageVersion.Major ||
            source.PackageVersion.Minor != target.PackageVersion.Minor ||
            source.PackageVersion.Patch != target.PackageVersion.Patch)
            throw new InvalidOperationException($"PackageVersion mismatch");

        if (source.BuildTimestamp != target.BuildTimestamp)
            throw new InvalidOperationException($"BuildTimestamp mismatch");

        if (source.AssetEntries.Count != target.AssetEntries.Count)
            throw new InvalidOperationException($"AssetEntries count mismatch: {source.AssetEntries.Count} vs {target.AssetEntries.Count}");

        if (source.BundleEntries.Count != target.BundleEntries.Count)
            throw new InvalidOperationException($"BundleEntries count mismatch: {source.BundleEntries.Count} vs {target.BundleEntries.Count}");

        if (source.DeliveryBundles.Count != target.DeliveryBundles.Count)
            throw new InvalidOperationException($"DeliveryBundles count mismatch: {source.DeliveryBundles.Count} vs {target.DeliveryBundles.Count}");

        for (int i = 0; i < source.AssetEntries.Count; i++)
        {
            var src = source.AssetEntries[i];
            var tgt = target.AssetEntries[i];
            if (src.EntryId != tgt.EntryId || src.Address != tgt.Address || src.PrimaryType != tgt.PrimaryType)
                throw new InvalidOperationException($"AssetEntry[{i}] mismatch");
            if (src.PayloadKind != tgt.PayloadKind)
                throw new InvalidOperationException($"AssetEntry[{i}] PayloadKind mismatch");
            if (src.Labels.Count != tgt.Labels.Count)
                throw new InvalidOperationException($"AssetEntry[{i}] Labels count mismatch");

            var runtimeEntry = tgt.ToRuntimeEntry();
            if (runtimeEntry.PayloadKind != tgt.PayloadKind)
                throw new InvalidOperationException($"AssetEntry[{i}] RuntimeAssetEntry PayloadKind mismatch");
        }

        for (int i = 0; i < source.BundleEntries.Count; i++)
        {
            var src = source.BundleEntries[i];
            var tgt = target.BundleEntries[i];
            if (src.BundleName != tgt.BundleName || src.FileHash != tgt.FileHash || src.FileCRC != tgt.FileCRC)
                throw new InvalidOperationException($"BundleEntry[{i}] mismatch");
            if (src.DependBundleIndices.Length != tgt.DependBundleIndices.Length)
                throw new InvalidOperationException($"BundleEntry[{i}] DependBundleIndices length mismatch");
        }

        for (int i = 0; i < source.DeliveryBundles.Count; i++)
        {
            var src = source.DeliveryBundles[i];
            var tgt = target.DeliveryBundles[i];
            if (src.BundleName != tgt.BundleName || src.FileHash != tgt.FileHash || src.FileCRC != tgt.FileCRC)
                throw new InvalidOperationException($"DeliveryBundle[{i}] mismatch");
            if (src.DependBundleIndices.Length != tgt.DependBundleIndices.Length)
                throw new InvalidOperationException($"DeliveryBundle[{i}] DependBundleIndices length mismatch");
        }
    }
}
