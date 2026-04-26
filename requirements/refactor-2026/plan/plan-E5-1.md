# Sub-Plan E5-1: Build Pipeline Core Engine

> **Risk**: High (defines the execution framework that ALL subsequent Tasks plug into)
> **Dependencies**: E1-1 (data model, enums for Constants.cs)
> **Parent**: [plan-E5.md](plan-E5.md)
> **Status**: Draft — discussion complete, pending approval

---

## Objective

Implement the build pipeline core infrastructure: `IBuildTask` interface, `BuildContext` type-safe data bus, `BuildTaskResult`/`BuildResult` result types, `BuildPipelineConfig` SO + `TaskEntry` for task wiring, `BackendMode` enum with switch mechanism, `DAGScheduler` (Kahn topological sort + batch parallel execution + wiring validation + conflict detection), and `BuildContextKeys` constants.

This sub-plan contains ZERO Task implementations. It defines the contract that E1-3/E4/E5-2/E6 implement against.

---

## Confirmed Design Decisions

### D1: IBuildTask 4-Field Contract

```csharp
public interface IBuildTask
{
    string TaskName { get; }
    string[] DependsOn { get; }      // TaskName 列表
    string[] ReadKeys { get; }       // BuildContext 读键声明
    string[] WriteKeys { get; }      // BuildContext 写键声明
    BuildTaskResult Execute(BuildContext ctx);
}
```

### D2: BuildContext Type-Safe Generic Access

```csharp
public class BuildContext
{
    public void Set<T>(string key, T value) where T : class;
    public T Get<T>(string key) where T : class;
    public T Require<T>(string key) where T : class;  // null → throw
    public bool Has(string key);
}
```

Keys managed centrally in `Constants.BuildContextKeys` static class — no string magic values in Task code.

### D3: BuildTaskResult — Fatal vs Non-Fatal

```csharp
public class BuildTaskResult
{
    public bool Success;
    public string ErrorCode;          // 机器可读，如 "CIRCULAR_DEPENDENCY"
    public string ErrorMessage;       // 人类可读
    public List<string> Warnings;     // 非致命警告
    public bool IsFatal;              // true → 调度器中止后续所有批次
    
    public static BuildTaskResult Ok(List<string> warnings = null);
    public static BuildTaskResult Fail(string code, string msg, bool fatal = true);
    public static BuildTaskResult Warn(string code, string msg);
}
```

Error handling: Fatal → abort remaining batches. Non-fatal → continue execution, aggregate all results.

### D4: BuildPipelineConfig SO + TaskEntry

```csharp
public class BuildPipelineConfig : ScriptableObject
{
    public BackendMode DefaultBackendMode = BackendMode.ABManifest;
    public bool SequentialMode = false;          // Debug 回退模式
    public List<TaskEntry> Tasks = new();
}

[Serializable]
public class TaskEntry
{
    public string TaskName;
    public string ClassName;           // IBuildTask 实现类名，反射创建
    public bool Enabled = true;        // 扩展节点可禁用
    public List<string> DependsOn;     // TaskName 列表
}
```

- 6 骨干节点默认 Enabled=true 且不可禁用（调度器强制检查）
- 扩展节点默认 Enabled=false
- Data format intentionally supports future Pipeline panel blueprint-style visual editing
- SO stored at `Assets/Build/BuildPipelineConfig.asset` (path in Constants.cs)

### D5: DAG Scheduler — Kahn Algorithm + Conflict Detection

**Two-phase execution model:**

```
Phase 0: Wiring Validation (triggered on "Save" or explicit "Validate" action)
  ├── Check all DependsOn TaskName exist in Task list
  ├── Topological sort → detect circular dependency (CIRCULAR_TASK_DEPENDENCY)
  ├── Write-Write conflict: two Tasks declare same WriteKey → error, reject
  └── Read-before-Write: ReadKey not produced by any predecessor → warning

Real-time pairwise check when editing connections:
  └── Two nodes being connected → immediate Write-Write overlap check on WriteKeys

Phase 1: Execution
  ├── Compute indegree map (0 = no pending dependencies)
  ├── Batch loop:
  │   ├── Collect indegree=0 AND Enabled=true Tasks → current batch
  │   ├── Sort batch alphabetically by TaskName (deterministic order)
  │   ├── Execute each Task in batch sequentially
  │   ├── If any Task returns IsFatal=true:
  │   │   └── Mark remaining batches as Skipped, abort
  │   └── Decrement successors' indegree
  └── Return BuildResult (aggregated)
```

- Batch internal order: alphabetical by TaskName — deterministic, reproducible
- SequentialMode: ignores batches, executes all Tasks in topological order one at a time
- Same-key read-write (Read + Write same key by one Task after another wrote it): allowed — intentional augmentation pattern
- Must-save-before-run: Phase 0 re-runs on save; execution refuses if unsaved changes exist

### D6: BackendMode Switch

```csharp
public enum BackendMode
{
    LegacyAddressable = 0,  // 旧后端 — version_state based
    ABManifest = 1          // 新后端 — ABManifest based
}
```

- Stored: `BuildPipelineConfig.DefaultBackendMode`
- Wired: TaskPrepareContext reads config + command-line override → writes to BuildContext
- Locked: once written, immutable for the rest of the pipeline
- Task branching: `ctx.Require<BackendMode>(BuildContextKeys.BackendMode)` inside Execute body

### BuildContextKeys Constants

```csharp
public static class BuildContextKeys
{
    public const string BackendMode = "BackendMode";
    public const string BuildVersion = "BuildVersion";
    public const string OutputRoot = "OutputRoot";
    public const string TargetPlatform = "TargetPlatform";
    public const string CollectedAssets = "CollectedAssets";
    public const string BundleDependencyGraph = "BundleDependencyGraph";
    public const string BundleBuildResults = "BundleBuildResults";
    public const string ABManifest = "ABManifest";
    public const string OutputPath = "OutputPath";
}
```

---

## New Files

| File | Path | Assembly | Lines (est.) | Description |
|------|------|----------|-------------|-------------|
| BackendMode.cs | Build/ | Runtime | ~12 | Enum: LegacyAddressable, ABManifest |
| IBuildTask.cs | Build/Pipeline/Editor/ | Editor | ~20 | Interface: TaskName, DependsOn, ReadKeys, WriteKeys, Execute |
| BuildTaskResult.cs | Build/Pipeline/Editor/ | Editor | ~35 | Result class: Success, ErrorCode, ErrorMessage, Warnings, IsFatal |
| BuildResult.cs | Build/Pipeline/Editor/ | Editor | ~30 | Aggregated result: TotalTasks, CompletedTasks, SkippedTasks, TaskResults |
| BuildContext.cs | Build/Pipeline/Editor/ | Editor | ~60 | Type-safe generic key-value store: Get<T>, Set<T>, Require<T>, Has |
| BuildPipelineConfig.cs | Build/Pipeline/Editor/ | Editor | ~45 | SO: DefaultBackendMode, SequentialMode, List<TaskEntry> |
| DAGScheduler.cs | Build/Pipeline/Editor/ | Editor | ~280 | Kahn topological sort + batch execution + wiring validation + pairwise conflict check |

All paths relative to `Assets/FYAsset/Scripts/`.

---

## Modified Files

| File | Change | Risk |
|------|--------|------|
| Constants.cs | Add `BuildContextKeys` static class (10 key constants) + `PIPELINE_CONFIG_ASSET_PATH` | Low — additive |

---

## Task Breakdown

| Task | Content | Depends On |
|------|---------|-----------|
| E5-1-T1 | Create `BackendMode.cs` enum (Runtime assembly) | — |
| E5-1-T2 | Create `IBuildTask.cs` interface | — |
| E5-1-T3 | Create `BuildTaskResult.cs` + `BuildResult.cs` | — |
| E5-1-T4 | Create `BuildContext.cs` (type-safe generic store) | — |
| E5-1-T5 | Create `BuildPipelineConfig.cs` SO + `TaskEntry` | T2 |
| E5-1-T6 | Update `Constants.cs` — add `BuildContextKeys` + `PIPELINE_CONFIG_ASSET_PATH` | — |
| E5-1-T7 | Create `DAGScheduler.cs` — Phase 0: wiring validation (DependsOn existence, topological sort, cycle detection, Write-Write conflict, Read-before-Write warning) | T2, T3, T4, T5, T6 |
| E5-1-T8 | Create `DAGScheduler.cs` — Phase 1: Kahn batch execution (indegree map, batch loop, alphabetical sort, fatal abort, SequentialMode) | T7 |
| E5-1-T9 | Create `DAGScheduler.cs` — real-time pairwise conflict check (for future editor wiring) | T7 |
| E5-1-T10 | Compilation verification (`dotnet build XLuaHotfix.sln`) | All above |

---

## Invariants (Must Hold After E5-1)

1. `IBuildTask` interface compiles; can be implemented by any class
2. `BuildContext.Set<T>` / `Get<T>` / `Require<T>` type-safe access works; `Require` throws on missing key
3. `BuildTaskResult` factory methods produce correct Success/IsFatal states
4. `BuildPipelineConfig` SO can be created via Unity menu and serialized/deserialized correctly
5. `TaskEntry` fields (TaskName, ClassName, Enabled, DependsOn) serialize correctly in SO
6. `DAGScheduler.Validate` detects: missing DependsOn, circular dependencies, Write-Write conflicts
7. `DAGScheduler.Execute` correctly topologically sorts Tasks and executes in batch order
8. Alphabetical batch sort produces deterministic, reproducible execution order
9. Fatal Task failure → all remaining batches marked Skipped, `BuildResult.Success = false`
10. Non-fatal Task failure → execution continues, warnings aggregated
11. `SequentialMode = true` → all Tasks execute sequentially in topological order
12. `dotnet build XLuaHotfix.sln` passes with 0 errors

---

## Not In Scope

- Any IBuildTask implementation (backbone or extension)
- Pipeline panel visual editor
- Builder panel UI
- Command-line entry point

---

## Approval Checklist

- [ ] Agree to `IBuildTask` 4-field contract (TaskName, DependsOn, ReadKeys, WriteKeys)
- [ ] Agree to `BuildContext` type-safe generic access with centralized key constants
- [ ] Agree to `BuildTaskResult` with Fatal/Non-Fatal distinction
- [ ] Agree to `BuildPipelineConfig` SO + `TaskEntry` list (supports future visual editing)
- [ ] Agree to `DAGScheduler` two-phase: wiring validation (Phase 0) + Kahn batch execution (Phase 1)
- [ ] Agree to Write-Write conflict = error (reject wiring), Read-before-Write = warning
- [ ] Agree to real-time pairwise check (edit time) + full graph validation (save time) + must-save-before-run
- [ ] Agree to batch internal fixed alphabetical order
- [ ] Agree to `SequentialMode` debug fallback
- [ ] Agree to `BackendMode` enum in BuildPipelineConfig, locked at PrepareContext time
- [ ] Agree to same-key read-write (CollectedAssets augmentation) as non-conflict pattern
- [ ] Agree to 7 new files + 1 modified file + 10 tasks
