# Requirements Index

| Requirement ID | Title | Status | Created | Notes |
|---------------|-------|--------|---------|-------|
| refactor-2026 | Three-System Refactoring (UI / AB Package Mgmt / Lua Directory) | In progress | 2026-03-16 | Plans under `plan/` (active) and `plan/archive/` (executed/abandoned); drafts under `plan/drafts/`; reviews under `review/` |
| tools-docs-hash-20260420 | Tools docs polish and HashGenerator generalization | In Progress | 2026-04-20 | Documentation and common tool cleanup |
| architecture-docs-reset-20260425 | Architecture docs reset | In Progress | 2026-04-25 | Human-facing architecture docs reset and cleanup |
| editor-beautification-20260506 | Editor beautification | In Progress | 2026-05-06 | Editor UI improvements |

---

## Requirement Directory Structure

Each requirement follows this layout:

```
requirements/{id}/
  plan.md              # High-level plan summary (approved)
  progress.txt         # Execution log
  plan/                # Executable sub-plans (active only)
    INDEX.md           # Active plan inventory + archive criteria
    archive/           # Executed, superseded, or cancelled plans
      INDEX.md
    drafts/            # Non-executable drafts
      INDEX.md
      archive/         # Promoted or deprecated drafts
        INDEX.md
  review/              # Review artifacts
    INDEX.md           # Active review inventory + archive criteria
    archive/           # Reviews with findings addressed
      INDEX.md
```

### Archive Criteria

- **Plan → `plan/archive/`**: Status is Realized / Executed / DONE / Superseded / Cancelled / Deprecated
- **Draft → `plan/drafts/archive/`**: Promoted to formal plan, superseded, or explicitly deprecated
- **Review → `review/archive/`**: All findings addressed and verified, or reviewed code superseded

**Important**: Approval alone does NOT qualify for archiving. A plan must be executed or explicitly abandoned.

---

## Resuming Previous Progress

Send `continue {requirementID}` and the AI will read the corresponding progress.txt and summarize the current state.

Example: `continue refactor-2026`

---

## Status Definitions

| Status | Meaning |
|--------|---------|
| Draft | Plan written, awaiting developer approval of the approval checklist |
| In Progress | Approved, currently executing |
| Paused | Blocked during execution |
| Completed | All sub-tasks passed acceptance |
