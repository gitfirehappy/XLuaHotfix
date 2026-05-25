# Drafts Directory

Non-executable planning drafts. These are discussion material only — they do not authorize implementation.

Promoted, superseded, or deprecated drafts go to `archive/`.

## Rules

- Drafts do not authorize implementation
- Promote stable decisions into approved plan files in the parent `plan/` directory
- When promoting: condense and annotate the original draft, then move it to `archive/` — never delete

## Active Drafts

| File | Status | Description |
|------|--------|-------------|
| draft-build-repository-followup-20260524.md | Draft | Build Repository follow-up issues after Plans 1/2/3 shipped: AA Push, repository serialization, orphan-object cleanup, concurrent push coordination, published-state derived view |
| draft-lua-index-pipeline-independence-20260520.md | Draft | LuaScriptsIndexExporter pipeline-independence open decision |
| draft-offline-standalone-package-20260513.md | Draft | Offline standalone package design |
| draft-debug-panel-20260512.md | Draft | Runtime debugger panel |
| plan-playmode-draft.md | Draft | PlayMode 三模式设计 |

## Discussion Order

| Order | Draft | Why First / Later |
|-------|-------|-------------------|
| 1 | `draft-build-repository-followup-20260524.md` | Highest remaining repository-impact surface. It isolates the unresolved AA Push / persistence / cleanup questions from the shipped batch. |
| 2 | `draft-offline-standalone-package-20260513.md` | Depends on clear build output ownership and runtime path rules. It should be discussed after the repository follow-up so offline output and hotfix output do not create conflicting truths. |
| 3 | `plan-playmode-draft.md` | Depends on Collector, manifest generation, and runtime backend boundaries. It should follow build-output discussions because Simulate/Runtime modes reuse those contracts. |
| 4 | `draft-lua-index-pipeline-independence-20260520.md` | Narrow open decision. Discuss after PlayMode because address-source and index-generation boundaries are easier to decide once backend/play-mode behavior is fixed. |
| 5 | `draft-debug-panel-20260512.md` | Mostly observational tooling. It is valuable but should follow the core runtime/build contracts so the panel exposes stable concepts instead of chasing moving boundaries. |

## Archive Criteria

A draft moves to `archive/` when:
- Promoted to a formal plan (trace retained, draft archived)
- Superseded by another draft or plan
- Explicitly deprecated or abandoned
