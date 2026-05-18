# Sub-Plan E7: Diff Snapshot Adaptation

> **Status**: ❌ DEPRECATED — 2026-05-17 归档至 drafts。8 设计问题 + 2 combined review findings 已吸收至 `draft-E7-diff-snapshot-20260517.md`（E13 独立，已执行 plan-E13-legacy-sidebar.md）
> **Superseded by**: `plan/drafts/draft-E7-diff-snapshot-20260517.md`
> **Archived reviews**: `review/archive/review-E7-E13-plan-20260515.md`
> **保留原因**: 原始设计讨论历史 + mistake absorption 追溯
>
> ## 2026-05-15 Review 结论：打回重写
>
> 8 个设计问题需要重新讨论后重写：
> 1. **DiffResult 多态不自洽** — 接口返回 DiffResult 但两个 backend 产出不同类型 delta，ApplyDiff 方法存在意义存疑
> 2. **BundleDigest/BundleDigestList 错误放 Runtime assembly** — 当前只有构建期用，应放 Editor
> 3. **文件路径错误** — Diff 是 Pipeline 关注点，不应放 Collector 目录
> 4. **Hash 算法未对齐** — 指定 MD5 但项目已有 HashGenerator + HashAlgorithmType 统一基础设施
> 5. **D5d 多渠道参数过度设计** — 为未实现功能污染接口签名
> 6. **MaxHistoryVersions 归属不当** — 混入 BuildPipelineConfig（执行配置）不合适
> 7. **E7-T9 可选 ReadKeys** — DAGScheduler 是否支持可选读取未确认
> 8. **E7-T11 与 E12-2 边界模糊** — CLI vs Editor 入口未澄清
>
> 以下原始设计保留作为参考，重写时需逐一解决上述问题。

---

## Objective

Migrate the existing `DifferentialProcessor` (fully coupled to Addressables asset groups) to an Interface-Backend separated architecture matching `IHotfixPipeline` and `AssetPackageManager` patterns.

- **LegacyDiffBackend**: wraps existing `DifferentialProcessor` logic (Asset-level diff, BuildSnapshots SO)
- **ABDiffBackend**: new implementation operating on Bundle-level digest (BundleDigestList, .bin/.json via SerializationUtility)

Diff is a **build-time-only** concern. Runtime does NOT consume diff results — runtime hotfix compares full ABManifest (local vs remote), skips matching bundles, downloads the rest. There is no runtime differential download.

---

## Architecture Principle: Interface-Backend Separation

Matching `IHotfixPipeline` / `AssetPackageManager` / `IPackageBackend` patterns already established in the codebase:

```
IDiffPipeline (interface, 5 methods)
  ├── LegacyDiffBackend  — wraps DifferentialProcessor, uses BuildSnapshots SO, Asset-level diff
  └── ABDiffBackend      — new, uses BundleDigestList + .bin/.json, Bundle-level diff
```

BackendMode (SO default + build-parameter override) selects backend. No mid-build switching.

---

## Confirmed Design Decisions

### D1: Interface — 5 Methods (Matching IHotfixPipeline)

```csharp
public interface IDiffPipeline
{
    void GenerateSnapshot(BuildContext ctx);        // Full build: produce BundleDigestList
    DiffResult PrepareDiff(BuildContext ctx);        // Hotfix: compare current vs previous → delta
    void ApplyDiff(BuildContext ctx, DiffResult diff); // Apply delta to build pipeline
    void ConfirmRelease();                           // Stage → solidify into history
    void RollbackHotfix();                           // Discard stage, restore pre-hotfix state
}
```

Architecture consistency principle: every Interface-Backend pair in this project uses the same granularity. IDiffPipeline is the Diff-domain IHotfixPipeline.

### D2: Diff Granularity — Each Backend Produces Its Own Delta

Not one merged DiffResult. Each backend produces what it needs:

| Backend | Data Source | Delta Type | Why |
|---------|------------|------------|-----|
| LegacyDiffBackend | AssetSnapshot[] (per-asset hash) | AssetDelta (added/modified/deleted asset lists) | Existing logic; data source is per-asset |
| ABDiffBackend | BundleDigestList (per-bundle hash) | BundleDelta (added/modified/removed bundle lists) | Natural unit; new pipeline's atomic delivery is the Bundle |

Two backends never run simultaneously — BackendMode selects one. No cross-backend delta merging needed.

### D3: Persistence — New Backend .bin/.json, Legacy Stays SO

| Backend | Snapshot Format | Storage |
|---------|----------------|---------|
| LegacyDiffBackend | BuildSnapshots (SO) | `Assets/Build/Snapshots.asset` — unchanged |
| ABDiffBackend | BundleDigestList (.bin/.json) | `BuildData/Snapshots/` — file system |

Legacy SO NOT migrated. Rationale: legacy backend is parked (G5), not worth ~1h migration cost for a retiring code path.

BundleDigestList uses Phase S `[BinarySerializable]` + `SerializationUtility` — same infrastructure as ABManifest.

### D4: Rollback — Trivial for AB Backend

New pipeline does NOT physically relocate assets between groups. Group is a scan-result label, not a physical container. Therefore:

| Action | LegacyDiffBackend | ABDiffBackend |
|--------|------------------|---------------|
| RollbackHotfix | RestoreOriginalGroups (~100 lines): move assets from HotfixGroup back to original groups | Delete staged.bin + clear hotfix output dir (~5 lines) |
| Why the difference | Assets were physically moved via `settings.MoveEntry` — must undo | Assets never moved — just discard metadata |

ABDiffBackend.RollbackHotfix: delete `staged.bin`, delete built bundles (if any), reset BuildContext diff entries. Head snapshot untouched.

### D5: ConfirmRelease — head.json + Per-Version History Files + Storage Enhancements

```
BuildData/Snapshots/
  ├── head.json             ← {"Head": "v4.0.2", "Staged": "v4.0.3"}  or Staged:null
  ├── staged.bin            ← pending release snapshot (absent when no pending)
  ├── changelog.jsonl       ← append-only operation audit log
  └── history/
        ├── v4.0.0.bin      ← BundleDigestList per version
        ├── v4.0.1.bin
        └── v4.0.2.bin
```

**head.json format** (human-readable pointer, ~100 bytes):

```json
{
  "Head": "v4.0.2",
  "Staged": "v4.0.3"
}
```

#### D5a: Atomic Write

All writes to `head.json` and `staged.bin` use `FileHelper.WriteAllBytesAtomic` (write to `.tmp` → rename). This prevents state corruption if the process is interrupted mid-write. `ConfirmRelease` and `RollbackHotfix` are both multi-step — if any step fails, earlier steps are safe because each file is atomically consistent.

#### D5b: Operation Audit Log (changelog.jsonl)

Append-only JSONL file recording every state-changing operation:

```jsonl
{"ts":"2026-05-15T10:30:00Z","op":"GenerateSnapshot","version":"v4.0.3","user":"cfy"}
{"ts":"2026-05-15T11:00:00Z","op":"ConfirmRelease","version":"v4.0.3","from":"v4.0.2","user":"cfy"}
{"ts":"2026-05-16T09:00:00Z","op":"RollbackHotfix","version":"v4.0.4","user":"cfy"}
```

Fields: `ts` (ISO 8601), `op` (GenerateSnapshot/PrepareDiff/ConfirmRelease/RollbackHotfix), `version`, `from` (previous Head, ConfirmRelease only), `user` (Environment.UserName).

Not read by any pipeline logic — purely diagnostic. Append via `File.AppendAllText`.

#### D5c: History GC Strategy

`ConfirmRelease` runs GC after successful commit:
- Configurable retention count `MaxHistoryVersions` (default 10, stored on `BuildPipelineConfig` SO)
- When `history/` contains more than `MaxHistoryVersions` .bin files, delete the oldest (by VersionNumber comparison)
- GC is best-effort: failure to delete does not fail the release

#### D5d: Multi-Channel Extension Point (Interface Only)

`IDiffPipeline` methods accept an optional `channel` parameter (default `null` → single-channel behavior). The current implementation ignores it — all operations go to the single `BuildData/Snapshots/` directory.

Future multi-channel expansion: `BuildData/Snapshots/{channel}/` subdirectories, each with its own `head.json` + `history/`. Not implemented in E7.

**ConfirmRelease flow**:
1. Read head.json → get Staged version
2. Move `staged.bin` → `history/{version}.bin` (atomic rename)
3. Write head.json atomically: `Head = version`, `Staged = null`
4. Append changelog.jsonl entry
5. Run history GC (retain most recent `MaxHistoryVersions`)

**RollbackHotfix flow**:
1. Read head.json → get Staged version
2. Delete `staged.bin`
3. Write head.json atomically: `Staged = null`
4. Append changelog.jsonl entry
5. (Head and history untouched)

**Rollback to historical version** (ops):
1. Update head.json atomically: `Head = "v4.0.1"` (must exist in history/)
2. Append changelog.jsonl entry
3. Next hotfix diff uses v4.0.1 as baseline

### D6: BuildContext Version Source

BuildContext holds a unified `VersionNumber` field — the single source of truth for the current build. Both backends read from it. No independent version tracking.

### D7: version_state Boundary

`version_state.json` is legacy-backend-only. ABDiffBackend never generates or consumes it. It retires naturally with the legacy backend.

### D8: DeleteList — Dual-Track Transition

| Track | Content | Consumer |
|-------|---------|----------|
| Asset-level (legacy) | List of deleted asset GUIDs — existing field in BuildSnapshot | LegacyDiffBackend |
| Bundle-level (AB) | List of removed bundle names in BundleDelta | ABDiffBackend, for build cleanup |

Not a merged format — each backend tracks deletions in its own delta type.

### D9: ChangedBundles Granularity

Start from legacy-minimal field set: bundle name + hash + size. Expand later by demand — not frozen in E7.

### D10: ConfirmRelease Scope

ConfirmRelease solidifies the snapshot (metadata). It does NOT:
- Upload bundles to CDN (separate build step)
- Trigger runtime updates (runtime polls independently)
- Modify ABManifest (ABManifest is generated by E6, snapshot is a separate artifact)

---

## Data Structures

### BundleDigest.cs (Runtime assembly, new)

```csharp
/// <summary>
/// Per-bundle snapshot record for diff comparison.
/// Lives in Runtime assembly for potential future runtime snapshot validation.
/// </summary>
[BinarySerializable]
public class BundleDigest
{
    [BinaryField(0)] public string BundleName;
    [BinaryField(1)] public string Hash;       // MD5 hex string
    [BinaryField(2)] public long Size;         // bytes
}
```

### BundleDigestList.cs (Runtime assembly, new)

```csharp
/// <summary>
/// Snapshot of all bundles at a specific build version.
/// Serialized as .bin (primary) + .json (debug fallback).
/// </summary>
[BinarySerializable(Magic = 0x42444C53)] // 'BDLS'
public class BundleDigestList
{
    [BinaryField(0)] public VersionNumber Version;
    [BinaryField(1)] public string Timestamp;         // ISO 8601
    [BinaryField(2)] public List<BundleDigest> Digests;
}
```

### BundleDelta.cs (Editor assembly, new)

```csharp
/// <summary>
/// ABDiffBackend diff output. Build-time only.
/// </summary>
public class BundleDelta
{
    public List<BundleDigest> AddedBundles;       // new bundles, not in previous snapshot
    public List<BundleDigest> ModifiedBundles;    // hash changed since previous snapshot
    public List<string> RemovedBundles;           // bundle names no longer in current build
}
```

Asset-level delta types (AssetDelta, AssetSnapshot) are unchanged — they belong to the legacy backend and live in existing files.

---

## New Files

| File | Path | Assembly | Lines (est.) | Description |
|------|------|----------|-------------|-------------|
| BundleDigest.cs | Build/Collector/ | Runtime | ~15 | BundleDigest data class + [BinarySerializable] |
| BundleDigestList.cs | Build/Collector/ | Runtime | ~25 | BundleDigestList container + [BinarySerializable] |
| BundleDelta.cs | Build/Collector/Editor/Diff/ | Editor | ~20 | BundleDelta: Added/Modified/Removed lists |
| IDiffPipeline.cs | Build/Collector/Editor/Diff/ | Editor | ~20 | Interface: 5 methods |
| ABDiffBackend.cs | Build/Collector/Editor/Diff/ | Editor | ~120 | Bundle-level diff: GenerateSnapshot, PrepareDiff, ConfirmRelease, RollbackHotfix |
| LegacyDiffBackend.cs | Build/Collector/Editor/Diff/ | Editor | ~80 | Adapter wrapping existing DifferentialProcessor static methods |
| TaskGenerateSnapshot.cs | Build/Collector/Editor/Diff/ | Editor | ~60 | E5 Task: Full build → BundleDigestList + save |
| TaskPrepareDiff.cs | Build/Collector/Editor/Diff/ | Editor | ~80 | E5 Task: Hotfix → BundleDelta → BuildContext |

Total: 8 new files, ~420 lines estimated.

All paths relative to `Assets/FYAsset/Scripts/`.

---

## Modified Files

| File | Change | Risk |
|------|--------|------|
| DifferentialProcessor.cs | Extract core logic into `LegacyDiffBackend` adapter; original class becomes thin wrapper or marked `[Obsolete]` with redirect to `LegacyDiffBackend` | Low-Medium — existing callers (BuildProjectManager) refactored to use IDiffPipeline |
| BuildProjectManager.cs | Switch from direct `DifferentialProcessor.StaticMethod()` calls to `IDiffPipeline` instance (selected by BackendMode). Orchestrator flow unchanged | Medium — core build entry point; must preserve both backend paths |
| Constants.cs | Add `SNAPSHOT_DIR`, `SNAPSHOT_HEAD_FILE`, `SNAPSHOT_STAGED_FILE`, `SNAPSHOT_HISTORY_DIR` constants + `BDLS` Magic registration | Low — additive constants |

### Not Modified

- `BuildSnapshots.cs` — unchanged (legacy backend continues using it)
- `VersionState.cs` — unchanged (legacy-only artifact)
- Runtime hotfix code (`HotfixManager`, `IHotfixPipeline`, backends) — E7 is build-time only

---

## BuildContext Contract

E7 declares expected keys; exact API follows E5 BuildContext contract.

| Key | Type | Direction | Description |
|-----|------|-----------|-------------|
| `BundleDigestList` | `BundleDigestList` | Write (TaskGenerateSnapshot) | Full build snapshot output |
| `BundleDelta` | `BundleDelta` | Write (TaskPrepareDiff) | Hotfix diff output |
| `DiffResult` | `DiffResult` | Write (LegacyDiffBackend) | Legacy Asset-level diff output |

Note: `DiffResult` is a new type created by E7 (LegacyDiffBackend's PrepareDiff return value). It wraps the legacy diff output into a BuildContext-compatible structure.

---

## Task Breakdown

| Task | Content | Depends On |
|------|---------|-----------|
| E7-T1 | Create `BundleDigest.cs` + `BundleDigestList.cs` with `[BinarySerializable]` annotations | — |
| E7-T2 | Run S2 code generator → produce `BundleDigestList_Serializer.g.cs`, register Magic `BDLS` in `BinarySerializerInitializer` | T1 |
| E7-T3 | Create `BundleDelta.cs` (AddedBundles / ModifiedBundles / RemovedBundles) | — |
| E7-T4 | Create `IDiffPipeline.cs` interface (5 methods) | — |
| E7-T5 | Create `LegacyDiffBackend.cs` — adapter wrapping `DifferentialProcessor` static methods into `IDiffPipeline`. `PrepareDiff()` returns `DiffResult` (existing type). `RollbackHotfix()` delegates to `RestoreOriginalGroups()` | T4 |
| E7-T6 | Create `ABDiffBackend.cs` — `GenerateSnapshot`: scan built bundles → BundleDigestList; `PrepareDiff`: compare with head.bin → BundleDelta; `ApplyDiff`: write BundleDelta to BuildContext; `ConfirmRelease`: staged→history+update head.json; `RollbackHotfix`: delete staged | T2, T3, T4 |
| E7-T7 | Create `TaskGenerateSnapshot.cs` — E5 IBuildTask, Full build only. Reads TaskBuildBundles output, generates BundleDigestList, writes to BuildContext + saves to disk | T6 |
| E7-T8 | Create `TaskPrepareDiff.cs` — E5 IBuildTask, Hotfix only. Reads head.bin, compares with current bundles, produces BundleDelta, writes to BuildContext | T6 |
| E7-T9 | Modify `TaskBuildBundles.cs` — add `BuildContextKeys.BundleDelta` to ReadKeys; when BundleDelta present, only rebuild changed bundles (skip unchanged) | T8 |
| E7-T10 | Refactor `BuildProjectManager.cs` — switch from `DifferentialProcessor` static calls to `IDiffPipeline` interface. Select backend via BackendMode | T5, T6 |
| E7-T11 | Wire `DAGScheduler` into `BuildCommandLine.cs` — new pipeline entry point routes through DAGScheduler.Execute instead of BuildProjectManager directly. Legacy path preserved via BackendMode check | T10 |
| E7-T12 | Add Constants entries: `SNAPSHOT_DIR`, snapshot file names, `BDLS` Magic | — |
| E7-T13 | Compilation verification (`dotnet build XLuaHotfix.sln`) | All above |
| E7-T14 | (Optional) Legacy BuildSnapshots SO → .bin/.json migration. Deferred decision — execute only if legacy backend retirement timeline extends | T5 |

---

## E5 DAG Integration

```
Full Build:
  ... → TaskBuildBundles → TaskGenerateSnapshot → TaskGenerateManifest → ...

Hotfix Build:
  ... → TaskPrepareDiff → TaskBuildBundles (reads BundleDelta, rebuilds changed only) → ...
```

`TaskGenerateSnapshot` and `TaskPrepareDiff` are backbone nodes (non-skippable, like the other 6 backbone nodes). They define the snapshot lifecycle — without them, the diff chain breaks.

---

## Invariants (Must Hold After E7)

1. `IDiffPipeline` interface has exactly 5 methods, matching `IHotfixPipeline` granularity
2. `LegacyDiffBackend` preserves all existing `DifferentialProcessor` behavior (PrepareHotfix, ConfirmRelease, RestoreOriginalGroups, ReBuildSnapShots)
3. `ABDiffBackend.GenerateSnapshot()` produces a `BundleDigestList` that round-trips through SerializationUtility (.bin ↔ .json)
4. `ABDiffBackend.PrepareDiff()` correctly identifies added/modified/removed bundles by comparing bundle name + hash against `head.bin`
5. `ABDiffBackend.ConfirmRelease()` moves `staged.bin` → `history/{version}.bin` and updates `head.json`. If `history/{version}.bin` already exists, ConfirmRelease MUST fail with an error (prevents silent overwrite of historical snapshots)
6. `ABDiffBackend.RollbackHotfix()` deletes `staged.bin` and resets build output without touching Head snapshot or history
7. `BuildProjectManager` uses `IDiffPipeline` via BackendMode selection — no direct `DifferentialProcessor` static calls remain
8. Existing `version_state.json` generation (BuildProjectManager.GenerateVersionStateFile) is unchanged — it is legacy backend scope
9. Runtime hotfix code has zero changes — E7 is build-time only
10. `dotnet build XLuaHotfix.sln` passes with 0 errors

---

## Not In Scope

- Runtime differential download (runtime hotfix = full ABManifest comparison, same-skip / rest-download)
- `version_state.json` modification or removal (legacy backend artifact)
- `BuildSnapshots` SO format change (legacy backend, not migrated)
- CDN upload or deployment orchestration (separate build step, outside diff pipeline)
- Historical snapshot cleanup/archival policy beyond simple count-based GC (time-based, size-based)
- Inspector UI for snapshot history visualization (G-series editor tool, Phase 6+)
- Cross-backend delta merging (two backends never run simultaneously)

---

## Change Log

| Date | Change |
|------|--------|
| 2026-04-28 | Initial version: 10 design decisions, 12 tasks (T12 optional), 8 new files, 3 modified files. All decisions converged from plan-E-draft.md G4/G5 direction + deep-dive discussion (interface granularity, diff unit, persistence, rollback, ConfirmRelease) |
| 2026-05-08 | Audit fixes: (1) Added T9 — TaskBuildBundles must read BundleDelta key for incremental rebuild; (2) Added T11 — DAGScheduler integration into BuildCommandLine entry point; (3) Fixed DiffResult description (new type, not existing); (4) Added ConfirmRelease guard against history file overwrite; (5) Renumbered T9→T10, T10→T12, T11→T13, T12→T14 |
| 2026-05-15 | D5 storage layer enhancements: (1) D5a atomic write via FileHelper; (2) D5b changelog.jsonl append-only audit log; (3) D5c history GC strategy (MaxHistoryVersions count-based, configurable on BuildPipelineConfig); (4) D5d multi-channel extension point (interface parameter only, implementation deferred). ConfirmRelease/RollbackHotfix flows updated to include audit + GC steps |
