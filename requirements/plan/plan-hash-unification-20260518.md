# Sub-Plan HU-1: Hash Metadata Unification

> **Risk**: Low-Medium
> **Dependencies**: Existing `HashGenerator.GenerateFileHash` / `HashGenerator.GenerateFileCRC`, existing AB `ManifestBundleEntry.FileCRC`
> **Status**: Executed — 2026-05-18
> **Source Draft**: `drafts/draft-aa-ab-alignment-analysis-20260518.md` — Hash unification section

---

## Objective

Unify AA and AB bundle metadata around a two-hash contract:

- `FileHash` / MD5 remains the content identity used for diff, version comparison, and incremental download decisions.
- `FileCRC` / CRC32 becomes the fast verification checksum used after bundle copy or download.

This first sub-plan is intentionally small. It aligns metadata and adds download-time verification only. It does not change runtime AssetBundle loading behavior.

---

## Background

Current verified state:

| Pipeline | MD5 | CRC32 | Current gap |
|----------|-----|-------|-------------|
| AA / `VersionState.BundleInfo` | `FileHash` exists | Missing | AA cannot provide fast checksum metadata |
| AB / `ManifestBundleEntry` | `FileHash` exists | `FileCRC` exists | CRC is stored but not propagated into the common download item |
| Common hotfix download path | `BundleDownloadItem.FileHash` exists | Missing | `HotfixManager` cannot verify copied/downloaded bundles by CRC |

The AA-AB alignment draft confirmed that MD5 and CRC32 serve different purposes and should coexist rather than replace each other.

---

## Confirmed Design Decisions

### D1: MD5 Remains the Content Identity

`FileHash` continues to mean content identity.

It is used for:

- build-time diff
- version comparison
- deciding whether a local bundle can be reused instead of downloaded

### D2: CRC32 Is Added as Fast Verification Metadata

`FileCRC` means fast checksum.

It is used for:

- validating copied bundles
- validating downloaded bundles
- future runtime corruption checks

### D3: AA and AB Keep Their Existing Manifest Types

This plan does not rename or replace `VersionState`, `BundleInfo`, `ABManifest`, or `ManifestBundleEntry`.

AA keeps `version_state.json`. AB keeps `ABManifest.json` / `ABManifest.bin`.

### D4: Runtime Loading CRC Checks Are Deferred

`ABBundleLoader` and the AA loading path are not changed in this sub-plan.

Load-before-use corruption detection can be planned separately after download-time verification is stable.

### D5: Legacy Missing CRC Means Skip Verification

Old AA `version_state.json` files do not contain `BundleInfo.FileCRC`. Json deserialization leaves the field as `0`.

`0` is treated as "CRC metadata unavailable" and skips CRC verification. A source TODO marks this as temporary until the broader `VersionState` unification decision from the AA-AB alignment draft is executed.

---

## Planned Changes

| Area | File | Change |
|------|------|--------|
| AA metadata model | `Assets/FYAsset/Scripts/Build/BuildManage/HelperBuildData_Remote/VersionState.cs` | Add `BundleInfo.FileCRC` as `uint` |
| AA build export | `Assets/FYAsset/Scripts/Build/BuildManage/Editor/LegacyAddressableBuildBackend.cs` | Compute `FileCRC` with `HashGenerator.GenerateFileCRC(file)` when writing `version_state.json` |
| Common download item | `Assets/FYAsset/Scripts/LegacyRuntime/IHotfixPipeline.cs` | Add `BundleDownloadItem.FileCRC` |
| Legacy hotfix adapter | `Assets/FYAsset/Scripts/LegacyRuntime/LegacyHotfixBackend.cs` | Populate `BundleDownloadItem.FileCRC` from `BundleInfo.FileCRC` |
| AB hotfix adapter | `Assets/FYAsset/Scripts/LegacyRuntime/ABHotfixBackend.cs` | Populate `BundleDownloadItem.FileCRC` from `ManifestBundleEntry.FileCRC` |
| Download verification | `Assets/FYAsset/Scripts/LegacyRuntime/HotfixManager.cs` | Verify target bundle CRC after copy/download when `FileCRC != 0` |

---

## Task Breakdown

| Task | Content | Depends On |
|------|---------|------------|
| HU1-T1 | Add `BundleInfo.FileCRC` and generate it in AA `version_state.json` | Existing `HashGenerator.GenerateFileCRC` |
| HU1-T2 | Add `BundleDownloadItem.FileCRC` and propagate it from both hotfix backends | T1 for AA data source; existing AB field |
| HU1-T3 | Add CRC verification helper in `HotfixManager` and apply it after local copy and network download | T2 |
| HU1-T4 | Verification: grep field propagation, run `dotnet build XLuaHotfix.sln`, and inspect no direct CRC implementation is duplicated outside `HashGenerator` | T1-T3 |

---

## Invariants

1. `FileHash` behavior must not change.
2. Existing AA `version_state.json` files without `FileCRC` must remain readable; missing CRC is treated as `0` and verification is skipped.
3. AB `ManifestBundleEntry.FileCRC` remains the single AB CRC source.
4. CRC calculation must use `HashGenerator.GenerateFileCRC`; no new CRC implementation is allowed.
5. Download decisions continue to use `FileHash`, not `FileCRC`.
6. This sub-plan must not change `ABBundleLoader` load behavior.

---

## Acceptance Criteria

- [x] Fresh AA `version_state.json` includes `FileCRC` for each bundle.
- [x] `BundleDownloadItem` carries `FileCRC` for both AA and AB backends.
- [x] Copied bundles are CRC-checked when CRC metadata exists.
- [x] Downloaded bundles are CRC-checked when CRC metadata exists.
- [x] CRC mismatch fails the hotfix flow with an explicit error log.
- [x] `dotnet build XLuaHotfix.sln` passes with 0 errors.
- [x] `rg "GenerateFileCRC|FileCRC"` confirms CRC calculation is centralized through `HashGenerator` and propagated through the planned files.

---

## Out of Scope

- Build Repository implementation
- `ArtifactDigest` implementation
- Runtime AssetBundle load-time CRC verification
- Renaming `VersionState`, `BundleInfo`, `Manifest`, or `ABManifest`
- Replacing `version_state.json`
- Changing AA group movement or hotfix diff behavior

---

## Approval Checklist

- [x] AA `BundleInfo` should add `FileCRC` now, while keeping old `version_state.json` readable with `FileCRC = 0`.
  **Decision**: Approved by developer on 2026-05-18. Add TODO in source comments because the AA-AB draft already contains a later `VersionState` unification decision.
- [x] `BundleDownloadItem` should become the common carrier for `FileCRC` across both AA and AB hotfix backends.
  **Decision**: Yes.
- [x] `HotfixManager` should verify CRC after copy/download when `FileCRC != 0`.
  **Decision**: Yes.
- [x] Runtime load-time CRC verification should stay deferred and out of this small sub-plan.
  **Decision**: Yes.
- [x] CRC calculation must only use `HashGenerator.GenerateFileCRC`, with no duplicated CRC utility.
  **Decision**: Yes. `HashGenerator` was moved from Editor-only path into runtime-visible helpers so both build and runtime verification use the same implementation.

---

## Execution Summary

HU-1 landed on 2026-05-18.

Implemented:

- `BundleInfo.FileCRC`
- AA `version_state.json` CRC generation
- `BundleDownloadItem.FileCRC`
- AA/AB backend CRC propagation
- `HotfixManager` copy/download CRC verification
- `HashGenerator` moved to runtime-visible helper path with Editor-only `GenerateDeepHash`

Verification:

- `dotnet build XLuaHotfix.sln` passed with 0 errors.
- Existing warnings remained: `System.Net.Http` version conflict and `.AdditionalFile.txt` analyzer warning.
