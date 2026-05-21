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
| `plan-aa-editor-task-acceptance-20260521.md` | Executed; awaiting sign-off | Validate AA Task migration from the Unity Editor by matching the AB BuildGraph/Validate workflow and avoiding duplicate Addressables UI |
| `plan-editor-ux-polish-20260521.md` | Executed; awaiting sign-off | Low-risk Editor UX polish for BuildGraph source navigation, Pipeline Validate details, splitter visibility, and Collector scan scrolling |

## Subdirectories

- `drafts/` — Non-executable planning drafts and convergence notes
- `archive/` — Executed, realized, superseded, or cancelled plans

## Archive Criteria

A plan moves to `archive/` when:
- Status is Realized / Executed / DONE (explicitly executed)
- Status is Superseded / Cancelled / Deprecated (explicitly abandoned)
- Status is Container and all sub-plans are archived

Never archive a plan solely because it was "approved" — approval alone is not execution.
