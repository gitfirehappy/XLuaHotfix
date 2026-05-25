# Requirements Workspace

`requirements/` is for work management: plans, approvals, progress, drafts, and reviews. Reusable technical facts belong
in `context/`; human-facing documentation belongs in `docs/`.

## Structure
```text
requirements/
  README.md
  plan.md              # authoritative shared plan/status table
  progress.txt         # shared progress log
  plan/                # shared active executable plans only
    INDEX.md
    archive/           # executed, superseded, cancelled, or deprecated plans
    drafts/            # shared non-executable drafts
      INDEX.md
      archive/
  review/
    INDEX.md
    archive/
```

## Rules
- Keep planning aligned to `requirements/plan.md`.
- Do not create `requirements/{id}/plan.md` or `requirements/{id}/plan/` unless the developer explicitly asks for an
  isolated planning structure.
- Use `requirements/plan/` for shared active plans and `requirements/plan/archive/` for archived plans.
- Do not keep standalone requirement folders long term.
- Before deleting a standalone requirement folder, copy every meaningful entry from its `progress.txt` into
  `requirements/progress.txt` with the requirement id preserved.
- Never replace detailed progress history with a summary-only entry.
- Archive plans after execution, sign-off, supersession, cancellation, or explicit deprecation. Approval alone is not
  enough.
- Before resuming work, read `requirements/progress.txt` and `requirements/plan.md`, summarize status, then wait for
  confirmation.
