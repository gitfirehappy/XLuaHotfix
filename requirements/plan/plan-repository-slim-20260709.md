# Build Repository Slim Plan 2026-07-09

> **Status**: Implemented / Verified / Pending developer sign-off
> **Requirement ID**: repository-slim-20260709
> **Origin**: A4 from `requirements/plan/drafts/draft-fyasset-architecture-review-20260707.md`
> **Scope**: Keep repository health and explicit push, remove repair/quarantine and persistent push history.

## Goal

Reduce Build Repository maintenance surface while preserving the behavior still needed for build safety: status/health
checks, build/push blocking on fatal health, commit history, staging diff, and explicit push.

## Locked Decisions

1. Keep Repository Health checks.
2. Keep fatal-health blocking for build/push paths.
3. Keep commit objects, HEAD, staging diff, history display from commit data, and `PushHead`.
4. Keep CLI explicit push support.
5. Delete Repair and RepairDryRun flows.
6. Delete quarantine output and repair logs/actions.
7. Delete persistent `PushHistory.json` and UI history display.
8. Push becomes current operation state only; it does not persist a long-term push history list.
9. Do not change repository commit format except removing repair/push-history-only fields if they are unused by commit
   objects.

## Implementation Checklist

1. Remove repair result models and repair methods from repository interfaces/facades/implementations.
2. Remove CLI `Repair` and `RepairDryRun` commands and related output.
3. Remove repair buttons/log rendering from `RepositoryStatusPanel`.
4. Remove quarantine directory creation and repair action serialization.
5. Remove `PushHistory.json` read/write/list code and the repository UI section that displays push history.
6. Keep `PushHead` and explicit CLI push wired to the existing push targets.
7. Update health/status text so repository corruption is reported as a blocker, not auto-repaired.

## Acceptance Criteria

- Repository health can still be viewed from UI/CLI.
- Fatal repository health still blocks build/push paths.
- `Repair` and `RepairDryRun` are absent from active UI/CLI/facade/repository code.
- `PushHistory.json` is no longer read, written, or displayed.
- Push still works through editor `PushHead` and CLI explicit push.
- Commit history and staging diff remain available.

## Verification

- `dotnet build XLuaHotfix.sln --no-restore`
- `git diff --check`
- Static checks:
  - no active `RepairDryRun`, `RepositoryRepairResult`, `repair-quarantine`, or `RepairActions`
  - no active `PushHistory.json`, `PushHistoryEntry`, or `ListPushHistory`
  - `PushHead` and CLI explicit push remain active

## Non-Goals

- No repository storage redesign.
- No push target redesign.
- No preview cache work.
- No channel reset tooling.
