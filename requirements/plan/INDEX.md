# Plan Queue

Shared executable plans live here while active. `requirements/plan.md` remains the authoritative status table.

## Rules
- Keep only active shared plans in this directory.
- Move executed, signed-off, superseded, cancelled, or deprecated plans to `archive/`.
- Keep non-executable ideas in `drafts/`.
- Do not create per-requirement `plan.md` files or `plan/` folders unless the developer explicitly requests it.
- When deleting standalone requirement folders, first merge their detailed `progress.txt` entries into
  `requirements/progress.txt`; do not replace detailed history with summary-only lines.

## Active

No active implementation plans. Pending acceptance and the current audit/history-cleanup round remain tracked in `requirements/plan.md`.

## Recently Archived
| File | Status |
|---|---|
| `2026-09-04-pipeline-custom-tasks.md` | Executed; archived 2026-09-05 with AA/AB build acceptance carried to ACCEPT-01 |
| `2026-09-04-backend-selection-decoupling.md` | Executed; archived 2026-09-05 with Editor runtime acceptance carried to ACCEPT-02 |
| `plan-playmode-editor-20260724.md` | Signed off 2026-07-28; archived 2026-09-04 |
| `2026-09-03-aa-ab-decoupling.md` | Completed & archived (renamed from misdated `2026-07-24-aa-ab-decoupling`) |
| `2026-09-03-xluaframework-export.md` | Completed & archived (X1-X6; renamed from misdated `2026-07-24-xluaframework-export`) |
| `plan-build-repo-diff-module-20260523.md` | Archived |
| `plan-build-repository-core-20260523.md` | Archived |
| `plan-build-repository-release-20260523.md` | Archived |
| `plan-comment-debug-coverage-20260524.md` | Archived |
| `plan-hotfix-diff-task-20260524.md` | Archived |
| `plan-collector-asset-metadata-bundle-packing-20260531.md` | Signed off and archived |
| `plan-dag-staged-write-order-fix-20260601.md` | Signed off and archived |
| `plan-review-hardening-20260602.md` | Signed off and archived |
| `plan-build-repository-aa-push-20260603.md` | Signed off and archived |
| `plan-address-generation-conflict-policy-20260604.md` | Signed off and archived |
| `plan-assets-collection-settings-cleanup-20260605.md` | Signed off and archived |
| `plan-assets-collection-followup-20260605.md` | Signed off and archived |
| `plan-ab-cumulative-hotfix-delivery-20260605.md` | AI verified, signed off, and archived |
| `plan-review-build-chain-blockers-20260607.md` | Signed off and archived |
| `plan-ab-build-report-panel-20260606.md` | Signed off and archived |
| `plan-repository-git-style-diff-20260606.md` | Signed off and archived |
| `plan-fyasset-bundle-identity-rawfile-root-fix-20260611.md` | Signed off and archived |
| `plan-hotfix-url-publish-20260712.md` | Local and Cloudflare verified, signed off, and archived |
| `plan-hotfix-runtime-state-machine-20260712.md` | Superseded and archived |
| `plan-hotfix-major-baseline-pointer-20260713.md` | Superseded and archived |
| `plan-build-state-cleanup-tools-20260707.md` | Executed; pending acceptance consolidated to one draft |
| `plan-hotfix-progress-steps-20260709.md` | Executed, verified, and archived |
| `plan-linear-build-pipeline-runner-20260709.md` | Executed, verified, and archived |
| `plan-pipeline-sequence-list-editor-20260709.md` | Executed, verified, and archived |
| `plan-repository-slim-20260709.md` | Executed, verified, and archived |
| `plan-aa-ab-shared-split-20260709.md` | Executed; pending Editor acceptance consolidated to one draft |
| `plan-build-panel-task-slim-20260711.md` | Executed; pending Editor acceptance consolidated to one draft |
| `plan-hotfix-windows-state-machine-20260713.md` | Executed; post-review findings remain in the current review |
| `plan-lua-resource-boundary-separation-20260719.md` | Signed off and archived |
| `plan-build-test-pipeline-20260721.md` | Completed, verified, and archived; 12/12 local matrix |
| `plan-e2e-test-pipeline-20260722.md` | Completed, verified, and archived; 12/12 local matrix |
| `plan-standalone-offline-20260724.md` | Completed, verified, and archived; AB Standalone E2E passed |
| `plan-hotfix-review-hardening-20260714.md` | Implemented/verified; archived during 2026-07-22 workspace cleanup |
| `plan-fyasset-full-audit-localization-slim-20260714.md` | Implemented/verified; archived during 2026-07-22 workspace cleanup |
| `plan-ab-e2e-local-cloudflare-20260715.md` | Delivery verified; superseded by automated Build/E2E test plans and archived |
