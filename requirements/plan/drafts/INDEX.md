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
| draft-legacy-plan-review-followups-20260714.md | Draft / Not approved | Consolidated manual acceptance, repository failure-path checks, and still-valid ponytail deletion candidates from archived plans/reviews |
| draft-debug-panel-20260512.md | Draft | Runtime debugger panel |
| plan-playmode-draft.md | Draft / Partially promoted | Editor mode promoted; Simulate mode remains deferred |
| draft-fyasset-architecture-review-20260707.md | Draft / Partially promoted | FYAsset architecture optimization — A10/A2/A4/A1+A3+A5+A9 extracted, executed, and archived; A0/A6/A8 remain deferred |

## Discussion Order

| Order | Draft | Why First / Later |
|-------|-------|-------------------|
| 1 | `draft-legacy-plan-review-followups-20260714.md` | Single queue for old acceptance and deletion candidates; resolve or explicitly defer before promoting unrelated work. |
| 2 | `plan-playmode-draft.md` | Editor mode is promoted; only the deferred Simulate design remains for later discussion. |
| 3 | `draft-debug-panel-20260512.md` | Mostly observational tooling; follow the core runtime/build contracts. |
| 4 | `draft-fyasset-architecture-review-20260707.md` | A0/A6/A8 remain real deferred architecture choices; promote only after a concrete trigger. |

## Archive Criteria

A draft moves to `archive/` when:
- Promoted to a formal plan (trace retained, draft archived)
- Superseded by another draft or plan
- Explicitly deprecated or abandoned

## Recently Archived Drafts

| File | Reason |
|------|--------|
| draft-lua-index-pipeline-independence-20260520.md | Promoted into `../plan-lua-resource-boundary-separation-20260719.md` after the verified AB startup P0 and ownership decisions |
| draft-build-repository-followup-20260524.md | HEAD orphan cleanup and PushHistory removal are implemented; serializer/locking ideas have no observed trigger |
| draft-docs-synchronization-20260707.md | Superseded by the completed 2026-07-13 project/plan/context alignment pass |
| draft-broken-pptr-scene-validation-20260707.md | Dangling scene reference was removed; draft already marked resolved |
| draft-hotfix-publish-integrity-20260712.md | Local validation moved to the active hardening plan; deployed AA bytes/cache policy were audited |
| draft-version-system-test-features-20260707.md | Promoted into `../archive/plan-build-state-cleanup-tools-20260707.md` and implemented |
| draft-buildresults-management-panel-20260707.md | Promoted into `../archive/plan-build-state-cleanup-tools-20260707.md` and implemented with the first-pass package deletion scope |
| draft-repository-reset-20260707.md | Promoted into `../archive/plan-build-state-cleanup-tools-20260707.md` and implemented as channel-scoped test reset |
| draft-offline-standalone-package-20260513.md | Promoted into `../archive/plan-standalone-offline-20260724.md`, verified, and archived |
