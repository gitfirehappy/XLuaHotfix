# Build Pipeline 构建管线

> 返回总览：[资源管理架构文档](./资源管理架构文档.md)

> **关联代码**
>
> `Assets/FYAsset/Scripts/Build/Pipeline/Editor/` · `Assets/FYAsset/Scripts/Build/BackendMode.cs`

---

## 概述

Build Pipeline 采用 **Task + DAG 拓扑调度** 模型。每个构建步骤（采集资产、分析依赖、打包 Bundle、生成清单等）实现为独立的 `IBuildTask`，通过 `DAGScheduler` 按依赖关系自动编排执行顺序。配置集中在 `BuildPipelineConfig` ScriptableObject 中。

---

## 核心概念

### IBuildTask — Task 接口

每个 Task 实现 `IBuildTask`，声明自己的身份、依赖关系和数据流：

```csharp
public interface IBuildTask
{
    string TaskName { get; }        // 唯一标识，如 "TaskBuildBundles"
    string[] DependsOn { get; }     // 前置依赖的 TaskName 列表
    string[] ReadKeys { get; }      // 从 BuildContext 读取的 Key
    string[] WriteKeys { get; }     // 向 BuildContext 写入的 Key
    BuildTaskResult Execute(BuildContext ctx);
}
```

实现要求：
- 无参公共构造函数（由 `BuildTaskResolver` 反射实例化）
- `TaskName` 全局唯一
- `Execute` 同步返回结果（Unity AssetBundle API 本身是同步的）

### BuildContext — 数据总线

Task 之间不直接通信，所有数据通过 `BuildContext` 传递。内部是 `Dictionary<string, object>`，提供类型安全的 `Set<T>` / `Get<T>` / `Require<T>` / `Has` 方法。

```
TaskA (WriteKeys: ["CollectedAssets"])     TaskB (ReadKeys: ["CollectedAssets"], WriteKeys: ["BundleGraph"])
    ↓                                            ↓
    ctx.Set("CollectedAssets", list)              list = ctx.Get<List<CollectedAssetInfo>>("CollectedAssets")
```

- `Get<T>` — Key 不存在返回 `default(T)`
- `Require<T>` — Key 不存在抛出 `KeyNotFoundException`
- `Has` — 检查 Key 是否存在

`ReadKeys` / `WriteKeys` 声明用于 DAG 调度器的静态校验，不是运行时强制。调度器在执行前检查 Read-before-Write 和 Write-Write 冲突。

### DAGScheduler — 调度器

基于 Kahn 拓扑排序算法，实现两阶段模型：

**Validate（校验阶段）**：
1. 依赖存在性 — 所有 `DependsOn` 指向的 Task 必须存在且已启用
2. 循环依赖检测 — Kahn 排序后剩余节点数 > 0 → 报告 `CIRCULAR_TASK_DEPENDENCY`
3. Write-Write 冲突 — 两个 Task 声明了相同的 `WriteKeys` → 报告 `CONFLICTING_WRITE_KEYS`
4. Read-before-Write 警告 — Task 读取的 Key 没有任何前置 Task 写入 → 报告 `UNSATISFIED_READ_KEY`（Warning，不阻断）

**Execute（执行阶段）**：
- 入度表驱动批循环：入度为 0 的节点形成当前批次
- 批内按 TaskName 字母序确定执行顺序（确定性）
- `SequentialMode = true` 时忽略批并发，逐 Task 串行执行
- Fatal 错误立即中止所有后续批次
- `BuildContextKeys` 常量类存储标准 Key 名称

### BackendMode — 后端模式

决定构建管线的数据源和输出格式：

| 值 | 含义 |
|----|------|
| `LegacyAddressable` | 基于 AAManifest 的旧版构建 |
| `ABManifest` | 基于 ABManifest 的新版构建（默认） |

由 `BuildPipelineConfig.DefaultBackendMode` 配置，CLI 可通过 `--backend` 覆盖。DAG 调度器通过 W-W 冲突检测确保一个 Task 独占写入 BackendMode 相关 Key。

---

## BuildPipelineConfig — 配置资产

ScriptableObject，存储路径 `Assets/Build/BuildPipelineConfig.asset`。

```
BuildPipelineConfig
├─ DefaultBackendMode    (ABManifest / LegacyAddressable)
├─ FileNameStyle         (BundleName / HashName / BundleName_HashName)
├─ SequentialMode        (Debug 串行模式)
└─ Tasks[]               (TaskEntry 列表)
     ├─ TaskName         ("TaskPrepareContext")
     ├─ Enabled          (true / false)
     └─ DependsOn[]      (SO 面板级依赖)
```

### BundleFileNameStyle

| 值 | 输出格式 |
|----|---------|
| `BundleName` | `{pkg}_{group}_{packKey}.bundle` |
| `HashName` | `{MD5}.bundle` |
| `BundleName_HashName` | `{pkg}_{group}_{packKey}_{MD5}.bundle`（默认） |

### DependsOn 合并

调度器合并两处来源的依赖声明：
1. `IBuildTask.DependsOn` — 程序级声明（强约束，代码中写死）
2. `TaskEntry.DependsOn` — SO 面板级声明（用户可在编辑器中追加额外依赖）

取并集、去重后作为该 Task 的完整依赖列表。只允许依赖 Enabled=true 的 Task。

---

## BuildTaskResolver — Task 发现

启动时扫描所有已加载程序集，找到所有 `IBuildTask` 的非抽象实现类，按 `TaskName` 构建 `Type` 索引并缓存。`CreateTask(taskName)` 通过 `Activator.CreateInstance` 实例化，重复调用返回新实例。

`BuildPipelineConfig.TaskEntry` 只存储 `TaskName` 字符串（不存 `ClassName`），因此类名修改不影响已有 SO 配置数据。

---

## BuildTaskResult / BuildResult

### BuildTaskResult — 单 Task 结果

通过静态工厂方法构造：

```csharp
// 成功
BuildTaskResult.Ok(warnings: new List<string> { "..." });

// 失败
BuildTaskResult.Fail("ERROR_CODE", "description", fatal: true);
```

`IsFatal = true` 的失败会中止调度器所有后续批次。`IsFatal = false` 仅记录错误，调度继续。

### BuildResult — 管线汇总

```
BuildResult
├─ Success        (所有 Task 成功且无 Fatal 中止)
├─ TotalTasks     (Enabled=true 的 Task 总数)
├─ CompletedTasks (成功数)
├─ SkippedTasks   (因前序 Fatal 跳过数)
└─ TaskResults[]  (逐个 Task 结果，按执行顺序)
```

---

## 调度流程

```
Validate
  ├─ 1. 依赖存在性
  ├─ 2. Kahn 拓扑排序 → 循环依赖检测
  ├─ 3. Write-Write 冲突
  └─ 4. Read-before-Write 警告
         ↓ 全部通过
Execute
  ├─ 构建入度表 + 后继表
  └─ while 剩余节点 > 0:
       ├─ 取入度=0的节点 → 当前批次
       ├─ 批内逐 Task 执行
       ├─ Fatal → 中止
       └─ 更新入度表 → 下一批
```

---

## 标准数据流 Key

`BuildContextKeys` 类集中管理 BuildContext 的标准 Key 名称：

| Key | 类型 | 写入者 | 消费者 |
|-----|------|--------|--------|
| `CollectedAssets` | `List<CollectedAssetInfo>` | TaskPrepareContext | TaskAnalyzeDependencies, TaskBuildBundles |
| `BundleDependencyGraph` | `BundleDependencyGraph` | TaskAnalyzeDependencies | TaskBuildBundles |
| `ABManifest` | `ABManifest` | TaskGenerateManifest | TaskOrganizeOutput |
| `BackendMode` | `BackendMode` | TaskPrepareContext | 所有模式感知 Task |
| `BuildVersion` | `VersionNumber` | TaskPrepareContext | TaskGenerateManifest |

---

## 现有 Task 列表

| TaskName | 职责 | 状态 |
|----------|------|------|
| `TaskPrepareContext` | 初始化 BuildContext（版本号、后端模式、收集资产） | 计划中 |
| `TaskAnalyzeDependencies` | 依赖分析与共享抽取 | 已落地 |
| `TaskBuildBundles` | 调用 Unity AssetBundle 构建 API | 计划中 |
| `TaskCollectBuiltins` | 自动收集 Shader 等内置资源 | 计划中 |
| `TaskVerifyBuildResult` | 构建结果 6 点校验 | 计划中 |
| `TaskGenerateManifest` | 生成 ABManifest + 序列化输出 | 计划中 |
| `TaskOrganizeOutput` | 组织输出目录结构 | 计划中 |
