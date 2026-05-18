# Plan E5: Build Pipeline Engine（父计划 — 总览）

> **Risk**: High
> **Status**: Container — 拆分为 E5-1 (Realized) + E5-2a (Realized) + E5-2b (Realized)。E5-2 原计划已 superseded

---

## 子计划

| 子计划 | 内容 | 文件 | 行数 | 依赖 |
|--------|------|------|------|------|
| **[plan-E5-1.md](plan-E5-1.md)** | 核心引擎：IBuildTask / BuildContext / BuildTaskResult / BuildPipelineConfig / DAGScheduler / BackendMode | 7 新 + 1 改 | ~490 | E1-1 |
| **[plan-E5-2a.md](plan-E5-2a.md)** | 骨干 Phase 1：TaskPrepareContext / TaskCollectBuiltins / TaskBuildBundles + BundleBuildInfo | 4 新 | ~335 | E5-1 + E1-3 + E4 |
| **[plan-E6.md](plan-E6.md)** | TaskGenerateManifest：组装 ABManifest + CRC32 校验 | 2 新 + 3 改 | ~240 | E5-2a |
| **[plan-E5-2b.md](plan-E5-2b.md)** | 骨干 Phase 2：TaskVerifyBuildResult / TaskOrganizeOutput | 2 新 | ~180 | E6 |

> E5-2 原计划已弃用，被 E5-2a + E5-2b 替代。执行顺序：E5-2a → E6 → E5-2b。

---

## 共享设计决策（D1-D8 适用两个子计划）

所有 8 个设计决策 D1-D8 在讨论中已确认，分别归入对应的子计划文档。此处保留 **D7 6 骨干节点契约**作为跨子计划的接口引用：

```
┌─────────────────────────┬──────────────────────┬──────────────────────────────────────┐
│ Task                    │ ReadKeys             │ WriteKeys                            │
├─────────────────────────┼──────────────────────┼──────────────────────────────────────┤
│ TaskPrepareContext      │ —                    │ BackendMode, BuildVersion,           │
│   [E5-2 实现]            │                      │ OutputRoot, TargetPlatform           │
├─────────────────────────┼──────────────────────┼──────────────────────────────────────┤
│ TaskCollectAssets       │ BackendMode          │ CollectedAssets                      │
│   [E1-3 实现]            │                      │ (List<CollectedAssetInfo>)           │
├─────────────────────────┼──────────────────────┼──────────────────────────────────────┤
│ TaskAnalyzeDependencies │ CollectedAssets      │ CollectedAssets (augmented),         │
│   [E4 实现]              │                      │ BundleDependencyGraph                │
├─────────────────────────┼──────────────────────┼──────────────────────────────────────┤
│ TaskBuildBundles        │ CollectedAssets,     │ BundleBuildResults                   │
│   [E5-2 实现]            │ BundleDependencyGraph│ (List<BundleBuildInfo>)              │
│                         │ OutputRoot,          │                                      │
│                         │ BackendMode          │                                      │
├─────────────────────────┼──────────────────────┼──────────────────────────────────────┤
│ TaskGenerateManifest    │ CollectedAssets,     │ ABManifest                           │
│   [E6 实现]              │ BundleBuildResults,  │ (含 AssetEntries + BundleEntries)     │
│                         │ BuildVersion         │                                      │
├─────────────────────────┼──────────────────────┼──────────────────────────────────────┤
│ TaskOrganizeOutput      │ ABManifest,          │ OutputPath                           │
│   [E5-2 实现]            │ BundleBuildResults,  │ (最终输出目录路径)                     │
│                         │ OutputRoot           │                                      │
└─────────────────────────┴──────────────────────┴──────────────────────────────────────┘
```

**Same-key read-write**: `CollectedAssets` 被 TaskCollectAssets 写入后被 TaskAnalyzeDependencies 读+写（原地增强）。这不是 Write-Write 冲突——是 intentional augmentation pattern。

---

## 管线执行流

```
DAGScheduler.Execute(config, commandLineArgs)
  │
  ├── Phase 0: Validate(config)
  │   ├── All DependsOn exist? → missing → error
  │   ├── Topological sort → cycle? → CIRCULAR_TASK_DEPENDENCY
  │   ├── Write-Write conflicts? → CONFLICTING_WRITE_KEYS
  │   └── Unsatisfied ReadKeys? → warn
  │
  ├── Phase 1: Execute batches
  │   ├── Batch 0: [TaskPrepareContext]
  │   ├── Batch 1: [TaskCollectAssets]
  │   ├── Batch 2: [TaskAnalyzeDependencies]
  │   ├── Batch 3: [TaskBuildBundles]
  │   ├── Batch 4: [TaskGenerateManifest]
  │   └── Batch 5: [TaskOrganizeOutput]
  │
  └── Return BuildResult
```

扩展节点（如 TaskGenerateSnapshot）声明 DependsOn 后由调度器自动计算拓扑位置。

---

## 执行顺序

1. **E5-1 先** — 定义所有 Task 的接口契约。仅依赖 E1-1
2. **E5-2 后** — 实现 3 个骨干 Task。需要 E5-1 + E1-3 + E4
3. E5-1 可与 E1-3/E4 并行开发

---

## 共同的不在范围

- Pipeline 面板蓝图可视化编辑器（后续编辑器子计划）
- TaskCollectAssets / TaskAnalyzeDependencies / TaskGenerateManifest 实现（E1-3 / E4 / E6）
- 扩展节点实现（TaskGenerateSnapshot / TaskPrepareDiff / TaskExportRuntimeIndex）
- Builder 面板 UI
- 命令行构建入口（现有系统，后续适配）
- 增量构建 / 缓存

---

## 关联文档

- [plan-E5-1.md](plan-E5-1.md) — 核心引擎详细计划
- [plan-E5-2.md](plan-E5-2.md) — 骨干 Task 详细计划
- [plan-E4.md](plan-E4.md) — TaskAnalyzeDependencies 实现
- [plan-E1-3.md](plan-E1-3.md) — TaskCollectAssets 上游
- [plan-E-draft.md](drafts/plan-E-draft.md) — E5 方向草案
