# Sub-Plan E4: Dependency Analysis + Shared Extraction

> **Risk**: Medium (Editor-only logic, no runtime impact; but dependency graph correctness directly affects bundle completeness)
> **Dependencies**: E1-1 (data model, enums, CollectedAssetInfo), E1-3 (CollectionScanner — provides `List<CollectedAssetInfo>` input), E5 (IBuildTask interface + BuildContext contract)
> **Status**: Draft — updated 2026-04-27 (terminology + naming + ForceShare + Address correction)
> **Execution order**: E5-1 (IBuildTask + BuildContext) must execute before E4. E4 core logic (DependencyAnalyzer) is independent of E5 contract types.

---

## Objective

Implement `TaskAnalyzeDependencies`, the E4 build pipeline task that runs after `TaskCollectAssets` (E1-3) and before `TaskBuildBundles` (E5). It performs three responsibilities in a single BFS pass:

1. **Bundle Dependency Graph Construction** — for all collected assets, trace Unity dependencies to build Bundle-level dependency edges
2. **Implicit Dependency Discovery** — find assets referenced by collected assets that are NOT in any Collector
3. **Shared Extraction Decision** — apply SharePolicy (per-Package) to decide whether each implicit dependency goes into a shared bundle or is duplicated into referencing bundles

Also detects asset-level circular dependencies (Error → abort build).

---

## Confirmed Design Decisions

### D1: Dependency Graph Construction — Per-Asset BFS

Single-pass BFS traversal. For each `CollectedAssetInfo`, call `AssetDatabase.GetDependencies(path, recursive=false)` to get direct dependencies. Recursively expand each dependency, tracking visited set and reference count. Single pass naturally distinguishes "already owned" (→ build Bundle edge) from "not owned" (→ record as implicit candidate).

### D2: SharePolicyConfig Placement — Runtime Assembly

`SharePolicyConfig` is a pure data class with no Editor dependencies. It must reside in Runtime assembly because `CollectorPackage` (Runtime, `[Serializable]`) holds it for SO serialization. The decision logic consuming it lives in Editor assembly (`DependencyAnalyzer`).

### D3: Depend Collector Assets

Depend Collector assets have unique ownership (E1-3 deepest-path dedup). E4 handles them as "already owned" during BFS — records Bundle dependency edges from referencing Bundle to the Depend asset's Bundle. No special-casing needed; the BFS visited-set check naturally distinguishes owned vs. unowned.

### D4: Shared Scope — Package Internal

Dependency analysis and shared extraction run per-Package. Shared bundles are Package-scoped. Cross-Package path overlap is already a configuration error (E1-3).

### D5: ImplicitDependency GroupName + Bundle Naming

GroupName = `"$shared"` (via SystemIdentifiers.SharedGroupName) (reserved keyword, users may not create a CollectorGroup named "shared"). PackKey = typeGroup inferred from PrimaryType (e.g., "materials", "shaders", "textures"). Logical name: `{pkg}_shared_{typeGroup}`. E5 appends content hash + extension like all other bundles.

Multiple implicit deps with same typeGroup naturally merge into one shared bundle (same logical name → same bundle).

### D6: ImplicitDependency Labels — Empty

Labels only come from user configuration. ImplicitDependency has no user-declared Collector/Group → Labels = empty. Runtime loading guaranteed by Bundle dependency edges, not label-based filtering.

### D7: ImplicitDependency Address — Auto-Generated

Address = `AssetAddressGenerator.GenerateShortAddress(assetPath, primaryType)`. Same auto-generation logic as explicit assets — no special handling. ImplicitDependency IS addressable, just not explicitly collected.

### D8: ECollectorType.Implicit

Add `Implicit = 3` to `ECollectorType` enum. Expresses "no user declaration" — distinct from Main/Static/Depend which are user configuration intents. Increment: 1 line.

### D9: SharedBundleDef — Not Needed

E4 directly sets `BundleName` on each `ImplicitDependency` entry and appends to the `CollectedAssetInfo` list. E5 groups by `BundleName` — shared bundles and regular bundles are indistinguishable to the packer. No separate data structure required.

### D10: BundleDependencyGraph — Structured, Human-Readable

```csharp
public class BundleDependencyEdge
{
    public string FromBundle;       // referencing Bundle name
    public string ToBundle;         // referenced Bundle name
    public List<string> ViaAssets;  // asset paths that cause this edge
}
```

Stored as `List<BundleDependencyEdge>` — flat list with full traceability, consumable by Inspector UI (E1-4 extension or Phase 6 Inspector).

### D11: Cycle Detection — Asset Level

BFS visited-set detects asset-level cycles (A→B→A). Asset-level cycle → Bundle-level cycle (asset A in Bundle X, asset B in Bundle Y). Detected during BFS expansion — if a dependency is already in the current BFS stack, report error.

### D12: Shared Bundle Granularity — Rule Layer, Not E4

Shared bundle granularity is controlled by user configuration (GroupRule + PackRule on Collectors), not by E4. E4 only decides shared vs. duplicated per implicit dependency based on SharePolicy.

---

## SharePolicyConfig

```csharp
/// <summary>
/// Per-Package shared extraction policy.
/// Lives in Runtime assembly for SO serialization on CollectorPackage.
/// </summary>
[Serializable]
public class SharePolicyConfig
{
    /// <summary>Minimum number of referencing Bundles to trigger shared extraction.</summary>
    public int MinReferenceCount = 2;

    /// <summary>Assets smaller than this (bytes) are NOT extracted to shared bundle.</summary>
    public long MinAssetSizeBytes = 0;

    /// <summary>Glob patterns matching asset paths. Matched assets are never shared.</summary>
    public List<string> NoSharePatterns = new();

    /// <summary>Glob patterns matching asset paths. Matched assets are always shared (ignoring MinReferenceCount).</summary>
    public List<string> ForceSharePatterns = new();
}
```

---

## Bundle Dependency Graph

```csharp
/// <summary>
/// Structured Bundle dependency graph output from E4.
/// Consumed by E5 (bundle build order) and Inspector UI.
/// </summary>
public class BundleDependencyGraph
{
    /// <summary>All directed edges: FromBundle depends on ToBundle.</summary>
    public List<BundleDependencyEdge> Edges = new();

    /// <summary>Per-Bundle dependency lookup (derived index, built on first access).</summary>
    public Dictionary<string, HashSet<string>> GetDependencyMap();
}

public class BundleDependencyEdge
{
    public string FromBundle;
    public string ToBundle;
    public List<string> ViaAssets;
}
```

---

## CollectedAssetInfo Additions

Two new fields on `CollectedAssetInfo`:

```csharp
/// <summary>True when this asset is packed into a shared bundle (E4 decision).</summary>
public bool IsInSharedBundle;

/// <summary>True when this is one of multiple copies of the same asset across Bundles.</summary>
public bool IsDuplicated;
```

---

## Algorithm: Single-Pass BFS

```
TaskAnalyzeDependencies.Execute(BuildContext ctx)
│
├── Read: List<CollectedAssetInfo> (key: "CollectedAssets")
│
├── For each Package:
│   │
│   ├── Build lookup: HashSet<string> ownedGUIDs, Dictionary<string, CollectedAssetInfo> guidToAsset
│   │
│   ├── Phase 1: BFS + Implicit Discovery
│   │   For each CollectedAssetInfo in Package:
│   │   │
│   │   ├── BFS queue: asset.GUID
│   │   ├── BFS stack: current path (for cycle detection)
│   │   │
│   │   ├── While queue not empty:
│   │   │   ├── dequeue guid
│   │   │   ├── if guid in ownedGUIDs:
│   │   │   │   ├── Record Bundle edge: from.Asset.BundleName → owned.BundleName
│   │   │   │   └── continue (don't expand — already processed)
│   │   │   │
│   │   │   ├── if guid in BFS stack → CIRCULAR_DEPENDENCY error
│   │   │   │
│   │   │   ├── deps = AssetDatabase.GetDependencies(assetPath, recursive=false)
│   │   │   │
│   │   │   ├── For each dep (filter .meta/.cs/.dll/.asmdef/.asmref/Editor/):
│   │   │   │   ├── if dep in current expansion set → skip (already queued)
│   │   │   │   ├── if dep in ownedGUIDs → record edge + don't re-expand
│   │   │   │   └── else → queue dep as implicit candidate
│   │   │   │
│   │   │   └── Mark as visited
│   │   │
│   │   └── Aggregate implicit candidates with refCount
│   │
│   ├── Phase 2: SharePolicy Decision
│   │   For each implicit candidate:
│   │   │
│   │   ├── Skip if matches any NoSharePatterns glob → force duplicate
│   │   │
│   │   ├── Skip if matches any ForceSharePatterns glob → force shared (bypass refCount)
│   │   │
│   │   ├── if refCount >= MinReferenceCount AND assetSize >= MinAssetSizeBytes:
│   │   │   ├── Create CollectedAssetInfo with IsInSharedBundle=true
│   │   │   ├── GroupName = "$shared", PackKey = typeGroup
│   │   │   ├── BundleName = BundleNameBuilder.Build(pkg, "shared", typeGroup)
│   │   │   ├── Address = AssetAddressGenerator.GenerateShortAddress(...)
│   │   │   ├── Labels = empty, CollectorType = Implicit
│   │   │   └── One entry for the shared bundle
│   │   │
│   │   └── else:
│   │       ├── For each referencing Bundle:
│   │       │   └── Create CollectedAssetInfo with:
│   │       │       ├── IsDuplicated=true, IsInSharedBundle=false
│   │       │       ├── BundleName = referencingBundleName
│   │       │       └── Same AssetGUID (multiple entries, intentional)
│   │       └── (N referencing Bundles → N CollectedAssetInfo entries)
│   │
│   └── Phase 3: Append to global asset list
│
├── Write: List<CollectedAssetInfo> (augmented, key: "CollectedAssets")
├── Write: BundleDependencyGraph (key: "BundleDependencyGraph")
│
└── Return: BuildTaskResult (success / failure with messages)
```

### Edge Cases

| Scenario | Handling |
|----------|----------|
| Dependency is .meta / .cs / .dll / .asmdef / .asmref / Editor/ | Skip (same filter as CollectAll) |
| Dependency outside Assets/ (e.g., Packages/) | Skip with warning — not ownable |
| Dependency path doesn't exist | Skip with warning — stale .meta reference |
| Shared bundle has only 1 asset after filtering | Still create shared bundle (MinReferenceCount already passed) |
| Two Collectors in different Packages reference same implicit dep | Each Package independently decides → may duplicate across Packages |

---

## Error/Warning Conditions

| Condition | Severity | Code |
|-----------|----------|------|
| Asset-level circular dependency | Error | `CIRCULAR_DEPENDENCY` |
| Dependency outside Assets/ | Warning | `EXTERNAL_DEPENDENCY` |
| Dependency path not found | Warning | `DEPENDENCY_PATH_NOT_FOUND` |
| Shared bundle created with only 1 asset | Warning | `SINGLE_ASSET_SHARED` |
| Implicit dep matches NoSharePatterns + refCount high | Info | `NOSHARE_OVERRIDE` |

---

## Pipeline Position

```
TaskCollectAssets (E1-3)
    │
    ▼
TaskAnalyzeDependencies (E4)   ← this sub-plan
    │
    ├── Augmented List<CollectedAssetInfo>  →  TaskBuildBundles (E5)
    └── BundleDependencyGraph                →  TaskBuildBundles + Inspector UI
    │
    ▼
TaskBuildBundles (E5)
```

### BuildContext Contract

| Key | Type | Direction | Description |
|-----|------|-----------|-------------|
| `CollectedAssets` | `List<CollectedAssetInfo>` | Read + Write | Input from E1-3, augmented with ImplicitDependency entries |
| `BundleDependencyGraph` | `BundleDependencyGraph` | Write | E4 output, consumed by E5 |

Note: IBuildTask interface + BuildContext infrastructure are defined by E5. E4 declares its expected keys; exact API follows E5 contract.

---

## New Files

| File | Path | Assembly | Lines (est.) | Description |
|------|------|----------|-------------|-------------|
| SharePolicyConfig.cs | Build/Collector/ | Runtime | ~25 | Data class: MinReferenceCount, MinAssetSizeBytes, NoSharePatterns |
| BundleDependencyGraph.cs | Build/Collector/Editor/DependencyAnalysis/ | Editor | ~60 | BundleDependencyGraph + BundleDependencyEdge |
| DependencyAnalyzer.cs | Build/Collector/Editor/DependencyAnalysis/ | Editor | ~280 | BFS engine: dep expansion, cycle detection, implicit discovery, SharePolicy decision |
| TaskAnalyzeDependencies.cs | Build/Collector/Editor/DependencyAnalysis/ | Editor | ~80 | IBuildTask orchestration: read ctx → call analyzer → write ctx |

All paths relative to `Assets/FYAsset/Scripts/`.

---

## Modified Files

| File | Change | Risk |
|------|--------|------|
| CollectorEnums.cs | Add `Implicit = 3` to `ECollectorType` | Low — additive, existing switch statements handle default |
| CollectorSetting.cs | Add `public SharePolicyConfig SharePolicy = new();` to `CollectorPackage` (uncomment placeholder) | Low — additive field with default |
| CollectedAssetInfo.cs | Add `IsInSharedBundle` + `IsDuplicated` bool fields | Low — additive fields, default false preserves existing behavior |

---

## Task Breakdown

| Task | Content | Depends On |
|------|---------|-----------|
| E4-T1 | Add `Implicit = 3` to `ECollectorType` in `CollectorEnums.cs` | E1-1 done |
| E4-T2 | Create `SharePolicyConfig.cs` + add field to `CollectorPackage` | T1 |
| E4-T3 | Create `BundleDependencyGraph.cs` (`BundleDependencyGraph` + `BundleDependencyEdge`) | — |
| E4-T4 | Add `IsInSharedBundle` + `IsDuplicated` fields to `CollectedAssetInfo` | E1-1 done |
| E4-T5 | Create `DependencyAnalyzer.cs` — Phase 1: BFS engine with owned-guid lookup + direct dep retrieval via `AssetDatabase.GetDependencies` | E1-3 done |
| E4-T6 | Extend `DependencyAnalyzer.cs` — asset-level cycle detection (BFS stack check) | T5 |
| E4-T7 | Extend `DependencyAnalyzer.cs` — Phase 2: SharePolicy decision (shared vs duplicated) + ImplicitDependency entry creation | T2, T4, T6 |
| E4-T8 | Create `TaskAnalyzeDependencies.cs` — IBuildTask orchestration (read ctx, per-Package loop, call analyzer, write ctx) | T3, T7 |
| E4-T9 | Create `BundleDependencyGraph.GetDependencyMap()` — derived index for O(1) lookup | T3 |
| E4-T10 | Compilation verification (`dotnet build XLuaHotfix.sln`) | All above |

---

## Invariants (Must Hold After E4)

1. `TaskAnalyzeDependencies` correctly builds Bundle dependency edges for all collected assets (including Depend)
2. Implicit dependencies (assets not in any Collector) are discovered and recorded with `CollectorType = Implicit`
3. `refCount >= MinReferenceCount` (or matches ForceSharePatterns) → asset enters shared bundle with `IsInSharedBundle = true`, GroupName = "$shared", PackKey = typeGroup, BundleName via BundleNameBuilder
4. `refCount < MinReferenceCount` → asset duplicated into each referencing Bundle with `IsDuplicated = true`
5. Assets matching `NoSharePatterns` are forced to duplicate regardless of refCount
6. Asset-level circular dependency → `CIRCULAR_DEPENDENCY` error, build aborted
7. `BundleDependencyGraph.Edges` is a complete, human-readable edge list with `ViaAssets` trace
8. All BFS operations use `HashSet<string>` for O(1) visited/owned lookups; no unbounded recursion
9. E1-3's unique ownership (one asset → one Collector) is NOT violated — E4 only adds new entries for unowned assets
10. `dotnet build XLuaHotfix.sln` passes with 0 errors

---

## Not In Scope

- IBuildTask interface / BuildContext infrastructure (E5 defines the contract; E4 declares expected keys)
- Actual bundle building (E5 TaskBuildBundles)
- Inspector UI for BundleDependencyGraph visualization (Phase 6 Inspector panel)
- SharePolicyConfig Editor UI (E1-4 Settings panel extension or Phase 6)
- Cross-Package shared bundles (D4: Package-internal scope)
- Implicit dependency asset bundle building (E5 handles all BundleName groups equally)
- Runtime asset loading with shared bundle awareness (E6 manifest + runtime backend)

---

## Change Log

| Date | Change |
|------|--------|
| 2026-04-26 | Initial version: 12 design decisions, 10 tasks. Approved by developer |
| 2026-04-27 | **Discussion refinement**: (1) SharePolicyConfig +ForceSharePatterns (4 fields total). (2) ImplicitDependency Address → auto-generated via AssetAddressGenerator (not empty). (3) Shared bundle GroupName = "$shared" (reserved), PackKey = typeGroup. Removed sm_ prefix and GUID-based shortHash. Naming unified with BundleNameBuilder 3-segment format. (4) Labels confirmed empty (only user config produces Labels). (5) Execution order: E5-1 before E4 |

---

## Approval Checklist

- [ ] Agree to single-pass BFS (Phase 1: Bundle edges + implicit discovery; Phase 2: SharePolicy decision)
- [ ] Agree to `SharePolicyConfig` in Runtime assembly with 4 fields (MinReferenceCount, MinAssetSizeBytes, NoSharePatterns, ForceSharePatterns)
- [ ] Agree to Depend assets handled as "already owned" → Bundle dependency edges only
- [ ] Agree to Package-internal shared bundle scope
- [ ] Agree to ImplicitDependency GroupName = "$shared" (reserved), PackKey = typeGroup, BundleName via BundleNameBuilder
- [ ] Agree to ImplicitDependency Labels = empty (only user config produces Labels), Address = auto-generated (AssetAddressGenerator)
- [ ] Agree to `ECollectorType.Implicit = 3` for "no user declaration" semantics
- [ ] Agree to SharedBundleDef NOT being a separate data structure (BundleName on CollectedAssetInfo is sufficient)
- [ ] Agree to `BundleDependencyGraph` as structured, human-readable edge list with `ViaAssets`
- [ ] Agree to asset-level cycle detection (BFS stack), not Bundle-level
- [ ] Agree to 4 new files + 3 modified files + 10 tasks
- [ ] Agree to E5 defining IBuildTask/BuildContext contract; E4 declares expected keys. E5-1 executes BEFORE E4
