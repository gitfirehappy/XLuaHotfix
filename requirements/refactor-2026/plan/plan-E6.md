# Sub-Plan E6: ABManifest Generation

> **Risk**: Low (ABManifest runtime structure already implemented in B6; E6 only adds build-time assembly logic)
> **Dependencies**: E5-1 (IBuildTask + BuildContext + BuildContextKeys), E5-2 (BundleBuildInfo), E4 (BundleDependencyGraph), B6 (ABManifest + ManifestAssetEntry + ManifestBundleEntry + SerializationUtility)
> **Status**: Draft — discussion complete, pending approval

---

## Objective

Implement `TaskGenerateManifest`, the E5 backbone node 5 that consumes `CollectedAssets` + `BundleBuildResults` from BuildContext and produces a fully populated `ABManifest`. This is the bridge between the new build pipeline (E5) and the existing runtime data structures (B6).

The ABManifest runtime structure (`ABManifest`, `ManifestAssetEntry`, `ManifestBundleEntry`, serialization, runtime indexing) was implemented in B6 and requires NO modification. E6 only adds the build-time assembly logic.

---

## Confirmed Design Decisions

### D1: Mapping — CollectedAssetInfo → ManifestAssetEntry

| CollectedAssetInfo | ManifestAssetEntry | Note |
|---------------------|---------------------|------|
| AssetGUID | EntryId | Direct mapping |
| Address | Address | Empty for ImplicitDependency |
| PrimaryType | PrimaryType | Direct mapping |
| Labels | Labels | Shallow copy; empty for ImplicitDependency |
| AssetPath | SourcePath | Direct mapping |
| GroupName | Group | From IGroupRule routing (E1-3) |
| — | AutoAddress | V1: true for all non-empty Address; manual override defer to future |
| BundleName → index | BundleIndex | Resolved via `bundleNameToIndex` dictionary |

### D2: Mapping — BundleBuildInfo + BundleDependencyGraph → ManifestBundleEntry

| Source | ManifestBundleEntry | Note |
|--------|---------------------|------|
| BundleBuildInfo.OutputFileName | BundleName | Full file name with hash |
| BundleBuildInfo.Hash | FileHash | From Unity BuildPipeline |
| CRC32 computed from built file | FileCRC | E6 computes CRC32; see D3 |
| BundleBuildInfo.Size | FileSize | Direct mapping |
| default false | Encrypted | No encryption in V1 |
| Inferred from bundle contents | BundleType | See D4 |
| Aggregated from CollectedAssetInfo.Tags | Tags | Union of all asset Tags within the bundle (see D6) |
| BundleDependencyGraph edges | DependBundleIndices | int[] pointing to BundleEntries indices |

### D3: FileCRC — Computed at Build Time

CRC32 computed from the built bundle file at TaskGenerateManifest time. Standard CRC32 polynomial (0xEDB88320). Implementation as a small static utility `CRC32Helper.Compute(string filePath)`.

Rationale: BundleBuildInfo already has file path and size. CRC32 is cheap to compute (~MB/s). Having it immediately enables integrity checks without a follow-up pass.

### D4: BundleType Inference — >80% Threshold Rule

For each bundle, count PrimaryType distribution across contained assets:

```
if dominantTypeRatio > 0.8 → mapToBundleType(dominantPrimaryType)
else → EBundleType.Mixed
```

PrimaryType → EBundleType mapping:

| PrimaryType | EBundleType |
|-------------|-------------|
| GameObject | Prefab |
| Texture2D, Sprite, SpriteAtlas | Texture |
| Shader, ShaderVariantCollection | Shader |
| AudioClip, AudioMixer | Audio |
| MonoScript | Script |
| SceneAsset | Scene |
| ScriptableObject, Material, AnimationClip, AnimatorController, AnimatorOverrideController, Font, PhysicMaterial, Mesh, VideoClip | Config |
| (anything else) | Mixed |

### D5: DependBundleIndices — From BundleDependencyGraph

```
For each Bundle in BundleEntries:
  Query BundleDependencyGraph.Edges where FromBundle == this.BundleName (logical name)
  For each matching edge:
    Resolve ToBundle → index in BundleEntries
    Add to DependBundleIndices[]
```

Name→index resolution via `Dictionary<string, int> bundleNameToIndex` built from `BundleBuildInfo.BundleName` (logical name) → position in list.

### D6: Bundle-Level Tags — Aggregated from Asset Tags

`ManifestBundleEntry.Tags` = union of `CollectedAssetInfo.Tags` across all assets in the same bundle (deduplicated). Tags are download strategy identifiers ("startup", "background", "on-demand").

Source chain: `Collector.Tags ∪ Group.Tags` (E1-3 merge at asset level) → `CollectedAssetInfo.Tags` (per-asset) → E6 union aggregation (per-bundle) → `ManifestBundleEntry.Tags`.

Multiple Collectors routing to the same Bundle (via same Group + PackRule) → all their Tags union into the Bundle's Tags. Simple set union, no frequency counting or priority rules needed.

---

## TaskGenerateManifest Specification

```
ReadKeys:  CollectedAssets, BundleBuildResults, BuildVersion
WriteKeys: ABManifest
DependsOn: [TaskBuildBundles]

Logic:
  1. ctx.Require<List<CollectedAssetInfo>>("CollectedAssets")
  2. ctx.Require<List<BundleBuildInfo>>("BundleBuildResults")
  3. ctx.Require<string>("BuildVersion")
  4. (optional) ctx.Get<BundleDependencyGraph>("BundleDependencyGraph")

  5. Build ManifestBundleEntry list:
     For each BundleBuildInfo:
       a. Map basic fields (BundleName, FileHash, FileSize)
       b. Compute CRC32 from built file
       c. Assign temporary index for later DependBundleIndices resolution
       d. Build bundleNameToIndex dict (logical name → index)

  6. Build ManifestAssetEntry list:
     For each CollectedAssetInfo:
       a. Map fields per D1
       b. Resolve BundleName → BundleIndex via bundleNameToIndex
       c. If BundleName not found → error (data integrity)

  7. Infer BundleType for each ManifestBundleEntry:
     Group ManifestAssetEntry by BundleIndex
     Count PrimaryType per bundle → apply D4 threshold rule

  8. Resolve DependBundleIndices:
     For each ManifestBundleEntry:
       Query BundleDependencyGraph for outgoing edges
       Resolve ToBundle names → BundleEntry indices
       Set DependBundleIndices

  9. Assemble ABManifest:
     PackageName = from CollectorSetting SO (first Package name, or config)
     PackageVersion = ParseVersion(BuildVersion)
     BuildTimestamp = DateTime.UtcNow.ToString("o")  (ISO 8601)
     AssetEntries = assembled list
     BundleEntries = assembled list

  10. Write ABManifest to BuildContext
```

---

## New Files

| File | Path | Assembly | Lines (est.) | Description |
|------|------|----------|-------------|-------------|
| TaskGenerateManifest.cs | Build/Pipeline/Editor/Tasks/ | Editor | ~180 | IBuildTask: mapping + BundleType inference + ABManifest assembly |
| CRC32Helper.cs | Build/Pipeline/Editor/ | Editor | ~40 | Static utility: Compute(string filePath) → uint |

All paths relative to `Assets/FYAsset/Scripts/`.

---

## Modified Files

None. All runtime structures (`ABManifest`, `ManifestAssetEntry`, `ManifestBundleEntry`) are B6 deliverables and already exist with correct fields.

---

## Task Breakdown

| Task | Content | Depends On |
|------|---------|-----------|
| E6-T1 | Create `CRC32Helper.cs` (static CRC32 computation from file) | — |
| E6-T2 | Create `TaskGenerateManifest.cs` — implement IBuildTask: read ctx, map BundleBuildInfo → ManifestBundleEntry | T1, E5-1 done, E5-2 done |
| E6-T3 | Extend `TaskGenerateManifest.cs` — map CollectedAssetInfo → ManifestAssetEntry + BundleIndex resolution | T2 |
| E6-T4 | Extend `TaskGenerateManifest.cs` — BundleType inference (>80% threshold + PrimaryType mapping table) | T2 |
| E6-T5 | Extend `TaskGenerateManifest.cs` — DependBundleIndices resolution from BundleDependencyGraph | T2, E4 done |
| E6-T6 | Extend `TaskGenerateManifest.cs` — assemble ABManifest (PackageName, PackageVersion, BuildTimestamp) + write to BuildContext | T2, T3, T4, T5 |
| E6-T7 | Compilation verification (`dotnet build XLuaHotfix.sln`) | All above |

---

## Invariants (Must Hold After E6)

1. `TaskGenerateManifest` correctly implements `IBuildTask` with ReadKeys/WriteKeys per E5 D7 contract
2. Every `CollectedAssetInfo` with a valid `BundleName` maps to exactly one `ManifestAssetEntry` with correct `BundleIndex`
3. `CollectedAssetInfo` with missing/invalid `BundleName` → error, no partial manifest produced
4. Every `BundleBuildInfo` maps to exactly one `ManifestBundleEntry` with correct `BundleName`, `FileHash`, `FileSize`, `FileCRC`
5. `DependBundleIndices` correctly reflects `BundleDependencyGraph` edges; all indices in range
6. `BundleType` inference: >80% dominant type → specific type; else → Mixed
7. `FileCRC` is computed from actual built bundle file; non-zero for valid files
8. `ABManifest` is fully populated and `Initialize()` can be called without exception
9. `dotnet build XLuaHotfix.sln` passes with 0 errors

---

## Not In Scope

- ABManifest / ManifestAssetEntry / ManifestBundleEntry data structures (B6 implemented)
- Binary serialization format / SerializationUtility (B6 implemented)
- Runtime indexing / ABAssetIndex (B6/B7 implemented)
- Bundle-level Tags population (B9 / future)
- Manifest compression or encryption
- VersionNumber type definition (assumed to exist in B6 scope; if not, E6 creates a minimal wrapper)

---

## Approval Checklist

- [ ] Agree to `TaskGenerateManifest` as a single IBuildTask consuming existing runtime structures
- [ ] Agree to CollectedAssetInfo→ManifestAssetEntry mapping table (D1)
- [ ] Agree to BundleBuildInfo→ManifestBundleEntry mapping (D2) including CRC32 computation
- [ ] Agree to >80% threshold BundleType inference (D4) with PrimaryType mapping table
- [ ] Agree to DependBundleIndices resolution from BundleDependencyGraph (D5)
- [ ] Agree to Bundle-level Tags = union of asset Tags within each bundle (D6)
- [ ] Agree to AutoAddress = true for V1 (no manual override yet)
- [ ] Agree to 2 new files + 0 modified files + 7 tasks
- [ ] Agree E6 does NOT need sub-plan split
