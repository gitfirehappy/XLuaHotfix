# Sub-Plan AAM: AAManifest Rename and HelperBuildData Fusion

> **Risk**: High overall, split into small approval-gated sub-plans
> **Dependencies**: HU-1 hash metadata unification, existing serialization infrastructure, existing Legacy Addressables backend
> **Status**: DONE
> **Source Draft**: `drafts/draft-aa-ab-alignment-analysis-20260518.md` — naming, HelperBuildData fusion, and AA binary serialization sections

---

## Objective

Rename the Legacy AA `VersionState` concept into `AAManifest`, then gradually fuse `AddressableLabelsConfig` data into that manifest so the AA pipeline becomes self-contained like the AB pipeline.

This plan is intentionally destructive and must stay aligned with the approved draft direction. Do not improvise unrelated cleanups or re-architect the touched flow while executing the sub-plans.

Target end state:

- AA package metadata uses `AAManifest.json` and `AAManifest.bin`.
- `AAManifest` contains the Legacy bundle download list plus runtime asset index data.
- Legacy runtime no longer needs `AddressableLabelsConfig` as a hotfix helper-data asset.
- `LuaScriptsIndex` remains loadable and independent. It is not folded into `AAManifest`.
- `HelperBuildData` group has been removed; `LuaScriptsIndex` remains a normal Addressable asset in the `LuaScripts` group.

This plan does not implement Build Repository, PackageIndex rename, path-manager cleanup, or AA group-move refactoring.

---

## Current Verified State

| Area | Current state |
|------|---------------|
| Legacy package metadata | `AAManifest.json/.bin` serialized from `AAManifest`, including bundle metadata and embedded AA asset index lists |
| Bundle download item | `BundleInfo { BundleName, FileHash, FileCRC, FileSize }` |
| Legacy runtime index | `AssetPackageManager.InitializeWithLegacyIndex()` reads `AAManifest.bin` or `AAManifest.json` from `PathManager.CurrentGUIDRoot` |
| Lua index exporter | `LuaScriptsIndexExporter.ExportData()` exports `LuaScriptsIndex` as a normal Addressable asset in the `LuaScripts` group |
| Lua loader | `XLuaLoader` loads `LuaScriptsIndex` through `AssetPackageManager` |
| AB manifest | `ABManifest.json/.bin` already owns asset entries, bundle entries, and runtime query indexes |

---

## PRS Design

### Paradigm

| Mechanism | Data | Invariant |
|-----------|------|-----------|
| AA package manifest | `AAManifest` top-level data object | Owns AA package version, file hash, total bundle size, bundle list, and eventually asset index entries |
| Bundle download metadata | `BundleInfo` | Keeps AA-only download semantics; does not inherit from `ManifestBundleEntry` |
| Runtime asset index metadata | `PackageEntry`, `TypeToKeys`, `LabelToKeys` | Stored in `AAManifest` so Legacy query behavior is package-manifest-backed |
| Serialization formats | JSON and Binary | `AAManifest.bin` is preferred; `AAManifest.json` remains emitted as readable fallback |
| Lua routing data | `LuaScriptsIndex` | Remains a separate asset and must stay loadable through the Legacy runtime path |

### Rules

| Condition | Action | Order | Recovery |
|-----------|--------|-------|----------|
| Existing old package contains only `version_state.json` | No compatibility fallback is retained | AAM-1 | Rebuild the package to emit `AAManifest.json` |
| `AAManifest` lacks embedded index fields | Legacy runtime logs an error; `AddressableLabelsConfig` fallback no longer exists | AAM-5 final | Rebuild the package to emit the current manifest schema |
| Embedded index exists | Legacy runtime builds query caches from `AAManifest` | AAM-3+ | If index invalid, package metadata is invalid |
| Binary serializer not generated or not registered | Do not emit or consume `AAManifest.bin` | AAM-4 | `SerializationUtility` initializes serializer registration before format detection |
| `LuaScriptsIndex` depends on Addressables | Keep it as a normal Addressable asset in the `LuaScripts` group | AAM-5 | Re-run `LuaScriptsIndexExporter.ExportData()` if the asset entry is missing |

### System

| Integration point | Contract |
|-------------------|----------|
| `LegacyAddressableBuildBackend` | Generates `AAManifest` metadata through `GeneratePackageManifest` |
| `LegacyHotfixBackend` | Prefers local/remote `AAManifest.bin`, falls back to `AAManifest.json`, and keeps downloading `catalog.json` |
| `HotfixManager` | Continues to consume `HotfixVersionInfo` and `BundleDownloadItem`; no orchestrator contract change |
| `AssetPackageManager` | Legacy index source is `AAManifest`; direct asset loading remains Addressables-backed |
| `AAAssetIndexBuilder` | Single Editor-only builder for AA `PackageEntry`, `TypeToKeys`, and `LabelToKeys` data written into `AAManifest` |
| `LuaScriptsIndexExporter` | Exports only `LuaScriptsIndex`; it does not build AA asset indexes |
| `BinarySerializerInitializer` | Registers `AAManifest` and `ABManifest` binary Magic values during `SerializationUtility` initialization |

---

## Naming Decisions

| Current | Target | Notes |
|---------|--------|-------|
| `VersionState` | `AAManifest` | Type rename; no compatibility wrapper in AAM-1 |
| `version_state.json` | `AAManifest.json` | File rename; legacy fallback is intentionally not retained |
| `version_state.json` binary equivalent | `AAManifest.bin` | Added only in AAM-4 |
| `BundleInfo` | Keep | AA download-list entry; intentionally separate from AB `ManifestBundleEntry` |
| `AddressableLabelsConfig` | Removed | Data is fused into `AAManifest`; no runtime fallback remains |
| `LuaScriptsIndex` | Keep | Independent Lua routing data, not part of `AAManifest` |

---

## Sub-Plan Sequence

### AAM-1: Rename Shell Directly — DONE

**Goal**: Introduce `AAManifest` naming directly and remove the old `version_state` shell.

Tasks:

| Task | Content |
|------|---------|
| AAM-1-T1 | Rename `VersionState.cs` type to `AAManifest`, preserving `BundleInfo` |
| AAM-1-T2 | Add constants for `AAManifest.json` |
| AAM-1-T3 | Update `LegacyAddressableBuildBackend` to write `AAManifest.json` |
| AAM-1-T4 | Update `LegacyHotfixBackend` to read and write `AAManifest.json` |
| AAM-1-T5 | Rename `GenerateVersionState` to the new manifest-oriented API across `IBuildBackend`, `BuildProjectManager`, and both backends |
| AAM-1-T6 | Verification: `rg "VersionState|version_state"` must show only archived historical references; `dotnet build XLuaHotfix.sln` passes |

Acceptance criteria:

- [x] New Legacy builds emit `AAManifest.json`.
- [x] `FileCRC == 0` compatibility TODO from HU-1 is resolved; AAM-1 no longer carries old `version_state` compatibility comments.
- [x] No `AddressableLabelsConfig` runtime behavior changes in AAM-1.

Out of scope:

- Embedding asset index fields
- Binary output
- Removing HelperBuildData
- Changing `XLuaLoader`

#### AAM-1 Approval Checklist

- [x] `GenerateVersionState` was renamed to `GeneratePackageManifest`.
- [x] AAM-1 introduced only `AAManifest.json`; `.bin` remains AAM-4 scope.
- [x] `VersionState.cs` was physically renamed to `AAManifest.cs`.

---

### AAM-2: Embed Addressable Index Data Into AAManifest — DONE

**Goal**: Make AA manifest contain the same index data currently stored in `AddressableLabelsConfig`.

Tasks:

| Task | Content |
|------|---------|
| AAM-2-T1 | Add `AssetEntries`, `KeysByType`, and `KeysByLabel` fields to `AAManifest` |
| AAM-2-T2 | Extract shared index-building logic from `HelperBuildDataExporter.ExportAddressableLabels()` into `AAAssetIndexBuilder` |
| AAM-2-T3 | Populate the new fields from `LegacyAddressableBuildBackend.GeneratePackageManifest()` through the shared builder |
| AAM-2-T4 | Keep `AddressableLabelsConfig` export as runtime fallback, but make it consume the same builder |
| AAM-2-T5 | Verification: compile and source-audit that both `AAManifest` and `AddressableLabelsConfig` use one index source |

Acceptance criteria:

- [x] `AAManifest.AssetEntries` and `AddressableLabelsConfig.allEntries` are populated from the same `AAAssetIndexBuilder` result.
- [x] `KeysByType` and `KeysByLabel` preserve existing Legacy query behavior: first label as type, `"Untyped"` fallback, original label casing.
- [x] No runtime source switch occurs yet.
- [x] Old JSON files with missing index fields remain safe for current AAM-2 consumers because runtime still reads only version and bundle metadata.

#### AAM-2 Approval Checklist

- [x] `AssetEntries` reuses existing `PackageEntry` directly; no bridge or wrapper DTO is introduced.
- [x] Index-building logic lives in a new shared Editor-only `AAAssetIndexBuilder` utility.
- [x] `LuaScriptsIndex` is not filtered; it is treated as a normal Addressable asset and remains separately loadable.

Execution notes:

- `HelperBuildData` is being retired, not deleted in AAM-2. Its index-building responsibility has moved out first; group deletion waits for AAM-5.
- Because this plan contains destructive follow-up changes, implementation must continue to align with this plan and the promoted draft decisions. Do not perform unrelated cleanup, group deletion, runtime source switching, or asset movement without a sub-plan approval.

---

### AAM-3: Switch Legacy Runtime Index Source — DONE

**Goal**: Make Legacy `AssetPackageManager` build query caches from `AAManifest` instead of loading `AddressableLabelsConfig`.

Tasks:

| Task | Content |
|------|---------|
| AAM-3-T1 | Use the persisted package-root `AAManifest.json`; no cross-object cache was added to `LegacyHotfixBackend` |
| AAM-3-T2 | Add an `AAManifest` loader for `PathManager.CurrentGUIDRoot` inside `AssetPackageManager` |
| AAM-3-T3 | Update `AssetPackageManager.InitializeWithLegacyIndex()` to build caches from `AAManifest` |
| AAM-3-T4 | Keep `AddressableLabelsConfig` fallback for one step with explicit warning |
| AAM-3-T5 | Verify compile and source path: query caches from manifest, loading backend remains Addressables, `LuaScriptsIndex` still loads by address |

Acceptance criteria:

- [x] Legacy query caches are built from `AAManifest` when index data exists.
- [x] `XLuaLoader` can still load `LuaScriptsIndex` because `AssetPackageManager.LoadAssetAsync()` still uses `AddressablesBackend` in Legacy mode.
- [x] Legacy direct Addressables asset loading still works; only query-cache source changed.
- [x] Fallback path logs explicitly when `AddressableLabelsConfig` is used.

#### AAM-3 Approval Checklist

- [x] `AssetPackageManager` independently loads `AAManifest.json` from `PathManager.CurrentGUIDRoot`; `LegacyHotfixBackend` does not become a runtime cache owner.
- [x] `AddressableLabelsConfig` fallback remains only through the transition and is removed with the AAM-5 HelperBuildData retirement step.
- [x] Missing `AAManifest` index data is a warning fallback while compatibility remains, not a hard error.

---

### AAM-4: Add Binary AAManifest — DONE

**Goal**: Add `AAManifest.bin` support after JSON behavior is stable.

Tasks:

| Task | Content |
|------|---------|
| AAM-4-T1 | Added `[BinarySerializable]` and `[BinaryField]` annotations to `AAManifest`, `BundleInfo`, `PackageEntry`, `TypeToKeys`, and `LabelToKeys` |
| AAM-4-T2 | Added `AAManifestMagic = 0x41414D46` in `BinarySerializerInitializer` |
| AAM-4-T3 | Added generated `AAManifest_BinarySerializer.cs` |
| AAM-4-T4 | Legacy build emits both `AAManifest.json` and `AAManifest.bin` |
| AAM-4-T5 | Legacy hotfix/runtime loaders prefer `.bin` and fall back to `.json` |

Acceptance criteria:

- [x] `AAManifest.bin` is serialized/deserialized through `SerializationUtility`.
- [x] Magic registration is initialized from `SerializationUtility` before format detection is used.
- [x] JSON remains available as fallback.
- [x] Binary serializer generated files are included in the relevant project file.

#### AAM-4 Approval Checklist

- [x] Magic value `0x41414D46` (`AAMF`) is used for `AAManifest`.
- [x] AAM-4 keeps emitting JSON alongside binary.
- [x] `.json` remains a fallback after binary support lands.

---

### AAM-5: Retire HelperBuildData Group — DONE

**Goal**: Remove the obsolete HelperBuildData export path after runtime no longer consumes `AddressableLabelsConfig`.

Tasks:

| Task | Content |
|------|---------|
| AAM-5-T1 | Stopped exporting and loading `AddressableLabelsConfig` |
| AAM-5-T2 | Moved `LuaScriptsIndex` to `Assets/Build/LuaScriptsIndex.asset` and the `LuaScripts` Addressables group |
| AAM-5-T3 | Renamed/simplified `HelperBuildDataExporter` to `LuaScriptsIndexExporter` |
| AAM-5-T4 | Removed `HELPER_BUILD_DATA_GROUP_NAME` and `AA_LABELS_CONFIG` constants |
| AAM-5-T5 | Removed HelperBuildData Addressables group/schema assets and the `AddressableLabelsConfig` asset/type |

Acceptance criteria:

- [x] No runtime code loads `AddressableLabelsConfig`.
- [x] `LuaScriptsIndex` remains addressable as `LuaScriptsIndex` in the `LuaScripts` group.
- [x] Addressables settings no longer contain stale helper group entries after cleanup.
- [x] `rg "HelperBuildData|AddressableLabelsConfig"` returns only approved historical docs/plan notes.

#### AAM-5 Approval Checklist

- [x] `LuaScriptsIndex` stays as an Addressable asset in the existing `LuaScripts` group.
- [x] `AddressableLabelsConfig` asset files are deleted immediately.
- [x] `FYAssetSettings.AddressableLabelsConfigPath` is removed.

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
