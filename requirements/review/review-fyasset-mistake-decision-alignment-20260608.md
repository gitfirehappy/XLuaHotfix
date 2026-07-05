# FYAsset Mistake And Decision Alignment Review

> **Date**: 2026-06-08
> **Reviewer**: Codex
> **Scope**: Current FYAsset code under `Assets/FYAsset/Scripts/`, FYAsset build/runtime helper usage, active and archived FYAsset review artifacts, `context/architecture/*`, `context/conventions/field-semantics.md`, and `context/mistakes/implementation-pitfalls.md`.
> **Method**: Static source review, targeted pattern search, current requirements/progress inspection, archived review comparison, and git-history sampling. Unity Editor build/scan workflows were not executed.

## Executive Summary

At review creation time, no runtime or build code had been changed.

The recent build-chain decisions are mostly reflected in current code: repository Changes and AB Delivery are separated, version advancement is transactional with package/repository success, active settings are reduced to the three Resources settings, and the collector P0/P1 findings from the 2026-05-21 review are largely remediated.

The remaining risks are concentrated around the same historical themes recorded in `context/mistakes/implementation-pitfalls.md`: silent skips, swallowed diagnostics, default values that hide missing facts, raw error-code strings, and direct filesystem access bypassing the shared helper. One issue is a build correctness blocker candidate rather than just style.

## Extraction Status

> **Status**: Partially extracted on 2026-06-08; root-cause plan extracted on 2026-06-11.
> **Extracted Scope**: Minimal fail-fast and diagnostics fixes only: RawFile mixed-output silent skip, task resolver diagnostics, `MinAssetSizeBytes` unknown-size failure, touched error-code constants, and touched AB task `FileHelper` usage.
> **Root-Cause Plan**: Extracted to `requirements/plan/plan-fyasset-bundle-identity-rawfile-root-fix-20260611.md`.
> **Retained Scope**: This review remains active for discussion history and sign-off tracking. The extracted root-cause plan covers payload/type-aware bundle identity, scanner RawFile PackSeparately normalization, manifest membership from physical build outputs, ABManifest payload schema, and RawFile bytes/text runtime API.
> **Archive Policy**: Do not archive this review from the partial extraction or root-cause plan capture alone.

## Decision Habits Used As Review Criteria

- Prefer one authority per fact: collector metadata, package delivery, repository HEAD, and runtime manifest should not create second truths.
- Fail explicitly on critical build/bootstrap paths; warnings are acceptable only when the downstream behavior remains unambiguous.
- Keep preview, build, delivery, and publication semantics separate.
- Route common filesystem behavior through shared helpers, extending helpers before repeating raw I/O.
- Keep diagnostics structured and centrally named so reviews, reports, and UI can reason about failures consistently.

## Findings

### [P0] RawFile assets can be silently dropped when they share a logical bundle with serialized output

**Files**

- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskBuildBundles.cs:78`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskBuildBundles.cs:95`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskBuildBundles.cs:173`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskBuildBundles.cs:223`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskBuildBundles.cs:237`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskBuildBundles.cs:241`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskGenerateManifest.cs:36`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskGenerateManifest.cs:68`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskGenerateManifest.cs:76`

**Problem**

`TaskBuildBundles` first groups assets by logical `BundleName`, sends serialized assets to Unity's `BuildPipeline.BuildAssetBundles`, and stores Unity output names in `processedOutputs`. RawFile handling then skips any raw entry whose logical `bundleName` already exists in `processedOutputs`.

For a mixed logical bundle containing both serialized assets and RawFile assets, the serialized output name is normally the same as the logical bundle name. The raw loop therefore hits `processedOutputs.Contains(bundleName)` and `continue`s before the raw-file conflict guard or copy step.

`TaskGenerateManifest` then maps all `CollectedAssetInfo` rows by logical `BundleName` into `BundleBuildResults`, so the skipped RawFile asset can still be written as a manifest asset entry under the serialized bundle index.

**Impact**

The generated manifest can claim an asset exists in a bundle that never received that raw file. This breaks the metadata-first runtime contract and can push the failure to runtime loading instead of failing during build.

This is not fully prevented by importer-first `AssetClassifier.Auto`: manual `ForcePayloadKind.RawFile`, unsupported project files, or future import changes can still produce RawFile entries inside a group that packs serialized assets together.

**Mistake / decision mismatch**

- Recurs against IP-03: invalid pipeline data is skipped without diagnostic.
- Recurs against IP-25: validation and execution do not share the same payload/bundle rule.
- Conflicts with the current collector model where `PayloadKind` is authoritative build-routing metadata.

**Recommendation**

Validate payload composition before calling Unity build output generation:

- fail if a logical bundle contains both RawFile and serialized payloads, unless an explicit supported split rule exists
- run the RawFile multi-file guard before any `processedOutputs` skip
- consider deriving manifest asset membership from actual `BundleBuildInfo.AssetPaths` instead of every collected row with the same logical `BundleName`

### [P1] `BuildTaskResolver` hides task discovery and construction failures

**Files**

- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/BuildTaskResolver.cs:21`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/BuildTaskResolver.cs:25`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/BuildTaskResolver.cs:26`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/BuildTaskResolver.cs:37`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/BuildTaskResolver.cs:41`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/BuildTaskResolver.cs:43`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/DAGScheduler.cs:90`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/DAGScheduler.cs:97`

**Problem**

`BuildTaskResolver.Initialize()` silently skips an assembly when `asm.GetTypes()` throws `ReflectionTypeLoadException`, and silently skips any `IBuildTask` type whose constructor throws. The scheduler later only sees a missing task, or the editor task menu simply lacks it.

**Impact**

A broken task registration path becomes a vague `TASK_NOT_FOUND` or invisible UI state. Loader exceptions, failing task type names, and constructor errors are lost at the point where they are most actionable. This is a critical bootstrap/discovery path for the build graph, so silent skip behavior makes build-chain diagnosis materially harder.

**Mistake / decision mismatch**

- Recurs against IP-02: critical bootstrap failure can leave the system partially initialized.
- Recurs against IP-03 and IP-04: invalid input/exception paths are swallowed without diagnostic.
- Conflicts with the recent preview-diagnostics direction, where task-level failure details should reach the UI/report.

**Recommendation**

Make resolver initialization return or store structured diagnostics:

- include `ReflectionTypeLoadException.LoaderExceptions`
- include failing task type names and constructor exception messages
- fail validation with a dedicated code such as `TASK_RESOLUTION_FAILED` when a configured task cannot be reliably discovered
- avoid catch-all `continue` in resolver bootstrap

### [P2] `MinAssetSizeBytes` still treats unknown file size as "share allowed"

**Files**

- `Assets/FYAsset/Scripts/Build/Collector/SharePolicyConfig.cs:18`
- `Assets/FYAsset/Scripts/Build/Collector/Editor/DependencyAnalysis/DependencyAnalyzer.cs:286`
- `Assets/FYAsset/Scripts/Build/Collector/Editor/DependencyAnalysis/DependencyAnalyzer.cs:290`
- `Assets/FYAsset/Scripts/Build/Collector/Editor/DependencyAnalysis/DependencyAnalyzer.cs:291`
- `Assets/FYAsset/Scripts/Build/Collector/Editor/DependencyAnalysis/DependencyAnalyzer.cs:435`
- `Assets/FYAsset/Scripts/Build/Collector/Editor/DependencyAnalysis/DependencyAnalyzer.cs:446`
- `Assets/FYAsset/Scripts/Build/Collector/Editor/DependencyAnalysis/DependencyAnalyzer.cs:450`

**Problem**

When `SharePolicyConfig.MinAssetSizeBytes > 0`, dependency analysis calls `GetAssetFileSize()`. That helper returns `-1` for empty paths, missing files, invalid paths, or exceptions, and it has an empty `catch`. `ApplySharePolicy()` only disables sharing when `fileSize > 0 && fileSize < policy.MinAssetSizeBytes`, so unknown size passes as if the configured threshold was satisfied.

**Impact**

An exposed Package-level share policy can silently do the opposite of what the user configured. Assets whose size cannot be evaluated may be shared when they should have been copied or at least reported.

**Mistake / decision mismatch**

- This is the still-open P2 item from `requirements/review/review-collector-20260521.md`.
- Recurs against IP-04: empty catch blocks hide failures.
- Recurs against IP-29: missing/unknown and valid default are indistinguishable.
- Intersects IP-12: an exposed config field can appear effective while one failure path ignores it.

**Recommendation**

Replace `GetAssetFileSize()` with a presence-aware API, for example `TryGetAssetFileSize(assetPath, out long size, out string error)`. If `MinAssetSizeBytes` is configured and size cannot be read, emit a `BuildMessage.Warning` or `BuildMessage.Error` with package, asset path, and reason. Prefer failing build-time policy evaluation unless there is a documented fallback.

### [P2] Build diagnostics still use raw string error codes outside `BuildErrorCodes`

**Files**

- `Assets/FYAsset/Scripts/Build/Collector/Editor/DependencyAnalysis/TaskAnalyzeDependencies.cs:23`
- `Assets/FYAsset/Scripts/Build/Collector/Editor/DependencyAnalysis/TaskAnalyzeDependencies.cs:62`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskCollectBuiltins.cs:43`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/DAGScheduler.cs:130`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/DAGScheduler.cs:147`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/DAGScheduler.cs:219`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/DAGScheduler.cs:241`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskVerifyBuildResult.cs:59`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskVerifyBuildResult.cs:104`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskVerifyBuildResult.cs:146`

**Problem**

The project has a central `BuildErrorCodes` class, but several build tasks and scheduler/verification paths still emit raw string codes such as `NO_COLLECTED_ASSETS`, `DEPENDENCY_ANALYSIS_FAILED`, `CIRCULAR_TASK_DEPENDENCY`, `TASK_EXECUTION_ERROR`, `FILE_EXISTENCE`, `HASH_RE_VERIFY`, and `COUNT_CROSS_CHECK`.

Some raw codes are reused across contexts without a central definition; others are verification issue codes but are not documented as a separate namespace.

**Impact**

Repository preview formatting, AB build reports, docs, and future filters cannot reliably reason over a complete code inventory. This also makes duplicate or semantically drifting codes harder to catch in review.

**Mistake / decision mismatch**

- Recurs against IP-19: raw error-code strings scattered.
- Recurs against IP-21: same or similar code names can drift without a central authority.
- Conflicts with the R1-style decision that build/runtime diagnostics should have structured message types and centralized code constants.

**Recommendation**

Move scheduler and task result codes into constants. If verification issue codes should remain separate from task failure codes, create an explicit `BuildVerificationIssueCodes` class and document the namespace boundary.

### [P2] AB build tasks still bypass `FileHelper` for core filesystem operations

**Files**

- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskBuildBundles.cs:117`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskBuildBundles.cs:118`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskBuildBundles.cs:252`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskGenerateManifest.cs:49`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskVerifyBuildResult.cs:57`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskVerifyBuildResult.cs:76`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskVerifyBuildResult.cs:122`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskVerifyBuildResult.cs:124`
- `Assets/Tools/Scripts/FileHelper.cs`

**Problem**

AB build tasks still use raw `Directory.Exists`, `Directory.CreateDirectory`, `File.Copy`, `File.Exists`, `File.OpenRead`, and `Directory.GetFiles` on core build-output paths. The project already has `FileHelper` with shared existence, directory, copy, delete, read, and atomic-write behavior.

This finding is not about harmless `Path.GetFileName` or `FileInfo.Length` usage. The concern is filesystem side effects and existence/enumeration checks in the build chain.

**Impact**

The AB path bypasses shared diagnostics and helper semantics. It also makes future path behavior harder to change consistently, especially for preview output roots, package output cleanup, and cross-platform helper expectations.

**Mistake / decision mismatch**

- Recurs against IP-16: raw file I/O bypasses shared helper.
- Matches the non-blocking observation from `review-collector-20260521.md` that AB task raw I/O should be reviewed separately.

**Recommendation**

Route direct existence, directory creation, copy, and enumeration through `FileHelper`. If header inspection needs a dedicated helper, extend `FileHelper` or add a narrowly named build-file helper instead of keeping one-off raw I/O in task code.

## Reviewed Alignment That Looked Correct

- `TaskScanABHotfixDiff` now keeps current-vs-HEAD Changes separate from AB Delivery preview, and missing Full baseline is scoped to Delivery/Hotfix behavior.
- `BuildProjectManager` stages `VersionNumber` and writes `VersionDataBase` only after successful backend build and repository commit.
- Active settings ownership in code and context is aligned around `FYAssetSettings`, `FYAssetAASettings`, and `FYAssetABSettings`.
- Collector scan now rejects manual `Implicit` collector configuration and uses structured scanner execution errors for rule/classification failures.
- `AssetClassifier.Auto` is importer-first, so the old extension-whitelist classifier issue is not present in current code.

## Suggested Fix Order

1. Fix the RawFile/serialized mixed bundle path in `TaskBuildBundles` and add a pre-build validation for mixed payloads per logical bundle.
2. Harden `BuildTaskResolver` diagnostics before more DAG/report UI work, because it is a build graph bootstrap boundary.
3. Close the open `MinAssetSizeBytes` policy ambiguity.
4. Centralize raw task/verification codes.
5. Route AB build filesystem operations through `FileHelper` or a shared build-file helper.

## Verification Gaps

This review did not run Unity Editor build, Project Scan, AB Full, AB Hotfix, Repository Changes, or AB Delivery Preview workflows. The P0 finding is based on static control-flow evidence and should be confirmed with a focused fixture: one logical AB bundle containing at least one serialized asset and one RawFile asset.
