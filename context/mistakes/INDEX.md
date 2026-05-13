# Mistakes Knowledge Index

> Verified historical errors and prevention rules, extracted from `requirements/refactor-2026/review/` (22 reviews, 2026-04 through 2026-05).

Rules:
- Record only verified mistakes or confirmed troubleshooting outcomes.
- Each entry: symptom, root cause, fix, prevention rule.
- Keep workflow status and plan sequencing out of these files.
- All content in English (AI-facing).

## Files

| File | Scope | Count |
|------|-------|-------|
| `process-pitfalls.md` | Plan-implementation drift, doc sync, naming, cross-plan coordination | 14 |
| `implementation-pitfalls.md` | Silent failure, dual truth, infrastructure bypass, data structure contracts | 34 |
| `platform-performance.md` | Editor/Runtime boundary, platform I/O, allocations, wasted computation | 14 |
| `troubleshooting.md` | Legacy Chinese notes — superseded, retained for git history | — |
