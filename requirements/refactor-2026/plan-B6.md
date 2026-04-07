# Sub-Plan B6: ABAssetIndex Runtime Index Implementation

> **Risk**: Medium-High
> **Dependencies**: B5-1 + B5-2 completed, ABManifest data layer available as Phase 3 baseline
> **Status**: DONE — Coded 2026-04-01, compilation verified, pending developer secondary review

---

## Objective

Implement `ABAssetIndex` as the first real runtime `IAssetIndex` backed by `ABManifest`, so that:

- `AssetPackageManager` can build runtime query state from `ABManifest` instead of `AddressableLabelsConfig`
- `AssetResolver` can run against real manifest-derived entries
- B5-3 / B5-4 can proceed using a real runtime data source instead of a placeholder index

This phase only replaces the **index source / query construction logic**. It does **not** replace the runtime loading backend.

---

## Background

The project has already completed:

- B5-1: `RuntimeAssetEntry` and runtime index rules
- B5-2: `Resolve / Load / AssetHandle` contract
- ABManifest data layer: `ABManifest`, `ManifestAssetEntry`, `ManifestBundleEntry`

Previously, B5-3 and B5-4 were deferred because there was no real runtime index source for `RuntimeAssetEntry`.
Now that the manifest data layer exists, B6 becomes the missing bridge:

`ABManifest.json -> ABManifest -> ABAssetIndex -> AssetResolver -> AssetPackageManager`

The first B6 round is intentionally scoped as a relatively independent phase:

- replace index source only
- keep `AddressablesBackend` unchanged
- do not expand into bundle loading handoff, ref-count pool, or full hotfix-core replacement

---

## Confirmed Scope Boundaries

1. B6 only replaces the runtime **index source**, not the runtime backend
2. `AssetPackageManager` remains the integration point; B6 does not change external loading API shape
3. B6 uses the **real hotfix runtime path** for manifest selection
4. B6 first-round verification is **coexistence validation**, not immediate full cutover
5. B6 does not introduce B4-style hotfix-core replacement or B7 bundle-loading behavior
6. B6 does not change `AssetPackageManager.Initialize()` into a Result-style contract in this round

---

## Confirmed Design Decisions

### A. Integration Strategy

1. `ABAssetIndex` is used through an **explicit code constant switch** inside `AssetPackageManager.Initialize()`
2. The switch is internal to `AssetPackageManager`, not exposed as a debug menu or external provider framework
3. If AB-index initialization fails, the manager **logs an error and stops initialization**
4. This first round does **not** auto-fallback to the legacy index after a selected AB path fails

### B. Manifest Source Strategy

1. B6 uses the **real active runtime manifest path**, not a temporary offline-only path
2. Active selection policy:
   - Prefer `CurrentGUID` manifest first
   - Fall back to built-in manifest only at source-selection level
3. Once the active source is selected, any manifest read / parse / index-build failure is treated as a hard initialization failure

### B2. Path Integration Strategy (Supplementary — 2026-04-01)

1. Manifest Loader uses **pure file I/O** (`File.ReadAllText` / StreamingAssets read), **not** Addressables
2. Primary path: `Path.Combine(PathManager.CurrentGUIDRoot, "ABManifest.json")`
3. Fallback path: `Path.Combine(Application.streamingAssetsPath, "ABManifest.json")`
4. **Note**: PathManager integration is provisional. Future build modules may change path conventions; the Loader must stay easy to adapt
5. No Addressables dependency in the AB index initialization path — this is the first step of decoupling from Addressables

### C. File Naming Strategy

1. Runtime local active manifest uses a **fixed local file name**: `ABManifest.json`
2. Versioned manifest naming is reserved for remote/distribution concerns
3. If needed later, build/distribution may keep a versioned artifact in parallel, but B6 runtime reads the fixed local file name

### D. Loader / Abstraction Strategy

1. Do **not** introduce `IManifestProvider` or a full provider/strategy framework in B6
2. Use a **minimal loader/helper** only, responsible for:
   - determining the active manifest path
   - reading file content
   - calling `ABManifest.DeserializeFromJson(...)`
3. No lifecycle manager, no multi-version manifest cache, no over-designed abstraction layer in this phase

### E. Object Ownership Strategy

1. `ABAssetIndex` **holds a reference to `ABManifest`**
2. `ABAssetIndex` implements all runtime query methods by using the already-initialized manifest data
3. This phase does not flatten the manifest into a second redundant runtime structure unless necessary for `IAssetIndex` behavior

### E2. RuntimeAssetEntry Cache Strategy (Supplementary — 2026-04-01)

1. On Initialize, **pre-convert all** `ManifestAssetEntry` → `RuntimeAssetEntry` into a cached `RuntimeAssetEntry[]` array
2. Query methods return references to cached entries — **zero allocation on query hot paths**
3. Rationale: RuntimeAssetEntry is index-only data (~600 bytes/entry), 1000 entries ≈ 600KB, 5000 entries ≈ 3MB — negligible memory overhead
4. ABAssetIndex uses ABManifest's existing `_addressIndex` / `_entryIdIndex` / `_typeIndex` / `_labelIndex` as int-index lookups, maps indices to the cached `RuntimeAssetEntry[]`

### E3. Legacy Key Mapping Strategy (Supplementary — 2026-04-01)

1. In legacy `IAssetIndex` methods (`GetKeysByLabel`, `GetKeysByType`, `GetLabels`, `ContainsKey`), the **"key" maps to `RuntimeAssetEntry.Address`**
2. This is consistent with the existing `AddressableLabelsConfig` behavior where keys are Addressable addresses
3. **No deduplication** is enforced on returned Address lists — Address may repeat when multiple entries share the same Address (different EntryId / PrimaryType). This is harmless for legacy callers because the Addressables backend handles duplicate Address loads idempotently
4. New `B5-2` resolve paths (`LoadByAddress<T>`, `LoadByTypeKey<T>`) use type disambiguation and EntryId-level precision, which is the intended migration target

### F. Query Capability Target

B6 first round must implement the **full current `IAssetIndex` contract**, including:

- `GetKeysByLabel`
- `GetKeysByType`
- `GetLabels`
- `ContainsKey`
- `GetEntryById`
- `GetEntriesByAddress`
- `GetEntriesByAddressAndType`
- `GetAllEntries`

This is required so that both legacy query helpers and B5 resolver-based paths can run on the same real runtime index.

### G. Logging / Diagnostics Strategy

1. Keep only **necessary error logs** in B6 first round
2. Do not add old-vs-new query diff tools in this phase
3. Do not add debug API surface unless it becomes necessary during implementation review

---

## Planned Tasks

### Task 1: Implement ABAssetIndex Core Query Logic

- Replace the current 10-line stub implementation with full `IAssetIndex` implementation
- Constructor accepts an initialized `ABManifest`
- **On construction / Initialize**: pre-convert all `ManifestAssetEntry` → `RuntimeAssetEntry` into cached `RuntimeAssetEntry[]`
- Reuse `ABManifest`'s internal index dictionaries (`_addressIndex`, `_entryIdIndex`, `_typeIndex`, `_labelIndex`) for int-index lookups, map to cached array
- Implement all 8 `IAssetIndex` methods:

| Method | Implementation |
|--------|---------------|
| `GetKeysByLabel(label)` | ABManifest._labelIndex → indices → _entries[i].Address list |
| `GetKeysByType(type)` | ABManifest._typeIndex → indices → _entries[i].Address list |
| `GetLabels()` | ABManifest._labelIndex.Keys as list |
| `ContainsKey(key)` | ABManifest._addressIndex.ContainsKey(key) |
| `GetEntryById(entryId)` | ABManifest._entryIdIndex → index → _entries[i] |
| `GetEntriesByAddress(address)` | ABManifest._addressIndex → indices → _entries subset |
| `GetEntriesByAddressAndType(address, type)` | GetEntriesByAddress + type filter |
| `GetAllEntries()` | Return entire _entries array as IReadOnlyList |

### Task 2: Add Minimal Manifest Loader

- Static helper method (not a separate class), likely on ABAssetIndex or a small ManifestLoader utility
- Path strategy: `PathManager.CurrentGUIDRoot/ABManifest.json` → fallback `StreamingAssets/ABManifest.json`
- **Pure file I/O** — `File.ReadAllText()` or `UnityWebRequest` for StreamingAssets (platform-dependent)
- Calls `ABManifest.DeserializeFromJson(json)` which auto-calls Initialize()
- Returns `ABManifest` on success, `null` on failure (with error log)
- **Note**: PathManager path integration is provisional; future build modules may change conventions
- Avoid provider interfaces and broader lifecycle management

### Task 3: Integrate with AssetPackageManager Initialize Path

- Add an internal constant switch: `const bool USE_AB_INDEX = false;` (default off)
- When `USE_AB_INDEX == true`:
  - Call Manifest Loader → get ABManifest
  - Create ABAssetIndex(manifest) → set as `_index`
  - Build `_labelToKeys` cache from new index (same loop as legacy path)
- When `USE_AB_INDEX == false`:
  - Existing AddressableLabelsConfig path unchanged
- On AB path failure: log error and stop initialization (no fallback to legacy)
- Keep the legacy path intact behind the switch for coexistence validation

---

## Preservation Requirements (Must Pass)

- [ ] Do not change runtime backend selection in this phase; backend remains `AddressablesBackend`
- [ ] Do not smuggle B4 hotfix-core replacement into B6
- [ ] Do not introduce unnecessary provider abstractions or lifecycle systems
- [ ] Do not change `AssetPackageManager.Initialize()` return contract in this phase
- [ ] Keep coexistence validation explicit via internal switch; no silent global cutover

---

## Acceptance Criteria

- [ ] `ABAssetIndex` fully implements the current `IAssetIndex` contract
- [ ] `AssetPackageManager` can build runtime query state from `ABManifest` when the internal switch is enabled
- [ ] The phase remains independent from backend replacement and bundle loading handoff
- [ ] Manifest loading path follows the approved real-runtime strategy without over-expanding into provider architecture
- [ ] Failure behavior is explicit: critical AB-index initialization failure logs and stops manager initialization

---

## Out of Scope

- `ABPackageBackend` implementation
- `ABBundleLoader` runtime loading behavior
- Bundle dependency loading handoff
- Ref-count pool / handle pool
- Build-side ABManifest generation changes
- B4 catalog / locator replacement
- Result-style initialization contract for `AssetPackageManager.Initialize()`
- Rich debug diff tooling between old index and AB index
- ABManifest export / generation tools (deferred to Phase 5-6)
- New-vs-old query comparison validation tools (deferred)

---

## Verification Strategy

B6 verification is **deferred** to when build-time tools exist (Phase 5-6). Rationale:
- No build pipeline currently generates `ABManifest.json` with real project data
- `AddressableLabelsConfig` data lacks EntryId / BundleIndex — cannot produce a complete ABManifest
- B6 guarantees: **compilation passes + full IAssetIndex contract implemented + const switch integration**
- End-to-end verification will occur when E6 (ABManifest build export) is complete

---

## Approval Checklist

### Original Approvals (2026-03-30)

- [x] Should B6 replace only the index source/query construction logic, without changing backend behavior?
  **Decision**: Yes. B6 only replaces `AssetPackageManager` index/query construction logic.
- [x] Should B6 use real hotfix runtime paths rather than an offline-only manifest path?
  **Decision**: Yes.
- [x] Should coexistence validation use an explicit internal switch rather than immediate cutover?
  **Decision**: Yes. Use an internal code constant switch inside `AssetPackageManager`.
- [x] Should `ABAssetIndex` hold `ABManifest` directly?
  **Decision**: Yes.
- [x] Should B6 introduce `IManifestProvider` / provider abstraction in this round?
  **Decision**: No. Keep a minimal loader/helper only.
- [x] Should runtime local manifest naming use fixed local name or versioned local name?
  **Decision**: Fixed local runtime name (`ABManifest.json`); versioned naming is for remote/distribution concerns.
- [x] Should B6 implement only the resolver-needed subset or the full current `IAssetIndex` contract?
  **Decision**: Full `IAssetIndex` contract.
- [x] If AB-index initialization fails, should the manager auto-fallback to the legacy index?
  **Decision**: No. Log error and stop initialization.
- [x] Should B6 also upgrade `AssetPackageManager.Initialize()` to a structured Result-style return contract?
  **Decision**: No. Leave that for a later phase.
- [x] Should B6 add richer debug diff tooling or just necessary error logs?
  **Decision**: Necessary error logs only.

### Supplementary Approvals (2026-04-01 Design Review)

- [x] RuntimeAssetEntry cache strategy: pre-convert all on Initialize or lazy-convert on query?
  **Decision**: Pre-convert all. Memory ~600 bytes/entry is negligible; zero-allocation queries on hot paths.
- [x] Legacy "key" in GetKeysByLabel/GetKeysByType/ContainsKey maps to what?
  **Decision**: key = Address. Natural mapping, no forced deduplication. Consistent with AddressableLabelsConfig behavior.
- [x] Manifest Loader path: how to integrate with PathManager?
  **Decision**: `PathManager.CurrentGUIDRoot/ABManifest.json` primary, `StreamingAssets/ABManifest.json` fallback. Pure file I/O, no Addressables dependency. PathManager integration is provisional — future build modules may change path conventions.
- [x] Verification strategy without build tools?
  **Decision**: Deferred. B6 guarantees compilation + contract implementation. End-to-end verification after Phase 5-6 build export tools exist.
