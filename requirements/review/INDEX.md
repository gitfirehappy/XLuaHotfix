# Review Directory

Review artifacts for the requirement. Active reviews (findings still under discussion or not yet fully addressed) stay here.

Archived reviews (findings addressed, historical reference) go to `archive/`.

## Report Format Rule

Every review report **MUST** include the following metadata header:

```markdown
# <Title>

> **Date**: YYYY-MM-DD
> **Reviewer**: <name or tool>
> **Scope**: <what was reviewed>
> **Method**: <static analysis, diff review, perf profiling, etc.>
```

Reports missing Date or Reviewer will be rejected before content review.

## Active Reviews

| File | Status | Scope |
|------|--------|-------|
| [review-xluaframework-fyasset-20260905.md](review-xluaframework-fyasset-20260905.md) | Open: 39 findings (10 P1, 28 P2, 1 P3); 15 candidates retain separate validation status | Current-tree runtime/lifecycle, build/publication, editor state, exports, tests, docs and decision drift; includes fresh commands and RED probes |

Supporting evidence: the exact 66-commit / 737-path inventory is retained only in ignored `Logs/history-cleanup-20260905/history-inventory.json`; the review queue contains reports and indexes, not machine-generated temporary inventories. It is an inventory, not a rewrite authorization or evidence of completed history cleanup.

## Recently Archived

| File | Status | Scope |
|------|--------|-------|
| `review-hotfix-review-hardening-20260714.md` | Archived during cleanup | Hotfix hardening implementation, draft audit, and requirements alignment |
| `review-fyasset-full-audit-localization-slim-20260714.md` | Archived during cleanup | Full FYAsset complexity, localization, and dead-code audit |
| `review-ab-e2e-local-cloudflare-20260715.md` | Archived during cleanup; superseded by automated Build/E2E plans | Real AB Full/Hotfix Local and Cloudflare E2E |
| `review-project-plan-context-alignment-20260713.md` | Superseded; three P1 findings resolved and P2 transferred | Project/plan/context alignment and original Hotfix findings |
| `review-collector-20260521.md` | P0/P1 remediated; remaining concrete P2 fixed or accepted/deferred | Collector subsystem and direct build-pipeline consumers |
| `review-fyasset-ponytail-audit-20260621.md` | Processed; valid deletion candidates consolidated to one draft | FYAsset over-engineering and simplification audit |
| `review-fyasset-repository-flow-20260705.md` | Transaction findings fixed; residual acceptance consolidated | FYAsset repository commit, publication, push, and status UI flow |
| `review-fyasset-mistake-decision-alignment-20260608.md` | Findings addressed or superseded; root-cause plan signed off | FYAsset mistake recurrence and decision-alignment review |
| `review-build-chain-blockers-20260606.md` | Remediated, signed off, and archived | AA/AB staging diff, official builds, repository commit, and push chain |
| `review-staging-diff-collections-20260606.md` | Remediated, signed off, and archived | AA/AB Repository staging diff compile failure |
| `review-build-e2e-cli-batch-20260722.md` | Completed and archived; superseded by reset-isolation matrix | CLI matrix classification + post-fix acceptance for Build/E2E pipelines |
| `review-build-e2e-reset-isolation-20260723.md` | Completed and archived; 12/12 PASS | Clean-slate reset isolation and retained-Player acceptance |

## Archive Criteria

A review moves to `archive/` when:
- All findings have been addressed (fixes applied and verified)
- The review is explicitly marked as historical/archived
- The reviewed code has been superseded or removed
