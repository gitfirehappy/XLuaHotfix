# Collector Review Fix Plan

> **Date**: 2026-05-22
> **Status**: Approved for execution
> **Source Review**: `requirements/review/review-collector-20260521.md`

## Summary

Fix Collector review `P0 + P1` issues first: implicit dependency package/reference accounting, manual `Implicit` collector misuse, scanner exception containment, build-time validation, and diagnostic error-code semantics.

`P2` items are intentionally deferred: broader Validator/Scanner abstraction, `MinAssetSizeBytes` unknown-size policy, raw file I/O cleanup, and full automated test matrix.

## Implementation

- Fix `DependencyAnalyzer` so implicit dependencies preserve source `PackageName`, copied implicit entries do not use empty `GroupName`, and reference counting is independent from dependency-subtree expansion caching.
- Treat `ECollectorType.Implicit` as system-generated only: keep the enum value for generated intermediate data, but reject manual Collector configuration and hide manual UI selection.
- Make Collector scanning fail through structured `BuildMessage` errors instead of raw exceptions for invalid collector type and rule execution failures.
- Run `CollectorSettingValidator` in `TaskCollectAssets` before scanning so invalid config such as empty `PackageName` cannot be silently skipped.
- Centralize P1 diagnostic codes in `BuildErrorCodes`; stop reusing `RULE_NOT_FOUND` for empty rule names, invalid bundle segments, labels, rule execution failures, cycles, and share-policy conflicts.

## Verification

- Static review with targeted `rg` checks:
  - no `PackageName = string.Empty // 由调用方填充`
  - no raw `CYCLE_DEPENDENCY` / `SHAREPOLICY_CONFLICT` diagnostics in implementation
  - manual Collector UI no longer exposes full `ECollectorType` enum
  - scanner has structured guards around rule execution/classification
- Run available compile/static checks if present in the local Unity workspace.
- If Unity compilation cannot be run from this environment, record that explicitly in the final report.

## Approval Checklist

- [x] Scope: fix review `P0 + P1` only; defer `P2`.
- [x] `Implicit`: system-generated only; manual Collector config is invalid.
- [x] Testing: no full test matrix; use static review and available compile checks.
- [x] Build behavior: allowed to correct implicit dependency package/reference accounting.

## Progress Log

- [start] 2026-05-22 Plan approved by developer; execution started.
- [done] 2026-05-22 Added centralized P1 build error codes and routed Collector validation/scanner/dependency diagnostics through them.
- [done] 2026-05-22 Fixed dependency analysis to preserve implicit dependency PackageName and to record referencing bundles independently from dependency query caching.
- [done] 2026-05-22 Blocked manual Implicit Collector configuration through validator/scanner and replaced manual UI enum fields with Main/Static/Depend-only popups.
- [done] 2026-05-22 TaskCollectAssets now runs CollectorSettingValidator before scan execution, preventing invalid packages from being silently skipped.
- [done] 2026-05-22 Static verification completed: targeted grep checks found no stale raw dependency error codes, no empty implicit PackageName assignment, and no generic CollectorType enum UI field.
- [blocked] 2026-05-22 Unity compile was not run from this shell because the workspace exposes no `.sln`, `.csproj`, or `.asmdef` compile entry; verification is static review only per approved plan.
