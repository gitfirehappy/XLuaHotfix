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
| `review-build-repository-batch-20260524.md` | Archived | Build Repository batch archive review: plan lifecycle, documentation alignment, repository storage, push path, and unresolved follow-up extraction |

## Archive Criteria

A review moves to `archive/` when:
- All findings have been addressed (fixes applied and verified)
- The review is explicitly marked as historical/archived
- The reviewed code has been superseded or removed
