# Build Pipeline 构建管线

> 返回总览：[资源管理架构文档](./资源管理架构文档.md)

> **关联代码**
>
> `Assets/FYAsset/Scripts/Shared/Build/Pipeline/Editor/` · `Assets/FYAsset/Scripts/AA/Build/Pipeline/Editor/` · `Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/` · `Assets/FYAsset/Scripts/Compat/Runtime/BackendMode.cs`

---

## 概述

Build Pipeline 由三个职责分离的部分组成：

```text
PipelineBackbone（各后端）  →  固定主干 CoreTaskSlot 列表
BuildPipelineConfig（SO）   →  自定义 Task 的 TaskName + Slot
BuildPipelineComposer       →  组装成线性 Task 序列
BuildPipelineRunner         →  顺序执行、管理 attempt、成功后提升正式输出
```

Runner **不理解后端**：它只接受已经组装好的 Task 列表，不读配置、不解析主干、不参与差异、发布、PackageIndex、版本回滚和 UI 操作。

---

## 核心概念

### IBuildTask — Task 接口

每个 Task 只声明唯一的 `TaskName`，并通过 `Execute(BuildContext)` 完成一个构建步骤。

实现要求：

- 无参公共构造函数（**只有自定义 Task** 由 `BuildTaskResolver` 反射实例化）
- `TaskName` 全局唯一，且不得与主干阶段同名
- `Execute` 同步返回结果（Unity AssetBundle API 本身是同步的）

### BuildContext — 数据总线

Task 之间不直接通信，所有数据通过 `BuildContext` 传递。内部是 `Dictionary<string, object>`，提供类型安全的 `Set<T>` / `Get<T>` / `Require<T>` / `Has` 方法。

- `Get<T>` — Key 不存在返回 `default(T)`
- `Require<T>` — Key 不存在抛出异常
- `Has` — 检查 Key 是否存在

构建只通过 Context 传递构建事实（配置、请求、模式、输出路径、校验结果与摘要）；交付清单与历史差异不进入 Context，由交付事务与发布事务各自持有。中性键名集中在 Shared 的 `BuildContextKeys`，后端私有键放在 `ABBuildContextKeys` / `AABuildContextKeys`：

| Key 常量类 | 键 |
|---|---|
| `BuildContextKeys` | `BuildConfig`、`BuildRequest`、`BuildType`、`OutputPath`、`DeferPackagePublication`、`BuildVerificationResult`、`BuildSummary`、`BuildStartedAtUtc` |
| `ABBuildContextKeys` | `ABManifest`、`CollectedAssets`、`SharePolicy`、`BundleDependencyGraph`、`BundleBuildResults`、`ABDeliveryContents`、`ABDeliveryPreviewMode`、`BuildRecipeFingerprint` |
| `AABuildContextKeys` | `AAManifest`、`AASourceScan` |

### BuildPipelineRunner — 线性执行器

`BuildPipelineRunner.Run(BuildRequest request, IReadOnlyList<IBuildTask> tasks)` 的确定行为：

1. `request.Environment` 为 null 时只执行任务，不建 Context 标准键、不提升产物。
2. 通过 `IBuildRunEnvironment.PrepareContext(context, request)` 写入 Context 标准键；该接口是 Runner 唯一允许的“只有编辑器/具体后端才知道”的动作出口。
3. `IBuildRunEnvironment.BeginAttempt(context, request)` 建立本次运行的产物事务；返回 null 表示不需要 attempt 中间目录。
4. 先对全部 Task 报 `Pending`，再逐个报 `Running` 并按 `Success|Failed` 报结果，剩余 Task 报 `Skipped`。
5. **首个失败即停**：即使 Task 声明 `IsFatal = false`，Runner 同样中止后续 Task，因为后续 Task 会消费不完整的 Context。（`BuildTaskResult.IsFatal` 字段仍存在，但当前不改变 Runner 的调度决策。）
6. Task 抛出异常或返回 null 时转换为 `TASK_EXECUTION_ERROR` / `NULL_RESULT` 失败结果。
7. 失败时调用 `IBuildAttempt.Discard()` 回收 attempt，正式输出与构建事实保持运行前状态。
8. 成功后 `IBuildAttempt.TryPromote(out IBuildDeliveryToken, out string error)` 提升正式输出；提升失败按失败处理。
9. 把交付 token 交给调用方，由调用方在自己的事务边界 `Commit` / `Rollback`。

Runner **不提供**：whitelist、stop-after、自由 DAG、可编辑主干、后端工厂、PackageIndex、历史 baseline 语义、发布和版本回滚（逐项现状见文末“已移除的旧管线能力”）。预览功能直接调用无副作用的扫描或 Diff 服务，不通过截断生产管线实现。

### BuildPipelineComposer — 主干与自定义 Task 组装

```csharp
public static IReadOnlyList<IBuildTask> Compose(
    IReadOnlyList<CoreTaskSlot> coreTasks,
    IReadOnlyList<CustomTaskEntry> customTasks);
```

顺序语义：

```text
Input 槽 → [主干[0] 槽的自定义 Task] → 主干[0] → … → 主干[N-1] → Output 槽
```

- `CoreTaskSlot(Slot, Task)`：主干阶段名 + 主干 Task 实例。主干 Task 由后端直接 `new`，不经反射。
- `CustomTaskEntry(TaskName, Slot)`：`Slot` 表示“插入到该槽主干任务之前”，同一槽内多条按配置顺序稳定排列，每槽允许 `0..N` 条。
- 合法槽位 = `Input` + 全部主干槽位名 + `Output`。
- 所有校验失败都抛 `BuildPipelineException`，不做静默跳过：空槽位名、重复槽位名、缺主干 Task 实例、空主干、空 TaskName、未知 Slot、重复自定义 TaskName、自定义 Task 与主干同名、TaskName 无法解析。

`BuildTaskResolver` 启动时扫描已加载程序集，找出全部 `IBuildTask` 非抽象实现并按 `TaskName` 建索引。**反射只服务自定义 Task，不解析主干。**

---

## 固定主干

### AA：5 段

```text
PrepareAAInput
→ BuildAAContent
→ GenerateAAManifest
→ VerifyAAContent
→ ExportAAOutput
```

| 阶段 | 职责 |
|---|---|
| `PrepareAAInput` | 记录 Addressables source 快照（`AASourceScan`）；Hotfix 计算差异并迁移热更分组。Full/Standalone 不做临时移动 |
| `BuildAAContent` | 调用 Addressables 原生 `BuildPlayerContent` |
| `GenerateAAManifest` | 规范化 catalog，并从实际输出建立完整 AA 清单与哈希 |
| `VerifyAAContent` | 校验 catalog、清单与内容文件集合 |
| `ExportAAOutput` | 形成完整构建结果（`CompleteBuildSummary`）、模式输出与包内 BuildIndex；写 AA 源快照 `AASourceScan.json` |

Addressables Group 的修改必须由 AA 入口 `try/finally` 恢复，不由尾部 Task 恢复。

### AB：6 段

```text
CollectABAssets
→ AnalyzeABDependencies
→ BuildABContent
→ GenerateABManifest
→ VerifyABContent
→ ExportABOutput
```

| 阶段 | 职责 |
|---|---|
| `CollectABAssets` | 扫描 Group/Collector，应用排除、RawFile 与 Address/Labels 覆盖，补框架内置内容 |
| `AnalyzeABDependencies` | 分析 Asset 依赖、显式/隐式来源与共享策略，生成计划图 |
| `BuildABContent` | 按输入指纹从历史正式 Summary 复用制品；SerializedObject/Scene 走 Unity，RawFile 直接复制 |
| `GenerateABManifest` | 读取 Unity `AssetBundleManifest` 的实际依赖，生成完整 Asset/Content 映射 |
| `VerifyABContent` | 校验 Address 唯一、Public 边界、成员关系、Content 类型、依赖、文件集合、Hash/CRC/Size |
| `ExportABOutput` | 计算交付内容集合（Hotfix 为相对基准 Full 的新增/修改内容），写 Manifest、包内 BuildIndex 与 `ABDeliveryContents` |

---

## BuildPipelineConfig — 配置资产

AB 配置默认位于 `Assets/Build/BuildPipelineConfig.asset`，AA 配置默认位于 `Assets/Build/AABuildPipelineConfig.asset`。

```
BuildPipelineConfig
├─ BundleCompression     (LZ4 / LZMA / Uncompressed，默认 LZ4)
└─ Tasks[]               (CustomTaskEntry 列表：TaskName + Slot)
```

配置只保存构建选项与自定义 Task 的插入位置，**不能增删主干**。后端键由 concrete build manager 的 `BuildRequest` 决定；Shared 不读取项目级后端选择配置。配置升级由 `BuildPipelineConfigUpgrader` 与 AA/AB 各自的 `*PipelineConfigUpgrade` 处理，把旧的“TaskName 列表”数据迁移成带槽位的 `CustomTaskEntry`。

### 物理内容文件名

配置里不再有文件名风格选项；物理内容文件名由 `BundleNameBuilder.BuildPhysicalName` 统一生成：

| 段 | 含义 |
|----|------|
| `group` | 内容逻辑名的分组段 |
| `kind` | `asset` / `scene` / `raw` / `all` / `labels` / `unlabeled` |
| `readable` | 可读段（自动 Address 用资源短名；整组 / 无标签模式不生成），超长截断 |
| `hash12` | 由内容逻辑名、kind 与可读段派生的 12 位身份哈希 |

最终形如 `{group}_{kind}[_{readable}]_{hash12}`，不含文件扩展名。物理名的唯一性与长度上限由这 12 位哈希承担，逻辑名到物理名的映射由 Manifest 的 `ManifestContentEntry.FileName` 承担。规则版本是 `BundleNameBuilder.PhysicalNameRuleVersion`，它参与身份哈希与构建配方指纹，规则变化必须同时提升它。

### BuildType 与模式输出

| 值 | 含义 |
|----|------|
| `Full` | 完整内置包：`BuildIndex(RuntimeMode=Online)` + 完整 Manifest + 全部内容，交付到 `Packages/Build_*` 独立目录 |
| `Hotfix` | AB：完整目标 Manifest + 相对最近成功 Full 的新增/修改内容；AA：本次 Addressables 构建的产出内容（相对基准 Full 裁剪属延期矩阵）。都交付到独立 `Build_*` 目录，不写包内 BuildIndex |
| `Standalone` | 完整离线包：`BuildIndex(RuntimeMode=Standalone)` + 完整 Manifest + 全部内容，交付到 `StreamingAssets/Standalone/` |

`RuntimeMode` 由 `CompleteBuildSummary.ResolveRuntimeMode(BuildType)` 推导并在构建导出时写入 `BuildIndex`，运行时只读该字段。

---

## 构建结果摘要

`CompleteBuildSummary` 是**构建结果面板的唯一数据源**：

```csharp
public sealed class CompleteBuildSummary
{
    public string BuildId;         // 当前为包名
    public string BackendId;       // AA / AB
    public BuildType BuildType;
    public RuntimeMode RuntimeMode;
    public VersionNumber Version;
    public string Platform;
    public DateTime StartedAt;     // UTC
    public TimeSpan Duration;
    public bool Success;
    public List<FileDigest> Files;
    public List<ContentReuseRecord> Contents;   // 内容复用事实
    public List<BuildMessage> Messages;
    public BuildStatistics Statistics;   // AssetCount / ContentCount / FileCount / TotalBytes
}
```

- Export 阶段把它写入 `BuildContextKeys.BuildSummary`，后端通过 `BuildResult.Summary` 返回，`BuildProjectRunner.LastSummary` 供 `PipelinePanel` 结果摘要区读取；每次构建开始时先清空。
- 详细 Task 日志仍由 Runner 的逐个 Task 结果独立承载。
- 摘要有两份落盘形式：正式摘要写在 `BuildData/Summaries/{AA|AB}/{BuildId}.json`（不可变，含文件清单与内容复用事实），定位索引是可重建的 `BuildData/Summaries/index.json`；包目录内不再有摘要文件。
- 摘要与 Index 由交付事务提交，Index 是最后一个可见身份提交点；发布侧的包身份由发布 UI 从正式摘要解析后注入，不再从包目录解析。
- 摘要不拥有生命周期，也不定义复用字节来源：历史 `Build_*` 包才是字节来源，摘要是复用索引。

AB 窗口另有独立的 `ABReportPanel`（`BuildData/Reports/AB` 下的 editor-only JSON 报告，由 `ABBuildReportBuilder` 在 `ABBuildBackend` 中写出）。它与上面的摘要区数据源不同：摘要区只读 `CompleteBuildSummary`，报告面板只读报告文件，两者都不写包输出。

---

## BackendMode — 宿主后端模式

`BackendMode` 是 Compat Runtime 的身份枚举（`Unspecified` / `AA` / `ABManifest`）；Shared 构建请求和序列化协议只携带 `BackendKey` / `BackendMode` 字符串字段，不引用该枚举。

| 枚举 `BackendMode` | 字符串名（`BackendModeNames`） | 含义 |
|----|----|------|
| `AA` | `"AA"` | 基于 Addressables 的 AA 构建 |
| `ABManifest` | `"AB"` | 基于 ABManifest 的自研构建 |
| `Unspecified` | — | 未选择；`IsValid` 返回 false，正式入口会拒绝 |

`BackendMode` 只做身份校验与诊断，不创建、不选择、不切换后端；后端身份不一致一律 Error 严格阻断。正式 AA/AB concrete 构建入口各自固定使用所属后端。

---

## Editor Layout

Build Pipeline 编辑器保留两个独立窗口，菜单入口统一归属 `FYAsset`：

| 窗口 | 面板 |
|---|---|
| `FYAsset/Build/AA Build Pipeline` | `SettingsPanel`、`AAConfigPanel`、`AABuildPanel`、`AAReportPanel`、`PublishTargetPanel`、`AAHotfixGroupMaintenancePanel` |
| `FYAsset/Build/AB Build Pipeline` | `SettingsPanel`、`ABConfigPanel`、`AssetsCollectionPanel`、`PipelinePanel`、`BuildPanelActions`、`ABReportPanel`、`PublishTargetPanel`、`ABTestMaintenancePanel` |

- `PipelinePanel` 负责自定义 Task 的槽位编辑、主干展示、构建入口与结果摘要区。
- 独立 Project Labels 页面已移除：AB 在 Collection 候选配置内修改 Labels，统一 Save/Cancel；AA 在 Addressables 原生编辑器维护。
- 人工构建由管线面板的 `Mode + Build` 发起，AB 支持 Full/Hotfix/Standalone，AA 支持 Full/Hotfix。不存在统一的三栏 Repository 页面，也不存在 Diff Preview 页面。
- 流程图和文件职责见 [HTML 建模文档](./fyasset-modeling.html)。

---

## 路径规范

- `BuildConfig.OutputRoot` 在创建时解析为规范本地路径。
- 远端 URL 只使用 `FYAssetPathUtility.JoinUrl(...)` 拼接。
- 构建输出、临时目录、包体目录、manifest、bundle、`StreamingAssets` 导出等本地路径使用 `FYAssetPathUtility.JoinFilePath(...)` / `ResolveFilePath(...)`。
- Unity `AssetDatabase` 路径保持 `Assets/...` 和 `/` 分隔符，通过 `NormalizeAssetPath(...)` / `JoinAssetPath(...)` 处理。
- `BuildPathManager` 提供 `PackagesDir`（`{OutputRoot}/{BuildPackagesFolderName}`）、`AttemptPackagesRoot`（`{OutputRoot}/_attempt`）与 `StandalonePackageDir`（`StreamingAssets/Standalone`）；不再有固定累计 Hotfix 目录，Full 与 Hotfix 都交付到 `Packages` 下按包名隔离的目录。

---

## 已移除的旧管线能力

以下能力在当前源码中不存在，文档与配置都不得再按现行机制描述：

| 已移除 | 现状 |
|---|---|
| 可编辑主干 / DAG / 拓扑排序 | 主干由 AA/AB `PipelineBackbone` 固定定义；Runner 只顺序执行 Composer 结果 |
| `whitelist` / `stop-after` 预览 | 无该参数；预览改为直接调用无副作用服务 |
| `TaskEntry.DependsOn` / `BuildTaskListUtility` / `BuildResult` | 已删除；改为 `CustomTaskEntry` + `BuildRunResult` |
| `TaskPrepareContext` / `TaskWritePackageIndex` / `TaskExportLocalBuildData` 等独立 Task | 职责并入 Runner 环境、Export 阶段与 `BuildProjectRunner` 交付事务 |
| 生产管线截断预览（`BuildPreviewRunner`、`*RepositoryPreview`） | 已删除 |
| Runner 内的 PackageIndex 写入与 baseline 提交 | 已删除；PackageIndex 由发布事务最后写入 |
