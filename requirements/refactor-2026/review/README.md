# Review Directory

This directory stores review artifacts.

Use it for:
- review findings
- review checklists
- review-phase summaries
- fix verification notes

## Report Format Rule

Every review report **MUST** include the following metadata header:

```markdown
# <Title>

> **Date**: YYYY-MM-DD
> **Reviewer**: <name or tool>
> **Scope**: <what was reviewed>
> **Method**: <static analysis, diff review, perf profiling, etc.>
```

- **Date** — when the review was performed (ISO format). Required for traceability.
- **Reviewer** — who or which tool produced the report. Required for accountability.
- **Scope + Method** — recommended, makes the report self-contained.

Reports missing Date or Reviewer will be rejected before content review.
