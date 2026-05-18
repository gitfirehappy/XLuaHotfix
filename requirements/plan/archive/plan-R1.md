# Sub-Plan R1: Unified Error Handling Architecture

> **Risk**: Medium (renames across ~15 files, but types are structurally identical — mechanical refactor)
> **Dependencies**: None (cross-cutting infrastructure)
> **Status**: Realized — 2026-05-06, BuildMessage/RuntimeMessage landed, ScanMessage/AssetLoadError retired

---

## Objective

Unify project error handling into two aligned architectures (build-time + runtime), sharing the same philosophy: Severity × Code × Message × Factory methods. Eliminate the 3-system fragmentation identified in code review (2026-04-27).

---

## Architecture

```
Build-time (Editor)                      Runtime
─────────────────────                    ────────────────
BuildSeverity { Warning, Error }         RuntimeSeverity { Warning, Error }
BuildMessage {                           RuntimeMessage {
    Severity : BuildSeverity                 Severity : RuntimeSeverity
    Code     : string                        Code     : string
    Message  : string                        Message  : string
    Source   : string                        (no Source needed at runtime)
}                                        }
Factory:                                 Factory:
  Error(code, msg, source)                 Error(code, msg)
  Warning(code, msg, source)               Warning(code, msg)
```

**Why string Code on both sides**: No code switches on runtime error codes (verified 2026-04-27). String allows modules to define their own codes without touching a central enum — same extensibility benefit for both build-time and runtime.

**Why Warning on runtime**: Future consumers (fallback-path loading, retry-recovery, degraded type matching) need a "succeeded but not ideal" signal. Adding Severity now costs nothing — it's a single field, zero current consumers are affected.

**Why separate types**: BuildMessage has `Source` (path/task-name for Editor diagnostics); RuntimeMessage doesn't. They live in different assemblies. Forcing them into a shared base class would be over-engineering.

---

## Task Breakdown

### Build-Time

| Task | Content | Files |
|------|---------|-------|
| R1-B1 | Create `BuildMessage.cs` — rename `ScanMessage`→`BuildMessage`, `ScanSeverity`→`BuildSeverity`, `CollectorPath`→`Source`. Add `Error()`/`Warning()` static factories | 1 new |
| R1-B2 | Update `ScanResult.cs` — change `ScanMessage`→`BuildMessage`, `ScanSeverity`→`BuildSeverity`, use factories in `HasErrors` | 1 modified |
| R1-B3 | Update `CollectionScanner.cs` — replace `new ScanMessage { Severity=..., Code=..., ... }` with `BuildMessage.Error(...)` / `BuildMessage.Warning(...)` factory calls | 1 modified |
| R1-B4 | Compilation verification | — |

### Runtime

| Task | Content | Files |
|------|---------|-------|
| R1-R1 | Create `RuntimeSeverity` enum + `RuntimeMessage` class (replace `AssetLoadError`). Code enum→string, add Severity field, add Warning() factory. Keep existing factory methods, change to Error severity | 1 new + 1 deleted |
| R1-R2 | Update `ResolveResult.cs` — `AssetLoadError`→`RuntimeMessage`, update factory calls | 1 modified |
| R1-R3 | Update `AssetHandle.cs` — `AssetLoadError GetError()`→`RuntimeMessage GetError()` | 1 modified |
| R1-R4 | Update `HandleRegistry.cs` — error references | 1 modified |
| R1-R5 | Update `ABBundleLoader.cs` — factory calls `AssetLoadError.Xxx()`→`RuntimeMessage.Error(...)` | 1 modified |
| R1-R6 | Update `ABPackageBackend.cs` — factory calls | 1 modified |
| R1-R7 | Update `AssetPackageManager.cs` — error references | 1 modified |
| R1-R8 | Compilation verification | — |

---

## PATH_NOT_FOUND Decision

Per unified severity rules:
- **Error** = result is incorrect/incomplete, continuing would produce wrong output
- **Warning** = anomaly detected but unaffected assets/configs can still produce correct output

`PATH_NOT_FOUND`: A missing directory means this Collector collects zero assets. Other Collectors in the same Package may be fine. The Package result would be missing those assets but not corrupted. → **Warning** (consistent with plan-E1-3.md original spec).

If you prefer strict CI behavior, a future E5 build-pipeline-level switch can upgrade Warnings to Errors in CI mode — but the scan engine itself should use Warning as the default severity.

---

## New Files

| File | Path | Assembly | Description |
|------|------|----------|-------------|
| BuildMessage.cs | Build/Editor/ | Editor | BuildSeverity + BuildMessage + factories + BuildErrorCodes |
| RuntimeMessage.cs | Runtime/Models/ | Runtime | RuntimeSeverity + RuntimeMessage + factories + RuntimeErrorCodes |

All paths relative to `Assets/FYAsset/Scripts/`.

---

## Modified / Deleted Files

| File | Change |
|------|--------|
| ScanResult.cs | ScanMessage→BuildMessage, ScanSeverity→BuildSeverity, CollectorPath→Source |
| CollectionScanner.cs | All ScanMessage constructions → BuildMessage factories |
| AssetLoadError.cs | **Deleted** — replaced by RuntimeMessage.cs |
| ResolveResult.cs | AssetLoadError→RuntimeMessage |
| AssetHandle.cs | GetError() return type |
| HandleRegistry.cs | Error references |
| ABBundleLoader.cs | Factory calls |
| ABPackageBackend.cs | Factory calls |
| AssetPackageManager.cs | Error references |

---

## Invariants

1. BuildMessage and RuntimeMessage share the same conceptual structure but zero shared code (different assemblies)
2. All error/warning construction goes through factory methods — no bare `new BuildMessage { ... }`
3. String Code on both sides — extensible without central enum
4. Severity on both sides — Error blocks, Warning reports
5. No existing runtime behavior changes — Severity is additive, current Error paths stay Error
6. `dotnet build XLuaHotfix.sln` passes with 0 errors

---

## Not In Scope

- Old build pipeline migration to BuildMessage (System 2) — deferred, each module migrates when touched
- Cross-assembly shared error base class — intentional separation
- Error code registry / documentation — ad-hoc strings, same as current ScanMessage.Code pattern
- Runtime Warning consumers — infrastructure only, no new call sites in this plan

---

## Approval Checklist

- [x] Agree to BuildMessage / RuntimeMessage as separate types (build-time has Source, runtime doesn't)
- [x] Agree to string Code on both sides, with `BuildErrorCodes.cs` / `RuntimeErrorCodes.cs` const files for centralized definition
- [x] Agree to Warning severity on both sides (runtime currently has zero Warning callers — infrastructure only)
- [x] Agree to PATH_NOT_FOUND = Warning (per plan spec, re-evaluable in CI mode later)
- [x] Agree to AssetLoadError → RuntimeMessage rename
- [x] Agree to ScanMessage → BuildMessage rename
- [x] Agree to factory methods as the only construction path (no bare new)
- [x] Agree to old build pipeline migration deferred ("触及即迁移")
