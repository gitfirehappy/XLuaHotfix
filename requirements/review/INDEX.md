# Review Directory

Review artifacts for the requirement. Active reviews (findings still under discussion or not yet fully addressed) stay here.

Archived reviews (findings addressed, historical reference) go to `archive/`.

## Report Format Rule

Every review report **MUST** include the following metadata header:

```markdown
# <Title>

> **Date**: YYYY-MM-DD
> **Reviewer**: <name or tool>
> **Scope**: <what was reviewed>
> **Method**: <static analysis, diff review, perf profiling, etc.>
```

Reports missing Date or Reviewer will be rejected before content review.

## Active Reviews

| File | Status | Scope |
|------|--------|-------|
| `review-fyasset-repository-flow-20260705.md` | Open | FYAsset repository commit, package publication, push, and status UI flow |
| `review-fyasset-ponytail-audit-20260621.md` | Pending | FYAsset over-engineering and simplification audit |
| `review-fyasset-mistake-decision-alignment-20260608.md` | Open; partial extraction applied | FYAsset mistake recurrence and decision-alignment review |
| `review-collector-20260521.md` | P0/P1 remediated; P2 deferred | Collector subsystem and direct build-pipeline consumers |

## Recently Archived

| File | Status | Scope |
|------|--------|-------|
| `review-build-chain-blockers-20260606.md` | Remediated, signed off, and archived | AA/AB staging diff, official builds, repository commit, and push chain |
| `review-staging-diff-collections-20260606.md` | Remediated, signed off, and archived | AA/AB Repository staging diff compile failure |

## Archive Criteria

A review moves to `archive/` when:
- All findings have been addressed (fixes applied and verified)
- The review is explicitly marked as historical/archived
- The reviewed code has been superseded or removed
