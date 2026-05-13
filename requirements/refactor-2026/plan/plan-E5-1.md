# Sub-Plan E5-1: Build Pipeline Core Engine

> **Risk**: High (defines the execution framework that ALL subsequent Tasks plug into)
> **Dependencies**: E1-1 (data model, enums for Constants.cs)
> **Parent**: [plan-E5.md](plan-E5.md)
> **Status**: Realized — 2026-04-30, 8 files created + Constants.cs updated (later migrated to FYAssetConstants), build passes 0 errors, all 15 invariants met

---

## Objective

Implement build pipeline core: `IBuildTask` 4-field contract, `BuildContext` type-safe store, `BuildTaskResult`/`BuildResult`, `BuildPipelineConfig` SO + `TaskEntry`, `BuildTaskResolver` assembly scan, `BackendMode` enum with DAG-guaranteed exclusivity, `DAGScheduler` (Kahn sort + batch execution + validation + conflict detection), and `BuildContextKeys`. Contains ZERO Task implementations.

---

## Confirmed Design Decisions

### D1: IBuildTask 4-Field Contract

```csharp
public interface IBuildTask
{
    string TaskName { get; }         // 唯一标识，如 "TaskBuildBundles"
    string[] DependsOn { get; }      // 依赖的 TaskName 列表
    string[] ReadKeys { get; }       // 从 BuildContext 读取的 Key 声明
    string[] WriteKeys { get; }      // 向 BuildContext 写入的 Key 声明
    BuildTaskResult Execute(BuildContext ctx);
}
```

Execute 同步返回——Unity AssetBundle 构建 API 本身是同步的，async 无实际收益。

### D2: BuildContext Type-Safe Generic Access（已修改）

**修改**：去掉 `where T : class` 约束，内部用 `Dictionary<string, object>` 存储。值类型（enum、bool）可直接存取。

```csharp
public class BuildContext
{
    public void Set<T>(string key, T value);
    public T Get<T>(string key);
    public T Require<T>(string key);  // null → throw
    public bool Has(string key);
}
```

Keys 集中在 `Constants.BuildContextKeys` 静态类——Task 代码中不出现裸字符串。

### D3: BuildTaskResult — Fatal vs Non-Fatal

```csharp
public class BuildTaskResult
{
    public bool Success;
    public string ErrorCode;          // "CIRCULAR_TASK_DEPENDENCY"
    public string ErrorMessage;       // 人类可读
    public List<string> Warnings;     // 非致命警告
    public bool IsFatal;              // true → 调度器中止后续所有批次

    public static BuildTaskResult Ok(List<string> warnings = null);
    public static BuildTaskResult Fail(string code, string msg, bool fatal = true);
}
```

二分模型足够——管线是线性依赖链，中间断裂下游无意义。Non-Fatal 覆盖"有问题但继续"场景。

### D4: BuildPipelineConfig SO + TaskEntry（已修改）

**修改**：TaskEntry 移除 `ClassName` 字段。新增 `BuildTaskResolver` 启动时扫描 Assembly 找到所有 `IBuildTask` 实现，构建 `Dictionary<string, Type>` 按 TaskName 索引。类改名不影响 SO 数据。

```csharp
public enum BundleFileNameStyle
{
    BundleName = 0,           // pkg_group_packKey.bundle
    HashName = 1,             // {MD5}.bundle
    BundleName_HashName = 2,  // pkg_group_packKey_{MD5}.bundle (default)
}

public class BuildPipelineConfig : ScriptableObject
{
    public BackendMode DefaultBackendMode = BackendMode.ABManifest;
    public BundleFileNameStyle FileNameStyle = BundleFileNameStyle.BundleName_HashName;
    public bool SequentialMode = false;          // Debug 回退模式
    public List<TaskEntry> Tasks = new();
}

[Serializable]
public class TaskEntry
{
    public string TaskName;
    public bool Enabled = true;        // 6 骨干节点强制 true；扩展节点默认 false
    public List<string> DependsOn;     // TaskName 列表
}
```

- 6 骨干节点默认 Enabled=true 且不可禁用（调度器强制检查）
- 扩展节点默认 Enabled=false
- SO 存储路径：`Assets/Build/BuildPipelineConfig.asset`（常量 `Constants.PIPELINE_CONFIG_ASSET_PATH`）

**BuildTaskResolver**（新增）：
```csharp
public static class BuildTaskResolver
{
    // 启动时扫描，缓存 TaskName → Type
    public static void Initialize();                        // 扫描 Assembly，构建索引
    public static IBuildTask CreateTask(string taskName);   // 按名实例化
    public static bool Exists(string taskName);             // 用于 Validate
}
```

### D5: DAG Scheduler — Kahn Algorithm + Conflict Detection

**两阶段模型**：

```
Phase 0: Validate (保存 / 显式校验时触发)
  ├── 检查所有 DependsOn 的 TaskName 存在于 Task 列表
  ├── Kahn 拓扑排序 → 检测循环依赖 → CIRCULAR_TASK_DEPENDENCY
  ├── Write-Write 冲突：两个 Task 声明同一 WriteKey → 拒绝
  └── Read-before-Write：ReadKey 无前置 Task 生产 → Warning

Phase 1: Execute
  ├── 计算入度表（indegree=0 → 无未满足依赖）
  ├── 批循环：
  │   ├── 收集 indegree=0 且 Enabled=true 的 Task → 当前批
  │   ├── 批内按 TaskName 字母序排列（确定性可复现）
  │   ├── 顺序执行批内每个 Task
  │   ├── 任何 Task 返回 IsFatal=true → 剩余批标记 Skipped
  │   └── 递减后继 Task 的 indegree
  └── 返回 BuildResult（聚合全部结果）
```

**冲突检测规则**：
- Write-Write 冲突 → Error，拒绝执行
- Read-before-Write → Warning，继续执行
- 同 Key 读写（Read + Write 同一 Key，前一个 Task 已写过）→ **允许**（有意的 augmentation pattern，如 CollectedAssets 被 CollectAssets 写入后被 CollectBuiltins/AnalyzeDeps 读+写增强）
- SequentialMode：忽略批，按拓扑序逐个串行执行

**预留接口**（UI 在后续编辑器子计划实现）：
- `ValidatePair(taskA, taskB)` — 编辑连线时实时检查 WriteKeys 冲突
- `ValidateAll(tasks)` — 全图校验
- must-save-before-run：执行前检查是否有未保存的 SO 修改

### D6: BackendMode — DAG 结构保证独占写入

```csharp
public enum BackendMode
{
    LegacyAddressable = 0,  // version_state based
    ABManifest = 1          // ABManifest based
}
```

- 存储：`BuildPipelineConfig.DefaultBackendMode`（SO 默认值）
- 覆盖：命令行 `--backend LegacyAddressable|ABManifest`（CLI > SO）
- 写入：`TaskPrepareContext` 独占 `WriteKeys: [BackendMode, ...]`
- 保护：DAGScheduler W-W 冲突检测 → 任何其他 Task 声明 BackendMode 为 WriteKey 直接拒绝

不存在"运行时锁定"——DAG 拓扑结构本身就是锁。

---

## BuildContextKeys Constants

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
    // E5-2 addition:
    public const string BuildVerificationResult = "BuildVerificationResult";
}
```

---

## New Files

| # | File | Path | Assembly | Lines (est.) | Description |
|---|------|------|----------|-------------|-------------|
| 1 | BackendMode.cs | Build/ | Runtime | ~12 | Enum: LegacyAddressable, ABManifest |
| 2 | IBuildTask.cs | Build/Pipeline/Editor/ | Editor | ~20 | Interface: TaskName, DependsOn, ReadKeys, WriteKeys, Execute |
| 3 | BuildTaskResult.cs | Build/Pipeline/Editor/ | Editor | ~35 | Result: Success, ErrorCode, ErrorMessage, Warnings, IsFatal + factories |
| 4 | BuildResult.cs | Build/Pipeline/Editor/ | Editor | ~30 | Aggregated: TotalTasks, CompletedTasks, SkippedTasks, TaskResults |
| 5 | BuildContext.cs | Build/Pipeline/Editor/ | Editor | ~55 | Type-safe KV store: Get<T>, Set<T>, Require<T>, Has（无 class 约束） |
| 6 | BuildPipelineConfig.cs | Build/Pipeline/Editor/ | Editor | ~45 | SO: DefaultBackendMode, FileNameStyle, SequentialMode, List\<TaskEntry\> |
| 7 | BuildTaskResolver.cs | Build/Pipeline/Editor/ | Editor | ~50 | Assembly scan: TaskName → Type 索引 + CreateTask 工厂 |
| 8 | DAGScheduler.cs | Build/Pipeline/Editor/ | Editor | ~300 | Kahn 拓扑 + 批执行 + Validate 校验 + ValidatePair + ValidateAll + conflict detection |

Total: **8 new files**, ~547 lines estimated.

All paths relative to `Assets/FYAsset/Scripts/`.

---

## Modified Files

| File | Change | Risk |
|------|--------|------|
| Constants.cs | Add `BuildContextKeys` static class (10 key constants) + `PIPELINE_CONFIG_ASSET_PATH` | Low — additive |

---

## Task Breakdown

| # | Task | Content | Depends On |
|---|------|---------|-----------|
| E5-1-T1 | Create `BackendMode.cs` enum (Runtime assembly) | — |
| E5-1-T2 | Create `IBuildTask.cs` interface | — |
| E5-1-T3 | Create `BuildTaskResult.cs` + `BuildResult.cs` | — |
| E5-1-T4 | Create `BuildContext.cs` (type-safe store, no class constraint) | — |
| E5-1-T5 | Create `BuildPipelineConfig.cs` SO + `TaskEntry` (no ClassName) | T2 |
| E5-1-T6 | Create `BuildTaskResolver.cs` — assembly scan, TaskName → Type index, CreateTask | T2 |
| E5-1-T7 | Update `Constants.cs` — add `BuildContextKeys` + `PIPELINE_CONFIG_ASSET_PATH` | — |
| E5-1-T8 | Create `DAGScheduler.cs` — Phase 0: Validate (DependsOn existence, topological sort, cycle detection, W-W conflict, R-before-W warning) | T2, T3, T4, T5, T6, T7 |
| E5-1-T9 | Create `DAGScheduler.cs` — Phase 1: Execute (indegree map, batch loop, alphabetical sort, fatal abort, SequentialMode) | T8 |
| E5-1-T10 | Create `DAGScheduler.cs` — ValidatePair + ValidateAll public API | T8 |
| E5-1-T11 | Compilation verification (`dotnet build XLuaHotfix.sln`) | All above |

---

## Invariants (Must Hold After E5-1)

1. `IBuildTask` interface compiles; can be implemented by any class
2. `BuildContext.Set<T>` / `Get<T>` / `Require<T>` works with both reference and value types
3. `BuildTaskResult` factory methods produce correct Success/IsFatal states
4. `BuildPipelineConfig` SO can be created via Unity menu and serialized/deserialized correctly
5. `TaskEntry` fields (TaskName, Enabled, DependsOn) serialize correctly in SO
6. `BuildTaskResolver.Initialize()` discovers all IBuildTask implementations; `CreateTask` instantiates correctly
7. `DAGScheduler.Validate` detects: missing DependsOn, circular dependencies, Write-Write conflicts
8. `DAGScheduler.Execute` correctly topologically sorts Tasks and executes in batch order
9. Alphabetical batch sort produces deterministic, reproducible execution order
10. Fatal Task failure → all remaining batches marked Skipped, `BuildResult.Success = false`
11. Non-fatal Task failure → execution continues, warnings aggregated
12. `SequentialMode = true` → all Tasks execute sequentially in topological order
13. `ValidatePair` correctly detects W-W overlap between two hypothetical Task connections
14. BackendMode W-W exclusivity: DAGScheduler rejects any config where two Tasks declare BackendMode as WriteKey
15. `dotnet build XLuaHotfix.sln` passes with 0 errors

---

## Not In Scope

- Any IBuildTask implementation (backbone or extension)
- Pipeline panel visual editor (uses ValidatePair/ValidateAll from D5)
- Builder panel UI
- Command-line entry point

---

## Approval Checklist

- [x] Agree to `IBuildTask` 4-field contract (TaskName, DependsOn, ReadKeys, WriteKeys)
- [x] Agree to `BuildContext` type-safe generic access **without** `where T : class` constraint
- [x] Agree to `BuildTaskResult` with Fatal/Non-Fatal distinction
- [x] Agree to `BuildPipelineConfig` SO + `TaskEntry` list; **remove ClassName**, add `BuildTaskResolver` assembly scan
- [x] Agree to `DAGScheduler` two-phase: wiring validation (Phase 0) + Kahn batch execution (Phase 1)
- [x] Agree to Write-Write conflict = error (reject wiring), Read-before-Write = warning
- [x] Agree to pairwise check API (ValidatePair/ValidateAll) + must-save-before-run; UI deferred to editor sub-plan
- [x] Agree to batch internal fixed alphabetical order
- [x] Agree to `SequentialMode` debug fallback
- [x] Agree to `BackendMode` enum in BuildPipelineConfig; DAG W-W conflict detection guarantees single-writer exclusivity
- [x] Agree to same-key read-write (CollectedAssets augmentation) as non-conflict pattern
- [x] Agree to 8 new files + 1 modified file + 11 tasks

---

## Change Log

| Date | Change |
|------|--------|
| 2026-04-28 | Initial version: 7 design decisions, 7 new files, 10 tasks |
| 2026-04-29 | Approved with modifications: D2 drop `where T : class`; D4 remove ClassName, add BuildTaskResolver (assembly scan); D6 clarified (DAG W-W guarantees exclusivity, not runtime lock); +1 new file (BuildTaskResolver), +1 task; 15 invariants |
| 2026-04-30 | **Realized**: 8 files (608 lines) + Constants modified. Build verify: 0 errors. Minor deviation: DAGScheduler.Execute takes BuildContext not commandLineArgs; ValidatePair same-name guard fixed post-review. Constants later migrated to FYAssetConstants (separate plan). |
| 2026-04-29 | Approved with modifications: D2 drop `where T : class`; D4 remove ClassName, add BuildTaskResolver (assembly scan); D6 clarified (DAG W-W guarantees exclusivity, not runtime lock); +1 new file (BuildTaskResolver), +1 task; 15 invariants |
