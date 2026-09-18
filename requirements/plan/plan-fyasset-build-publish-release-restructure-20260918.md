# Draft Plan: FYAsset Build / Publish / Release / UI Responsibility Restructure

> **Date**: 2026-09-18
> **Status**: Approved / implemented — pure .NET and generated Unity project verification passed; real Unity Editor/Player matrix remains an explicit environment gate
> **Approval**: Developer approved execution 2026-09-18
> **Scope**: Align the confirmed discussion decisions for the FYAsset build pipeline, package publishing, release-directory removal, result contracts, target configuration, and UI/report ownership.
> **Source**: 2026-09-12 through 2026-09-18 grill decisions, confirmed through decision 101.
> **Important**: This draft is independent of the existing `plan-fyasset-ab-remediation-20260916.md`. That plan is not modified.

## Execution State

Implementation is complete for the approved source, target, delivery, directory, and documentation scope. The worktree was normalized without reverting unrelated pre-existing changes. The real Unity Editor/Player build matrix was not run in this environment; that gate remains explicitly open and is not represented as a passing result.


Remove the remaining duplicated request, result, context, publish-target, transaction, compatibility, and directory concepts from FYAsset. Leave one explicit owner for each build fact, publish fact, output transition, target location, and persistent Summary fact.

The implementation must simplify the existing architecture rather than rename old wrappers one-for-one.

## Current Progress

- [done] Read-only call-chain review completed for Shared Build, AA/AB Pipeline, Publish, Target, Summary, UI, and Hotfix boundaries.
- [done] 2026-09-12 to 2026-09-18 design grill completed through decision 101.
- [done] Confirmed the build request/result/context, temporary-output, publish, target, Summary, directory, and `.meta` ownership rules recorded below.
- [done] Confirmed verification preference: reuse existing tests and self-checks; do not add new test files unless an existing check cannot cover a confirmed contract.
- [blocked] Implementation not approved. This draft does not authorize code, asset, test, workflow, or directory changes.
- [blocked] The worktree already contains extensive unrelated and prior uncommitted changes. Those changes must be preserved and separated from this scope.

## Constraints

- Do not execute this plan until the developer explicitly approves this draft.
- Do not overwrite, revert, stage, clean, or mix existing worktree changes.
- Do not change the existing remediation plan or its review evidence.
- Do not add compatibility aliases, migration shims, fallback configuration, or old-data conversion.
- Old `BuildPipelineConfig` migration behavior and old target Settings data are deleted; developers reconfigure from the current schema.
- Shared contains only confirmed current AA/AB contracts. Do not keep speculative abstractions.
- `Editor` is a terminal implementation directory: use `Responsibility/Editor`, never `Editor/Responsibility`.
- Preserve `.meta` files and GUIDs when moving Unity files. Delete obsolete source and `.meta` files together.
- Delete a legacy directory only after it is confirmed empty. If an unexpected file remains, stop instead of deleting it.
- Do not add a new test project or test file by default. Extend the nearest existing self-check only when necessary to cover a changed contract.

## Confirmed Ownership Rules

### Build request, pipeline, and results

- `BuildRequest` is the only build request fact. It contains the request version, build type, backend key, package name, `TemporaryOutputDir`, `FinalOutputDir`, and `BundlesDir`.
- Delete `OutputDir`, `DeliveryOutputDir`, `PublishSourceDir`, `IsAttemptLayout`, and `WithPromotedOutput(...)`.
- Delete `BuildPipelineRequest`; pass `BuildRequest`, `BuildExecutionOptions`, `IBuildRunEnvironment`, and tasks explicitly.
- `BuildExecutionOptions` retains only the UI task-status callback. Requested channel is an explicit BuildProjectRunner input.
- `BuildPipelineResult` is the Task execution result.
- `BuildBackendResult` is the Backend-to-Runner stage result and is public because `IBuildBackend` is public.
- `BuildResult` is the final BuildProjectRunner result and contains only `Success`, `Error`, `Summary`, and `ReportPath`.
- Do not expose `BuildRunContext` or delivery state through final `BuildResult`.
- Public `BuildStandalone`, `BuildFullPackage`, and `BuildHotfix` return final `BuildResult`, not `bool`.
- Remove `LastBuildSuccess`, `LastBuildResult`, and `BuildProjectRunner.LastSummary` caches. UI queries persistent Summary data instead.

### Pipeline Context

- `BuildRunContext` is a frozen transfer bus for request/config DTOs and real Task stage DTOs. It is not an additional source of facts.
- Keep `BuildRequest`, `BuildConfig`, `BuildVerificationResult`, `BuildSummary`, and renamed `PipelineStartedAtUtc` as justified keys.
- Delete `BuildType`, `OutputPath`, and `DeferPackagePublication` keys.
- `BuildContextKeys.cs` remains and moves to `Shared/Build/Pipeline/Editor/`.
- `BuildConfig.OutputRoot` becomes `BuildWorkRoot`; it remains configuration data and is separate from `BuildRequest.TemporaryOutputDir`.
- `BuildSummaryIndex` is a rebuildable derived index. `CompleteBuildSummary` is the unique complete build-fact source.
- Add the confirmed `BuildSummaryStore.TryReadLatestSummary(...)` query. Publish UI may still read the Summary list for user selection.

### Build output and temporary directories

- Every build writes to `BuildPathManager.OutputRoot/_temp/{PackageName}`. Existing directory means immediate failure; do not guess whether it is stale or active.
- AB/AA Task work directories under `BuildWorkRoot/_temp` remain separate from the package delivery temporary directory.
- Delete the attempt double mode, `IBuildAttempt`, `BeginAttempt`, `TryPromote`, `Discard`, `DeliveryToken`, and all attempt path concepts.
- `BuildOutputDelivery` moves the temporary package output to the final output.
- `BuildDeliveryResult` is the successful-but-unsettled directory transition result; it only exposes `Commit()` and `Rollback()`.
- Put both types in `Shared/Build/Delivery/Editor/BuildOutputDelivery.cs`.
- Existing final output moves to `_temp/_recovery/{targetIdentifier}` before replacement. Existing recovery causes failure. `Commit` deletes recovery; `Rollback` restores it.
- `BuildPipelineRunner` executes Tasks only. `BuildProjectRunner` owns output delivery and the larger delivery compensation order.

### Publish chain

- `PackagePublisher` is the only public publish orchestrator.
- `PackagePublisher` sequence is request validation, identity validation, target path/URL resolution, local file scan, remote fact read, content resolution, and upload.
- `PackageFileScanner` only scans the local source package and creates digests.
- `PackageRemoteReader.Read(...)` always returns `RemotePackageFacts` with `Empty`, `Usable`, `Invalid`, or `Inaccessible` state.
- `PackageContentResolver.TryResolve(...)` returns a complete `PackageContentResolution` or a structured failure; it never returns a partial resolution.
- `PackageContentResolution` contains only final files, digest, source path, source kind, and `IsFullUpload`.
- `PackageUploader.Upload(...)` receives `PublishRequest`, `backendRoot`, `publicUrl`, and `PackageContentResolution`, then returns `PublishResult`.
- Publish temporary directory is `{BackendRoot}/_temp/{PackageName}`. Target implementations do not create or manage it.
- `PackageUploader` owns staging, copy verification, immutable same-name checks, final package move, final `PackageIndex` write, and rollback.
- Same-name identical package is idempotent. Same-name different content is rejected. `PackageIndex` is written only after package contents are ready and verified.
- Delete `PackagePublishTransaction`, `PublishPlan`, `PackageAssemblyPlan`, and `PackageTargetAssembler` without one-to-one replacement wrappers.
- Rename `PackageFileNames` removal already confirmed; no `.fyasset_push` or target-specific temporary names remain.
- `PackageRetentionCleaner` replaces `PublishMaintenance` and exposes `CleanupUnreferencedPackages(...)` using the target root resolver. It deletes only valid package names and refuses cleanup when the current index is unreadable.

### Targets and Settings

- `PublishTargetConfig` replaces `PushTargetConfig` and directly implements `IPublishTarget`.
- Remove `PushTargetType`, `LocalDirectoryPushTarget`, `CloudflarePagesPushTarget`, `CompatPushTargetFactory`, `PublishTargetFactoryRegistry`, and automatic registration.
- `PublishTargetConfig` retains only `TargetId`, `Name`, `Path`, and `PublicBaseUrl` as persisted data plus stateless path/URL resolution.
- Empty `PublicBaseUrl` is valid. Invalid non-empty URLs fail and do not fall back to a local path.
- Targets provide backend root and public URL only. They do not copy files, write PackageIndex, decide sources, or own rollback.
- `PublishTargetContracts.cs` retains `IPublishTarget` and `PublishResult`; remove external payload and target-capability forks.
- Old target Settings data is deleted and regenerated. No migration or compatibility behavior is added.
- `PackageIndex` filename remains in Shared Settings. AB and AA Manifest filenames move to their respective existing Settings types.

### Naming and documentation

- Define the single `BuildPackageName` source inside the existing `PackageBuildIdentity.cs`. `BuildRequest` calls its create method; all parsing calls its parse method.
- `BuildPackageIdentity` remains the Summary-derived publish identity and is not placed in Context.
- Add semantic XML documentation to existing public `FYAssetPathUtility` methods without changing behavior, names, or file split.
- Move `CopyDirectory` into `FileHelper` with the confirmed strict contract: recursive ordinary-file copy, no empty-directory copy, overwrite parameter, direct failure, no rollback, no link/reparse traversal, and source/target containment checks.
- AB Report remains AB-specific. `ABReportPanel` is UI; report model, builder, and store are AB Build responsibilities. No Shared report interface is added.
- AB Hotfix three-file responsibility remains unchanged. AB/AA Settings singleton behavior remains unchanged.

## Directory Plan

### Shared Build

```text
Shared/Build/Pipeline/Editor/
  BuildContextKeys.cs
  BuildRequest.cs
  BuildPipelineResult.cs
  BuildBackendResult.cs
  BuildPipelineRunner.cs
  BuildRunContext.cs
  EditorBuildRunEnvironment.cs
  BuildExecutionOptions.cs
  BuildPipelineComposer.cs
  BuildPipelineConfig.cs
  IBuildRunEnvironment.cs
  BuildVerificationResult.cs
  existing valid Pipeline task contracts

Shared/Build/Runner/Editor/
  BuildProjectRunner.cs
  BuildResult.cs
  BuildPathManager.cs

Shared/Build/Delivery/Editor/
  BuildOutputDelivery.cs
  LocalBuildDataExporter.cs

Shared/Build/Summary/Editor/
  CompleteBuildSummary.cs
  BuildExportWriter.cs
  BuildArtifactReuseService.cs
  BuildSummaryIndex.cs
  BuildSummaryStore.cs
  BuildVersionPlanner.cs
  HotfixBaselineResolver.cs
  PublishSourceCatalog.cs

Shared/Build/UI/Editor/
  BuildPipelineUI.cs
  BuildPipelineUIToolkitPanel.cs
  BuildPipelineWindow.cs
  IBuildPipelinePanel.cs
  BuildPanelActions.cs
  PipelinePanel.cs
  PublishTargetPanel.cs
  BuildPackageResultsView.cs

Shared/Build/
  AssetLabelValidator.cs
  BuildMessage.cs
```

Delete the empty generic `Shared/Build/Editor` tree after all files are moved and checked.

### AB and AA Build

```text
AB/Build/Pipeline/Editor/
  ABBuildBackend.cs

AB/Build/Package/Editor/
  ABPackageManifestReader.cs

AB/Build/Report/Editor/
  ABBuildReport.cs
  ABBuildReportBuilder.cs
  ABBuildReportStore.cs

AB/Build/UI/Editor/
  ABReportPanel.cs

AA/Build/Pipeline/Editor/
  AABuildBackend.cs
  AAAssetIndexBuilder.cs
  AABuildOutputOrganizer.cs

AA/Build/Package/Editor/
  AAPackageManifestReader.cs
  AASourceScanFile.cs
```

`ABBuildReportStoreSelfCheck` moves to the existing test build area and is not deleted unless a separate review proves it obsolete. Delete empty AB/AA `Release` directories only after the complete inventory passes.

### Pipeline and publish obsolete files

Delete the obsolete source and `.meta` pairs after reference cleanup:

```text
BuildPipelineConfigUpgrader.cs
ABPipelineConfigUpgrade.cs
AAPipelineConfigUpgrade.cs
BuildPipelineException.cs
BuildPipelineRequest.cs
BuildRunResult.cs (after rename)
PackagePublishTransaction.cs
PublishPlan.cs
PackageAssemblyPlan.cs
PackageTargetAssembler.cs
PushModels.cs (after PublishTargetContracts rename)
PackageFileNames.cs
PublishTargetFactoryRegistry.cs
LocalDirectoryPushTarget.cs
CloudflarePagesPushTarget.cs
```

The exact deletion list must be reconciled against the current worktree before any deletion. Existing developer changes in these files must not be overwritten.

## Implementation Order

1. Freeze the worktree inventory and isolate this scope from existing changes.
2. Apply type/result/context contract changes and remove attempt/config-migration exception paths.
3. Move Shared Build files and update references while preserving `.meta` GUIDs.
4. Move AB/AA Release files and delete empty Release directories only after inventory checks.
5. Implement unified build temporary output and `BuildOutputDelivery` compensation.
6. Replace the publish chain with the four confirmed responsibilities and unified Target config.
7. Apply Settings constant ownership, package-name single source, FileHelper directory copy, and path documentation changes.
8. Update existing tests/self-checks and current documentation references only where required by renamed contracts.
9. Run the narrow verification matrix and perform a stale symbol, path, `.meta`, and diff audit.
10. Stop for developer review and sign-off. Do not commit unless separately requested.

## Verification Plan

Prefer existing checks and self-checks; do not add new test files by default.

- Run existing `pipeline_compose`, `build_cache`, `publish_diff`, `runtime_resource`, `s3_resource_boundary`, and `hotfix_flow` scenarios after updating renamed contracts.
- Extend an existing scenario only when the changed contract has no current assertion. Do not create a new test project for this plan without separate approval.
- Run solution compilation with no new errors.
- Run existing build delivery and publish self-checks for temporary output, rollback, idempotence, source priority, and final PackageIndex ordering.
- Run static scans for deleted symbols, old directory names, old target types, migration calls, attempt names, duplicate Context keys, and direct Summary/cache sources.
- Run `git diff --check`.
- Verify moved `.meta` GUIDs, absence of orphaned `.meta`, and no duplicate GUIDs.
- Unity Editor/Player and real remote deployment remain verification gates; do not claim success without fresh output.

## Success Criteria

- There is one explicit BuildRequest, one final BuildResult, one Summary fact source, and no duplicate Context facts.
- All build output uses the unified temporary-output and delivery model; no attempt terminology remains.
- Publish has one main path and no Target-specific transaction implementation.
- Old migration, registry, compatibility, transaction, plan, and assembler code has no active reference.
- `Release`, generic `Common`, and generic `Editor` directories are empty or removed according to the confirmed mapping.
- Existing tests and self-checks pass without introducing a new test project.
- The worktree changes remain reviewable and unrelated existing changes are preserved.

## Execution Evidence

- `dotnet build XLuaHotfix.sln --no-restore --no-incremental`: exit 0; only existing assembly-version conflict warnings.
- Pure .NET scenarios: `ab_remediation` 6/6, `build_cache` 45/45, `pipeline_compose` 24/24, `pipeline_realignment` 8/8, `publish_diff` 3/3, `runtime_resource` passed, `s3_resource_boundary` 6/6, `serialization` passed, `hotfix_flow` passed, `HotfixRuntimeStateMachine` passed, and `S2RuntimeBoundary` passed.
- `git diff --check`: passed after removing generated formatting residue.
- Unity asset integrity: all checked Assets source/config files have `.meta`; duplicate GUID count is zero.
- Static symbol scan: obsolete Build Pipeline, attempt, Publish transaction, target factory, and target class symbols have no active production/test references. Historical requirement records may still mention deleted names as decisions.
- Real Unity Editor/Player build, LocalDirectory Unity smoke, and external deployment were not rerun in this execution.
