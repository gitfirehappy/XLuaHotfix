# Sub-Plan HPU-1: Hotfix Pipeline Robustness And Output Alignment

> **Risk**: Medium
> **Dependencies**: Existing `IHotfixPipeline`, `HotfixManager`, AA/AB hotfix backends, `SerializationUtility`, manifest binary serializers
> **Status**: Executed — 2026-05-20, awaiting developer sign-off
> **Source Draft**: `drafts/archive/draft-hotfix-pipeline-unification-20260519.md`
> **Positioning**: Runtime hotfix robustness and build-output consistency. AA build UI and AA DAG alignment are explicitly deferred.

---

## Objective

Improve the AA/AB hotfix pipeline in three concrete areas:

1. Make bundle download/apply safer under network interruption and CRC failure.
2. Align AA/AB manifest output so both pipelines produce JSON and binary manifest files by default.
3. Reduce duplicate error logging by making outer orchestrators the final logging boundary.

The plan intentionally does not extract a generic manifest helper. The current AA/AB backend differences are small and clearer when kept local.

---

## Background

Current verified state:

| Area | Current behavior | Gap |
|------|------------------|-----|
| Network download | Any failed bundle download fails the whole hotfix flow immediately | No retry under weak network |
| CRC mismatch | Failed CRC deletes the file but is not retried | Corrupt download is not recovered |
| Partial downloads | Bundle bytes can be written directly to the target path | Interrupted downloads can leave corrupt files |
| `FileCRC == 0` | Verification is skipped | No observability for skipped verification |
| Manifest output | AA/AB binary support exists but build output is not fully aligned | Inconsistent release artifacts |
| Logging | Lower layers and outer layers can both log the same failure as `Error` | Console noise and unclear ownership |

---

## Design Decisions

### D1: Do Not Extract Manifest Loading Helper

`AAHotfixBackend` and `ABHotfixBackend` keep their own manifest loading and conversion logic.

Reason:

- `SerializationUtility.ReadFromFile<T>()` already handles binary/json detection.
- AB needs `manifest.Initialize()` while AA does not.
- Shared helper would require callbacks/generic constraints for very small code reduction.

### D2: CRC `0` Means Verification Metadata Unavailable

`FileCRC == 0` follows Unity convention: skip CRC verification.

However, skipping verification must emit a warning so the build/runtime logs show that integrity verification was unavailable.

### D3: Retry Download And CRC Failure Through One Policy

A CRC mismatch is treated like a failed download attempt.

Default retry policy:

- max attempts: 3
- backoff: `1s`, `2s`, `4s`

### D4: Bundle Writes Use Temp File Then Replace

Downloaded bundles are written to a `.tmp` path first. Only after successful download and verification should the temp file replace the target bundle file.

Startup should clear stale `.tmp` files in the target bundle root.

### D5: JSON + Binary Manifest Is The Default Build Output

AA and AB builds should produce both JSON and binary manifest files by default.

`JsonAndBinary` is the default release-safe mode. `BinaryOnly` must not become the default formal release mode in this plan.

### D6: Outer Orchestrator Owns Final Error Logging

Build-side final failure summaries belong to `BuildProjectManager`.

Hotfix-side final failure summaries belong to `HotfixManager`.

Intermediate layers should return structured results or throw controlled exceptions for the outer layer to summarize. Low-level warnings may remain when they add distinct diagnostic value.

---

## Planned Changes

| Area | File / Module | Change |
|------|---------------|--------|
| Retry settings | `FYAssetSettings` | Add hotfix retry settings: max retry count and base delay |
| Download flow | `HotfixManager` / download orchestration | Retry failed downloads and CRC mismatches under one policy |
| Temp file safety | Hotfix download/apply flow | Write bundles to `.tmp`, verify, then replace target file |
| Startup cleanup | Hotfix bundle root handling | Remove stale `.tmp` files before download/apply starts |
| CRC observability | CRC verification helper | Log warning when `FileCRC == 0` skips verification |
| Manifest output | AA/AB build backends or manifest output tasks | Ensure both JSON and binary manifests are emitted by default |
| Output format config | `FYAssetSettings` / build UI | Add a shared output format setting with `JsonAndBinary` default |
| Logging boundary | `ABBuildBackend`, `AAHotfixBackend`, `ABHotfixBackend`, `HotfixPackageSizeGuard` | Avoid duplicate low-layer `Error` logging where outer summary already reports failure |

---

## Task Breakdown

| Task | Content | Depends On |
|------|---------|------------|
| HPU1-T1 | Add hotfix retry configuration to `FYAssetSettings` with safe defaults | — |
| HPU1-T2 | Add stale `.tmp` cleanup before bundle download/apply | — |
| HPU1-T3 | Change bundle download writes to temp-file-then-replace | T2 |
| HPU1-T4 | Implement retry loop for download failure and CRC failure | T1, T3 |
| HPU1-T5 | Add warning when `FileCRC == 0` causes verification skip | Existing CRC verification helper |
| HPU1-T6 | Add/align manifest output format setting and default `JsonAndBinary` | Existing manifest serializers |
| HPU1-T7 | Ensure AA and AB build outputs both write JSON and binary manifests by default | T6 |
| HPU1-T8 | Reduce duplicate low-layer error logging and keep outer summary logging | T4-T7 |
| HPU1-T9 | Verification: source audit, build compile, and manual failure-path checks | T1-T8 |

---

## Invariants

1. Runtime manifest loading order remains binary first, JSON fallback.
2. Download decisions continue to use `FileHash`, not `FileCRC`.
3. `FileCRC == 0` remains a non-fatal skip-verification state.
4. CRC mismatch must not silently accept a corrupt bundle.
5. A target bundle file must not be replaced until the new temp file has passed download and verification requirements.
6. AA and AB hotfix backend manifest models remain separate.
7. This plan must not introduce a generic manifest helper abstraction.
8. This plan must not implement AA build UI or AA DAG alignment.

---

## Acceptance Criteria

- [ ] Failed bundle download retries according to the configured retry policy.
- [ ] CRC mismatch retries through the same retry policy.
- [ ] Exhausted retries produce a clear final hotfix failure.
- [ ] Interrupted downloads leave only `.tmp` files, and startup cleanup removes stale `.tmp` files.
- [ ] `FileCRC == 0` logs a warning and skips verification.
- [ ] AA build output includes both `AAManifest.json` and `AAManifest.bin` by default.
- [ ] AB build output includes both `ABManifest.json` and `ABManifest.bin` by default.
- [ ] Low-level duplicate `Error` logs are removed or downgraded where the outer orchestrator already reports the failure.
- [ ] `dotnet build XLuaHotfix.sln` or Unity Editor compilation passes with 0 new errors.

---

## Out of Scope

- AA build UI.
- AA build DAG / Task graph alignment.
- Addressables internal build-flow replacement.
- Generic manifest loading helper extraction.
- CDN upload/push workflow.
- Build Repository integration.
- Reworking progress reporting beyond low-risk consistency fixes.

---

## Approval Checklist

- [x] Keep AA/AB manifest loading logic separate; do not add a generic manifest helper.
- [x] Treat CRC failure as retryable download failure.
- [x] Use `.tmp` download files and replace target only after verification.
- [x] Default manifest output format is JSON + binary.
- [x] Do not make `BinaryOnly` the formal release default.
- [x] Keep AA build UI and AA DAG alignment out of this plan.
- [x] Let outer orchestrators own final error summaries.

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-19 | Promoted from draft into executable plan with conservative defaults for open questions |
| 2026-05-20 | Approved by developer; all checklist items confirmed |
| 2026-05-20 | Executed; implemented bundle retry/tmp safety, CRC observability, default JSON+binary output, and logging-boundary cleanup |
