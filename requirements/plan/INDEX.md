# Plan Queue

Shared executable plans live here while active. `requirements/plan.md` remains the authoritative status table.

## Rules
- Keep only active shared plans in this directory.
- Move executed, signed-off, superseded, cancelled, or deprecated plans to `archive/`.
- Keep non-executable ideas in `drafts/`.
- Do not create per-requirement `plan.md` files or `plan/` folders unless the developer explicitly requests it.
- When deleting standalone requirement folders, first merge their detailed `progress.txt` entries into
  `requirements/progress.txt`; do not replace detailed history with summary-only lines.

## Recently Archived
| File | Status |
|---|---|
| `plan-build-repo-diff-module-20260523.md` | Archived |
| `plan-build-repository-core-20260523.md` | Archived |
| `plan-build-repository-release-20260523.md` | Archived |
| `plan-comment-debug-coverage-20260524.md` | Archived |
| `plan-hotfix-diff-task-20260524.md` | Archived |

## Active
| File | Status |
|---|---|
| `plan-collector-asset-metadata-bundle-packing-20260531.md` | Executed on `main`; awaiting sign-off |
