# Hotfix Progress Steps Simplification Plan 2026-07-09

> **Status**: Implemented / Verified / Pending developer sign-off
> **Requirement ID**: hotfix-progress-steps-20260709
> **Origin**: A10 from `requirements/plan/drafts/draft-fyasset-architecture-review-20260707.md`
> **Scope**: Remove manual progress step indexing from `HotfixManager`.

## Goal

Make hotfix progress calculation follow the actual ordered step list so future step additions/removals cannot silently
desynchronize `TotalSteps` and manual step indexes.

## Locked Decisions

1. Use one ordered step-name table as the source of truth for hotfix progress.
2. Delete the hardcoded `TotalSteps = 11`.
3. Delete manual `BeginStep(name, index)` call sites.
4. Keep the existing hotfix orchestration flow; do not introduce a wrapper/delegate pipeline.
5. Keep existing progress event shape and caller-facing behavior.
6. Do not change runtime loading, hot-update package format, backend selection, or download semantics.

## Implementation Checklist

1. Capture the current hotfix step names in their existing execution order.
2. Replace `BeginStep(string stepName, int stepIndex)` with index lookup from the ordered step table.
3. Replace all `BeginStep(..., number)` calls with `BeginStep(...)`.
4. Keep progress clamped to the existing 0..1 range and preserve sub-progress behavior.
5. Remove obsolete constants/comments that imply manual indexing.

## Acceptance Criteria

- Hotfix progress total is derived from the ordered step table.
- No active `TotalSteps` constant remains in `HotfixManager`.
- No active `BeginStep` call passes a numeric index.
- Adding/removing a step requires changing the ordered table and the corresponding call name, not a separate numeric
  count.
- Hotfix flow order and backend calls remain unchanged.

## Verification

- `dotnet build XLuaHotfix.sln --no-restore`
- `git diff --check`
- Static checks:
  - no active `TotalSteps` anywhere under `Assets/FYAsset/Scripts`
  - no active `BeginStep(..., <number>)` call sites

## Non-Goals

- No hotfix backend redesign.
- No retry/download behavior change.
- No wrapper pipeline or async operation framework.
