# Plan Directory

Executable plan files for the requirement. Only active (unexecuted, in-progress, or pending approval) plans stay here.

Completed and abandoned plans go to `archive/`.

## Rules

- Keep approved executable plan files here
- Keep rough, pre-approval, or idea-stage materials under `drafts/`
- Move executed or abandoned plans to `archive/` (never delete — always leave a trace)
- Do not put review artifacts here; use `review/`

## Active Plans

| File | Status | Description |
|------|--------|-------------|
| `plan-build-repo-diff-module-20260523.md` | Awaiting Sign-off | Build Repository Plan 1 / 2: artifact diff module extracted; implementation and verification completed |
| `plan-build-repository-core-20260523.md` | Awaiting Sign-off | Build Repository Plan 2 / 2: filesystem JSON repository, automatic build commits, status, and read-only diff preview |
| `plan-build-repository-release-20260523.md` | Awaiting Sign-off | Build Repository Plan 3: AB Push + IPushTarget + PushHistory + Repository CLI; deletes ConfirmReleaseHotfix |

## Subdirectories

- `drafts/` — Non-executable planning drafts and convergence notes
- `archive/` — Executed, realized, superseded, or cancelled plans

## Archive Criteria

A plan moves to `archive/` when:
- Status is Realized / Executed / DONE (explicitly executed)
- Status is Superseded / Cancelled / Deprecated (explicitly abandoned)
- Status is Container and all sub-plans are archived

Never archive a plan solely because it was "approved" — approval alone is not execution.
