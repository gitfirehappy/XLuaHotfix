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

| File | Date | Reviewer | Status |
|------|------|----------|--------|
| fyasset-data-structures-review-20260508.md | 2026-05-08 | gpt-5.4/codex | 16 findings, some open |
| fyasset-redundancy-architecture-review-20260508.md | 2026-05-08 | gpt-5.4/codex | 14 findings, some open |
| fyasset-naming-boundaries-maintainability-review-20260508.md | 2026-05-08 | gpt-5.4/codex | 14 findings, some open |
| fyasset-infrastructure-consistency-review-20260511.md | 2026-05-11 | — | — |
| review-full-landed-code-20260507.md | 2026-05-07 | deepseekv4pro-claudecode | 7 CRITICAL + 18 HIGH, some open |

## Archive Criteria

A review moves to `archive/` when:
- All findings have been addressed (fixes applied and verified)
- The review is explicitly marked as historical/archived
- The reviewed code has been superseded or removed
