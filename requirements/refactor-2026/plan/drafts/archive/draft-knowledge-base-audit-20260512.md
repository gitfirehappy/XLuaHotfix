# Draft: 知识库全量审计 — FYAsset 当前方向 vs 已知最佳实践

> **Date**: 2026-05-12
> **Status**: Draft — 待开发者审阅讨论
> **Scope**: 全量阅读 context/ + requirements/ + memory/ + dependencies/reference/ + zhihu-resource-management/，比对当前 FYAsset 已落地代码，识别功能缺口与优化机会
> **Methodology**: Working Backwards from 开发者体验 + YooAsset/Zhihu 参考系

---

## 一、当前状态全景

### 1.1 已落地能力矩阵

| 层次 | 能力 | 成熟度 | 参考来源 |
|------|------|:------:|----------|
| **Runtime 加载** | IAssetIndex + IPackageBackend 双后端 | 稳定 | B1-B2 |
| **Runtime 加载** | AssetHandle\<T\> struct + HandleRegistry 引用计数 | 稳定 | B5-2, B8 |
| **Runtime 加载** | ABAssetIndex + ABBundleLoader + ABPackageBackend | 稳定 | B6-B7 |
| **Runtime 加载** | AssetResolver (Address/TypeKey → Entry 解析) | 稳定 | B5-2 |
| **Runtime 加载** | RuntimeMessage 统一错误体系 | 稳定 | R1 |
| **Hotfix** | HotfixManager orchestrator + Legacy/AB 双后端 | 稳定 | B4B9 |
| **Hotfix** | NetworkDownloader + PathManager + FileHelper | 稳定 | B4B9, plan-filehelper |
| **构建-收集** | Collector 四级层次 (Setting→Package→Group→Collector) | 稳定 | E1-1 |
| **构建-收集** | AssetClassifier + 3 条默认规则 + RuleResolver | 稳定 | E1-2 |
| **构建-收集** | CollectionScanner (最深路径去重 + IgnorePatterns + 7 错误条件) | 稳定 | E1-3 |
| **构建-收集** | PackRule (PackSeparately/PackByDirectory/PackByLabel) + BundleNameBuilder | 稳定 | E2 |
| **构建-收集** | CollectorPanel TreeView + PropertyPanel + Validator (9 规则) | 稳定 | E1-4 |
| **构建-管线** | IBuildTask + BuildContext + DAGScheduler (Kahn + 读写冲突验证) | 稳定 | E5-1 |
| **构建-管线** | TaskPrepareContext + TaskCollectBuiltins + TaskBuildBundles | 稳定 | E5-2a |
| **构建-管线** | TaskVerifyBuildResult (6 检查) + TaskOrganizeOutput | 稳定 | E5-2b |
| **构建-管线** | TaskGenerateManifest (ABManifest JSON 导出, CRC32) | 稳定 | E6 |
| **构建-管线** | BuildProjectManager orchestrator + IBuildBackend 双后端 | 稳定 | E10 |
| **构建-管线** | BuildMessage 统一错误体系 | 稳定 | R1 |
| **基础设施** | VersionNumber (SemVer 2.0, IComparable, Parse/TryParse) | 稳定 | E9 |
| **基础设施** | SerializationUtility (JSON + Binary codec + 代码生成) | 稳定 | S1-S4 |
| **基础设施** | FileHelper (跨平台 I/O, 原子写, TryDelete) | 稳定 | plan-filehelper |
| **基础设施** | HashGenerator (CRC32 统一 + enum) | 稳定 | E5-2b |
| **约定** | Labels vs Tags 语义分离, EntryId 唯一身份, PascalCase 统一 | 稳定 | field-semantics |

### 1.2 待执行计划

| 计划 | 状态 | 优先级 | 预计增量 |
|------|------|:------:|----------|
| **E11** FYAssetSettings SO + Settings 面板 | Draft converged | **高** | ~5 文件 |
| **E7** Diff snapshot 适配 (IDiffPipeline 双后端) | Draft | **高** | ~8 文件 |
| **E8** FileSystem 抽象 | 未起草 | 中 | TBD |
| **F1-F3** 交付策略 (离线包/后台下载/A/B) | Ideas only | 中 | TBD |
| **H1** AsyncOp 优先级调度 + 取消 | TBD | 低 | TBD |
| **H2** LRU/LFU 缓存策略 | Deferred | 低 | TBD |
| **Phase 10** 程序集拆分 | 最后 | 低 | TBD |

---

## 二、知识库 vs 当前实现 — 缺口分析

以下按影响程度从高到低排列。每个缺口标注了参考来源、当前状态、建议方案。

### 缺口 1 (高): 无 Editor 模拟模式 — 迭代效率瓶颈 ✅ 已深度讨论, 收敛至 draft

**参考**: Zhihu Ch.10 (Editor Simulation Mode), YooAsset EditorSimulatePlayMode, Addressables `BuildScriptVirtualMode` (Unity 源码分析)

**当前状态**: AB 后端每次资源变更都需要完整构建管线，无 Editor 快速预览路径。

**收敛方案**: 详见 `plan-playmode-draft.md` (2026-05-12 收敛版)。核心决策:

- **三模式对标 Addressables**: EPlayMode.Editor (Fast Mode) / Simulate (Virtual Mode) / Runtime (Packed Mode)
- **Simulate 构建侧**: 复用现有 DAG 管线 + EBuildMode 枚举, TaskBuildBundles/VerifyBuildResult/OrganizeOutput 加 skip 开关——不写独立 Build Script, 不重复打包算法 (对标 Addressables `PrepGroupBundlePacking` 共享模式)
- **Editor/Simulate 加载侧**: 单一 `EditorBackend` 类 (~60 行), 通过注入不同 `IAssetIndex` 实例分化行为。Editor 模式用 `EditorAssetIndex` (Collector→address→path 直查), Simulate 模式用 `ABAssetIndex` (虚拟 Manifest)
- **两层开关**: `UseABBackend` (第一层) → `EPlayMode` (第二层), 不冲突
- **虚拟 Manifest 深度**: 等同真实 ABManifest (完整 Asset/Bundle entries + 依赖), 唯一区别是不调 `BuildPipeline.BuildAssetBundles` + 不序列化到磁盘
- **增量**: ~225 行, 4 新文件, 7 文件修改, 零新 IBackend 接口

比本 draft 初版简化方案 (单一 Simulation on/off) 更完整, 更贴近 Addressables 用户心智。

### 缺口 2 (高): 无运行时资源调试面板 — 内存泄漏排查困难 ✅ 已深度讨论, 收敛至 draft

**参考**: Zhihu Ch.12 (ResourceDebuggerWindow), Unity Profiler/Frame Debugger 布局分析

**当前状态**: 运行时没有任何资源可视化工具。排查资源泄漏靠 Debug.Log + 重新构建 + 复现。

**收敛方案**: 详见 `draft-debug-panel-20260512.md` (待写)。核心决策:

- **面板位置**: BuildPipelineWindow 侧栏新增 DEBUG 组, 含 Runtime Debugger 面板
- **数据访问**: `#if UNITY_EDITOR` 轻量访问器方法 (~40 行 Runtime 增量), 类型安全, 零反射, 零 build 开销
- **布局**: Toolbar (Auto-Refresh/Pause + Export + Force GC) → 状态摘要行 (PlayMode/Bundle数/Handle数) → 左右分栏 45:55 (Bundle 列表/Handle 列表 Tab 切换 + 选中项详情) → 底部异常提示 (条件显示)
- **颜色编码**: 浅绿=RefCount>0, 浅黄=RefCount==0 仍在内存 (泄漏嫌疑), 浅红=LoadError, 浅蓝=选中
- **刷新策略**: Auto-Refresh (OnInspectorUpdate ~10Hz) + Pause 按钮冻结快照 (对标 Profiler Record)
- **导出**: TXT (可读分享) + JSON (CI 自动化泄漏检测) 双格式
- **Handle 独立列表**: 与 Bundle 列表并列 Tab, 支持按 ErrorCode/RefCount 过滤排序
- **异常检测**: RefCount>0 但反向推导无任何追踪到的引用者 → 标记警告
- **PlayMode 指示**: 整合到面板状态摘要行 (来自缺口 1 Q4 决策)
- **增量**: 1 个 EditorWindow 类 (~350 行) + Runtime 访问器 (~40 行, `#if UNITY_EDITOR`), 零 Runtime 开销

### 缺口 3 (中高): Handle→GameObject 生命周期自动绑定 ⚠️ 方案待定

**参考**: Zhihu Ch.9 (AssetBindingListener)

**当前状态**: `AssetHandle<T>` 实现 IDisposable, 释放完全依赖调用方手动操作。

**分歧点**: Zhihu 的 AssetBindingListener (MonoBehaviour + OnDestroy 自动释放) 是直接可参考的方案, 但用户认为这个点比较重要, 可能有更好的方案——例如 scope-based `using` 模式、WeakReference 自动追踪、或场景级生命周期绑定。**暂不收敛。**

**待进一步讨论**: 对比不同 auto-binding 方案的 tradeoff 表。

### 缺口 4 (中): Bundle 加载无并发控制 — 大量加载时可能卡帧 ✅ 已收敛

**参考**: Zhihu Ch.8 (RequestScheduler), YooAsset (BundleLoadingMaxConcurrency)

**当前状态**: `ABBundleLoader` 无并发上限。

**收敛方案**:
- `RequestScheduler` 类 (~50 行): `Queue<TaskCompletionSource<bool>>` + `WaitSlot()`/`ReleaseSlot()`
- 默认并发上限 = **10**, 可在 FYAssetSettings 或 ABBundleLoader 构造函数中配置
- 集成到 `ABBundleLoader.LoadBundleInternalAsync`: `WaitSlot` → double-check `_bundleCache` → `LoadFromFileAsync` → `ReleaseSlot`
- 依赖加载 (递归) 不排队——依赖数量通常很少, 排队反而降低并行度

### 缺口 5 (中): 异步加载无超时机制 — 卡死风险 ✅ 已收敛

**参考**: Zhihu Ch.11 (Task.WhenAny 软超时)

**当前状态**: ABBundleLoader.LoadBundleAsync 无超时控制。

**收敛方案**:
- **位置**: 仅 ABBundleLoader 层 (Bundle 加载入口)。Asset 提取超时由 Bundle 超时覆盖
- **机制**: `Task.WhenAny(loadTask, Task.Delay(timeout))` 软超时 — 底层 I/O 无法取消, 调用方放弃等待
- **超时后清理**: 移除 inflight 记录 + 返回 RuntimeMessage.Timeout + 不调用 UnloadBundle (native 层可能还在跑)
- **双 await**: `WhenAny` 后要再次 `await loadTask` 捕获异常
- **增量**: ~20 行 (ABBundleLoader) + 1 行 (RuntimeErrorCodes)

### 缺口 6 (中): 构建无增量缓存 — 全量构建浪费 ⏸️ 暂不定时

**参考**: Zhihu Ch.15-17 (BuildCacheManager — 两级检测 + 依赖传播)

**当前状态**: 每次构建全量扫描+全量打包。

**讨论笔记**:
- 增量缓存逻辑可与 E7 Diff snapshot 合并 — 两者都涉及 Asset hash 比较和变更检测, 共享基础设施
- 可整合进 Task 框架 — 增量检测作为独立 Task 或现有 Task 的模式开关
- **存储位置**: 不使用 `Library/` (Unity 管理目录, 自定义文件可能被清理)。备选: `BuildData/Cache/` 或与 snapshot 数据同目录
- **实施时机**: 暂不定。当前项目资源量可接受全量构建, 等成为瓶颈时已有完整方案

### 缺口 7 (中低): Provider 缓存层缺失 — 同 Asset 重复提取

**参考**: YooAsset (ProviderOperation + ProviderDic 缓存), Zhihu Ch.9 (AssetInternalNode.LoadingTask 去重)

**当前状态**: ABPackageBackend 没有 per-asset 的去重加载层。如果两个调用方同时请求同一个 Asset（通过不同 Address 指向同一 EntryId），各自走完整加载流程。

**分析**: 当前 HandleRegistry 已经按 EntryId 去重了 Handle 分配（多个 Handle 共享同一 EntryId 的 RefCount），但 Asset 的实际提取 (`bundle.LoadAssetAsync`) 没有去重。如果 10 个 Handle 同时请求同一 EntryId 的资源，会触发 10 次 `LoadAssetAsync`。

**建议方案**: 这个缺口的影响取决于业务层的使用模式。如果同一帧内对同一资源的并发请求很常见（如 UI 初始化时多个组件引用同一图集），则需要 Provider 缓存。否则可以延后。

```
方案 A (轻量): 在 ABPackageBackend 内部加 Dictionary<string, Task<T>> _inflightAssetTasks
  键 = EntryId, 值 = 正在进行的 LoadAssetAsync Task
  第二个请求 await 已有 Task 而非发起新加载

方案 B (完整): 引入独立的 ProviderOperation 层 (YooAsset 模式)
  每个 Asset 一个 ProviderOperation 状态机
  ProviderDic 缓存已完成的 Provider
```

建议先实施方案 A (~30 行)，观察是否需要方案 B。

### 缺口 8 (中): Build Graph 可视化编辑器 — 开发者有意向 🔄 重新评估

**参考**: Zhihu Ch.15-17 (BuildGraph), DAGScheduler 现有架构

**之前判断 (错误)**: 对标 Zhihu 3500+ 行, 且误以为边全由 Key 自动推导不需要手动连线。

**纠正**: Task 连线是双层机制:
- **DependsOn (显式)**: `IBuildTask.DependsOn` + `TaskEntry.DependsOn` → `BuildAdjacency` 构建 DAG 边。主干固定, 扩展 Task (如 LuaIndex 导出) 需要手动连线
- **ReadKeys/WriteKeys (校验层)**: 不参与构建邻接表, 在 Validate 阶段检测 Read-before-Write (隐式依赖提示)、Write-Write 冲突。`ValidatePair(taskA, taskB)` API 已预留供编辑器连线时实时校验

**Graph 编辑器核心功能**:
- 可视化 Task 节点 + DependsOn 连线 (手动拖拽创建/删除)
- 实时调 ValidatePair 防 Write-Write 冲突
- Read/Write 匹配高亮 (隐式依赖建议连线)
- 节点状态 (Pending/Running/Done/Failed) + 构建进度
- DAGScheduler 拓扑/校验逻辑全复用

**规模**: 非 3500 行。FYAsset 只有一种 TaskNode 类型, 无 Portal/SubGraph/多端口类型, GraphView API 处理画布基础。估计 ~400-500 行。

**时机**: 待排期。此项需单独讨论, 未包含在本次审计收敛范围内。

### 缺口 9 (低): 零 GC Async/Await 自定义 Awaiter

**参考**: Zhihu Ch.7 (Custom Awaiter — struct Awaiter + 编译器状态机, 0 GC, ~50 行代码)

**当前状态**: 项目使用 `async/await` + `Task` 模式处理异步加载。Task 本身有堆分配。

**分析**: Zhihu Ch.7 的自定义 Awaiter 方案非常优雅——不需要第三方库，用 ~50 行代码让 `AssetBundleCreateRequest` 原生支持 `await`，编译器生成的状态机是 struct（零堆分配）。但当前项目的异步加载链路不长（主要是热更启动 + 资源加载），GC 压力可能不是首要矛盾。

**建议**: 记录为优化储备。当 Profiler 显示 GC.Alloc 在加载路径上成为瓶颈时再实施。实施成本极低（~50 行），但需要测试所有 await 点的兼容性。

### 缺口 10 (低): IBundlePathProvider 平台抽象

**参考**: Zhihu Ch.11 (IBundlePathProvider 接口 + DefaultBundlePathProvider 沙盒优先策略)

**当前状态**: FileHelper 已经处理了 Android StreamingAssets 的特殊读取路径（通过 UnityWebRequest），PathManager 管理包根路径。但路径存在性检查和优先级策略分散在多个类中。

**分析**: 当前 FileHelper + PathManager 的组合已经覆盖了主要平台差异。抽象出 IBundlePathProvider 接口可以统一这些逻辑，但收益主要是代码整洁性而非功能增强。

**建议**: 在 E8 (FileSystem) 中统一考虑，不作为独立高优项。

---

## 三、优化点 (已有基础，可增强)

以下不是"缺口"而是"可以做得更好"的点——当前实现已经正确，但有优化空间。

### 优化 1: Manifest 反序列化预热

**当前**: ABManifest 在首次访问时完整解析 JSON。对于大型项目（5000+ Asset），JSON 解析可能耗时 50-200ms。

**建议**: 考虑在 Editor 构建时同时导出 Binary 格式（已有 BinaryCodec 基础设施），运行时优先加载 .bin（解析速度 5-10x），JSON 作为 fallback。S3/S4 已支持双格式导出，只需要开启。

### 优化 2: Bundle 预加载提示

**当前**: 没有机制让业务层提示"我马上需要这些 Bundle，请提前加载"。

**建议**: 在 ABPackageBackend 增加 `PreloadBundlesAsync(IEnumerable<string> bundleNames)` 方法，利用并发控制和依赖自动解析，在场景切换前预加载关键 Bundle。增量 ~30 行。

### 优化 3: Labels 反向索引

**当前**: ABAssetIndex 的 Labels 查询是通过遍历全部 Entry 实现的 O(n)。

**建议**: 在 ABAssetIndex 初始化时构建 `Dictionary<string, List<int>> _labelToEntryIndices` 反向索引，使 `GetEntriesByLabel` 从 O(n) 降到 O(1)。增量 ~15 行。内存开销: 每个唯一 Label 一个 List\<int\>。

### 优化 4: TaskCollectBuiltins Shader 收集确认

**当前**: E5-2a 的 TaskCollectBuiltins 已实现 Shader 自动收集（来自 YooAsset gap analysis #1）。需要确认: (1) 是否已处理 ShaderVariantCollection; (2) 生成的 shader bundle 是否在 Manifest 中正确标记。

**建议**: 作为 E7 的前置验证项，在实现 Diff snapshot 前确认 Shader bundle 的行为正确。如果 Shader bundle 的内容在版本间变化，Diff 必须能检测到。

### 优化 5: 构建产物大小统计

**当前**: TaskOrganizeOutput 生成 `build_summary.txt`，但格式和详细程度未知。

**建议**: 确认 build_summary.txt 包含: 总 Bundle 数、总大小、每个 Bundle 的大小/资源数/压缩率、Top 10 最大 Bundle。这些数据对于后续的包体优化至关重要。参考 YooAsset 的 TaskCreateReport 输出格式。

---

## 四、参考系对照: YooAsset 完整能力 vs 当前 FYAsset

| YooAsset 能力 | FYAsset 状态 | 差距 |
|---------------|:----------:|------|
| Collector 四级层次 | ✅ 已落地 | — |
| IAddressRule / IPackRule / IFilterRule 接口 | ✅ 已落地 | — |
| 依赖分析 (8 阶段) | ✅ E4 | — |
| IBuildTask pipeline | ✅ E5-1 | — |
| TaskVerifyBuildResult | ✅ E5-2b | — |
| PackageManifest 导出 | ✅ E6 | — |
| ResourcePackage facade | ✅ AssetPackageManager | — |
| AssetHandle + 引用计数 | ✅ B5-2 + B8 | — |
| ProviderOperation 缓存 | ⚠️ 部分 (HandleRegistry 有去重, Asset 提取无去重) | 缺口 7 |
| LoadBundleFileOperation | ✅ ABBundleLoader | — |
| IFileSystem 抽象 | ❌ 计划中 (E8) | 缺口(计划内) |
| PlayMode 多模式 | ⚠️ 2 后端 (Addressables + AB), 无 Editor 模拟 | 缺口 1 |
| 加密 (IEncryptionServices) | ❌ 未计划 | Out of scope |
| 下载续传+重试 | ❌ Deferred | gap analysis #8 |
| 离线包模式 | ❌ Ideas only | F1 |
| 后台下载 | ❌ Ideas only | F2 |
| A/B 变体 | ❌ Ideas only | F3 |
| Build Report | ⚠️ 部分 (build_summary.txt) | 优化 5 |
| FileNameStyle 多模式 | ⚠️ 已预留 enum, 仅 1 种实现 | gap analysis #6 |
| Group 开关 | ✅ CollectorGroup.Enabled | gap analysis #7 |
| Shader 自动收集 | ✅ TaskCollectBuiltins | — |
| 增量构建缓存 | ❌ 未计划 | 缺口 6 |
| Editor 模拟模式 | ❌ 未计划 | 缺口 1 |
| 资源调试面板 | ❌ 未计划 | 缺口 2 |
| 可视化 Build Graph 编辑器 | ❌ 未计划 | 缺口 8 |

---

## 五、优先级建议

综合考虑影响面、实现成本、依赖关系:

### 立即执行 (已就绪, 阻塞后续)

1. **E11 FYAssetSettings** — Draft converged, 零阻塞, 统一配置入口。建议立即 promote 为正式 plan 并执行。
2. **E7 Diff Snapshot** — Draft 完善, 阻塞热更 BuildHotfix 能力验证。AB 后端的热更链路需要 Diff 来驱动增量打包。

### 近期 (E7 之后, 1-2 个迭代)

3. **缺口 1: Editor 模拟模式** — DX 提升最显著, 消除"改个 Prefab 就要完整构建"的痛点。
4. **缺口 2: 运行时调试面板** — 资源泄漏排查从"猜"变"看", 维护效率质变。
5. **缺口 4: 并发控制** — 防止大量加载卡帧, 移动端必做。

### 中期 (Phase 7 阶段)

6. **缺口 6: 增量构建缓存** — 构建时间优化, 随项目资源量增长价值递增。
7. **F1-F3 交付策略** — 从 ideas 收敛为正式 plan。
8. **缺口 3: Handle 自动绑定** — 减少业务层泄漏风险。

### 储备 (按需触发)

9. 缺口 5 (超时), 缺口 7 (Provider 缓存), 优化 1-5, 缺口 9 (零 GC Awaiter), 缺口 10 (路径抽象)

---

## 六、待讨论决策点

以下问题需要开发者判断优先级和方向:

1. **Editor 模拟模式 vs 增量构建缓存**: 如果只能先做一个，哪个更痛？模拟模式省的是"验证资源正确性"的时间（每次改 Prefab 不需要构建），增量缓存省的是"完整构建"的时间（CI/发布场景）。两者的受益场景不完全重叠。

2. **F1-F3 的时间窗口**: offline package / background download / A/B test 是否有外部 deadline（如发行计划、运营需求）？如果有明确时间点，需要提前从 ideas 收敛为 plan。

3. **E8 FileSystem 的范围**: 当前 FileHelper + PathManager 已经覆盖了跨平台 I/O 差异。E8 是否需要完整的 IFileSystem 接口层（参考 YooAsset 5 种 FileSystem 实现），还是用更轻量的方式补足 Android APK 路径的特殊处理即可？

4. **Phase 10 程序集拆分的时机**: 当前所有 FYAsset 代码在 Assembly-CSharp 中。拆分程序集会带来 asmdef 管理的持续成本，但能显著减少编译时间（改一个 Editor 文件不再触发全量重编）。是现在做还是等代码量再翻倍后做？

5. **缺口 8 Build Graph 可视化编辑器**: 投入 ~3500 行代码获得可视化 DAG 编辑体验。在 Task 数量不超过 15 个的前提下，List 配置方式够用。是否需要预留架构扩展点但不实施完整编辑器？

---

## 变更记录

| Date | Change |
|------|--------|
| 2026-05-12 | Initial draft — 全量知识库审计, 10 缺口 + 5 优化 + 参考系对照 + 优先级建议 |
| 2026-05-12 | 缺口 1 深度讨论收敛: 三模式 (Editor/Simulate/Runtime), Task 跳过方案, EditorBackend 统一, 两层开关。同步更新 `plan-playmode-draft.md` |
