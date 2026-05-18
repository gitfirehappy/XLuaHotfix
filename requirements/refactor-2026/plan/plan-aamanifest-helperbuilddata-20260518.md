# Sub-Plan AAM: AAManifest Rename and HelperBuildData Fusion

> **Risk**: High overall, split into small approval-gated sub-plans
> **Dependencies**: HU-1 hash metadata unification, existing serialization infrastructure, existing Legacy Addressables backend
> **Status**: Awaiting approval for AAM-1 only
> **Source Draft**: `drafts/draft-aa-ab-alignment-analysis-20260518.md` — naming, HelperBuildData fusion, and AA binary serialization sections

---

## Objective

Rename the Legacy AA `VersionState` concept into `AAManifest`, then gradually fuse `AddressableLabelsConfig` data into that manifest so the AA pipeline becomes self-contained like the AB pipeline.

Target end state:

- AA package metadata uses `AAManifest.json` and later `AAManifest.bin`.
- `AAManifest` contains the Legacy bundle download list plus runtime asset index data.
- Legacy runtime no longer needs `AddressableLabelsConfig` as a hotfix helper-data asset.
- `LuaScriptsIndex` remains loadable and independent. It is not folded into `AAManifest`.
- `HelperBuildData` group is removed only after all runtime consumers no longer depend on it.

This plan does not implement Build Repository, PackageIndex rename, path-manager cleanup, or AA group-move refactoring.

---

## Current Verified State

| Area | Current state |
|------|---------------|
| Legacy package metadata | `version_state.json` serialized from `VersionState` |
| Bundle download item | `BundleInfo { BundleName, FileHash, FileCRC, FileSize }` |
| Legacy runtime index | `AssetPackageManager.InitializeWithLegacyIndex()` loads `AddressableLabelsConfig` through Addressables |
| Helper data exporter | `HelperBuildDataExporter.ExportData()` exports `AddressableLabelsConfig` and `LuaScriptsIndex` into the `HelperBuildData` group |
| Lua loader | `XLuaLoader` loads `LuaScriptsIndex` through `AssetPackageManager` |
| AB manifest | `ABManifest.json/.bin` already owns asset entries, bundle entries, and runtime query indexes |

---

## PRS Design

### Paradigm

| Mechanism | Data | Invariant |
|-----------|------|-----------|
| AA package manifest | `AAManifest` top-level data object | Owns AA package version, file hash, total bundle size, bundle list, and eventually asset index entries |
| Bundle download metadata | `BundleInfo` | Keeps AA-only download semantics; does not inherit from `ManifestBundleEntry` |
| Runtime asset index metadata | `PackageEntry`, `TypeToKeys`, `LabelToKeys` | Semantics match existing `AddressableLabelsConfig` so Legacy query behavior can be preserved |
| Serialization formats | JSON first, Binary second | JSON compatibility lands before Binary rollout; binary serializer must be generated and registered before `.bin` is consumed |
| Lua routing data | `LuaScriptsIndex` | Remains a separate asset and must stay loadable through the Legacy runtime path |

### Rules

| Condition | Action | Order | Recovery |
|-----------|--------|-------|----------|
| Old package contains `version_state.json` | Legacy loader must still read it during migration | AAM-1 before AAM-2 | Fallback from new file name to old file name |
| `AAManifest` lacks embedded index fields | Legacy runtime falls back to `AddressableLabelsConfig` | AAM-2 before AAM-3 | Keep fallback until AAM-3 verification passes |
| Embedded index exists | Legacy runtime may build query caches from `AAManifest` | AAM-3 | If index invalid, log error and fall back only while fallback is explicitly retained |
| Binary serializer not generated or not registered | Do not emit or consume `AAManifest.bin` | AAM-4 after AAM-3 | JSON remains canonical fallback |
| `LuaScriptsIndex` still depends on Addressables | Do not delete HelperBuildData group | AAM-5 only after LuaScriptsIndex placement/load path is confirmed | Keep group until a replacement load path is verified |

### System

| Integration point | Contract |
|-------------------|----------|
| `LegacyAddressableBuildBackend` | Generates `AAManifest` metadata; old method names may be bridged during early steps |
| `LegacyHotfixBackend` | Loads local/remote AA manifest with fallback during migration |
| `HotfixManager` | Continues to consume `HotfixVersionInfo` and `BundleDownloadItem`; no orchestrator contract change |
| `AssetPackageManager` | Legacy index source switches from `AddressableLabelsConfig` to `AAManifest` only after manifest contains equivalent index data |
| `HelperBuildDataExporter` | Shrinks from exporting two helper assets to only Lua-specific data, then may be removed if no longer needed |
| `BinarySerializerInitializer` | Registers `AAManifest` Magic only after generated serializer exists |

---

## Naming Decisions

| Current | Target | Notes |
|---------|--------|-------|
| `VersionState` | `AAManifest` | Type rename; old compatibility bridge required during migration |
| `version_state.json` | `AAManifest.json` | File rename; old file fallback required during migration |
| `version_state.json` binary equivalent | `AAManifest.bin` | Added only in AAM-4 |
| `BundleInfo` | Keep | AA download-list entry; intentionally separate from AB `ManifestBundleEntry` |
| `AddressableLabelsConfig` | Retire later | Data is fused into `AAManifest`; SO remains until runtime fallback is removed |
| `LuaScriptsIndex` | Keep | Independent Lua routing data, not part of `AAManifest` |

---

## Sub-Plan Sequence

### AAM-1: Rename Shell With Backward Compatibility

**Goal**: Introduce `AAManifest` naming without changing Legacy runtime behavior.

Tasks:

| Task | Content |
|------|---------|
| AAM-1-T1 | Rename `VersionState.cs` type to `AAManifest` or introduce `AAManifest` with compatibility aliases, preserving `BundleInfo` |
| AAM-1-T2 | Add constants for `AAManifest.json`, `AAManifest.bin`, and legacy `version_state.json` |
| AAM-1-T3 | Update `LegacyAddressableBuildBackend` to write `AAManifest.json` and optionally write legacy `version_state.json` as a temporary compatibility copy |
| AAM-1-T4 | Update `LegacyHotfixBackend` to prefer `AAManifest.json` and fall back to `version_state.json` |
| AAM-1-T5 | Rename method/comment surfaces that say `GenerateVersionState` only where safe, or add wrapper methods to avoid a large interface rename in this step |
| AAM-1-T6 | Verification: `rg "VersionState|version_state"` must show only approved compatibility references; `dotnet build XLuaHotfix.sln` passes |

Acceptance criteria:

- [ ] Existing old packages with `version_state.json` remain readable.
- [ ] New Legacy builds emit `AAManifest.json`.
- [ ] Temporary legacy compatibility output is explicitly documented if retained.
- [ ] `FileCRC == 0` compatibility TODO from HU-1 is resolved or rewritten to reference `AAManifest`.
- [ ] No `AddressableLabelsConfig` runtime behavior changes in AAM-1.

Out of scope:

- Embedding asset index fields
- Binary output
- Removing HelperBuildData
- Changing `XLuaLoader`

#### AAM-1 Approval Checklist

- [ ] During AAM-1, should new builds emit both `AAManifest.json` and legacy `version_state.json`, or only `AAManifest.json` with loader fallback for old packages?
- [ ] Should `IBuildBackend.GenerateVersionState` be renamed in AAM-1, or kept as a compatibility method until AAM-2/AAM-3?
- [ ] Should the type be a hard rename to `AAManifest`, or should `VersionState` remain as a deprecated wrapper class for one migration step?

---

### AAM-2: Embed Addressable Index Data Into AAManifest

**Goal**: Make AA manifest contain the same index data currently stored in `AddressableLabelsConfig`.

Tasks:

| Task | Content |
|------|---------|
| AAM-2-T1 | Add `AssetEntries`, `KeysByType`, and `KeysByLabel` fields to `AAManifest` |
| AAM-2-T2 | Extract shared index-building logic from `HelperBuildDataExporter.ExportAddressableLabels()` |
| AAM-2-T3 | Populate the new fields from `LegacyAddressableBuildBackend` or a shared AA manifest builder |
| AAM-2-T4 | Keep `AddressableLabelsConfig` export unchanged for runtime fallback |
| AAM-2-T5 | Verification: compare generated manifest index counts against generated `AddressableLabelsConfig` counts |

Acceptance criteria:

- [ ] `AAManifest.AssetEntries.Count` matches `AddressableLabelsConfig.allEntries.Count`.
- [ ] `KeysByType` and `KeysByLabel` are case-compatible with existing Legacy query behavior.
- [ ] No runtime source switch occurs yet.
- [ ] Old JSON files with missing index fields still deserialize safely.

#### AAM-2 Approval Checklist

- [ ] Should `AssetEntries` reuse `PackageEntry` exactly, or introduce a new `AAManifestAssetEntry` that can evolve independently?
- [ ] Should index-building logic live in `LegacyAddressableBuildBackend`, `HelperBuildDataExporter`, or a new shared `AAAssetIndexBuilder` utility?
- [ ] Should entries include helper assets such as `LuaScriptsIndex`, or should helper assets be filtered from the embedded runtime index?

---

### AAM-3: Switch Legacy Runtime Index Source

**Goal**: Make Legacy `AssetPackageManager` build query caches from `AAManifest` instead of loading `AddressableLabelsConfig`.

Tasks:

| Task | Content |
|------|---------|
| AAM-3-T1 | Let `LegacyHotfixBackend` expose loaded local/remote `AAManifest` data to runtime initialization or persist it in a runtime-accessible path |
| AAM-3-T2 | Add an `AAManifest` loader for current package root with old-file fallback |
| AAM-3-T3 | Update `AssetPackageManager.InitializeWithLegacyIndex()` to build caches from `AAManifest` |
| AAM-3-T4 | Keep `AddressableLabelsConfig` fallback for one step with explicit warning |
| AAM-3-T5 | Verify Legacy load/query paths: by type, by label, by labels intersection, direct address load, LuaScriptsIndex load |

Acceptance criteria:

- [ ] Legacy query caches are built from `AAManifest` when index data exists.
- [ ] `XLuaLoader` can still load `LuaScriptsIndex`.
- [ ] Legacy direct Addressables asset loading still works.
- [ ] Fallback path logs explicitly when `AddressableLabelsConfig` is used.

#### AAM-3 Approval Checklist

- [ ] Should `AAManifest` be loaded by `LegacyHotfixBackend` and cached for `AssetPackageManager`, or should `AssetPackageManager` independently load it from `PathManager.CurrentGUIDRoot`?
- [ ] How long should `AddressableLabelsConfig` fallback remain after AAM-3 lands?
- [ ] Should missing `AAManifest` index data be a hard error or a warning fallback while compatibility remains?

---

### AAM-4: Add Binary AAManifest

**Goal**: Add `AAManifest.bin` support after JSON behavior is stable.

Tasks:

| Task | Content |
|------|---------|
| AAM-4-T1 | Add `[BinarySerializable]` and `[BinaryField]` annotations to `AAManifest` and any newly required nested types |
| AAM-4-T2 | Add an `AAManifestMagic` constant in `BinarySerializerInitializer` |
| AAM-4-T3 | Generate `AAManifest_BinarySerializer.cs` |
| AAM-4-T4 | Emit both `AAManifest.json` and `AAManifest.bin` |
| AAM-4-T5 | Update Legacy loader to prefer `.bin`, fall back to `.json`, then old `version_state.json` while compatibility remains |

Acceptance criteria:

- [ ] `AAManifest.bin` round-trips through `SerializationUtility`.
- [ ] Magic registration is present before format detection is used.
- [ ] JSON remains available as fallback.
- [ ] Binary serializer generated files are included in the relevant project file.

#### AAM-4 Approval Checklist

- [ ] Approve Magic value `0x41414D46` (`AAMF`) for `AAManifest`.
- [ ] Should AAM-4 keep emitting JSON permanently, or only during migration?
- [ ] Should old `version_state.json` fallback still exist after binary support lands?

---

### AAM-5: Retire HelperBuildData Group

**Goal**: Remove the obsolete HelperBuildData export path after runtime no longer consumes `AddressableLabelsConfig`.

Tasks:

| Task | Content |
|------|---------|
| AAM-5-T1 | Stop exporting `AddressableLabelsConfig` |
| AAM-5-T2 | Decide and implement the new `LuaScriptsIndex` placement/load strategy |
| AAM-5-T3 | Remove or simplify `HelperBuildDataExporter` |
| AAM-5-T4 | Remove `HELPER_BUILD_DATA_GROUP_NAME` only if no code/config uses it |
| AAM-5-T5 | Clean Addressables group/config assets only after project asset references are verified |

Acceptance criteria:

- [ ] No runtime code loads `AddressableLabelsConfig`.
- [ ] `LuaScriptsIndex` still loads in Legacy and AB modes.
- [ ] Addressables settings no longer contain stale helper entries after cleanup.
- [ ] `rg "HelperBuildData|AddressableLabelsConfig"` returns only approved historical docs or removed references.

#### AAM-5 Approval Checklist

- [ ] Should `LuaScriptsIndex` stay as an Addressable asset in a dedicated Lua group, or move to a non-Addressables runtime data path?
- [ ] Should `AddressableLabelsConfig` asset files be deleted immediately, or kept archived for one release cycle?
- [ ] Should `FYAssetSettings.AddressableLabelsConfigPath` be removed in AAM-5, or retained as a deprecated compatibility field?

---

## Global Invariants

1. AB runtime and AB build pipeline behavior must not change.
2. `HotfixManager` orchestration steps remain stable; backend adapters absorb file-name/format differences.
3. `BundleDownloadItem` remains the common download view.
4. `BundleInfo` and `ManifestBundleEntry` stay separate.
5. `LuaScriptsIndex` must remain loadable after every sub-plan.
6. Binary support cannot be introduced without generated serializers and Magic registration.
7. All new Editor scripts or generated serializers must be included in the relevant `.csproj` before relying on `dotnet build`.
8. Documentation and context sync are required after each executed sub-plan.

---

## Verification Strategy

Every executed sub-plan must run:

- `dotnet build XLuaHotfix.sln`
- targeted `rg` check for old/new names
- targeted runtime/build path grep for changed contracts

Runtime/Unity Editor manual verification required before sign-off:

- Legacy hotfix package build
- Legacy startup hotfix path
- direct address load through `AssetPackageManager`
- label/type query through `AssetPackageManager`
- `XLuaLoader` loading through `LuaScriptsIndex`

---

## Out of Scope

- Build Repository implementation
- `PackageIndex` rename for root `manifest.json`
- BuildPathManager / PathManager rename
- AA group movement refactor
- Removing Addressables from the Legacy loading backend
- Changing ABManifest schema

