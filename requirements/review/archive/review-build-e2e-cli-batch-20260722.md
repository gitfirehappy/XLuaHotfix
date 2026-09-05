# Build/E2E CLI Batch Acceptance Review

> **Date**: 2026-07-22  
> **Reviewer**: Claude Code (CLI batch + iterative test-pipeline fixes)  
> **Scope**: `plan-build-test-pipeline-20260721` + `plan-e2e-test-pipeline-20260722` CLI matrix against `--target local`  
> **Method**: Serial `fyasset-test` runs; classify failures as test-pipeline vs framework; fix only test-pipeline issues; re-run  
> **Evidence**: `HotfixOutput/TestRuns/cli-batch/`, `cli-batch-2/`, `cli-batch-3/`, and latest per-mode `result.json`
> **Status**: Completed / Superseded by reset-isolation acceptance / Archived — 2026-07-24

## Classification Verdict

| Failure | Class | Action taken |
|---|---|---|
| AA `LUA_INDEX_INVALID` / missing `FYAssetPipelineLua` | **Test pipeline** | Fixture AA labels put type label first (`LuaScriptContainer` / smoke type) |
| AA acceptance `Declared artifact missing: <guid>` | **Test pipeline** | AA artifacts are GUIDs, not package paths; validate bundles/catalog instead |
| AA permanent address missing in manifest text scan | **Test pipeline** | Deserialize AAManifest and check `PackageEntry.key` |
| AA Hotfix delta missing fixture name | **Test pipeline** | Map AA GUID artifacts to asset paths when matching fixture |
| AA Hotfix blocked by pending group moves | **Test pipeline** | Snapshot/restore `HotfixGroupUndoLog.json`; clear moves in isolated Full prep |
| AB Chain `Declared artifact missing: <bundleName>` | **Test pipeline** | Hotfix packages only ship delivery delta; validate CommitDelta files, not full artifact list |
| Wrong BuildData snapshot path under `HotfixOutput` | **Test pipeline** | Snapshot/restore project-root `BuildData` |
| Local server port 18080 vs PublicBaseUrl 54321 | **Test pipeline** | E2E sets server port from Target PublicBaseUrl |
| Player timeout with `-nographics` / profiler connect | **Test pipeline** | Drop forced nographics + ConnectWithProfiler; longer timeout |
| Standalone Hotfix after always-restore Full | **Contract / ops** | Expected: no durable Full baseline remains; use Chain for Full→Hotfix proof |
| GameLauncher/runtime hang (initial) | **Mixed** | Reduced by player launch args; remaining runtime risks are framework-side if reappear |

**No pure framework production-path defect required code changes for the current local matrix.** Remaining standalone Hotfix exit 7 is contractual always-restore behavior.

## Final Local Matrix (after test-pipeline fixes)

| Command | Exit | Result |
|---|---:|---|
| `aa build full --target local` | 0 | PASS |
| `aa build chain --target local` | 0 | PASS |
| `aa build hotfix --target local` | 7 | Expected fail without pre-existing Full baseline after restore |
| `ab build full --target local` | 0 | PASS |
| `ab build chain --target local` | 0 | PASS |
| `ab build hotfix --target local` | 7 | Expected fail without pre-existing Full baseline after restore |
| `aa e2e full --target local` | 0 | PASS |
| `aa e2e chain --target local` | 0 | PASS |
| `aa e2e hotfix --target local` | 7 | Expected fail without pre-existing Full baseline |
| `ab e2e full --target local` | 0 | PASS |
| `ab e2e chain --target local` | 0 | PASS |
| `ab e2e hotfix --target local` | 7 | Expected fail without pre-existing Full baseline |

Focused Full / Chain / E2E Full+Chain paths for both backends are green on local Target.

## Initial Batch (pre-fix) Snapshot

Only `ab build full` passed. Root causes were all test-pipeline isolation/acceptance/fixture issues listed above. Details remain in `HotfixOutput/TestRuns/cli-batch/detailed_summary.json`.

## Test-pipeline Fixes Landed

1. `BuildTestFixtures` — AA/AB type label ordering  
2. `BuildTestState` — correct `BuildData` path; AA undo-log snapshot/restore; AA move cleanup  
3. `BuildTestAcceptance` — AA GUID artifact handling; AB Hotfix delivery-only physical checks; AAManifest key validation; AA GUID fixture delta matching  
4. `BuildTestEngine` — accurate stage tracking  
5. `E2ETestEngine` — port alignment, safer Player launch args, better exit classification  

## Residual Notes (not blockers for local green matrix)

1. **Standalone Hotfix** requires an intentionally retained Full baseline (outside always-restore transaction). Chain is the automated Full→Hotfix proof.  
2. **E2E Hotfix/Chain continuity** currently reuses Build engine composition for some modes; Full E2E Player path is exercised and green for AA/AB local. Deeper retained-Player forward-update fidelity can still be hardened later.  
3. **Cloudflare / multi-target** not re-run in this batch.  
4. Fixture files must remain at Full markers after restore; temporary v2 residue was observed when restore missed fixture ownership and was reset.

## Recommendation

- Treat Build local Full/Chain and E2E local Full/Chain as acceptance-ready for the automated pipeline landing.  
- Document standalone Hotfix precondition: existing Full HEAD + package + Target Full identity.  
- Optional follow-up: dedicated “seed Full without restore” helper only for standalone Hotfix diagnostics.
