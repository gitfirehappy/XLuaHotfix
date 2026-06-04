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
| draft-address-generation-conflict-policy-20260604.md | Draft | Address generation conflict policy: replace automatic `Filename_Type` upgrade with explicit asset/group operations while preserving Address duplicate semantics |
| draft-ab-cumulative-hotfix-delivery-20260604.md | Draft | AB cumulative hotfix package delivery: complete runtime manifest plus delivery bundle list relative to the Full baseline |
| draft-build-repository-followup-20260524.md | Draft | Build Repository follow-up issues after Plans 1/2/3 shipped: AA Push, repository serialization, orphan-object cleanup, concurrent push coordination, published-state derived view |
| draft-lua-index-pipeline-independence-20260520.md | Draft | LuaScriptsIndexExporter pipeline-independence open decision |
| draft-offline-standalone-package-20260513.md | Draft | Offline standalone package design |
| draft-debug-panel-20260512.md | Draft | Runtime debugger panel |
| plan-playmode-draft.md | Draft | PlayMode 三模式设计 |

## Discussion Order

| Order | Draft | Why First / Later |
|-------|-------|-------------------|
| 1 | `draft-ab-cumulative-hotfix-delivery-20260604.md` | Highest current package-shape decision. It affects AB manifest shape, hotfix delivery size, and runtime download semantics. |
| 2 | `draft-address-generation-conflict-policy-20260604.md` | Active collector behavior already defaults to `Filename_Type`; this needs a focused design before code changes to avoid another address-semantic drift. |
| 3 | `draft-build-repository-followup-20260524.md` | Residual repository hardening only. Keep it separate from hotfix delivery semantics. |
| 4 | `draft-offline-standalone-package-20260513.md` | Depends on clear build output ownership and runtime path rules. It should be discussed after hotfix package shape so offline output and hotfix output do not create conflicting truths. |
| 5 | `plan-playmode-draft.md` | Depends on Collector, manifest generation, and runtime backend boundaries. It should follow build-output discussions because Simulate/Runtime modes reuse those contracts. |
| 6 | `draft-lua-index-pipeline-independence-20260520.md` | Narrow open decision. Discuss after PlayMode because address-source and index-generation boundaries are easier to decide once backend/play-mode behavior is fixed. |
| 7 | `draft-debug-panel-20260512.md` | Mostly observational tooling. It is valuable but should follow the core runtime/build contracts so the panel exposes stable concepts instead of chasing moving boundaries. |

## Archive Criteria

A draft moves to `archive/` when:
- Promoted to a formal plan (trace retained, draft archived)
- Superseded by another draft or plan
- Explicitly deprecated or abandoned
