# Project, Plan, And Context Alignment Review

> **Date**: 2026-07-13
> **Reviewer**: Codex
> **Status**: Archived / Superseded 2026-07-14
> **Scope**: Current uncommitted Windows Hotfix state-machine and baseline-export changes; active plan/review queues; `context/`, `requirements/plan.md`, and `requirements/progress.txt` alignment.
> **Method**: Static diff review from checkpoint `68b75a5`, targeted source inspection, plan/progress trace comparison, archive-criteria audit, path-validation probes, and safe local verification. No Build, Push, Reset, external network, Cloudflare, or Unity GUI workflow was executed.

The three P1 findings were resolved by `plan-hotfix-review-hardening-20260714.md`. The unconfirmed Repository scroll P2
and the post-fix review result moved to `review-hotfix-review-hardening-20260714.md`.

## Executive Summary

The follow-up archive audit on 2026-07-14 found that all eight queued plans had completed execution. Their remaining
gaps were acceptance or later review findings, not active plan implementation. All eight plans and the three processed
older reviews are now archived. One consolidated, non-executable draft preserves the real legacy follow-ups; this report
is the only active review.

Context and requirements had several confirmed facts from before the strict AA/AB split and repository slimming. This
pass aligns the current manager ownership, requirements workspace workflow, VersionDataBase UI ownership, persistent
PushHistory removal, Zhihu reference path, and superseded folder-layout records.

The current Hotfix implementation has three P1 correctness/hardening findings. They do not invalidate the successful
AA acceptance evidence already recorded in progress, but they leave failure and malformed-input paths below the
contract stated by the archived Windows Hotfix execution plan.

## Findings

### [P1] Exceptions during target preparation or finalization bypass failed-target cleanup

**Files**

- `Assets/FYAsset/Scripts/Shared/Hotfix/HotfixFlowBase.cs:59`
- `Assets/FYAsset/Scripts/Shared/Hotfix/HotfixFlowBase.cs:454`
- `Assets/FYAsset/Scripts/Shared/Hotfix/HotfixFlowBase.cs:508`
- `Assets/FYAsset/Scripts/Shared/Hotfix/HotfixFlowBase.cs:601`

**Problem**

Expected failure results are routed through `HandleTargetFailureAsync`, which deletes the target directory before
falling back or failing. Thrown exceptions are different: `Task.WhenAll` can propagate an I/O exception from
`ReplaceFile`, and `FinishHotfix` can throw during finalization. These exceptions escape to the outer `InitializeAsync`
catch, which converts them to `HotfixFatalException` without calling target cleanup. Pointer persistence can also throw,
but it occurs after runtime initialization and therefore cannot safely use the same deletion path.

**Impact**

The archived execution plan states that every target Bundle, activation, or initialization failure removes the target directory.
An exceptional disk/initialization path can instead leave a partial `Build_*` target behind, with
`RuntimePathManager.CurrentGUIDRoot` already switched to that target in activation paths. The local pointer remains on
the previous package, so the next run can recover, but the failed-target deletion invariant is not met. Conversely,
blindly deleting after pointer persistence fails would remove content already owned by an initialized PackageManager.

**Recommendation**

Use a phase-aware exception boundary. Before PackageManager initialization succeeds, safely delete the direct-child
target, restore the previous runtime path, and preserve the original failure. After initialization succeeds, a pointer
commit failure must keep the active target, raise a fatal error, and avoid `OnFinished`; do not delete live content
without an explicit deinitialization/rollback API. Pre-stage the pointer temp file before activation to reduce the final
atomic-commit failure window. Add focused fault checks for Bundle replacement, activation, `FinishHotfix`, and pointer
commit exceptions.

### [P1] Full baseline staging is validated less strictly than runtime startup

**Files**

- `Assets/FYAsset/Scripts/Shared/Build/Pipeline/Editor/Tasks/TaskExportLocalBuildData.cs:184`
- `Assets/FYAsset/Scripts/Shared/Build/Pipeline/Editor/Tasks/TaskExportLocalBuildData.cs:197`
- `Assets/FYAsset/Scripts/Shared/Build/Pipeline/Editor/Tasks/TaskExportLocalBuildData.cs:204`
- `Assets/FYAsset/Scripts/Shared/Hotfix/HotfixPackageInspection.cs:41`

**Problem**

`ValidateStage` checks that a manifest/catalog exists and compares source/staged file counts only when the source count
is greater than zero. It does not deserialize the staged manifest, compare its Bundle names to staged files, or verify
the manifest-declared size and CRC. Runtime startup later performs all of those checks.

**Impact**

A stale manifest with the same file count, an empty/missing Bundle source paired with a non-empty manifest, or a damaged
staged Bundle can pass Full build publication and produce a player whose next startup fails fatally during baseline
inspection. Baseline correctness is discovered after publication rather than at the build boundary that owns it.

**Recommendation**

Keep this build-owned and small: deserialize the staged AA/AB manifest, require an exact manifest-to-file name set, and
verify every declared size/CRC before replacing `StreamingAssets`. Do not add a new validation interface solely for
this check.

### [P1] Windows path-segment validation accepts aliases and reserved filenames

**Files**

- `Assets/FYAsset/Scripts/Shared/Hotfix/HotfixPackageValidator.cs:9`
- `Assets/FYAsset/Scripts/Shared/Hotfix/HotfixPackageValidator.cs:29`
- `Assets/FYAsset/Scripts/Shared/Hotfix/HotfixPackageInspection.cs:46`
- `Assets/FYAsset/Scripts/Shared/Hotfix/HotfixFlowBase.cs:774`

**Problem**

`IsSafePathSegment` rejects separators, rooted paths, and colons, but does not reject all
`Path.GetInvalidFileNameChars()`, Windows device names (`CON`, `NUL`, and related forms), trailing dot/space aliases, or
case-insensitive filename collisions. The reviewed predicate accepts examples such as `CON`, `bundle*name`,
`bundle?name`, `bundle.`, and `bundle `. Duplicate Bundle sets use `StringComparer.Ordinal`, even though the current
acceptance target is Windows.

**Impact**

A malformed remote manifest can pass validation and then fail path creation/replacement, address a Windows device name,
or make two logical Bundle entries resolve to one physical file. Concurrent downloads of case-only aliases can also
write the same target path.

**Recommendation**

Define a Windows package-filename contract: reject invalid characters, reserved device basenames, and trailing dots or
spaces, and detect Bundle duplicates with a filesystem-appropriate comparer. Add focused validator tests for each alias
class.

### [P2] Repository navigation refresh still has no explicit scroll-state preservation

**Files**

- `Assets/FYAsset/Scripts/Shared/Build/Repository/Editor/RepositoryStatusPanel.cs:294`
- `Assets/FYAsset/Scripts/Shared/Build/Repository/Editor/RepositoryStatusPanel.cs:696`

**Problem**

The navigation list is hosted inside a `ScrollView`, but `RenderNavigation` clears and rebuilds its child container
without capturing/restoring the scroll offset. Commit and artifact selection are preserved separately; scroll position
is not.

**Impact**

Refreshes and artifact selections can move a developer away from the current navigation context in long histories. This
is the remaining P2 UX item from the repository-flow review and has no recorded manual acceptance.

**Recommendation**

First reproduce the jump with a long Changes/History list. If confirmed, retain the owning `ScrollView`, capture its
`scrollOffset` before rebuilding, and restore it after layout following the existing AssetsCollection pattern. Do not
add state code for an unobserved UI risk.

## Archive Audit

### Reviews

| Review | Decision | Evidence |
|---|---|---|
| `review-fyasset-mistake-decision-alignment-20260608.md` | Archive | Root-cause plan signed off 2026-07-07; reviewed diagnostics, size failure, error-code, and FileHelper findings are addressed or superseded. |
| `review-collector-20260521.md` | Archive | P0/P1 fixed; size-unknown P2 fixed later; broader dedup/tests are accepted or non-mandatory rather than active work. |
| `review-fyasset-ponytail-audit-20260621.md` | Archive after extraction | Resolved findings closed; still-valid deletion candidates consolidated into the legacy follow-up draft. |
| `review-fyasset-repository-flow-20260705.md` | Archive after extraction | Transaction findings fixed; failure-path acceptance consolidated and scroll preservation remains in this current review. |

### Executed Plans

| Plan | Decision | Residual owner |
|---|---|---|
| `plan-build-state-cleanup-tools-20260707.md` | Archive | Consolidated draft owns self-check/manual acceptance. |
| `plan-hotfix-progress-steps-20260709.md` | Archive | None; completed behavior remains active. |
| `plan-linear-build-pipeline-runner-20260709.md` | Archive | None; later task cleanup superseded part of its contract. |
| `plan-pipeline-sequence-list-editor-20260709.md` | Archive | None; later plan removed Validate UI. |
| `plan-repository-slim-20260709.md` | Archive | Consolidated draft owns optional failure-path acceptance. |
| `plan-aa-ab-shared-split-20260709.md` | Archive | Consolidated draft owns Editor acceptance. |
| `plan-build-panel-task-slim-20260711.md` | Archive | Consolidated draft owns Build/report acceptance. |
| `plan-hotfix-windows-state-machine-20260713.md` | Archive | This current review owns post-implementation findings. |

The already moved `plan-hotfix-runtime-state-machine-20260712.md` and
`plan-hotfix-major-baseline-pointer-20260713.md` remain archived as superseded. The active executable plan queue is now
empty; `draft-legacy-plan-review-followups-20260714.md` is discussion material only.

## Alignment Changes

- Corrected the Zhihu reference location in `context/INDEX.md`.
- Aligned requirements workflow with the shared plan/progress queues.
- Aligned runtime ownership with `AAPackageManager`, `ABPackageManager`, and compatibility-only `AssetPackageManager`.
- Replaced stale `VersionPanel` creation ownership with the current Repository display/reset behavior and explicit
  no-auto-create fact.
- Removed persistent PushHistory from current field semantics while preserving its historical mistake context.
- Removed non-existent 2026-05 standalone folder-plan rows from the master plan and recorded their supersession.
- Clarified that the build-state plan's VersionPanel references are historical after the build-panel follow-up.
- Removed stale `PackageCleaner`, duplicate A8, and resolved Repair/PushHistory question text from the architecture draft.

## Verification Scope

Safe verification for this review is limited to the focused pure state scenario, project compilation without restore,
Markdown/link/status consistency checks, and `git diff --check`. Unity build/export, Editor GUI acceptance, local server,
Push, Reset, Cloudflare, and external-network checks are intentionally excluded.

## Verification Results

- `dotnet run --project tests/scenario/HotfixRuntimeStateMachineTests.csproj --no-restore` exited 0 and reported the
  Windows Hotfix state decisions passed.
- `dotnet build XLuaHotfix.sln --no-restore` exited 0 with 0 errors and the two existing `System.Net.Http` version
  conflict warnings.
- All 48 active/context Markdown inputs resolved their relative links.
- Before the follow-up archive pass, the plan index matched 8 active plan files and the review index matched 4 active
  review files. The 2026-07-14 archive verification supersedes those queue counts.
- Targeted stale-fact searches passed, and the new report contains the required metadata and review sections.
- `git diff --check` exited 0 with line-ending conversion warnings only.
- Historical archive files retain old paths by design and were excluded from active-link validity checks.

### 2026-07-14 Archive Follow-up

- Confirmed 0 active executable plan files and exactly 1 active review: this report.
- Confirmed all 8 executed plans and 3 processed reviews targeted by the follow-up audit exist in their archive folders.
- Confirmed the single consolidated draft contains every retained legacy acceptance/review category and does not duplicate
  this report's current findings.
- All 38 current active/context Markdown inputs resolved relative links.
- Plan/review indexes matched the empty plan queue and single active review; `git diff --check` exited 0 with line-ending
  conversion warnings only.
