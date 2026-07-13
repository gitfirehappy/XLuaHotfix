# Build Pipeline 构建管线

> 返回总览：[资源管理架构文档](./资源管理架构文档.md)

> **关联代码**
>
> `Assets/FYAsset/Scripts/Shared/Build/Pipeline/Editor/` · `Assets/FYAsset/Scripts/Shared/Build/BackendMode.cs`

---

## 概述

Build Pipeline 采用 **Task + 线性执行列表** 模型。每个构建步骤（采集资产、分析依赖、打包 Bundle、生成清单等）实现为独立的 `IBuildTask`，通过 `BuildPipelineRunner` 按 `BuildPipelineConfig.Tasks` 的列表顺序执行。配置集中在 `BuildPipelineConfig` ScriptableObject 中。

---

## 核心概念

### IBuildTask — Task 接口

每个 Task 只声明唯一的 `TaskName`，并通过 `Execute(BuildContext)` 完成一个构建步骤。

实现要求：
- 无参公共构造函数（由 `BuildTaskResolver` 反射实例化）
- `TaskName` 全局唯一
- `Execute` 同步返回结果（Unity AssetBundle API 本身是同步的）

### BuildContext — 数据总线

Task 之间不直接通信，所有数据通过 `BuildContext` 传递。内部是 `Dictionary<string, object>`，提供类型安全的 `Set<T>` / `Get<T>` / `Require<T>` / `Has` 方法。

- `Get<T>` — Key 不存在返回 `default(T)`
- `Require<T>` — Key 不存在抛出 `KeyNotFoundException`
- `Has` — 检查 Key 是否存在

Task 的输入输出契约直接体现在 `Get/Require/Set` 调用和固定主干顺序中，不再维护重复的 `ReadKeys` / `WriteKeys` 声明。

### BuildPipelineRunner — 线性执行器

执行前先解析列表，再线性执行：

- 非 whitelist 模式检查 AA/AB 必需主干 Task 是否缺失
- 拒绝空、重复或无法解析的 `TaskName`
- 按 `BuildPipelineConfig.Tasks` 顺序逐个执行
- 执行运行在 Unity Editor 主线程上，确定性串行执行；不存在额外的并行/串行切换开关
- Fatal 错误立即中止所有后续 Task
- `stopAfterTaskName` 命中后提前停止，已执行 Task 产出的 `BuildContext` 数据可被调用方读取
- `taskWhitelist` 在解析前过滤列表，常用于 Diff Preview
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

- `Tools/Build/AA Build Pipeline`：Settings、AA Config、AA Build、AA Build Results、AA Repository。
- `Tools/Build/AB Build Pipeline`：Settings、AB Config、AssetsCollection、AB Build、AB Build Results、AB Repository。
- 旧 `Tools/Build/Build Pipeline` 菜单保留为兼容入口，根据 `UseABBackend` 打开 AA 或 AB 窗口。

两个窗口中的构建按钮分别直达 `AABuildProjectManager` 和 `ABBuildProjectManager`，不再由 `UseABBackend` 互斥置灰。`UseABBackend` 只保留给旧兼容入口与命令行路由。AA/AB Repository 都使用可拖动的左/中/右三栏布局，两条分隔线宽度按 backend 分别保存在 EditorPrefs 中。

---

## BuildPipelineConfig — 配置资产

AB 配置默认位于 `Assets/Build/BuildPipelineConfig.asset`，AA 配置默认位于 `Assets/Build/AABuildPipelineConfig.asset`。

```
BuildPipelineConfig
├─ FileNameStyle         (BundleName / HashName / BundleName_HashName)
├─ BundleCompression     (LZ4 / LZMA / Uncompressed，默认 LZ4)
└─ Tasks[]               (TaskEntry 顺序列表)
     └─ TaskName         ("TaskPrepareContext")
```

### BundleFileNameStyle

| 值 | 输出格式 |
|----|---------|
| `BundleName` | `{pkg}_{group}_{packKey}.bundle` |
| `HashName` | `{MD5}.bundle` |
| `BundleName_HashName` | `{pkg}_{group}_{packKey}_{MD5}.bundle`（默认） |

### 主干顺序护栏

`TaskEntry` 只保存 `TaskName`，执行顺序只由列表位置决定。`BuildPipelineBackbone` 提供 AA/AB 默认主干列表、缺失检查和编辑器展示顺序；runner 不做拓扑排序，也不维护第二套依赖声明。

---

## BuildTaskResolver — Task 发现

启动时扫描所有已加载程序集，找到所有 `IBuildTask` 的非抽象实现类，按 `TaskName` 构建 `Type` 索引并缓存。`CreateTask(taskName)` 通过 `Activator.CreateInstance` 实例化，重复调用返回新实例。

`BuildPipelineConfig.TaskEntry` 只存储 `TaskName` 字符串（不存 `ClassName`），因此类名修改不影响已有 SO 配置数据。

---

## BuildTaskResult / BuildResult

### BuildTaskResult — 单 Task 结果

Task 通过 `Ok` 或 `Fail` 工厂返回结构化结果。

`IsFatal = true` 的失败会中止 runner 后续 Task。`IsFatal = false` 仅记录错误，调度继续。

### BuildResult — 管线汇总

`BuildResult` 汇总整体成功状态、参与/完成/跳过数量和按执行顺序排列的 Task 结果。

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

1. 解析阶段先应用 whitelist，再检查必需主干及空、重复、未注册的 TaskName。
2. 解析成功后严格按配置顺序执行。
3. `stop-after` 正常提前结束；Fatal 失败中止并将后续 Task 标为跳过。

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
| `AAManifest` | `AAManifest` | TaskWriteAAPackageManifest | (context) |
| `RepositoryPreviewOutput` | `string` | RepositoryPreviewRunner | TaskPrepareContext (预览模式) |
| `RepositoryPreviewMode` | `bool` | RepositoryPreviewRunner | TaskScan*HotfixDiff |
| `ABDeliveryPreviewMode` | `bool` | RepositoryPreviewRunner | TaskScanABHotfixDiff |

---

## 现有 Task 列表

这里按阶段说明，不复制完整类清单；精确 TaskName 和顺序以各自 `BuildPipelineConfig` 资产为准。

| 阶段 | AB | AA |
|------|----|----|
| 准备与采集 | 初始化上下文，采集普通资产、Shader 和 Resources | 扫描 Addressables 源资产差异 |
| 依赖与分组 | BFS 分析依赖并抽取共享 Bundle | Hotfix 时临时移动变更资产到 Hotfix Group |
| 构建 | 按 PayloadKind 构建 Bundle/Scene/RawFile | 调用 Addressables BuildPlayerContent |
| 校验与差异 | 生成并校验 ABManifest；计算 HEAD Diff 与 Full-baseline Delivery | 整理输出并生成 AAManifest |
| 发布 | 整理 Full/Hotfix 交付文件，写 manifest 与 PackageIndex | 写 manifest 与 PackageIndex |
| 本地基线 | Full 导出 BuildIndex、manifest 和 bundles；Hotfix 跳过 | Full 导出 BuildIndex 与查询索引；Hotfix 跳过 |

两条管线共享 PackageIndex 写入和 Full 本地基线导出语义，但配置资产、后端实现和 Repository 通道彼此独立。
