# Full Review — Current Refactor Landed Code

> Date: 2026-04-30
> Reviewer: GPT (Claude Code review agent)
> Scope: 当前已落地的重构代码，重点覆盖 Runtime 资源加载链路、Collector/DependencyAnalysis、Build Pipeline Core（E4、E5-1 及其依赖模块）
> Method: 静态代码审查，按正确性、架构、整洁度、耦合/内聚、性能与可维护性综合评估

---

## Findings Summary

| Severity | Count | Focus |
|----------|-------|-------|
| P1 | 4 | 正确性风险、运行时跨平台问题、数据模型失真、计划能力未真正落地 |
| P2 | 4 | 配置契约漂移、隐藏耦合、死配置、缓存一致性 |

---

## P1 — Should Fix

### P1-1: `DAGScheduler` 会绕过对 disabled task 的依赖，导致错误拓扑仍可执行

- **Files**:
  - `Assets/FYAsset/Scripts/Build/Pipeline/Editor/DAGScheduler.cs:83-114`
  - `Assets/FYAsset/Scripts/Build/Pipeline/Editor/DAGScheduler.cs:230-249`
- **Problem**:
  - 校验阶段用 `allNames = config.Tasks.Select(e => e.TaskName)` 检查依赖是否“存在”，这里把 disabled task 也算进去了。
  - 执行阶段真正构图时只把 enabled task 放进 `instances`，所以某个 enabled task 依赖 disabled task 时，校验会通过，但执行图会直接忽略这条边。
- **Impact**:
  - 一旦有人在 `BuildPipelineConfig` 里关闭前置 task，后续 task 仍可能被执行，`ReadKeys` 对应的数据却根本没生产出来。
  - 这不是“配置无效”，而是“配置 silently unsafe”。
- **Why this matters architecturally**:
  - 当前管线的核心承诺是“DAG + Read/WriteKey 事前校验”。这段逻辑直接破坏了这个承诺。
- **Recommendation**:
  - 依赖校验必须只允许依赖 enabled task，或者在检测到“依赖了 disabled task”时直接报 fatal error。

### P1-2: `ABBundleLoader` 的 `StreamingAssets` fallback 仍是纯文件路径实现，Android 内置包路径大概率不可用

- **File**: `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABBundleLoader.cs:294-307`
- **Problem**:
  - `ResolveBundlePath()` 直接对 `Application.streamingAssetsPath` 做 `File.Exists`，随后走 `AssetBundle.LoadFromFile` / `LoadFromFileAsync`。
  - 这和 `ManifestLoader` 已经引入的 `FileHelper.ReadAllBytesAsync` 跨平台修正不一致。
- **Impact**:
  - 在 Android 这类 `StreamingAssets` 不是真实文件系统目录的平台上，manifest 已能读取，但 bundle fallback 仍可能失效。
  - 结果是“索引初始化成功，但首包 bundle 无法加载”，这类问题会比 manifest 读取失败更隐蔽。
- **Architecture concern**:
  - 同一条 AB 运行时链路里，manifest 已经做了跨平台抽象，bundle I/O 却又退回平台敏感实现，分层不一致。
- **Recommendation**:
  - bundle fallback 读取也应统一走跨平台 I/O 抽象，而不是手写 `File.Exists + LoadFromFile` 的平台假设。

### P1-3: E4 复制型隐式依赖被错误标记为 `GroupName="$shared"`，共享/复制语义在数据层被混淆

- **Files**:
  - `Assets/FYAsset/Scripts/Build/Collector/Editor/DependencyAnalysis/DependencyAnalyzer.cs:214-235`
  - `Assets/FYAsset/Scripts/Build/Collector/Editor/DependencyAnalysis/DependencyAnalyzer.cs:239-265`
  - `Assets/FYAsset/Scripts/Build/Collector/Editor/CollectedAssetInfo.cs:42-46`
- **Problem**:
  - 对 `noShare` 或“引用数不足”的隐式依赖，代码会把它复制到各个 `refBundle`。
  - 但 `CreateImplicitEntry()` 无论是否共享，始终写死 `GroupName = "$shared"`。
- **Impact**:
  - `CollectedAssetInfo` 层面会同时出现：
    - `BundleName = 某个普通 bundle`
    - `IsInSharedBundle = false`
    - `GroupName = "$shared"`
  - 这组字段彼此矛盾。后续 task 若根据 `GroupName` 做统计、报表、bundle 命名补充或 manifest 组装，会拿到错误语义。
- **Why this is more than a naming issue**:
  - 这是数据模型失真。当前实现把“共享策略结果”和“组归属语义”压成了一组互相冲突的字段，后续阶段很难安全消费。
- **Recommendation**:
  - 复制型隐式依赖不能复用 shared group 标识。
  - 要么保留真实 group 来源，要么显式引入“shared/duplicated decision”字段并避免让 `GroupName` 承担双重语义。

### P1-4: 依赖分析宣称支持 cycle 处理，但实际只做了防无限循环，没有真正产出诊断

- **Files**:
  - `Assets/FYAsset/Scripts/Build/Collector/Editor/DependencyAnalysis/DependencyAnalyzer.cs:82-174`
  - `Assets/FYAsset/Scripts/Build/Collector/Editor/DependencyAnalysis/DependencyAnalyzer.cs:306-312`
- **Problem**:
  - 注释写的是“全局 visited set 防止无限展开”，E4 规划中也明确提过 cycle diagnosis。
  - 但当前实现只有 `globalVisited/localVisited`，并没有真正报告 `same-bundle cycle` 或 `cross-bundle circular dependency`。
  - `bfsStack` 只是被维护，没有任何消费逻辑。
- **Impact**:
  - 当前实现最多只能“跑完”，不能告诉调用方资产依赖图已经进入不可接受状态。
  - 这会把错误从“构建期显式失败”延后成“后续 manifest / runtime 行为异常”。
- **Architecture concern**:
  - 依赖分析是 Phase 5/6 的前置正确性关口。这里只做 traversal，不做 diagnosis，模块职责没有真正闭环。
- **Recommendation**:
  - 要么补齐 cycle reporting，要么收缩注释与设计承诺，避免系统对外呈现出“已做了安全分析”的假象。

---

## P2 — Important But Non-Blocking

### P2-1: `BuildPipelineConfig.TaskEntry.DependsOn` 是死配置，SO 中的依赖信息根本不参与调度

- **Files**:
  - `Assets/FYAsset/Scripts/Build/Pipeline/Editor/BuildPipelineConfig.cs:46-55`
  - `Assets/FYAsset/Scripts/Build/Pipeline/Editor/DAGScheduler.cs:100-127`
  - `Assets/FYAsset/Scripts/Build/Pipeline/Editor/DAGScheduler.cs:238-248`
- **Problem**:
  - `TaskEntry` 上有 `DependsOn` 字段，配置文件表面看起来支持“面板编排依赖”。
  - 但调度器实际只读取 `IBuildTask.DependsOn`，SO 中的 `TaskEntry.DependsOn` 完全不生效。
- **Impact**:
  - 配置层与代码层出现双重真相，编辑器使用者会被误导。
  - 后续如果真做 UI 编排或 pipeline blueprint，可维护性会明显恶化。
- **Assessment**:
  - 这不是小偏差，而是架构契约漂移。

### P2-2: `TaskAnalyzeDependencies` 通过固定资产路径回读 `CollectorSetting`，把本应显式的数据依赖藏进了全局状态

- **Files**:
  - `Assets/FYAsset/Scripts/Build/Collector/Editor/DependencyAnalysis/TaskAnalyzeDependencies.cs:27-38`
  - `Assets/FYAsset/Scripts/FYAssetConstants.cs:67-72`
- **Problem**:
  - task 的 `ReadKeys` 只声明了 `CollectedAssets`，但执行时还会偷偷从固定路径加载 `CollectorSetting`。
- **Impact**:
  - 这让 task 无法仅靠 `BuildContext` 重放。
  - 测试、CLI 驱动、未来多配置并行执行都会被这个隐藏依赖卡住。
- **Architecture concern**:
  - E5-1 的目标是把构建流程变成显式 DAG 数据流；这里又把关键输入退回全局 AssetDatabase 查找，破坏了内聚性。

### P2-3: `SharePolicyConfig.MinAssetSizeBytes` 已进入数据模型，但当前实现完全未消费

- **Files**:
  - `Assets/FYAsset/Scripts/Build/Collector/SharePolicyConfig.cs:14-24`
  - `Assets/FYAsset/Scripts/Build/Collector/Editor/DependencyAnalysis/DependencyAnalyzer.cs:176-235`
- **Problem**:
  - 配置对象暴露了 `MinAssetSizeBytes`，注释里也说明了语义。
  - 实际决策只用了 `MinReferenceCount / NoSharePatterns / ForceSharePatterns`，没有任何 size 判断。
- **Impact**:
  - 这是典型“UI/数据层已有配置，但执行层没接”的死配置。
  - 后续调参时，使用者会以为已经生效，实际构建结果不会变化。
- **Assessment**:
  - 这类问题对系统信任度伤害很大，因为最难排查。

### P2-4: `BundleDependencyGraph` 的缓存索引没有失效机制，公开 API 存在陈旧数据风险

- **File**: `Assets/FYAsset/Scripts/Build/Collector/Editor/DependencyAnalysis/BundleDependencyGraph.cs:12-57`
- **Problem**:
  - `GetDependencyMap()` 会懒构建 `_dependencyMap`。
  - 但后续 `AddEdge()` 再写入 `Edges` 时，没有让 `_dependencyMap` 失效。
- **Impact**:
  - 如果未来某个调用方先读 `GetDependencyMap()`，后续又继续增边，它拿到的就是 stale map。
  - 当前项目里暂未看到直接触发，但这个类本身已经带着时序陷阱。
- **Assessment**:
  - 这是典型的“状态缓存类没有自洽封装”的整洁度问题。

---

## System-Level Assessment

### Code Quality

- 运行时主链 `AssetPackageManager -> IAssetIndex/IPackageBackend -> AB/Legacy` 的方向是对的，抽象边界基本清楚。
- `RuntimeMessage` / `BuildMessage` 分离也比早期枚举错误码更利于扩展。
- 但 Editor build pipeline 这边已经出现“注释/配置/实现三者不同步”的问题，说明落地速度开始快于收口速度。

### Architecture

- Runtime 侧最大的优点是“新链路挂在旧 facade 后面”，替换风险被控制住了。
- 当前主要问题不在“有没有抽象”，而在“抽象是否被一致执行”。
- E5-1 的配置模型、task 契约、实际调度行为之间已经出现明显漂移，这是后续阶段最需要收敛的点。

### Cleanliness

- 注释量充足，关键类都有设计说明，这一点很好。
- 但也出现了几类不整洁信号：
  - 已暴露但未生效的配置字段
  - 仍被维护但未消费的中间状态（例如 `bfsStack`）
  - 同一个语义被多个字段冲突表达（shared vs duplicated）

### Coupling / Cohesion

- Runtime 模块内聚度整体比旧实现好。
- Editor pipeline 的内聚度弱一些，尤其 `TaskAnalyzeDependencies` 直接回读全局 SO，说明 task 还没完全进入“显式输入、显式输出”的模式。

### Performance

- Editor 侧性能问题我建议以现有文档 `review-perf-E4-E5-1-20260430.md` 为主参考。
- 本次全量 review 中更值得优先处理的是“错误行为风险”和“契约漂移”，不是纯性能。

---

## Priority Suggestion

1. 先修 `P1-1`。这是最直接的正确性问题，且会污染整个 build pipeline 的可信度。
2. 接着修 `P1-2`。这是运行时跨平台问题，风险高且容易在 Android 首包阶段暴露。
3. 再处理 `P1-3` 和 `P1-4`。这两项都属于 E4 数据与分析闭环没收好，会影响 E5-2/E6 后续实现质量。
4. P2 里优先收口 `TaskEntry.DependsOn` 和 `MinAssetSizeBytes`，因为它们最容易误导后续开发者。

---

## Verdict

当前落地代码的整体方向是成立的，尤其 Runtime facade、AB/Legacy 双后端、错误模型分层，这些基础都比旧结构清晰很多。真正的问题集中在 Editor build pipeline 这一侧：配置契约开始漂移，部分 E4/E5-1 能力只完成了“代码形态”，还没完全闭合成可靠系统。

结论不是“重构方向有问题”，而是“Phase 5 以后不能再只看能跑通，必须开始强收口抽象契约和数据语义”。否则后续 E5-2、E6、E7 会在这些未收口点上叠加复杂度。
