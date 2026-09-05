# Draft: Build Repository Follow-up Issues

> **Status**: Archived / Closed 2026-07-14
> **Origin**: extracted from `requirements/plan/drafts/archive/draft-build-repository-20260518.md` after Plans 1/2/3 were executed and archived
> **Purpose**: collect the remaining unresolved Build Repository items that were explicitly deferred out of the shipped repository batch

## Archive Disposition

The HEAD failure path now removes an unreferenced object, persistent PushHistory was deleted, and the remaining
serializer/concurrency ideas have no observed trigger. Reopen from a new concrete failure instead of retaining
speculative infrastructure work.

## Remaining Open Items

1. **Repository serialization follow-up**
   - Re-evaluate `Newtonsoft.Json` for repository snapshot persistence.
   - Current Plan 2/3 implementation uses JSON serialization that is adequate for the shipped scope, but follow-up may be warranted for nullable and plain DTO ergonomics.

2. **Repository orphan-object cleanup**
   - If object write succeeds but HEAD swap fails, the orphan object is currently tolerated.
   - A garbage-collection policy is still open.

3. **Concurrent push coordination**
   - Multiple machines or processes sharing `BuildData/Snapshots/` still lack an explicit file-locking policy.

4. **Optional published-state derived view**
   - A UI badge or derived view could be computed from `PushHistory.json`.
   - This is not required for current behavior.

## Notes

- Do not reintroduce completed Plan 1/2/3 scope here.
- AA Push is no longer treated as a deferred repository follow-up. It is promoted to a basic build-pipeline closure prerequisite in `requirements/plan/plan-build-repository-aa-push-20260603.md`.
- This draft is only for the residual follow-up surface that remains after the shipped repository batch and AA Push closure plan.
