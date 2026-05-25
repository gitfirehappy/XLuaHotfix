# Draft: Build Repository Follow-up Issues

> **Status**: Draft
> **Origin**: extracted from `requirements/plan/drafts/archive/draft-build-repository-20260518.md` after Plans 1/2/3 were executed and archived
> **Purpose**: collect the remaining unresolved Build Repository items that were explicitly deferred out of the shipped repository batch

## Remaining Open Items

1. **AA Push support**
   - Needs AA commit-level bundle mapping or catalog reverse lookup.
   - Must reuse the existing `IPushTarget` / `PushHistory` contracts.
   - This is the only clearly product-facing repository capability still deferred from the shipped batch.

2. **Repository serialization follow-up**
   - Re-evaluate `Newtonsoft.Json` for repository snapshot persistence.
   - Current Plan 2/3 implementation uses JSON serialization that is adequate for the shipped scope, but follow-up may be warranted for nullable and plain DTO ergonomics.

3. **Repository orphan-object cleanup**
   - If object write succeeds but HEAD swap fails, the orphan object is currently tolerated.
   - A garbage-collection policy is still open.

4. **Concurrent push coordination**
   - Multiple machines or processes sharing `BuildData/Snapshots/` still lack an explicit file-locking policy.

5. **Optional published-state derived view**
   - A UI badge or derived view could be computed from `PushHistory.json`.
   - This is not required for current behavior.

## Notes

- Do not reintroduce completed Plan 1/2/3 scope here.
- This draft is only for the residual follow-up surface that remains after the shipped repository batch.
