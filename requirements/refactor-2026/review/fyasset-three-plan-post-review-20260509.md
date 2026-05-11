# FYAsset Three-Plan Post Review

Date: 2026-05-09
Scope:
- `requirements/refactor-2026/plan/plan-review-fix-20260509.md`
- `requirements/refactor-2026/plan/plan-E9-version.md`
- `requirements/refactor-2026/plan/plan-naming-unification.md`

Author: `gpt-5.5`
**Processed**: 2026-05-11 · All 3 plans executed. Fixes for review findings (Version→SO write-back, Channel whitelist, naming sync) landed in `34e002b` + `a1aff30`.
**Status**: 📦 Archived

## Executive Summary

本次审查覆盖刚执行完毕的三个计划：review 修复、E9 VersionNumber 扩展、旧管线命名统一。整体方向是正确的：`IAssetIndex` 边界明显收窄，`RuntimeAssetEntry.Labels` 的可变暴露被控制，Collector 路径工具重复代码被集中，typo 修复也已落到主要引用点。

但当前结果还不建议无条件进入依赖版本语义的后续计划，尤其是 E7。主要原因有三类：

1. `BuildContextKeys.Version` 已写入但下游未消费，版本来源仍然分裂。
2. `VersionNumber.CompareTo` 与 `Equals` 在未知 Channel 上可能产生不一致结果。
3. 命名统一与文档同步没有闭环，`VersionState.version` 仍保留小写，字段语义文档仍标记旧字段“待统一”。

进度记录显示三项计划执行后均有 `dotnet build 0 errors`，本报告没有重新执行构建；结论基于代码与文档静态审查。

## Review Dimensions

| Dimension | Result | Notes |
|-----------|--------|-------|
| Build surface | Pass by recorded evidence | `progress.txt` 记录三项计划后均已 `dotnet build 0 errors`，本轮未重跑 |
| Runtime behavior | Needs fixes | Query cache mutability/case sensitivity and version source split remain |
| Version semantics | Needs fixes | Unknown Channel comparison contract is inconsistent |
| Architecture boundary | Improved | `IAssetIndex` 砍 legacy 查询方法是正确方向 |
| Naming consistency | Partial | Major typo fixed, legacy JSON fields only partially unified |
| Documentation alignment | Needs fixes | `docs/FYAsset/字段语义参考表.md` still describes pre-plan reality |
| Test/verification depth | Insufficient | Version parsing/comparison, JSON shape, cache behavior lack targeted tests |

## Findings

### [High] `BuildContextKeys.Version` is written but not consumed, so E9 did not actually establish a single version source

Files:
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskPrepareContext.cs:15`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskPrepareContext.cs:67`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskGenerateManifest.cs:16`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskGenerateManifest.cs:147`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskGenerateManifest.cs:177`

`TaskPrepareContext` declares and writes `BuildContextKeys.Version`, but `TaskGenerateManifest` does not declare it in `ReadKeys`; it still calls `ResolveVersion()` and reloads `VersionDataBase.asset` directly. This means the DAG cannot validate the version dependency and the pipeline still has two sources of truth.

Why this matters:
- E9 explicitly added `BuildContextKeys.Version` so downstream tasks can consume one `VersionNumber`.
- E7 documentation already assumes `BuildContext` holds the unified current build version.
- CLI `--version` still only writes the string `BuildVersion`; no implemented path converts it into `VersionNumber`.

Recommendation:
- Make `TaskPrepareContext` parse `--version` into `VersionNumber` when present, otherwise read the SO.
- Make `TaskGenerateManifest.ReadKeys` include `BuildContextKeys.Version`.
- Replace `ResolveVersion()` with `ctx.Require<VersionNumber>(BuildContextKeys.Version)`.

### [High] `VersionNumber.CompareTo` can return equality for values that `Equals` treats as different

Files:
- `Assets/FYAsset/Scripts/Build/BuildManage/VersionDataBase.cs:97`
- `Assets/FYAsset/Scripts/Build/BuildManage/VersionDataBase.cs:113`
- `Assets/FYAsset/Scripts/Build/BuildManage/VersionDataBase.cs:140`

`GetChannelRank()` maps any unknown channel to rank `3`, the same rank as release. Therefore `1.2.3-dev` and `1.2.3` compare as equal, but `Equals()` returns false because it compares the actual channel string.

Why this matters:
- `CompareTo == 0` while `Equals == false` breaks expectations in sorted collections and update-decision logic.
- Version comparison is planned as a dependency for diff/update decisions.
- Unknown channels are easy to introduce later through CLI or editor UI.

Recommendation:
- Either reject unknown channels in `TryParse` / setters, or compare unknown channels deterministically after known channel rank.
- Keep `CompareTo`, `Equals`, and `GetHashCode` aligned on the same normalized channel semantics.

### [Medium] Legacy naming unification is only partially executed and the plan is internally inconsistent

Files:
- `Assets/FYAsset/Scripts/Build/BuildManage/HelperBuildData_Remote/VersionState.cs:9`
- `Assets/FYAsset/Scripts/LegacyRuntime/LegacyHotfixBackend.cs:76`
- `Assets/FYAsset/Scripts/LegacyRuntime/LegacyHotfixBackend.cs:103`
- `Assets/FYAsset/Scripts/LegacyRuntime/LegacyHotfixBackend.cs:193`
- `requirements/refactor-2026/plan/plan-naming-unification.md:25`
- `requirements/refactor-2026/plan/plan-naming-unification.md:37`

The task table says `VersionState.version -> Version`, while the modified-files table says the version field remains lowercase. The implementation keeps `public VersionNumber version;` and runtime references still use `versionState.version`.

Why this matters:
- The plan title promises PascalCase unification, but the highest-level field in `VersionState` remains camelCase.
- If the lowercase field is intentional for JSON compatibility, that contradicts the plan decision that old data can be discarded.
- If it is not intentional, this is a missed rename.

Recommendation:
- Decide explicitly: either rename `version` to `Version`, or mark it as a deliberate legacy exception.
- Update the plan, docs, and call sites to match that decision.

### [Medium] `AssetPackageManager` query caches expose mutable internal lists and use inconsistent case sensitivity

Files:
- `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs:24`
- `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs:104`
- `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs:138`
- `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs:144`

The manager now owns `_labelToKeys` and `_typeToKeys`, which is the right architectural direction after cutting legacy methods from `IAssetIndex`. However, both dictionaries use default string comparers and the public query methods return the stored `List<string>` directly.

Why this matters:
- Label matching elsewhere is documented and implemented as case-insensitive.
- External callers can mutate the returned list and corrupt manager caches.
- Legacy path assigns `config.keysByType/item.Keys` and `config.keysByLabel/item.Keys` directly into manager caches, so the manager also aliases SO-owned lists.

Recommendation:
- Use `StringComparer.OrdinalIgnoreCase` for label and type dictionaries unless type semantics must remain case-sensitive.
- Return copies or `IReadOnlyList<string>` from query APIs, or document that callers must not mutate and enforce it internally.
- Copy lists from `AddressableLabelsConfig` during legacy initialization.

### [Medium] Query caches are not cleared before initialization or fallback

Files:
- `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs:28`
- `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs:45`
- `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs:77`

`Initialize()` does not clear `_labelToKeys`, `_typeToKeys`, or `_addressSet`. The singleton may normally initialize once, but the method itself is public and async, and AB initialization can fall back to legacy initialization.

Why this matters:
- Re-initialization after backend mode changes can leave stale query data.
- Tests or editor play-mode reload flows can call initialization more than once.
- Fallback after a previous successful AB initialization could mix AB and legacy query caches.

Recommendation:
- Clear all query caches at the start of `Initialize()`, before choosing backend path.
- Consider an explicit idempotency guard if repeated initialization is not supported.

### [Medium] Human-facing field semantics documentation still describes the old field model

Files:
- `docs/FYAsset/字段语义参考表.md:17`
- `docs/FYAsset/字段语义参考表.md:46`
- `docs/FYAsset/字段语义参考表.md:119`
- `docs/FYAsset/字段语义参考表.md:201`
- `docs/FYAsset/字段语义参考表.md:219`
- `docs/FYAsset/字段语义参考表.md:232`

The docs still say `BundleInfo.hash`, `BundleInfo.bundleName`, `VersionState.totalSize`, and `Manifest.latestversion` are pending unification. They also describe `RuntimeAssetEntry.Labels` as `List<string>`, while implementation now exposes `IReadOnlyList<string>`.

Why this matters:
- The post-plan checklist requires docs/context alignment after each completed sub-plan.
- This document is specifically a field semantics reference; stale entries can mislead future refactors.

Recommendation:
- Update the field table to current reality.
- If `VersionState.version` remains lowercase intentionally, document it as an exception rather than a pending item.
- Add `BuildContextKeys.Version` to the BuildContext Keys table.

### [Low] `ABAssetIndex.GetEntriesByAddressAndType()` still allocates per call

Files:
- `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABAssetIndex.cs:10`
- `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABAssetIndex.cs:181`
- `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABAssetIndex.cs:190`

The main address/type result arrays are prebuilt, which fixes most of the earlier allocation drift. But `GetEntriesByAddressAndType()` still allocates a new `List<RuntimeAssetEntry>` per call.

Why this matters:
- This is lower risk because the method comment no longer explicitly says zero allocation.
- The class-level comment still presents the index as a zero-allocation hot path, which can overpromise if resolver usage grows.

Recommendation:
- Either prebuild `(Address, PrimaryType)` result slices if this is a real hot path, or narrow the class-level zero-allocation claim.

### [Low] Version parser accepts values outside the documented SemVer/channel contract

Files:
- `Assets/FYAsset/Scripts/Build/BuildManage/VersionDataBase.cs:178`
- `Assets/FYAsset/Scripts/Build/BuildManage/VersionDataBase.cs:209`

`TryParse` uses `int.TryParse`, so negative major/minor/patch/build values are accepted. Unknown channels are also accepted and then handled as release-rank channels by comparison.

Why this matters:
- Current UI/build calls may not pass malformed values, so this is not immediately fatal.
- Once CLI integration starts using `Parse`, invalid input can silently enter version state.

Recommendation:
- Reject negative numeric components.
- Restrict channels to `alpha`, `beta`, `rc`, and empty unless a custom-channel policy is defined.

## Positive Confirmations

- `DEAULT_XLUA_TYPE_CONFIG_LOAD_LABEL` was removed from active code; active references now use `DEFAULT_XLUA_TYPE_CONFIG_LOAD_LABEL`.
- `ScriptObjectDataBse` was renamed in active code to `ScriptObjectDataBase`, including editor references.
- `RuntimeAssetEntry.Labels` now has a guarded mutation boundary through `SetLabels()`, removing the original direct public `List<string>` cache hazard.
- `CollectorRef` and `AssetClassification` now implement explicit value semantics.
- `CollectorPathUtility` centralizes path normalization/depth/containment/ignore matching, reducing duplicated editor logic.
- `IAssetIndex` no longer carries legacy label/type/key query methods, which makes the AB index boundary cleaner.

## Suggested Fix Order

1. Fix the version source split: make `TaskGenerateManifest` consume `BuildContextKeys.Version`.
2. Align `VersionNumber.CompareTo` and `Equals`, and add focused comparison/parse tests.
3. Decide and close `VersionState.version` naming: rename or document as intentional exception.
4. Harden `AssetPackageManager` caches: clear on init, use consistent comparers, avoid returning mutable internals.
5. Update `docs/FYAsset/字段语义参考表.md` to match the landed code.
6. Add narrow regression tests for version parsing/comparison and manager label lookup case behavior.

## Open Questions

- Should `VersionState.version` be renamed to `Version`, or is it intentionally preserved for old JSON shape?
- Should custom channels beyond `alpha/beta/rc/release` be rejected, or should they have deterministic ordering?
- Should public query APIs keep returning `List<string>` for compatibility, or can they move to `IReadOnlyList<string>`?

