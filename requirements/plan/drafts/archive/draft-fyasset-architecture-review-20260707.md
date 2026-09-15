# Draft: FYAsset Architecture Review

**Date**: 2026-07-07  
**Status**: Draft  
**Category**: Architecture / Refactoring

## Overview

对当前 FYAsset 整体架构进行系统性审查，识别优化空间。本 Draft 记录讨论阶段的初步观察，具体 Plan 需逐项确认后单独立项。

## Promotion And Archive Update

The reduction/simplification items approved from this draft were extracted, executed, and archived:

| Item | Archived Plan | Status |
|------|-------------|------------------|
| A10 | `../archive/plan-hotfix-progress-steps-20260709.md` | Executed / Verified / Archived |
| A2 | `../archive/plan-linear-build-pipeline-runner-20260709.md` + `../archive/plan-pipeline-sequence-list-editor-20260709.md` | Executed / Verified / Archived |
| A4 | `../archive/plan-repository-slim-20260709.md` | Executed / Verified / Archived |
| A1 + A3 + A5 + A9 | `../archive/plan-aa-ab-shared-split-20260709.md` | Executed / Verified / Archived |

A9 is intentionally merged into the AA/AB/Shared split. Its retained decision is: keep three independent settings assets
and deduplicate `LoadOrCreate` through Shared. It is not a standalone settings merge/refactor plan.

A0/A12 preview cache, A6 HandleRegistry simplification, and A8 incremental build remain deferred and are not authorized
by the extracted plans.

## Current Architecture Summary

```
FYAsset 分层结构
├── AA
│   ├── Runtime (AAPackageManager + AddressablesBackend + AAManifest)
│   ├── Hotfix (AAHotfixManager + AAHotfixBackend + CatalogUpdater)
│   ├── Build (AABuildProjectManager + AA pipeline tasks + AA build backend)
│   └── Settings (FYAssetAASettings)
│
├── AB
│   ├── Runtime (ABPackageManager + ABPackageBackend + ABManifest + HandleRegistry models)
│   ├── Hotfix (ABHotfixManager + ABHotfixBackend)
│   ├── Build (ABBuildProjectManager + AB pipeline tasks + AB build backend)
│   ├── Collector (AssetsCollection, dependency analysis, collector UI)
│   └── Settings (FYAssetABSettings)
│
└── Shared
    ├── Runtime (contracts, resolver, PackageIndex, PackageManagerBase, RuntimeMessage, RuntimePathManager)
    ├── Hotfix (IHotfixPipeline, flow base, package validation, and DTOs)
    ├── Build (linear pipeline runner, common tasks, repository, snapshots, versioning, CLI/shared DTOs)
    ├── Compatibility (old AssetPackageManager/HotfixManager/BuildProjectManager facades)
    ├── Helpers
    └── Settings (FYAssetSettings + shared loader)
```

## Identified Optimization Areas

### Area 1: BuildProjectManager 职责过重

**当前问题：**
`BuildProjectManager` 单一静态类承担了：
- 版本号协调（BuildNextVersion + ApplyVersion）
- 后端路由（CreateBackend）
- 构建执行（RunBuild）
- Repository 提交（CommitBuildRepository）
- 产物发布（PublishBuildArtifacts）
- 失败处理（HandleFailedPackage + TryRollbackRepositoryHead）

**影响：**
- 单元测试困难（静态类 + 副作用耦合）
- 扩展新的构建类型需要修改核心类
- 失败回滚逻辑与构建逻辑混杂

**建议方向：**
拆分为职责更清晰的组件，或引入 Builder Pattern：
```
BuildOrchestrator.Build(request)
  -> VersionCoordinator.PrepareVersion()
  -> IBuildBackend.BuildAsync()
  -> RepositoryCoordinator.Commit()
  -> ArtifactPublisher.Publish()
```

**评估：** 改动面大，需专项 Plan。优先级 P2。

---

### Area 2: 双后端共存的复杂度

**当前问题：**
AB 和 AA 双后端并行维护，大量逻辑通过 `FYAssetSettings.UseABBackend` 判断分叉：
- `BuildProjectManager.CreateBackend()`
- `AssetPackageManager` 中多处 `if (UseABBackend)` 分支
- 两套独立的 HotfixBackend 实现

**影响：**
- 新功能需要同时实现 AB 和 AA 两套逻辑
- 测试复杂度翻倍
- 难以添加第三种后端

**建议方向：**
明确当前项目的长期策略：
- 若 **最终统一到 AB**：逐步废弃 AA 后端，减少维护负担
- 若 **长期维持双后端**：引入 Strategy Pattern 消除分散的 if/else 判断

**评估：** 需要先明确产品方向决策，再评估工作量。

---

### Area 3: 构建产物路径管理分散

**当前问题：**
构建产物路径由多处管理：
- `BuildPathManager`：定义基础路径常量
- `BuildPackageRequest`：负责具体构建目录
- `TaskExportLocalBuildData`：输出 LocalBuildData
- `TaskWritePackageIndex`：输出 PackageIndex
- `BuildRepositoryFacade`：Repository 路径

**影响：**
- 修改输出目录结构需要改多处
- 路径逻辑散落在 Task 内部，难以全局把控

**建议方向：**
引入 `BuildArtifactRegistry`，集中声明所有产物路径：
```csharp
public class BuildArtifactRegistry
{
    public string PackageDir { get; }
    public string ManifestPath { get; }
    public string PackageIndexPath { get; }
    public string RepositoryPath { get; }
}
```

**评估：** 中等工作量，可作为独立清理 Plan。优先级 P2。

---

### Area 4: Task 间数据耦合

**当前问题：**
`BuildContext` 作为数据共享容器，Task 间通过字符串键或 `object` 类型共享数据：

```csharp
// 疑似现有模式（待验证）
context.Set("BundleManifest", manifest);
var manifest = context.Get<ABManifest>("BundleManifest");
```

如果使用类型不安全的字典，运行时才能发现类型错误。

**建议方向：**
确认 BuildContext 当前实现，若存在类型安全问题，引入强类型 Slot：
```csharp
public static class BuildContextKeys
{
    public static readonly ContextKey<ABManifest> BundleManifest = new();
    public static readonly ContextKey<BuildPackageRequest> PackageRequest = new();
}
```

**评估（已核查）：** BuildContext 实现约40行，`Dictionary<string,object>` + 泛型转换，已足够类型安全。**无需改造。** 原 Area 4 关闭。

---

### Area 4-NEW: DAGScheduler 应用于线性链（过度设计）

**代码审查结果：**

提取全部 Task 的 DependsOn，AB Pipeline 的实际依赖图：
```
TaskPrepareContext → TaskCollectAssets → TaskCollectBuiltins
  → [TaskAnalyzeDependencies*] → TaskBuildBundles
  → TaskGenerateManifest → TaskVerifyBuildResult
  → TaskScanABHotfixDiff → TaskOrganizeOutput
  → TaskWriteABPackageManifest
```
\* `TaskAnalyzeDependencies` 被 `TaskBuildBundles.DependsOn` 引用，但在当前文件列表中不存在，需确认是否为失效引用。

**结论：这是一条完全线性的链，不存在任何并行分支或合流节点。**

DAGScheduler 的实际付出（Kahn 拓扑排序、循环检测、Backbone 校验、Read-before-Write 校验、whitelist 过滤）在线性链上全部变为无意义开销。

对比行业方案：
- Unity ScriptableBuildPipeline：`IBuildTask[]` 数组顺序执行
- YooAsset：直接方法调用，无调度抽象
- FYAsset：Kahn 算法 + 多层校验 → **约300行调度代码服务10个Task的线性链**

**建议方向（P2）：**
若未来不会出现真正的并行分支，将DAGScheduler 退化为`BuildPipelineRunner`（简单数组遍历），保留 `stopAfterTaskName` 和 `whitelist` 过滤即可。估时0.5天。

---

### Area 5-NEW: RepositoryPreviewRunner 触发完整构建（P1 性能问题）

**代码审查结果（最重要发现）：**

`RunABPreviewDetailed` 的 Task whitelist：
```csharp
var whitelist = new HashSet<string>
{
    "TaskPrepareContext", "TaskCollectAssets", "TaskCollectBuiltins",
    "TaskAnalyzeDependencies", "TaskBuildBundles",  // ← 调用 BuildPipeline.BuildAssetBundles!
    "TaskGenerateManifest", "TaskVerifyBuildResult", "TaskScanABHotfixDiff"
};
```

**每次用户点击"Refresh Staging"或"Preview Delivery"，都触发完整的`BuildPipeline.BuildAssetBundles`调用。** 输出到临时目录后再删除。

这也是**问题1（Broken PPtr）出现两次的直接原因**——两次 Preview 各触发一次完整构建，损坏引用的警告各出现一次。

行业对比：
- **YooAsset**：Bundle Hash 比对，无需重新构建
- **Addressables**：Content Catalog Hash 比对，无需重建
- **FYAsset Preview**：完整构建 → 对比 Hash → 删除临时目录（最慢路径）

**精简方向（P1，工作量较大）：**
1. 构建后将 Bundle → Hash 映射缓存到磁盘（`build-hashes.json`）
2. Preview 时只需读取缓存 + 对比当前 Repository HEAD，无需重建
3. 缓存失效条件：手动构建后自动刷新

---

### Area 6: 缺少构建缓存/增量构建支持

**当前问题：**
除 Hotfix 的 AB Diff 外，没有增量构建机制：
- 每次 Full Build 完整重新处理所有资源
- Prefab/Texture 等未变化资源仍会重新打包
- 构建时间随资源量线性增长

**建议方向：**
- 短期：在 TaskBuildBundles 前增加 Asset Hash 比对，跳过未变化资源
- 长期：引入构建缓存系统，存储 Bundle 的内容哈希

**评估：** 工程量较大，需要专项调研。优先级 P3。

---

### Area 7-NEW: HotfixManager 硬编码步骤数（P3 可维护性）

**代码审查结果：**
```csharp
private const int TotalSteps = 11;  // HotfixManager.cs
```

进度计算依赖 `_currentStepIndex / TotalSteps`。如果增删步骤，需手动同步这个常量，否则进度条比例错误。

**YooAsset 对比：**
- YooAsset 用 `IEnumeratorOperation` / `float Progress` 属性
- 每个操作自报告进度，无需中心化步骤计数

**建议：** P3 优先级，当前不影响功能，但未来扩展时易出错。

---

### Area 8-NEW: Settings 分散为3个 Singleton（P2 清理）

**现状：**
| Settings类 | 职责 | 路径 |
|------------|------|------|
| `FYAssetSettings` | 全局 + Backend开关 | `Assets/Resources/FYAssetSettings.asset` |
| `FYAssetABSettings` | AB 热更URL + 构建路径 | `Assets/Resources/FYAssetABSettings.asset` |
| `FYAssetAASettings` | AA 热更URL + 构建路径 | `Assets/Resources/FYAssetAASettings.asset` |

**问题：**
1. **50+ 行 `LoadOrCreate()` 模式重复3次**
2. **调用方需条件判断才能取到正确设置**：
   ```csharp
   string url = FYAssetSettings.Instance.UseABBackend
       ? FYAssetABSettings.Instance.HotfixUrl
       : FYAssetAASettings.Instance.HotfixUrl;
   ```
3. **3个 SO 全部打包进 Resources/**：包含未使用的后端设置

**YooAsset 对比：**
- 单一 `YooAssetSettingsData`，统一管理

**建议（P2）：** 合并为单一 `FYAssetSettings`，AB/AA 特定配置作为内嵌字段。估时0.5天。

---

### Area 9-NEW: Collector 层复杂度（合理，但需记录）

**统计：** 25个文件（Core 5 + Rules 4 + DependencyAnalysis 3 + UI 5 + Utilities 8）

这是一个**完整的 Collector 系统**，与 YooAsset Collector 规模相当。复杂度是合理的，因为需要支持：
- Glob pattern 匹配
- Filter/Group 规则系统
- 依赖分析
- UI Inspector 集成
- 路径反向索引

**无问题，仅作架构记录。**

---

### Area 6: Repository 系统的双重职责

**当前问题：**
Repository 系统同时承担：
1. **版本控制**：记录构建历史、支持 Rollback
2. **分发准备**：生成 CDN 可用的产物目录结构

两个职责耦合在 `FileBuildRepository` 中，导致：
- 版本控制逻辑与分发格式互相影响
- 单元测试需要同时 Mock 存储和分发逻辑

**建议方向：**
评估是否将分发准备逻辑（生成 CDN 目录）提取为独立的 `DistributionExporter`。

**评估：** 概念验证阶段，需要更深入的代码审查。优先级 P3。

## Action Items (已更新——基于代码审查)

| # | Area | 核心问题 | Action | Priority | Est. Effort |
|---|------|---------|--------|----------|-------------|
| A0 | RepositoryPreviewRunner | Preview = 完整构建，性能极差 | 缓存 build-hashes.json，Preview 读缓存替代重建。**已确认问题，暂缓决策** | **P1（已确认，暂缓）** | 2-3 人日 |
| A1 | 双后端策略 | 维护成本翻倍，无切换场景 | **已执行归档**：见 `../archive/plan-aa-ab-shared-split-20260709.md`，AB/AA 拆分为独立框架，AA 不废弃 | **P1（已完成）** | N/A |
| A2 | DAGScheduler / GraphView | Kahn算法和 GraphView 服务线性链 | **已执行归档**：见 `../archive/plan-linear-build-pipeline-runner-20260709.md` 和 `../archive/plan-pipeline-sequence-list-editor-20260709.md` | P2 | 0.5 人日 + follow-up |
| A3 | AssetPackageManager | Singleton 包装多余 | **已执行归档**：随 `../archive/plan-aa-ab-shared-split-20260709.md` 拆分为 ABPackageManager 和 AAPackageManager | P2 | 1 人日 |
| A4 | Repository Mini-VCS | 健康检查/修复/推送历史超出需求 | **已执行归档**：见 `../archive/plan-repository-slim-20260709.md` | P2 | 1.5 人日 |
| A5 | BuildProjectManager | 职责过重（5项职责） | **已执行归档**：随 `../archive/plan-aa-ab-shared-split-20260709.md` 保留串联总线定位并消除主线 UseABBackend 路由 | P2（随A1） | 0.5 人日 |
| A6 | HandleRegistry 世代号 | C# 不需要世代号，AA 双重引用计数 | **已确认**：去掉世代号简化引用计数；AA 路径直接返回 AA OperationHandle | P3 | 1 人日 |
| A7 | TaskAnalyzeDependencies | ~~被引用但不在代码库~~ **已确认**：在 `Collector/Editor/DependencyAnalysis/`，非失效引用 | 关闭 | ~~P1（验证）~~ **N/A** | 0 |
| A8 | 增量构建 | 每次 Full Build 重处理全部资源 | **已记录**：专项调研后决策 | P3（待调研） | TBD |
| A9 | Settings 分散 | 3个 Singleton SO，LoadOrCreate 重复，调用方条件判断 | **已执行归档**：随 `../archive/plan-aa-ab-shared-split-20260709.md` 提取 LoadOrCreate 共用基类/工具 | P2 | 0.5 人日 |
| A10 | HotfixManager TotalSteps | 硬编码常量，扩展时易出错 | **已执行归档**：见 `../archive/plan-hotfix-progress-steps-20260709.md` | P3 | 0.5 人日 |
| A11 | Collector 层 | 25文件，复杂度合理 | 无需改造，仅记录 | ✅ 无 | — |
| A12 | BuildRepositoryCLI `diff` | CLI diff 同样触发完整 AB 构建 | 随 A0 缓存方案一并修复 | P1（随A0） | 含在A0内 |

## 深度审查完成总结（基于逐文件代码审查）

| 优先级 | 编号 | 问题 | 影响 |
|--------|------|------|------|
| **P1** | A0+A12 | Preview / CLI diff = 完整AB构建 | 日常刷新极慢，Broken PPtr 反复出现 |
| **P1** | A1 | 双后端策略决策缺失 | 阻塞多项精简工作 |
| **P2** | A2 | DAGScheduler Kahn算法服务线性链 | 300行复杂度，无并行收益 |
| **P2** | A3 | AssetPackageManager Singleton包装 | 无切换场景，查询缓存重复 |
| **P2** | A4 | Repository Mini-VCS超出需求 | Health/Repair/PushHistory 维护负担 |
| **P2** | A5 | BuildProjectManager 5职责混合 | 测试困难，扩展需改核心类 |
| **P2** | A9 | 3个 Settings Singleton 分散 | LoadOrCreate重复，3个SO全打包进 Resources |
| **P3** | A6 | HandleRegistry 世代号 | AA路径双重引用计数 |
| **P3** | A10 | HotfixManager TotalSteps 硬编码 | 扩展时进度条比例易出错 |
| **✅** | A7 | TaskAnalyzeDependencies | 非失效引用，在 Collector/ 目录下 |
| **✅** | A11 | Collector 层 | 25文件，复杂度合理，无需改造 |

## 逐项确认结果（2026-07-07）

| 编号 | 决策 | 执行状态 |
|------|------|---------|
| A0 | Preview 缓存方案（完整构建 + Broken PPtr） | 确认问题，**暂缓立项** |
| A1 | AB/AA 彻底拆分独立框架，不共用接口，AA 不废弃 | **已执行并归档到 `../archive/plan-aa-ab-shared-split-20260709.md`** |
| A2 | DAGScheduler → 线性 BuildPipelineRunner（保留 stop/whitelist）；GraphView → Task 顺序列表 | **已执行并归档到 `../archive/plan-linear-build-pipeline-runner-20260709.md` 与 `../archive/plan-pipeline-sequence-list-editor-20260709.md`** |
| A3 | AssetPackageManager → ABPackageManager + AAPackageManager 独立 | **已随 `../archive/plan-aa-ab-shared-split-20260709.md` 完成** |
| A4 | 保留 Health；删除 Repair/Quarantine；Push 简化为单次状态（无历史） | **已执行并归档到 `../archive/plan-repository-slim-20260709.md`** |
| A5 | BuildProjectManager 保留串联总线定位，随 A1 消除路由判断 | **已随 `../archive/plan-aa-ab-shared-split-20260709.md` 完成** |
| A6 | HandleRegistry 去掉世代号，AA 路径直接返回 AA Handle | **已确认，待立项** |
| A7 | TaskAnalyzeDependencies 在 `Collector/Editor/DependencyAnalysis/` | **关闭** |
| A8 | 增量构建 | **记录问题，待调研** |
| A9 | Settings 文件保持独立，提取 LoadOrCreate 共用基类消除重复 | **已随 `../archive/plan-aa-ab-shared-split-20260709.md` 完成** |
| A10 | HotfixManager 改为动态步骤自报告进度 | **已执行并归档到 `../archive/plan-hotfix-progress-steps-20260709.md`** |
| A11 | Collector 层 25文件复杂度合理 | **关闭** |
| A12 | CLI diff 随 A0 缓存方案一并修复 | **随 A0 执行** |

## Next Steps（已更新）

**已执行、验证并归档：**
1. A10：`../archive/plan-hotfix-progress-steps-20260709.md`
2. A2：`../archive/plan-linear-build-pipeline-runner-20260709.md` + `../archive/plan-pipeline-sequence-list-editor-20260709.md`
3. A4：`../archive/plan-repository-slim-20260709.md`
4. A1/A3/A5+A9：`../archive/plan-aa-ab-shared-split-20260709.md`

**暂缓（待时机）：**
5. A0 Preview 缓存方案
6. A6 HandleRegistry 简化
7. A8 增量构建调研

## Open Questions（已更新）

1. ~~BuildContext 类型安全？~~ **已确认：安全，无需改造**
2. ~~TaskAnalyzeDependencies 失效引用？~~ **已确认：有效，在 Collector/ 目录**
3. ~~AB/AA 长期策略？~~ **已确认：彻底拆分，AA 不废弃**
4. ~~Repository Health/Repair/PushHistory 取舍？~~ **已确认：保留 Health，删除 Repair，简化 Push**
5. **A0 Preview 缓存方案何时立项？**（暂缓，需时机确认）
6. ~~Repository Repair/PushHistory 是否有 CI/CD 主动使用？~~ **已确认删除，当前无持久化 PushHistory。**
