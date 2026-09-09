# Requirements Workspace

`requirements/` stores work plans, approvals, concise progress, and review records. Current implementation facts belong
in source and tests. Current architecture and human-facing explanations belong in `docs/`. Stable reusable knowledge
belongs in `context/`.

## Structure

```text
requirements/
  README.md
  plan.md
  progress.txt
  plan/
    INDEX.md
    drafts/
    archive/
  review/
    INDEX.md
    archive/
```

## Responsibilities

- `plan.md` is the authoritative current status table.
- `progress.txt` is the execution history.
- A plan file contains its own purpose, constraints, success criteria, decisions, tasks, and verification evidence.
- `plan/` contains shared active plans, drafts, and archived plans.
- `review/` contains review reports and their archive.

## Progress

Write progress as concise, real-time events. New entries use:

```text
[type] YYYY-MM-DD scope: result
```

Use a short type such as `start`, `decision`, `done`, `blocked`, or `next`. Keep detailed reasoning, commands, raw
output, and review findings in the active plan or review report. Historical entries may be normalized for format and
repetition, but meaningful decisions, actions, blockers, and verification results must remain.

Append new entries only at the end of `progress.txt`. Never insert at the head or in the middle. Read the end of the file for the latest state.

## Plan Lifecycle

- Use the shared `plan/` queues; do not create `requirements/{id}/plan.md` or `requirements/{id}/plan/` by default.
- Drafts are not executable. Move a draft into the active queue only after explicit approval.
- Archive a plan after execution, sign-off, supersession, cancellation, or explicit deprecation.
- Before removing a legacy requirement folder, copy every meaningful progress entry into the root `progress.txt` with
  its requirement scope preserved.
- Before resuming non-trivial work, read `plan.md`, `progress.txt`, and the relevant active plan, then confirm the
  current status.

## Indexes

`plan/INDEX.md` is a queue index containing file, status, and short subject. `review/INDEX.md` is a review queue index
containing report format, file, status, and short scope. Neither index replaces a plan, the progress log, or a review
report.
