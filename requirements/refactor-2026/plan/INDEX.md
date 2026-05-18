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
| plan-A.md | A1/A2 done, A3 pending | UIForm animation and dynamic form system |
| plan-B5-4.md | Deferred | Migration strategy — evolves with implementation |
| plan-D.md | Draft | Awaiting developer approval |
| plan-E1-1.md | Needs back-change | IGroupRule interface missing |
| plan-E12-buildgraph-editor.md | E12-1/E12-2 done, awaiting sign-off | BuildGraph editor visualization and build trigger |
| plan-aamanifest-helperbuilddata-20260518.md | DONE | AAManifest rename and HelperBuildData fusion completed |
| plan-hash-unification-20260518.md | Executed, awaiting sign-off | HU-1 hash metadata unification: MD5 identity + CRC32 fast verification |
| plan-S1.md | Awaiting approval | — |
| plan-S2.md | Awaiting approval | — |
| plan-serialization.md | — | — |
| plan-urgent-tools.md | — | — |

## Subdirectories

- `drafts/` — Non-executable planning drafts and convergence notes
- `archive/` — Executed, realized, superseded, or cancelled plans

## Archive Criteria

A plan moves to `archive/` when:
- Status is Realized / Executed / DONE (explicitly executed)
- Status is Superseded / Cancelled / Deprecated (explicitly abandoned)
- Status is Container and all sub-plans are archived

Never archive a plan solely because it was "approved" — approval alone is not execution.
