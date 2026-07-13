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
| draft-docs-synchronization-20260707.md | Draft | Documentation synchronization audit and update proposal |
| draft-fyasset-architecture-review-20260707.md | Draft / Partially promoted | FYAsset architecture optimization — A10/A2/A4/A1+A3+A5+A9 extracted, implemented, and verified in active plans 2026-07-09; A0/A6/A8 remain deferred |
| draft-broken-pptr-scene-validation-20260707.md | Draft | Scene broken reference detection and cleanup (3 solutions) |
| draft-hotfix-publish-integrity-20260712.md | Draft | Strong package validation and post-deploy immutable-content auditing |

## Discussion Order

| Order | Draft | Why First / Later |
|-------|-------|-------------------|
| 1 | `draft-build-repository-followup-20260524.md` | Residual repository hardening only. Keep it separate from hotfix delivery semantics. |
| 2 | `draft-offline-standalone-package-20260513.md` | Depends on clear build output ownership and runtime path rules. It should be discussed after hotfix package shape so offline output and hotfix output do not create conflicting truths. |
| 3 | `plan-playmode-draft.md` | Depends on Collector, manifest generation, and runtime backend boundaries. It should follow build-output discussions because Simulate/Runtime modes reuse those contracts. |
| 4 | `draft-lua-index-pipeline-independence-20260520.md` | Narrow open decision. Discuss after PlayMode because address-source and index-generation boundaries are easier to decide once backend/play-mode behavior is fixed. |
| 5 | `draft-debug-panel-20260512.md` | Mostly observational tooling. It is valuable but should follow the core runtime/build contracts so the panel exposes stable concepts instead of chasing moving boundaries. |

## Archive Criteria

A draft moves to `archive/` when:
- Promoted to a formal plan (trace retained, draft archived)
- Superseded by another draft or plan
- Explicitly deprecated or abandoned

## Recently Archived Drafts

| File | Reason |
|------|--------|
| draft-version-system-test-features-20260707.md | Promoted into `plan-build-state-cleanup-tools-20260707.md` and implemented |
| draft-buildresults-management-panel-20260707.md | Promoted into `plan-build-state-cleanup-tools-20260707.md` and implemented with the first-pass package deletion scope |
| draft-repository-reset-20260707.md | Promoted into `plan-build-state-cleanup-tools-20260707.md` and implemented as channel-scoped test reset |
