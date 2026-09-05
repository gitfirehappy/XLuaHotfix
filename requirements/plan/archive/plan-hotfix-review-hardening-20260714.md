# Hotfix Review Hardening Plan 2026-07-14

> **Status**: Implemented / Verified / Pending developer sign-off
> **Requirement ID**: hotfix-review-hardening-20260714
> **Origin**: P1 findings from `requirements/review/archive/review-project-plan-context-alignment-20260713.md`
> **Scope**: Minimal exception cleanup, Windows filename validation, Full baseline stage validation, and draft audit.

## Goal

Close the three P1 review findings without adding a transaction framework, new runtime abstraction, dependency, or
package-format change.

## Locked Decisions

1. Keep failure handling in the existing flow methods. Do not add a transaction/state-machine class.
2. Before PackageManager initialization succeeds, exceptional target failures delete only the validated direct-child
   target and restore/fallback through the existing path.
3. After PackageManager initialization succeeds, pointer persistence failure keeps the live target, raises fatal, and
   does not raise `OnFinished`.
4. Windows package/Bundle names reject invalid filename characters, device names, trailing dot/space, and
   case-insensitive duplicates.
5. Full baseline stage validation reads the staged backend manifest and verifies exact Bundle names, sizes, and CRCs
   before replacing StreamingAssets. Keep the check build-owned; add no validation interface.
6. Repository scroll P2 is reproduction-only and receives no code change without evidence.
7. Audit every active draft. Archive drafts that are implemented, resolved, superseded, or purely speculative with no
   current trigger; preserve genuinely open product decisions.

## Implementation Checklist

1. Complete TDD steps 1-7 with one focused hardening scenario and one scenario test surface.
2. Extend existing lightweight validation tests for Windows filename aliases and case-insensitive duplicates.
3. Add strict staged AA/AB manifest-to-file validation in `TaskExportLocalBuildData`.
4. Contain target preparation/activation/initialization exceptions at existing boundaries; keep pointer failure
   phase-aware.
5. Run the focused scenario, temporary-directory self-checks, full solution build, and diff checks.
6. Audit `requirements/plan/drafts/`, move eligible drafts to `drafts/archive/`, and update the draft index.
7. Align the current review, master plan, plan queue, and progress; request developer sign-off before archiving this plan.

## Acceptance Criteria

- Invalid/reserved/aliased Windows names are rejected before path construction.
- Bundle duplicate detection is case-insensitive for the Windows-supported flow.
- A staged manifest/file name, size, or CRC mismatch blocks Full baseline publication.
- An exception before PackageManager initialization removes the safe target and follows existing fallback/fatal rules.
- Pointer persistence failure after initialization never deletes the live target and never raises `OnFinished`.
- No new runtime interface, transaction class, dependency, package format, Push, network, or Cloudflare behavior.
- Draft index and filesystem agree after the audit.

## Verification

- `dotnet run --project tests/scenario/HotfixRuntimeStateMachineTests.csproj --no-restore`
- Focused temporary-directory build-stage/self-check command with automatic cleanup
- `dotnet build XLuaHotfix.sln --no-restore`
- Active requirements/context Markdown link and index checks
- `git diff --check`

## Non-Goals

- No Repository scroll fix without reproduction.
- No AB/Android E2E, Build, Push, Reset, local server, or external network operation.
- No generalized package validation framework.

## Execution Result

- All three P1 findings are resolved in the existing flow, validator, inspection, and baseline-export classes.
- Focused TDD completed RED/GREEN/refactor verification; the solution compiles with 0 errors.
- Repository scroll was not reproduced without Unity GUI, so no P2 code was added.
- Ten active draft entries were audited: four archived and six retained with explicit ownership.
- The fresh result is recorded in `requirements/review/review-hotfix-review-hardening-20260714.md`.
