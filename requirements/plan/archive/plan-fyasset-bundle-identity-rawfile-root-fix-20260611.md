# FYAsset Bundle Identity + RawFile Root Fix Plan 2026-06-11

> **Status**: Signed off / Archived
> **Requirement ID**: fyasset-bundle-identity-rawfile-root-fix-20260611
> **Origin**: `requirements/review/review-fyasset-mistake-decision-alignment-20260608.md`
> **Scope**: Bundle payload/type identity, RawFile first-class runtime loading, ABManifest payload schema, and manifest physical-membership mapping.
> **Plan Capture**: 2026-06-11

## Goal

Fix the root model error behind RawFile + manifest mismatches:

- A physical bundle output bucket must have one payload route and one exact primary type.
- RawFile must be a first-class runtime asset loaded as bytes/text, not a fake `UnityEngine.Object` loaded through `AssetBundle.LoadAsset`.
- Manifest asset-to-bundle membership must come from actual build output membership, not from a collected logical bundle name alone.

This plan was executed and signed off by the developer on 2026-07-07.

## Locked Decisions

1. Global invariant: every physical bundle bucket is partitioned by `PayloadKind + exact PrimaryType`.
2. `PackTogether` and `PackTogetherByLabel` must not mix different `PrimaryType` values or different `PayloadKind` values.
3. `RawFile` is normalized during scanning to effective `PackSeparately`; one RawFile asset produces one physical output file.
4. Manifest `BundleIndex` is derived from `BundleBuildInfo.AssetPaths` actual membership, not from `CollectedAssetInfo.BundleName` alone.
5. `ManifestAssetEntry` and `RuntimeAssetEntry` store `PayloadKind`.
6. `ABManifest` binary schema is bumped to v3; v2 binary manifests are incompatible and require a new Full baseline build.
7. RawFile runtime v1 API returns direct `byte[]` / `string`; do not introduce `RawFileHandle`, RawFile cache, or RawFile container format.
8. Addressables backend raw APIs return an explicit unsupported error.

## Implementation Checklist

### 1. Bundle Identity

Modify `BundleNameBuilder` so bundle names include payload and exact primary type segments.

Code draft:

```csharp
public static string Build(
    string packageName,
    string groupName,
    BundlePackingMode mode,
    string address,
    string assetGuid,
    List<string> finalLabels,
    EPayloadKind payloadKind,
    string primaryType)
{
    string safePkg = SanitizeSegment(packageName);
    string safeGroup = SanitizeSegment(groupName);
    string payloadSegment = GetPayloadSegment(payloadKind);
    string typeSegment = SanitizeSegment(primaryType);
    string modeSegment = GetModeSegment(mode);
    string bundleKey = GetBundleKey(mode, address, assetGuid, finalLabels);

    return string.Concat(
        safePkg,
        SystemIdentifiers.SegmentSeparator,
        safeGroup,
        SystemIdentifiers.SegmentSeparator,
        payloadSegment,
        SystemIdentifiers.SegmentSeparator,
        typeSegment,
        SystemIdentifiers.SegmentSeparator,
        modeSegment,
        SystemIdentifiers.SegmentSeparator,
        SanitizeBundleKey(bundleKey));
}
```

Expected key semantics:

```csharp
PackTogether key = "all";
PackSeparately key = normalizedAddress + "~" + shortGuid;
PackTogetherByLabel key = sortedLabels or SystemIdentifiers.UnlabeledBundleKey;
```

Acceptance details:

- Type segment uses exact `PrimaryType` string after bundle-name sanitization.
- Payload segment is stable and lower-case, for example `serialized`, `rawfile`, `scene`.
- Existing `BuildShared(packageName, bundleKey)` remains for explicit shared system bundles unless a touched caller needs payload/type identity; if shared bundle identity is touched, shared serialized dependencies must include `serialized` and exact type.
- Keep reserved-character validation for user package/group/labels and bundle keys.

### 2. Scanner Normalization

Modify `CollectionScanner` so RawFile never inherits group packing.

Code draft:

```csharp
private static BundlePackingMode ResolvePackingMode(
    AssetCollectionGroup targetGroup,
    AssetClassification classification)
{
    if (classification.PayloadKind == EPayloadKind.Scene ||
        classification.PayloadKind == EPayloadKind.RawFile)
        return BundlePackingMode.PackSeparately;

    return targetGroup != null
        ? targetGroup.BundlePackingMode
        : BundlePackingMode.PackTogetherByLabel;
}
```

Update the scanner call site:

```csharp
string bundleName = BundleNameBuilder.Build(
    packageName,
    targetGroupName,
    packingMode,
    address,
    guid,
    labels,
    resolvedClassification.PayloadKind,
    primaryType);
```

Acceptance details:

- A RawFile with empty labels in a `PackTogetherByLabel` group still gets an `asset` bundle name with address + GUID.
- A RawFile with the same address/labels/type as a serialized asset cannot collide because payload segment differs and RawFile is independently packed.
- Scene behavior remains independently packed.

### 3. Build-Time Guardrails

Keep `TaskBuildBundles` defensive even after scanner normalization.

Code draft:

```csharp
private static BuildTaskResult ValidateBundleGroup(
    string bundleName,
    List<CollectedAssetInfo> assets)
{
    var payloads = new HashSet<EPayloadKind>();
    var primaryTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    for (int i = 0; i < assets.Count; i++)
    {
        payloads.Add(assets[i].Classification.PayloadKind);
        primaryTypes.Add(assets[i].PrimaryType ?? string.Empty);
    }

    if (payloads.Count != 1)
        return BuildTaskResult.Fail(BuildErrorCodes.MixedPayloadBundle, ...);

    if (primaryTypes.Count != 1)
        return BuildTaskResult.Fail(BuildErrorCodes.MixedPrimaryTypeBundle, ...);

    EPayloadKind payload = assets[0].Classification.PayloadKind;
    if (payload == EPayloadKind.RawFile && assets.Count != 1)
        return BuildTaskResult.Fail(BuildErrorCodes.RawfileMultiAsset, ...);

    return BuildTaskResult.Ok();
}
```

Acceptance details:

- Validate every `BundleName` group before calling Unity `BuildPipeline.BuildAssetBundles`.
- Serialized groups build through Unity as before.
- Scene groups remain one asset per scene output.
- RawFile groups copy exactly one source file to exactly one physical output.
- Any mixed payload/type group fails before output is produced.

### 4. Manifest Membership

Modify `TaskGenerateManifest` so manifest membership comes from build results.

Code draft:

```csharp
private readonly struct BuildMembershipKey : IEquatable<BuildMembershipKey>
{
    public readonly string AssetPath;
    public readonly string LogicalBundleName;
}

var membership = new Dictionary<BuildMembershipKey, int>(BuildMembershipKeyComparer.OrdinalIgnoreCase);

for (int bundleIndex = 0; bundleIndex < buildResults.Count; bundleIndex++)
{
    BundleBuildInfo b = buildResults[bundleIndex];
    for (int p = 0; p < b.AssetPaths.Count; p++)
    {
        var key = new BuildMembershipKey(b.AssetPaths[p], b.BundleName);
        if (membership.ContainsKey(key))
            return BuildTaskResult.Fail(BuildErrorCodes.DuplicateManifestMembership, ...);
        membership[key] = bundleIndex;
    }
}

for (int i = 0; i < collected.Count; i++)
{
    CollectedAssetInfo a = collected[i];
    var key = new BuildMembershipKey(a.AssetPath, a.BundleName);
    if (!membership.TryGetValue(key, out int bundleIndex))
        return BuildTaskResult.Fail(BuildErrorCodes.ManifestMembershipMissing, ...);

    BundleBuildInfo b = buildResults[bundleIndex];
    if (b.PayloadKind != a.Classification.PayloadKind)
        return BuildTaskResult.Fail(BuildErrorCodes.ManifestPayloadMismatch, ...);

    assetEntries.Add(new ManifestAssetEntry
    {
        EntryId = a.AssetGUID,
        Address = a.Address ?? "",
        PrimaryType = a.PrimaryType ?? "",
        Labels = a.Labels != null ? new List<string>(a.Labels) : new List<string>(),
        SourcePath = a.AssetPath ?? "",
        Group = a.GroupName ?? "",
        AutoAddress = true,
        BundleIndex = bundleIndex,
        PayloadKind = a.Classification.PayloadKind
    });
}
```

Acceptance details:

- Do not create manifest asset entries for collected assets missing from actual `BundleBuildInfo.AssetPaths`.
- Duplicate actual membership is fatal.
- Payload mismatch between collected metadata and actual output is fatal.
- `BundleNameToIndex` may still be used for dependency graph lookup, but not as the asset membership authority.

### 5. Manifest And Runtime Entry Schema

Add `PayloadKind` to serialized and runtime entries.

Code draft:

```csharp
public class ManifestAssetEntry
{
    ...
    [BinaryField(8)]
    public EPayloadKind PayloadKind;
}
```

```csharp
public class RuntimeAssetEntry
{
    ...
    public EPayloadKind PayloadKind;
}
```

```csharp
public RuntimeAssetEntry ToRuntimeEntry()
{
    var entry = new RuntimeAssetEntry
    {
        EntryId = EntryId,
        Address = Address,
        PrimaryType = PrimaryType,
        SourcePath = SourcePath,
        Group = Group,
        AutoAddress = AutoAddress,
        PayloadKind = PayloadKind
    };
    entry.SetLabels(Labels);
    return entry;
}
```

Schema draft:

```csharp
[BinarySerializable(Magic = 0x41424D46, SchemaVersion = 3)]
public class ABManifest
{
}
```

Acceptance details:

- Regenerate binary serializers after adding the field.
- Update `BinarySerializerInitializer` ABManifest registration to schema version `3`.
- Update ABManifest binary round-trip test data and assertions to include `PayloadKind`.
- Old v2 binary manifests are rejected; rebuild Full baseline after this change.
- JSON manifests generated after this change include `PayloadKind`.

### 6. RawFile Runtime API

Add v1 direct bytes/text APIs without RawFile handle or cache.

Public API draft:

```csharp
public Task<byte[]> LoadRawBytesAsync(
    string address,
    IReadOnlyList<string> labels = null);

public byte[] LoadRawBytesSync(
    string address,
    IReadOnlyList<string> labels = null);

public Task<string> LoadRawTextAsync(
    string address,
    IReadOnlyList<string> labels = null,
    Encoding encoding = null);

public string LoadRawTextSync(
    string address,
    IReadOnlyList<string> labels = null,
    Encoding encoding = null);
```

Backend draft:

```csharp
public interface IPackageBackend
{
    Task<(byte[] data, RuntimeMessage error)> LoadRawBytesAsync(string key, string entryId);
    (byte[] data, RuntimeMessage error) LoadRawBytesSync(string key, string entryId);
}
```

AB backend behavior draft:

```csharp
if (assetEntry.PayloadKind != EPayloadKind.RawFile)
    return (null, RuntimeMessage.InvalidPayloadKind(...));

ManifestBundleEntry bundleEntry = _manifest.GetBundleForAsset(assetEntry);
string rawPath = _bundlePathResolver.ResolveExistingPath(bundleEntry.BundleName);
byte[] data = await FileHelper.ReadAllBytesAsync(rawPath);
return (data, null);
```

Acceptance details:

- Raw bytes/text APIs resolve entries by address + optional labels and require `PayloadKind.RawFile`.
- `LoadAsset<T>` rejects RawFile entries with a clear runtime error such as `INVALID_PAYLOAD_KIND`.
- RawFile reading uses the same hotfix-first then StreamingAssets fallback path policy as AB bundles.
- Async raw reads use `FileHelper.ReadAllBytesAsync`.
- Sync raw reads only support real filesystem paths; if a platform path cannot be synchronously read, return a clear unsupported error rather than using `AssetBundle`.
- `AddressablesBackend` raw methods return explicit unsupported errors.

## Verification

- `dotnet build XLuaHotfix.sln --no-restore`
- `git diff --check`
- ABManifest binary round-trip covers:
  - schema v3
  - `ManifestAssetEntry.PayloadKind`
  - `RuntimeAssetEntry.PayloadKind` conversion
- Static checks:
  - `PackTogetherByLabel` bundle name includes payload and type segments.
  - `PackTogether` bundle name includes payload and type segments.
  - RawFile cannot inherit Group packing.
  - Manifest asset mapping uses `BundleBuildInfo.AssetPaths`.
  - `LoadAsset<T>` rejects RawFile.
  - Raw bytes/text path does not call `AssetBundle.LoadAsset`.
- Focused fixture:
  - Same Group, same empty labels, different exact `PrimaryType` produce separate bundle names.
  - Same Group, same empty labels, same exact `PrimaryType`, same `Serialized` payload can still pack together.
  - RawFile in a `PackTogetherByLabel` Group produces one raw output and loads through bytes/text.

## Non-Goals

- Do not execute any code fix as part of plan capture.
- Do not modify `Assets/FYAsset/CollectorData/CollectorSetting.asset`.
- Do not archive `requirements/review/review-fyasset-mistake-decision-alignment-20260608.md`.
- Do not change Push, Repository, or Hotfix delivery semantics.
- Do not introduce a RawFile container format.
- Do not introduce `RawFileHandle`, RawFile caching, or RawFile ref-counting.
- Do not change AA Addressables asset loading behavior beyond explicit raw API unsupported errors.

## Requirements Records

- Keep this plan active in `requirements/plan/`.
- Keep `requirements/review/review-fyasset-mistake-decision-alignment-20260608.md` open with root-cause extraction marked.
- Update `requirements/progress.txt` when plan capture is written, when execution starts, when verification completes, and when sign-off/archive happens.
