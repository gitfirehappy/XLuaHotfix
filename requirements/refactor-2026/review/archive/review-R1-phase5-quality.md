# Code Quality Review: R1 + Phase 5 (2026-04-28)

> Scope: 34 files across Build/Collector, Build/Editor, Runtime/Models, Runtime/Backends/AB
> Method: Architecture design, code cleanliness, style consistency, redundancy, design patterns
> Basis: ⬜ Jobs flavor — pixel-perfect, subtraction-first, DRI
> **Processed**: 2026-05-11 · R1 + Phase 5 executed, fixes in `c617631`
> **Status**: 📦 Archived

## CR-1 [Critical] HandleRegistry-to-AssetCache refcount desynchronization (use-after-free)

**Files**: ABPackageBackend.cs, HandleRegistry.cs
**Category**: Runtime correctness

### Reproduction

1. Two `AssetHandle<T>` created for same asset via `LoadByAddress` (AssetCache.RefCount=2, two HandleRegistry slots each RefCount=1)
2. `UnloadAsset(key)` called directly (bypasses Handle) → AssetCache.RefCount decrements to 1
3. `Handle2.Release()` fires callback → `UnloadByEntryId` → AssetCache.RefCount hits 0 → `bundle.Unload(true)` destroys asset
4. `Handle1` still valid in HandleRegistry (RefCount=1) but pointing to destroyed asset

### Root cause

`AssetCache._assetCache` refcount and `HandleRegistry` Slot.RefCount are independent counters. `ReleaseEntry()` decrements AssetCache count on ANY release path, but this count is global (not per-Handle).

### Proposed fix

Option A: Remove AssetCache refcount entirely — each Handle owns one ref, Asset is released only when ALL handles are released (HandleRegistry tracks shared refcount per EntryId).

Option B: Route ALL release through Handle — deprecate direct `UnloadAsset(key)` call, force assets loaded via Handle path to be released via Handle.

## MJ-1 [Major] ABPackageBackend dual error contract

**File**: ABPackageBackend.cs

Public API throws raw `Exception` wrapping `RuntimeMessage`, internal tuple API returns `RuntimeMessage` as value. Callers must know which path they're on:
- `LoadAssetAsync<T>(key)` → try-catch
- `LoadByAddress<T>(address)` → `Handle.Error`

**Proposed fix**: Either unify to RuntimeMessage returns, or throw typed `AssetLoadException : Exception` with `RuntimeMessage Error` property.

## MJ-2 [Major] Sync/async complete implementation duplication (~200 lines)

**Files**: ABBundleLoader.cs, ABPackageBackend.cs

`LoadBundle`/`LoadBundleAsync`, `LoadAssetInternalSync`/`LoadAssetInternalAsync` are near-identical line by line, differing only in sync vs async Unity API calls.

**Proposed fix**: Extract core logic accepting `Func<string, AssetBundle>` / `Func<string, Task<AssetBundle>>` strategy parameter.

## MJ-3 [Major] Code duplication: PackByDirectory.Fallback ≈ PackByCollectPath.GetPackKey

**Files**: PackByDirectory.cs:39-44, PackByCollectPath.cs:12-19

Identical logic duplicated. "default" fallback constant defined 3 separate times.

**Proposed fix**: PackByDirectory delegates to PackByCollectPath for fallback; move `DefaultPackKey` to SystemIdentifiers.

## MJ-4 [Major] 4 unused BuildErrorCodes constants

**File**: BuildErrorCodes.cs

`EmptyPackageName`, `DuplicatePackageName`, `EmptyGroupName`, `DuplicateGroupName` defined but zero call sites. Documented as "save-time validation" codes reserved for E1-4 `CollectorSettingValidator`.

**Status**: Reserved — add when E1-4 implements Validator.

## MJ-5 [Major] Region/XML doc language inconsistency

**Files**: CollectorSetting.cs, CollectorEnums.cs, AssetClassification.cs use Chinese region/doc tags; all Editor/ files use English.

**Proposed fix**: Unify to English region tags per `context/conventions/collaboration.md` rule "Keep code in English".

## MN-1~6 [Minor] Cleanliness items

| # | Description | File |
|---|-------------|------|
| MN-1 | Rule naming inconsistency: PackSeparately vs PackByDirectory vs PackByLabel | Rules/ |
| MN-2 | Empty #region artifact in AssetPackageManager (refactoring leftover) | AssetPackageManager.cs:11-17 |
| MN-3 | Address index case sensitivity mismatch: Ordinal vs OrdinalIgnoreCase | ABManifest.cs vs ABPackageBackend.cs |
| MN-4 | Log prefix strings repeated ("[ABPackageBackend]" x10, "[AssetPackageManager]" x5) | Multiple |
| MN-5 | BuildMessage missing ToString() override | BuildMessage.cs |
| MN-6 | BundleNameBuilder.SanitizeSegment non-ASCII char handling (c+32 instead of char.ToLowerInvariant) | BundleNameBuilder.cs:37 |

## Context/Architecture Alignment

| context doc | claim | reality |
|-------------|-------|---------|
| collector-framework.md | RuleResolver resolves via reflection, caches instances | ✅ Matches |
| runtime-resource-loading.md | "One Flag Controls Two Dimensions, no mixed mode" | ⚠️ AssetPackageManager uses `as ABPackageBackend` downcast, mild coupling |
| runtime-resource-loading.md | manager allocates handle whose release calls UnloadByEntryId | ⚠️ CR-1: refcount desync |
| conventions/collaboration.md | "LINQ allowed in editor/build code" | ✅ ScanResult.HasErrors uses LINQ |
| conventions/collaboration.md | "Keep code in English" | ⚠️ Chinese region tags in root Collector files |
| collector-framework.md | "framework defines build-time vocabulary for later pipeline work" | ✅ Matches current state |
