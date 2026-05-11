# Performance Review: E4 + E5-1 Implementation

> **Review date**: 2026-04-30
> **Scope**: DAGScheduler.cs, BuildTaskResolver.cs, DependencyAnalyzer.cs, BundleDependencyGraph.cs, BuildContext.cs, TaskAnalyzeDependencies.cs
> **Methodology**: Static analysis of allocations, algorithmic complexity, and Unity Editor API usage in hot paths
> **Context**: All code is Editor-only build pipeline, not runtime. Scale: ~100 Tasks max, ~10K assets typical collection.
> **Archival status**: 📦 Archived — P1-3/P1-4/P2-1/P2-2 addressed in `af1eb53`. Remaining perf items (P1-1, P1-2, P2-3) are Editor-only, low-impact at current scale (~100 Tasks / ~10K assets). No further action planned.
> **Processed**: 2026-05-11

---

## Findings Summary

| Severity | Count | Impact |
|----------|-------|--------|
| P1 — Allocation hotspot | 4 | Repeated LINQ allocations in scheduling loop and analysis loop |
| P2 — Wasted work | 3 | Dead code, unused API calls, double instantiation |
| P3 — Minor inefficiency | 3 | Low-impact in Editor context, fix at convenience |

---

## P1 — Allocation Hotspots

### P1-1: DAGScheduler.ExecuteInternal — per-batch LINQ allocation

**File**: `DAGScheduler.cs:258-261`
```csharp
var ready = remaining
    .Where(n => indegree[n] == 0)
    .OrderBy(n => n, StringComparer.Ordinal)
    .ToList();
```

**Problem**: Every batch iteration allocates an `IEnumerable` + `OrderedEnumerable` + `List<string>`. For 6 backbone nodes this is 6 allocations (one per batch). For extension-heavy configs with more parallel nodes, batch count stays ~6–10 — the absolute number is small.

**Fix**: Pre-allocate a `List<string>`, loop over `remaining` with an `if`, then `Sort()`:
```csharp
var ready = new List<string>();
foreach (var n in remaining)
    if (indegree[n] == 0) ready.Add(n);
ready.Sort(StringComparer.Ordinal);
```

**Severity**: Low. Editor-time only, batch count is single-digit. Fix for code quality not performance urgency.

---

### P1-2: DAGScheduler.ExecuteInternal — dual LINQ on results

**File**: `DAGScheduler.cs:312-314`
```csharp
Success = !fatalAbort && results.TrueForAll(r => r.Success),
CompletedTasks = results.Count(r => r.Success),
```

**Problem**: Two full iterations of the results list — `TrueForAll` + `Count`. `Count` is LINQ `IEnumerable.Count(Func<>)`, not `List.Count`.

**Fix**: Single loop tracking `completedCount` and `allSuccess`:
```csharp
int completed = 0;
bool allSuccess = true;
foreach (var r in results)
{
    if (r.Success) completed++;
    else allSuccess = false;
}
```

**Severity**: Low. Results list is ~10 entries for backbone pipeline.

---

### P1-3: DAGScheduler.ValidateInternal — WriteKeys.Contains is LINQ on Array

**File**: `DAGScheduler.cs:176`
```csharp
bool selfProduce = instance.WriteKeys != null && instance.WriteKeys.Contains(key);
```

**Problem**: `string[].Contains(string)` delegates to `Enumerable.Contains`, which does a linear scan with `IEnumerator<T>` allocation. WriteKeys arrays are typically 1–4 elements, so the scan is cheap, but the enumerator allocation is per-key.

**Fix**: Use `Array.IndexOf(instance.WriteKeys, key) >= 0` — static method, zero allocation:
```csharp
bool selfProduce = instance.WriteKeys != null
    && Array.IndexOf(instance.WriteKeys, key) >= 0;
```

**Severity**: Low. Small arrays, Editor code.

---

### P1-4: DependencyAnalyzer — ReferencingBundles.Contains is O(n) linear scan

**File**: `DependencyAnalyzer.cs:161`
```csharp
if (!candidate.ReferencingBundles.Contains(asset.BundleName))
    candidate.ReferencingBundles.Add(asset.BundleName);
```

**Problem**: `List<string>.Contains` is O(n). For an asset referenced by 20 bundles in a large project, this becomes O(400) per unique implicit dependency. The total impact depends on the number of implicit candidates, which is bounded by unique unowned assets in BFS.

**Fix**: Use `HashSet<string>` for `ReferencingBundles`:
```csharp
// In ImplicitCandidate:
public readonly HashSet<string> ReferencingBundles = new(StringComparer.Ordinal);
```

Then `.Add()` is O(1) and auto-deduplicates — `Contains` check becomes unnecessary.

**Severity**: Medium. This is in the BFS hot path, multiplied by total collected assets × their dependency depth. For a project with 5K collected assets and average 20 deps each, this could be hundreds of thousands of List.Contains calls.

---

## P2 — Wasted Work

### P2-1: DependencyAnalyzer — bfsStack maintained but never used for cycle detection

**File**: `DependencyAnalyzer.cs:94,106,111,122,172`

```csharp
var bfsStack = new List<string>();  // line 94 — allocated per asset
bfsStack.Add(guid);                // line 106
bfsStack.RemoveAt(bfsStack.Count - 1); // line 111,122,172
```

**Problem**: The BFS stack is maintained (add/remove) at every BFS node, but never actually checked. Cycle detection was deferred during implementation. The `globalVisited` set already prevents infinite loop, so the stack is pure overhead — per-asset allocation, per-dependency Add/RemoveAt.

**Fix**: Either implement the cycle check, or remove `bfsStack` entirely:
```csharp
// If implementing: at line 106, before globalVisited.Add:
int cycleIdx = bfsStack.IndexOf(guid);
if (cycleIdx >= 0) { /* report cycle */ }

// If removing: delete lines 94, 106, 111, 122, 172
```

**Severity**: Medium. Allocated once per collected asset (thousands), Add/RemoveAt per BFS node.

---

### P2-2: DependencyAnalyzer — depType fetched but unused

**File**: `DependencyAnalyzer.cs:141`
```csharp
string depType = AssetDatabase.GetMainAssetTypeAtPath(dep)?.Name ?? "Unknown";
```

**Problem**: `AssetDatabase.GetMainAssetTypeAtPath` is a Unity Editor API call that involves asset database lookup. The result is assigned to a local variable `depType` that is never read. The `graph.AddEdge` call on line 142 only uses `dep` (the path), not `depType`.

**Fix**: Remove line 141.

**Severity**: Medium. AssetDatabase call in the BFS inner loop. For 5K assets × 20 deps = 100K wasted API calls.

---

### P2-3: BuildTaskResolver — double instantiation of every IBuildTask at startup

**File**: `BuildTaskResolver.cs:35-48`

```csharp
// Initialize():
instance = (IBuildTask)Activator.CreateInstance(type);  // 1st instantiation — reads TaskName
_index[instance.TaskName] = type;

// CreateTask():
return (IBuildTask)Activator.CreateInstance(type);       // 2nd instantiation — returns task
```

**Problem**: Every IBuildTask is instantiated once in `Initialize()` (to read `TaskName`) and again in `CreateTask()` (to return the working instance). The first instance is created and immediately discarded. For a task with a heavy constructor, this doubles the init overhead. Currently the 6 backbone + ~3 extension tasks have trivial constructors, so this is theoretical.

**Fix**: Cache the instance from Initialize or use a static attribute for TaskName. The plan's original design chose instance property over static field for interface consistency — this tradeoff is documented and accepted.

**Severity**: Low. Editor-time startup only. 6–15 tasks with trivial constructors.

---

## P3 — Minor Inefficiency

### P3-1: ShouldSkip — linear EndsWith scan per dependency

**File**: `DependencyAnalyzer.cs:272-277`
```csharp
foreach (var ext in FilterExtensions)
{
    if (assetPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
        return true;
}
```

**Problem**: 8 extensions × every dependency path. `EndsWith` with `OrdinalIgnoreCase` is relatively slow. A `Path.GetExtension` + `HashSet.Contains` is faster.

**Fix**:
```csharp
string ext = Path.GetExtension(assetPath);
return FilterExtensions.Contains(ext);
```

**Severity**: Low. FilterExtensions is small (8 entries). BFS inner loop but Unity IO dominates.

---

### P3-2: DAGScheduler — Where/Select/ToList called redundantly

**File**: `DAGScheduler.cs:78,83,138,141,225,325`

```csharp
var enabled = config.Tasks.Where(e => e.Enabled).ToList();  // line 78 — ValidateInternal
var enabled = config.Tasks.Where(e => e.Enabled).ToList();  // line 225 — ExecuteInternal
config.Tasks.Count(e => e.Enabled)                           // line 325 — ErrorResult
```

**Problem**: `config.Tasks.Where(e => e.Enabled)` is computed separately in Validate, Execute, and ErrorResult. For ~10 tasks this is negligible.

**Fix**: Single computation at the entry point, pass the enabled list as a parameter.

**Severity**: Trivial. N = ~10 tasks.

---

### P3-3: ValidatePair — intermediate List allocation

**File**: `DAGScheduler.cs:53-59`
```csharp
var conflicts = new List<string>();
foreach (var key in writeB)
    if (setA.Contains(key)) conflicts.Add(key);
return conflicts.ToArray();
```

**Problem**: List + ToArray creates two allocations. Could return `List<string>` directly (caller doesn't require array contract) or pre-allocate based on smaller array size.

**Severity**: Trivial. Called once per editor UI interaction.

---

## Correctness Observations (non-performance)

### C1: DependencyAnalyzer.Analyze — result list double-copy pattern

```csharp
var result = new List<CollectedAssetInfo>(assets);  // copies all existing assets
// ... later:
result.Add(sharedEntry);  // augmented entries appended
```

**Problem**: Explicit assets are in `result` twice — once from the copy, once potentially from `packageAssets` processing (they're the same objects). Wait — `packageAssets` is a sub-list from `byPackage` which references the original `assets`. The `result` list copies references (not objects). So the same asset object appears in both `packageAssets` and `result`. This is fine — no double-counting, just reference sharing. But semantically unclear.

**Status**: Functional, not a bug. Clarify with a comment.

---

## Recommendations by Priority

| Priority | ID | Action | Effort |
|----------|----|--------|--------|
| **Fix** | P1-4 | ReferencingBundles → HashSet | 1 line change |
| **Fix** | P2-1 | Remove or implement bfsStack cycle detection | Choose one |
| **Fix** | P2-2 | Remove unused depType variable | 1 line deletion |
| **Consider** | P1-1 | Replace per-batch LINQ with manual loop+Sort | ~5 lines |
| **Consider** | P1-2 | Single-pass success/count instead of dual LINQ | ~6 lines |
| **Consider** | P1-3 | Array.IndexOf instead of Enumerable.Contains | 1 line |
| **Consider** | P2-3 | TaskResolver lazy init — documented tradeoff | No change |
| **Ignore** | P3-1 | ShouldSkip — Editor IO dominates | No change |
| **Ignore** | P3-2 | Redundant Where — N is trivial | No change |
| **Ignore** | P3-3 | ValidatePair List — UI interaction | No change |

---

## Verdict

No blocking performance issues. All hot-path concerns are in Editor-only code with single-digit to thousand-scale inputs. The three P1 fixes worth taking are low-effort (< 5 lines each) and have educational value for future pipeline code. The two P2 fixes remove dead code that confuses readers. Recommended: apply P1-4 + P2-1 + P2-2 immediately; P1-1/P1-2/P1-3 at next cleanup pass.
