# Requirements Index

# Requirements Index

refactor-2026 — Three-System Refactoring (UI / AB Package Mgmt / Lua Directory). In progress since 2026-03-16.

## Structure

```
requirements/
  plan.md              # Master plan index — all sub-plans + status
  progress.txt         # Execution log
  plan/                # Executable sub-plans
    INDEX.md           # Active plan inventory
    archive/           # Executed / superseded / cancelled plans
    drafts/            # Non-executable drafts
      archive/         # Promoted or deprecated drafts
  review/              # Review artifacts
    archive/           # Reviews with findings addressed
```

### Archive Criteria

- **Plan -> `plan/archive/`**: Executed / DONE / Superseded / Cancelled
- **Draft -> `plan/drafts/archive/`**: Promoted to formal plan, superseded, or deprecated
- **Review -> `review/archive/`**: All findings addressed and verified

Approval alone does NOT qualify. A plan must be executed or explicitly abandoned.

## Resuming

Send `continue refactor-2026` — the AI reads progress.txt and summarizes current state.
