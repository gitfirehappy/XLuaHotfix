# Draft: Legacy Plan And Review Follow-ups

> **Date**: 2026-07-14
> **Status**: Draft / Not approved
> **Source**: Eight executed plans and three processed reviews archived on 2026-07-14

## Purpose

Keep the small amount of unfinished acceptance and still-valid cleanup scope after removing executed plans and processed
reviews from the active queues. This draft does not authorize implementation, destructive editor actions, Build, Push,
Reset, or external access.

## Pending Acceptance

### Build State Cleanup

Source: `../archive/plan-build-state-cleanup-tools-20260707.md`.

- Run `FYAsset/Tests/AB Report Store Self Check` in Unity.
- With disposable data only, accept cancel/confirm behavior for Repository version reset, package deletion, and
  channel-scoped Repository reset.

### AA/AB Editor Split

Source: `../archive/plan-aa-ab-shared-split-20260709.md`.

- Open AA and AB windows together and verify independent state.
- Verify Repository splitters drag and persist after rebuild/reopen.
- Run `FYAsset/Tests/AA Hotfix Group Restore Self Check` and accept Address/path plus recovery-state presentation.

### Build Panel And Reports

Source: `../archive/plan-build-panel-task-slim-20260711.md`.

- Run disposable AA Full and Hotfix builds to verify persisted Build Paths, final package layout, and official report
  values.
- Inspect old/new AB reports for asset expansion, dependencies, reverse references, and details.
- Accept Repository version display/reset behavior.

## Residual Review Work

### Repository Failure Paths

Source: `../../review/archive/review-fyasset-repository-flow-20260705.md`.

- Add focused failure injection only if publication code changes again: package copy interruption, pointer write failure,
  and StreamingAssets baseline restore failure.
- Repository navigation scroll preservation is already tracked by
  `../../review/review-hotfix-review-hardening-20260714.md`; do not create a second implementation plan.

### Ponytail Cleanup Candidates

Source: `../../review/archive/review-fyasset-ponytail-audit-20260621.md`.

Still present and worth a later deletion-only pass:

1. Delete uninstantiated `PlaceholderPanel` and `BuilderPanel`.
2. Delete unused `RULE_GROUP_BY_TYPE`, `RULE_GROUP_BY_LABEL`, and `RULE_GROUP_BY_DIRECTORY` constants.
3. Recheck `RuleResolver` / `RuleDropdownHelper`; simplify only if the project still has no real extension need.
4. Recheck whether single-implementation `IBuildRepository` buys anything after repository stabilization.
5. Replace `CollectAll.ContainsEditorDirectory` with the existing normalized-path substring check.
6. Delete unused `FYAssetBuildSettingsProvider.CurrentBackend`, `RuleDropdownHelper.ClearCache()`, and
   `ABBuildReportStore.GetLatestReportPath()` if targeted searches still show no callers.

Closed ponytail findings are not retained: `AAReportPanel` now opens the official report, `IPushTarget` has Local and
Cloudflare implementations, `IBuildBackend` has AA and AB implementations, and settings share `FYAssetSettingsLoader`.

## Collector Review Disposition

The Collector P0/P1 findings were fixed. The later size-unknown change also closed the remaining concrete
`MinAssetSizeBytes` issue. Broader validator/scanner deduplication is accepted maintenance debt, and the project records
manual acceptance as primary rather than mandatory unit coverage. Reopen only on a reproduced drift; no speculative
Collector task is retained here.

## Current Review Boundary

The three Hotfix P1 findings are resolved by `../plan-hotfix-review-hardening-20260714.md`. The unconfirmed Repository
scroll P2 remains in `../../review/review-hotfix-review-hardening-20260714.md` and is intentionally not duplicated into
this draft.
