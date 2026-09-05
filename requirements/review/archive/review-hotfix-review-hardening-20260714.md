# Hotfix Review Hardening And Requirements Alignment Review

> **Date**: 2026-07-14
> **Reviewer**: Codex
> **Status**: Current / P1 resolved / one unconfirmed P2 retained
> **Scope**: Approved Hotfix exception cleanup, Windows filename validation, Full baseline staged Bundle validation,
> active plan/review/draft queues, and progress alignment.
> **Method**: TDD RED/GREEN scenario, static diff and call-path review, solution compilation without restore, temporary
> filesystem fixtures, full active-draft disposition audit, active Markdown/index checks, and whitespace checks. Unity
> Build/GUI, Push, Reset, network, Cloudflare, and package-format changes were excluded.

## Executive Summary

No P0 or P1 finding remains in the approved scope. The implementation closes all three P1 findings from the superseded
2026-07-13 review with existing classes and one phase Boolean:

1. Exceptional download and activation failures return through failed-target cleanup. Target initialization failure
   deletes the validated direct-child target and follows the existing fallback/fatal rule. Once PackageManager reports
   success, pointer persistence remains fatal but retains live content and cannot raise `OnFinished`.
2. Windows path segments now reject invalid filename characters, device basenames (including extension aliases),
   trailing dot/space, and case-insensitive Bundle duplicates.
3. Full AA/AB baseline staging now reads the staged backend manifest and requires an exact top-level Bundle filename
   set with matching size and CRC before replacing StreamingAssets.

The draft audit reduced ten active draft entries to six. Four completed, resolved, superseded, or triggerless drafts
were annotated and moved to `requirements/plan/drafts/archive/`; genuine product decisions and consolidated acceptance
work remain active drafts.

## Resolved Findings

### [Resolved P1] Phase-aware exceptional target cleanup

`HotfixFlowBase` contains thrown download and activation errors at the same Boolean failure boundaries already consumed
by `HandleTargetFailureAsync`. `FinalizeTargetAsync` uses `packageManagerInitialized` as the only new state. Before
success, errors use the safe direct-child cleanup path. After success, `HotfixFatalException` from pointer persistence
is rethrown without cleanup or completion notification.

Review result: resolved without a transaction class, rollback framework, deinitialization assumption, or package-flow
semantic change.

### [Resolved P1] Windows filename identity

`HotfixPackageValidator.IsSafePathSegment` now covers the Win32-invalid character set, reserved CON/PRN/AUX/NUL and
COM1-9/LPT1-9 basenames before the first extension, and trailing dot/space aliases. Runtime preparation and exact-file
validation both use `StringComparer.OrdinalIgnoreCase` for Bundle identity.

Review result: resolved. Focused cases include normal names, invalid `*`/`?`, device aliases with one or multiple
extensions, trailing aliases, and case-only duplicates.

### [Resolved P1] Full baseline stage integrity

`TaskExportLocalBuildData.ValidateStage` loads the staged ABManifest or AAManifest, converts its existing Bundle entries,
and calls the same exact-file validator used by runtime inspection. Count-only source comparisons were removed. Missing,
extra, size-mismatched, or CRC-mismatched files now fail before StreamingAssets replacement.

Review result: resolved without a new loader interface or validation subsystem.

## Remaining Finding

### [P2 / Unconfirmed] Repository navigation scroll position

`RepositoryStatusPanel.RenderNavigation` still rebuilds the navigation container without explicit scroll-offset state.
The approved workflow required reproduction before code. Unity GUI execution was excluded, so no real long-history
scroll jump was observed in this pass and no speculative state code was added.

Disposition: retain as one current review item. Reproduce with a long Changes/History list; only if confirmed, preserve
the owning ScrollView offset using the existing AssetsCollection pattern.

## Draft Audit

Archived:

- `draft-build-repository-followup-20260524.md`: HEAD orphan cleanup and PushHistory removal are implemented; remaining
  serializer/locking ideas have no observed trigger.
- `draft-docs-synchronization-20260707.md`: superseded by the completed project/plan/context alignment pass.
- `draft-broken-pptr-scene-validation-20260707.md`: dangling reference resolved; file already recorded archived status.
- `draft-hotfix-publish-integrity-20260712.md`: local validation is now owned by the active plan; deployed AA bytes and
  cache rules already received real audits; speculative remote automation has no current trigger.

Retained:

- `draft-legacy-plan-review-followups-20260714.md`: consolidated manual acceptance and deletion-only follow-ups.
- `draft-fyasset-architecture-review-20260707.md`: A0/A6/A8 remain deferred architecture decisions.
- `draft-lua-index-pipeline-independence-20260520.md`: shared address-source decision remains open.
- `draft-offline-standalone-package-20260513.md`: product/build-mode decision remains open.
- `draft-debug-panel-20260512.md`: runtime diagnostics product scope remains open.
- `plan-playmode-draft.md`: three-mode Editor workflow remains open.

## Verification And Limits

- Focused TDD scenario: passed after an expected compile-time RED; the final fresh run exited 0.
- Solution compilation: final fresh build exited 0 with 0 errors and the two existing System.Net.Http warning groups.
- Exact Bundle fixtures used OS temporary storage and deleted it in `finally`.
- Active indexes match the filesystem: one plan, one review, and six drafts. All 55 active
  context/docs/requirements Markdown files resolved their relative links.
- `git diff --check` exited 0 with line-ending conversion warnings only.
- No Unity Full export or runtime fault-injection E2E was run under the approved exclusions. This is an acceptance limit,
  not evidence of a code defect.
- No Build, Push, Reset, network, Cloudflare, new dependency, transaction abstraction, or package-format change occurred.
