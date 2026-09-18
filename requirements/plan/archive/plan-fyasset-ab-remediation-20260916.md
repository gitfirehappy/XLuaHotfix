# Plan: FYAsset AB Remediation

> **Date**: 2026-09-16
> **Status**: Executed and archived on 2026-09-18; superseded by the approved responsibility restructure plan
> **Approval**: Developer approved 2026-09-16
> **Baseline commit**: `5703a83`
> **Scope**: Remediate confirmed execution deviations from the archived AB build-chain simplification plan; simplify project publish boundaries; align current documentation and workflow records.
> **Source review**: `requirements/review/review-fyasset-ab-build-chain-simplification-execution-20260916.md`
> **Archived predecessor**: `requirements/plan/archive/plan-fyasset-ab-build-chain-simplification-20260912.md`

## Archive Note

This plan was executed and its evidence was reviewed before the 2026-09-18 Build / Publish / Release / UI responsibility restructure. It remains historical evidence; the current implementation status is tracked by the active restructure plan.


Restore the approved AB build-chain ownership boundaries that were only partially implemented, remove redundant publish state and duplicate execution paths, and leave one verifiable fact source at every build and delivery boundary.

## Constraints

- Do not change the prior plan or review findings; they remain historical execution evidence.
- Keep AA and AB concrete implementations separate. Shared contains only confirmed common contracts.
- Preserve Summary-based Content reuse. Historical successful `Build_*` packages remain the only reusable byte source.
- Keep the previously confirmed SerializedObject, RawFile, and Scene loading chain. This plan only restores the agreed RawFile ownership boundary.
- Keep `LocalDirectory` and `CloudflarePages` publish targets. Shared must not depend on Wrangler or other Compat deployment details.
- Do not include the existing Scene replacement and async follower runtime hazards in this plan. They are framework-hardening work, not deviations from this plan's execution boundary.
- Do not add broad abstractions, compatibility shims, caches, or speculative tests.
- Preserve unrelated worktree changes.

## Success Criteria

- `BuildRunContext` is a mutable per-run data bus only; AB collection and analysis facts have one immutable domain owner each.
- `CollectABAssetsTask` is the only reader of AB collection configuration. `AnalyzeABDependenciesTask` consumes only its frozen input and produces one analysis result.
- Resources are represented as a read-only scanner system source but never become AB Content; Shader dependency detection reports Player-retention requirements without changing Player settings.
- Build, Generate, Verify, and Export each consume only their owned facts and perform no duplicate file-fact calculation or directory scanning.
- AA and AB apply the same Labels validity rule and preserve first spelling.
- Publish has no local publish cache, no duplicate LocalDirectory execution path, one thin public entry point, and minimal cross-layer data models.
- Current documentation and queues describe the verified implementation and the archived plan accurately.
- Verification is narrow and fresh: affected pure .NET scenarios, Unity Editor AA/AB build, LocalDirectory publish, and `git diff --check`.

## Confirmed Decisions

- Rename `BuildContext` to `BuildRunContext`. It transfers mutable stage data and does not freeze or deep-copy arbitrary values.
- `ABCollectionSnapshot` is the only Collect output. It contains the deep-copied, read-only buildable asset set and all dependency-analysis configuration inputs.
- `ABDependencyAnalysisResult` is the only Analyze output. It exposes only the analyzed assets and expected dependency graph; it does not retain a snapshot reference.
- `Assets/Resources/**` is a fixed, non-editable `CollectionScanner.SystemSources` record. It is not a `CollectedAssetInfo`, does not enter AB snapshot/analysis/Content/Manifest/Summary/delivery, and remains Unity Player/Resources-owned.
- Do not automatically collect all project Shaders. Analyze reports Shaders reached through AB dependency analysis for Player-retention diagnosis; it never changes `GraphicsSettings`. Explicitly collected Shader assets remain ordinary AB assets.
- `ContentBuildResult` is the one physical output fact. Generate and Verify do not read `_temp`, recompute file facts, inspect UnityFS, or scan output directories.
- `ContentBuildItem.FileName` is the one planned physical output name and must exactly match the produced `ContentBuildResult.FileName`.
- `HotfixBaselineResolver` is the only exact Full baseline locator. It returns only package root and Full build id; `BuildProjectRunner` has no coarse Hotfix baseline precheck.
- `LuaScriptsIndexBuildTask` remains a Lua-named Compat task. For AB it runs after Collect and before Analyze, only reads the collection snapshot, and never inserts assets. `LuaScriptsIndex.asset` is a versioned, explicitly collected project asset.
- Labels may be absent. Each present Label must be non-empty, trimmed, and unique within an asset under case-insensitive comparison; first spelling is serialized.
- `ABRawFileReader` reads bytes from an already resolved physical path. `ABPackageManager` owns Address resolution and path selection; `IABAssetLoader` owns only SerializedObject behavior.
- `FileHelper` is not split in this plan. It receives responsibility `#region` grouping only.
- Delete Publish Cache entirely. Each publish reads the formal local Summary source and current server PackageIndex/Manifest facts.
- Rename `BuildPublisher` to `PackagePublisher` and limit it to target-capability dispatch.
- `PushTargetConfig` remains the five-field persisted target configuration. It has no global lookup methods; `FYAssetSettings` owns target-list resolution by `TargetId`.
- Both target kinds are available from the main Publish panel through an injected Compat publish action. `CompatPushTargetFactory` is the only concrete target constructor.
- Directory targets expose only server-root capability and use `PackagePublishTransaction`; external targets receive only `ExternalPublishPayload` and deploy it.
- Replace `PushReceipt` with the minimal `PublishResult`: success/error, target location, transfer mode, uploaded count, and reused count.
- Keep `PackageBuildIdentity` as the minimal identity projection from an approved Summary. Remove TargetId, cache, mutable server-root state, and unused Clone from `PublishRequest`.
- Keep `PublishPlan` and `PackageAssemblyPlan` separate but internal, with `PackagePublishTransaction` and `PackageTargetAssembler` internal as well.

## Tasks

### 1. Restore Build-Run And AB Fact Boundaries

**Purpose**: Make the build data bus, frozen collection input, and dependency-analysis output unambiguous.

**Changes**:

- Rename `BuildContext` to `BuildRunContext` and update Shared, AA, AB, Compat, tests, runners, and environments.
- Add immutable/deep-copied `ABCollectionSnapshot` for buildable scanned assets, SharePolicy, RawFileRules, effective IgnorePatterns, and dependency filter extensions.
- Add immutable `ABDependencyAnalysisResult` for analyzed assets and expected dependency graph only.
- Replace `CollectedAssets`, `SharePolicy`, and independent `BundleDependencyGraph` Context keys with snapshot/result keys.
- Make Collect the only configuration reader. Make Analyze fail when required snapshot facts are absent and remove SO/global fallback reads.
- Update Build, Generate, Lua index, and later AB tasks to consume only `ABDependencyAnalysisResult`.

**Verification**:

- Focused pure .NET boundary test that proves a later task cannot obtain or overwrite the old collection keys.
- Focused test that Analyze uses frozen rules rather than reloaded settings.

### 2. Unify Scanner System Sources And Shader Diagnosis

**Purpose**: Make scanner output complete without turning Unity-owned Player data into AB Content.

**Changes**:

- Add `ScanResult.SystemSources` and a read-only Resources source record.
- Remove `CollectABAssetsTask.AppendBuiltinAssets` and the generic `t:Shader` patch.
- Remove the editable `Assets/Resources` collector configuration that would otherwise duplicate the fixed system source.
- Keep system sources out of `ABCollectionSnapshot` and all later AB Content/Manifest/Delivery paths.
- Report Shader dependencies discovered through AB dependency analysis as Player-retention diagnostics only.

**Verification**:

- Scanner behavior tests for exactly one Resources system source, no Resources `CollectedAssetInfo`, and no generic Shader injection.
- Unity Editor AB build inspection showing Resources is not exported as AB Content and Shader diagnostics are emitted only when applicable.

### 3. Move Analysis Validation And Complete Content Fact Ownership

**Purpose**: Validate complete Content membership before physical construction and eliminate stale multi-output terminology.

**Changes**:

- Move ContentType/AssetType consistency, SerializedObject entry eligibility, RawFile one-file-per-Content, and implicit RawFile unique-content validation into Analyze.
- Reduce BuildABContentTask to planned file generation, reuse, actual dependency collection, digest creation, and final case-insensitive FileName collision validation.
- Rename `ContentBuildItem.PhysicalName` to `FileName` everywhere.
- Require actual output file name to equal the planned FileName.
- Migrate affected source-shape tests to narrow behavior checks.

**Verification**:

- Pure logic tests for invalid analyzed Content groups failing before Build writes outputs.
- Build-cache behavior tests for one Content/one stable FileName and reuse facts.

### 4. Restore Generate, Verify, And Export Boundaries

**Purpose**: Make `ContentBuildResult` the only physical output fact after Build.

**Changes**:

- Generate Manifest entries solely from analyzed assets and ContentBuildResults.
- Remove Generate reads of `_temp`, file existence checks, and digest recomputation.
- Restrict Verify to Manifest-to-ContentBuildResult mapping, membership, ContentIndex, and DependencyIndices checks.
- Remove Verify temporary-directory scans, orphan checks, UnityFS checks, and digest recomputation.
- Keep Export responsible only for selecting delivery content, copying attempt files, writing package Manifest/BuildIndex, and creating the Summary candidate.

**Verification**:

- Pure mapping tests that alter a ContentBuildResult and prove Generate/Verify reject inconsistent Manifest mapping without filesystem reads.
- Unity Editor AB build and LocalDirectory publish smoke test.

### 5. Apply AA/AB Labels, Baseline, And RawFile Boundaries

**Purpose**: Complete common contracts without reopening confirmed loading or hotfix design.

**Changes**:

- Add case-insensitive duplicate Label rejection to existing AB validation/scanning and AA index building while retaining first spelling.
- Remove `HotfixBaselineResolver` Summary DTO output and use only package root/Full build id in AA and AB consumers.
- Remove `BuildProjectRunner` Hotfix baseline precheck.
- Add stateless `ABRawFileReader` with one already-resolved physical-path byte-read entry.
- Remove RawFile methods from `IABAssetLoader`, `ABAssetLoader`, and `EditorAssetLoader`; route public RawFile calls through `ABPackageManager` after it selects the path.

**Verification**:

- AA/AB Label parity behavior tests.
- Baseline resolver scope and package-root behavior tests.
- Runtime raw-file path/ownership tests using existing controlled stubs.

### 6. Correct Lua Compat Task Timing

**Purpose**: Keep project Lua integration out of framework core and prevent post-analysis asset injection.

**Changes**:

- Keep `LuaScriptsIndexBuildTask` in Compat with its Lua-specific name.
- Run AA task in Input; run AB task after Collect snapshot and before Analyze.
- Ensure Lua index rebuild reads only the snapshot's resolved container addresses.
- Version and explicitly collect `Assets/Build/LuaScriptsIndex.asset` with stable Address `LuaScriptsIndex`.
- Remove dynamic `CollectedAssetInfo` insertion and old BuildABContent-slot configuration.

**Verification**:

- Pipeline composition behavior test for AA/AB slot order.
- Unity Editor AA/AB build confirms the index is present exactly once and uses scanner-owned addresses.

### 7. Simplify Publish Ownership

**Purpose**: Keep durable local Summary and live server facts as the only publish truth.

**Changes**:

- Delete Publish Cache models, storage, request fields, transaction calls, UI state, and cache-only tests.
- Rename and thin `BuildPublisher` to `PackagePublisher`.
- Replace `PushReceipt` with minimal `PublishResult`.
- Make `PushTargetConfig` a pure five-field configuration object; move list lookup/selection to `FYAssetSettings` and remove name-based identity fallback.
- Route main Publish panel execution through injected Compat action; use `CompatPushTargetFactory` as sole concrete target factory.
- Split directory and external target capabilities; remove `LocalDirectoryPushTarget.Push` and unused `PushPayload.ServerPackagesRoot`.
- Replace `PushPayload` with minimal `ExternalPublishPayload`; do not pass full PublishRequest to external deployment.
- Reduce PublishRequest to source package, backend, ManifestReader, Summary-derived identity, baseline source/id, and package folder.
- Restrict transaction, plans, assembler, and file scanner to internal implementation visibility.

**Verification**:

- `publish_diff` behavior coverage for server fact reads, sparse Hotfix assembly, LocalDirectory rollback/last-index write, external fake-runner success/failure rollback, and no cache paths.
- Unity Editor LocalDirectory publish smoke test.

### 8. Classify FileHelper And Align Documentation

**Purpose**: Improve maintainability without expanding implementation scope.

**Changes**:

- Add responsibility `#region` grouping to FileHelper only; do not split the class or change signatures.
- Update current FYAsset docs after behavior verification: BuildRunContext, AB task chain, system Resources source, Shader diagnostics, RawFile reader ownership, Publish facts, and AB runtime contracts.
- Do not rewrite archived plans or reviews.

**Verification**:

- `git diff --check`.
- Validate links and remove references to deleted/renamed types and APIs from current documentation.

## Verification Matrix

- Run the affected pure .NET scenario projects only: `pipeline_compose`, `build_cache`, `runtime_resource`, `publish_diff`, `s3_resource_boundary`, plus any new focused scenario project required by an otherwise uncovered confirmed contract.
- Run solution compilation with no new errors.
- Run Unity Editor once for AA build, AB build, and LocalDirectory publish. Inspect the generated Manifest/Content layout and package index ordering.
- Use the Cloudflare fake runner for success and rollback behavior. Do not require external credentials or network deployment.
- Do not require Player validation, real Cloudflare deployment, or a full platform build matrix for this plan.

## Exit Criteria

- Every task has fresh evidence at the narrowest applicable layer.
- The prior review findings are either remediated by evidence or explicitly moved to a separate framework-hardening plan.
- The plan is not marked complete until the Unity Editor and LocalDirectory checks above have passed and the developer has signed off.

## Execution Evidence

Implementation and verification completed on 2026-09-16. Developer sign-off is still required before archive.

- Pure .NET scenarios: `pipeline_compose` 29/29 assertions and 4/4 groups; `build_cache` 45/45 and 4/4; `runtime_resource` passed including resolved-path-only RawFile ownership; `publish_diff` 8/8; `s3_resource_boundary` 6/6.
- Solution: `dotnet build XLuaHotfix.sln --no-restore --no-incremental` exited 0 with no errors; existing Unity/MCP assembly-version warnings remain.
- Unity AA Full + LocalDirectory: run `20260916_081115_e3888d6a` passed; 7 artifacts / 35,687 bytes; publish, probe, target restore, and project restoration all succeeded.
- Unity AB Full + LocalDirectory: run `20260916_084412_668a738f` passed; 71 artifacts / 10,897,296 bytes; publish, probe, target restore, and project restoration all succeeded.
- Cloudflare behavior used the `publish_diff` fake runner only; no external deployment or credentials were used.
- `git diff --check` exited 0. Production-source scans found no active `BuildContext`, `BuildPublisher`, `PublishCache`, `PublishCacheStore`, `PushReceipt`, or `PushPayload` symbols; RawFile public loading remains only on `ABPackageManager`.
- Two Unity failures found during verification were corrected within Task 6 and Task 3 ownership: Compat now projects FYAsset exact type keys to Unity main type names for Lua validation, and planned AB output names use Unity's lowercase bundle-name convention. AB `_temp` is deleted and recreated for each build so stale sidecars cannot become cross-run facts.
