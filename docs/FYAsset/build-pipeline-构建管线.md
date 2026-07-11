# Build Pipeline 构建管线

> 返回总览：[资源管理架构文档](./资源管理架构文档.md)

> **关联代码**
>
> `Assets/FYAsset/Scripts/Shared/Build/Pipeline/Editor/` · `Assets/FYAsset/Scripts/Shared/Build/BackendMode.cs`

---

## 概述

Build Pipeline 采用 **Task + 线性执行列表** 模型。每个构建步骤（采集资产、分析依赖、打包 Bundle、生成清单等）实现为独立的 `IBuildTask`，通过 `BuildPipelineRunner` 按 `BuildPipelineConfig.Tasks` 中的启用顺序执行。配置集中在 `BuildPipelineConfig` ScriptableObject 中。

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

`ReadKeys` / `WriteKeys` 声明用于 runner 静态校验和诊断展示，不是运行时强制。`WriteKeys` 表示 Task 会写入或更新该 Key，不表示独占写锁；runner 在执行前检查依赖存在性、stop/whitelist 有效任务集和 Read-before-Write 警告。

### BuildPipelineRunner — 线性执行器

按配置列表顺序实现两阶段模型：

**Validate（校验阶段）**：
1. 依赖顺序 — 所有 `IBuildTask.DependsOn` 指向的 Task 必须存在、已启用，并且出现在当前 Task 之前
2. stop-after / whitelist 校验 — 只校验本次实际会执行的有效任务集
3. Read-before-Write 警告 — Task 读取的 Key 没有任何前序 Task 写入 → 报告 `UNSATISFIED_READ_KEY`（Warning，不阻断）

**Execute（执行阶段）**：
- 按 `BuildPipelineConfig.Tasks` 顺序逐个执行
- 执行运行在 Unity Editor 主线程上，确定性串行执行；不存在额外的并行/串行切换开关
- Fatal 错误立即中止所有后续 Task
- `stopAfterTaskName` 命中后提前停止，已执行 Task 产出的 `BuildContext` 数据可被调用方读取
- `taskWhitelist` 可限制本次只执行指定 Task 集合，常用于 Diff Preview
- `BuildContextKeys` 常量类存储标准 Key 名称

### BackendMode — 后端模式

决定构建管线的数据源和输出格式：

| 值 | 含义 |
|----|------|
| `AA` | 基于 Addressables 的 AA 构建 |
| `ABManifest` | 基于 ABManifest 的自研构建（显示名为 AB） |

正式 Full/Hotfix 构建由 AA/AB concrete build manager 创建 `BuildPackageRequest` 时显式决定。Repository CLI 的 `-backend` 只用于选择仓库通道，不覆盖正式构建后端。`BackendMode.AA` 与 `BackendMode.ABManifest` 分别对应两条构建管线，显示名分别为 `AA` / `AB`，各有独立的 `BuildPipelineConfig` 资产。

### Editor Layout

Build Pipeline 编辑器现在提供两个可同时打开的独立窗口：

- `Tools/Build/AA Build Pipeline`：Settings、AA Config、AA Build、AA Build Results、AA Repository、Version。
- `Tools/Build/AB Build Pipeline`：Settings、AB Config、AssetsCollection、AB Build、AB Build Results、AB Repository、Version。
- 旧 `Tools/Build/Build Pipeline` 菜单保留为兼容入口，根据 `UseABBackend` 打开 AA 或 AB 窗口。

两个窗口中的构建按钮分别直达 `AABuildProjectManager` 和 `ABBuildProjectManager`，不再由 `UseABBackend` 互斥置灰。`UseABBackend` 只保留给旧兼容入口与命令行路由。AA/AB Repository 都使用可拖动的左/中/右三栏布局，两条分隔线宽度按 backend 分别保存在 EditorPrefs 中。

---

## BuildPipelineConfig — 配置资产

ScriptableObject，存储路径 `Assets/Build/BuildPipelineConfig.asset`。

```
BuildPipelineConfig
├─ FileNameStyle         (BundleName / HashName / BundleName_HashName)
├─ BundleCompression     (LZ4 / LZMA / Uncompressed，默认 LZ4)
└─ Tasks[]               (TaskEntry 列表)
     ├─ TaskName         ("TaskPrepareContext")
     └─ Enabled          (true / false)
```

### BundleFileNameStyle

| 值 | 输出格式 |
|----|---------|
| `BundleName` | `{pkg}_{group}_{packKey}.bundle` |
| `HashName` | `{MD5}.bundle` |
| `BundleName_HashName` | `{pkg}_{group}_{packKey}_{MD5}.bundle`（默认） |

### DependsOn 顺序护栏

`TaskEntry` 不再保存 SO 面板级依赖。执行顺序只由 `BuildPipelineConfig.Tasks` 的列表顺序决定。

`IBuildTask.DependsOn` 保留为最小校验护栏：如果某个 Task 声明依赖另一个 Task，runner 会要求该依赖存在、已启用，并且位于当前 Task 之前。它不做拓扑排序，也不会改变执行顺序。

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

`IsFatal = true` 的失败会中止 runner 后续 Task。`IsFatal = false` 仅记录错误，调度继续。

### BuildResult — 管线汇总

```
BuildResult
├─ Success        (所有 Task 成功且无 Fatal 中止)
├─ TotalTasks     (参与调度的 Task 总数)
├─ CompletedTasks (成功数)
├─ SkippedTasks   (因 Fatal 或 stop-after 未执行的 Task 数)
└─ TaskResults[]  (逐个 Task 结果，按执行顺序)
```

---

## 跳过与提前终止规则

构建管线区分三类情况：Task 内部 no-op 跳过、runner 提前终止、错误中止。跳过必须保持数据不污染：只读预览不能写 `PackageIndex`、repository HEAD / objects 或正式输出目录；Task 内 no-op 只能返回成功，不能留下半成品状态。

| 场景 | 机制 | 结果 |
|------|------|------|
| AA Diff Preview | runner whitelist 只允许 `TaskScanAddressableHotfixDiff`，并在该 Task 后 stop-after | 只计算 `ArtifactDelta`，不移动 group、不构建、不写 PackageIndex、不提交 repository |
| AB Diff Preview | runner whitelist 允许 AB 构建到 `TaskScanABHotfixDiff`，并在该 Task 后 stop-after | 使用 `Temp/BuildRepositoryPreview/{guid}` 临时输出，finally 清理，不写正式 PackageIndex/HEAD/objects；展示 HEAD Diff 和 Full-baseline Hotfix Delivery 两组信息 |
| AA Full Build | `TaskScanAddressableHotfixDiff` 和 `TaskMoveAddressableHotfixGroups` 内按 `BuildType` 返回成功跳过 | Full 不做 hotfix diff/group move，但继续后续构建 |
| Full Build 本地启动数据 | `TaskExportLocalBuildData` 只在 `BuildType.Full` 执行 | 写 `BuildIndex` 和当前后端 baseline 到 `StreamingAssets`；AB 复制 `ABManifest + bundles`，AA 复制 `AAManifest` 查询索引 |
| Hotfix Build 本地启动数据 | `TaskExportLocalBuildData` 在 `BuildType.Hotfix` 返回成功跳过 | Hotfix 不覆盖整包启动数据 |
| AA Hotfix 无差异 | diff Task 写空 `ArtifactDelta`，group move Task no-op 成功 | 继续构建，确认无变更流程仍正确 |
| AB Hotfix 无差异 | `TaskScanABHotfixDiff` 写入空 `ABDeliveryBundles` 并返回成功 | 后续 organize/manifest/PackageIndex 仍按官方构建执行，输出 manifest-only Hotfix 包 |
| AB Hotfix 缺 Full baseline | `TaskScanABHotfixDiff` fatal fail | 缺少同 Channel/Backend/Major 且 `BuildType == Full` 的 baseline 时不进入 package finalization |
| `PackageIndex` 写入 | `TaskWritePackageIndex` 在官方 Full/Hotfix runner 中执行 | `PackageIndex` 是远端最新包指针，不是 Full-only 数据；Diff Preview 早停不会执行它 |
| Fatal Task 失败 | `BuildTaskResult.Fail(..., fatal: true)` | 调度器停止后续 Task，剩余 Task 标记 Skipped |
| runner 校验失败 | Validate 阶段阻断 | 不执行任何 Task |
| AA pending group move | `TaskMoveAddressableHotfixGroups` 检测 undo log 并 fatal fail | 要求先手动 reset，避免覆盖原始 group 归属 |
| AB 手动 reset | `ResetGroupsToOriginal()` 检测 AB backend | 直接跳过并提示，因为 AB 没有 Addressables group move |

---

## 路径规范

- `BuildConfig.OutputRoot` 在创建时解析为规范本地路径；CLI `--output`、Diff Preview 输出根和默认输出根进入后续 Task 前都会经过统一解析。
- 远端 URL 只使用 `FYAssetPathUtility.JoinUrl(...)` 拼接。
- 构建输出、临时目录、包体目录、manifest、bundle、`StreamingAssets` 导出等本地路径使用 `FYAssetPathUtility.JoinFilePath(...)` / `ResolveFilePath(...)`。
- Unity `AssetDatabase` 路径保持 `Assets/...` 和 `/` 分隔符，通过 `NormalizeAssetPath(...)` / `JoinAssetPath(...)` 处理。

---

## 执行流程

```
Validate
  ├─ 1. IBuildTask.DependsOn 顺序校验
  ├─ 2. stop-after / whitelist 有效任务集校验
  └─ 3. Read-before-Write 警告
         ↓ 全部通过
Execute
  └─ 按配置顺序遍历 Enabled Task:
       ├─ whitelist 不包含 → 跳过
       ├─ 逐 Task 执行
       ├─ stop-after 命中 → 停止
       └─ Fatal → 中止
```

---

## 标准数据流 Key

`BuildContextKeys` 类集中管理 BuildContext 的标准 Key 名称：

| Key | 类型 | 写入者 | 消费者 |
|-----|------|--------|--------|
| `BuildPackageRequest` | `BuildPackageRequest` | AB/AA Backend | 所有 Task |
| `BuildType` | `BuildType` | AB/AA Backend | TaskScan*, TaskExport*, TaskMove* |
| `CollectedAssets` | `List<CollectedAssetInfo>` | TaskCollectAssets, TaskCollectBuiltins | TaskAnalyzeDependencies, TaskBuildBundles, TaskGenerateManifest |
| `SharePolicies` | `Dictionary<string, SharePolicyConfig>` | TaskCollectAssets | TaskAnalyzeDependencies |
| `BundleDependencyGraph` | `BundleDependencyGraph` | TaskAnalyzeDependencies | TaskBuildBundles, TaskGenerateManifest |
| `BundleBuildResults` | `List<BundleBuildInfo>` | TaskBuildBundles | TaskGenerateManifest, TaskVerifyBuildResult |
| `ABManifest` | `ABManifest` | TaskGenerateManifest | TaskVerifyBuildResult, TaskScanABHotfixDiff, TaskOrganizeOutput, TaskWriteABPackageManifest |
| `ABDeliveryBundles` | `List<ManifestBundleEntry>` | TaskScanABHotfixDiff | TaskOrganizeOutput, TaskWriteABPackageManifest |
| `BuildVerificationResult` | `BuildVerificationResult` | TaskVerifyBuildResult | TaskOrganizeOutput |
| `OutputPath` | `string` | TaskOrganizeOutput / TaskOrganizeAAOutput | TaskWrite*Manifest, TaskExportLocalBuildData |
| `ArtifactDelta` | `ArtifactDelta` | TaskScan*HotfixDiff | TaskMoveAddressableHotfixGroups, BuildProjectRunner |
| `RepositoryArtifacts` | `List<ArtifactDigest>` | TaskScan*HotfixDiff | AB/AA Backend (for commit) |
| `AAServerDataPath` | `string` | TaskBuildAddressablesContent | TaskOrganizeAAOutput |
| `AAManifest` | `AAManifest` | TaskWriteAAPackageManifest | (context) |
| `RepositoryPreviewOutput` | `string` | RepositoryPreviewRunner | TaskPrepareContext (预览模式) |

---

## 现有 Task 列表

### AB 管线（12 个 Task）

| TaskName | 职责 | 依赖 |
|----------|------|------|
| `TaskPrepareContext` | 初始化 BuildContext（读取 BackendMode、Version、OutputRoot、TargetPlatform；正式构建后端来自 BuildPackageRequest） | — |
| `TaskCollectAssets` | 加载 AssetCollectionSetting、运行 CollectionScanner、写入 CollectedAssets 和 SharePolicies | TaskPrepareContext |
| `TaskAnalyzeDependencies` | BFS 依赖扫描、共享资产提取、构建 BundleDependencyGraph | TaskCollectAssets |
| `TaskCollectBuiltins` | 自动收集 Shader 和 Resources 内置资源，追加到 CollectedAssets | TaskCollectAssets |
| `TaskBuildBundles` | 按 PayloadKind 分流构建（Serialized → AB, Scene → 独立, RawFile → 拷贝），输出 BundleBuildResults | TaskAnalyzeDependencies |
| `TaskGenerateManifest` | 生成 ABManifest（AssetEntries + BundleEntries + 依赖索引 + BundleType 推断），调用 Initialize() | TaskBuildBundles |
| `TaskVerifyBuildResult` | 6 项校验：文件存在性、UnityFS 魔数完整性、孤立文件、Hash 重算、大小异常、计数交叉检查 | TaskGenerateManifest |
| `TaskScanABHotfixDiff` | 对比 AB Bundle 产物与 Repository HEAD，计算 `ArtifactDelta`；Hotfix 还对比同 Major Full baseline，计算 `ABDeliveryBundles` 并校验 baseline fallback | TaskVerifyBuildResult |
| `TaskOrganizeOutput` | Full 拷贝全部 `BundleEntries`；Hotfix 只拷贝 `ABDeliveryBundles`；生成 build_summary.txt、清理临时目录 | TaskScanABHotfixDiff |
| `TaskWriteABPackageManifest` | 发布完整 ABManifest（JSON + Binary）；Full 按全部 bundle、Hotfix 按 delivery bundle 校验热更包体大小 | TaskOrganizeOutput |
| `TaskWritePackageIndex` | 写入远端包体指针 PackageIndex.json | TaskWriteABPackageManifest |
| `TaskExportLocalBuildData` | Full Build 时导出 BuildIndexData、ABManifest 和 bundles 到 StreamingAssets；Hotfix 跳过 | TaskWritePackageIndex |

### AA 管线（7 个 Task）

| TaskName | 职责 | 依赖 |
|----------|------|------|
| `TaskScanAddressableHotfixDiff` | 对比 AA 源资产（GUID 粒度，含 .meta）与 Repository HEAD，计算 ArtifactDelta | — |
| `TaskMoveAddressableHotfixGroups` | 将 Added/Modified 资产移入 Hotfix Group，写 undo log；检测 pending move 阻断 | TaskScanAddressableHotfixDiff |
| `TaskBuildAddressablesContent` | 配置 Addressables（RemoteCatalog + PackTogetherByLabel）、清理 ServerData、调用 BuildPlayerContent | TaskMoveAddressableHotfixGroups |
| `TaskOrganizeAAOutput` | 整理 ServerData 输出到最终包目录 | TaskBuildAddressablesContent |
| `TaskWriteAAPackageManifest` | 扫描 .bundle 文件、构建 AAManifest（含 AAAssetIndex）、发布 JSON + Binary | TaskOrganizeAAOutput |
| `TaskWritePackageIndex` | 写入远端包体指针 PackageIndex.json | TaskWriteAAPackageManifest |
| `TaskExportLocalBuildData` | Full Build 时写 BuildIndexData、复制 AAManifest 查询索引、清理 stale AB baseline；Hotfix 跳过 | TaskWritePackageIndex |

> 两条管线共享 `TaskWritePackageIndex` 和 `TaskExportLocalBuildData`。PipelinePanel 按当前 BackendMode 加载对应的 BuildPipelineConfig 资产（AB: `BuildPipelineConfig.asset`，AA: `AABuildPipelineConfig.asset`）。
