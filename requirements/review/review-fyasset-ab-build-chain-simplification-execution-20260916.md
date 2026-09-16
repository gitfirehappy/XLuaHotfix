# FYAsset AB Build Chain Simplification 执行效果审查

> **日期**：2026-09-16  
> **审查范围**：`requirements/plan/plan-fyasset-ab-build-chain-simplification-20260912.md` 及其对应当前工作区实现  
> **审查目标**：核对实现效果、Confirmed Decisions 偏离程度、任务完成度与验证可信度  
> **工作区处理**：只读审查，未修改生产代码、测试代码或计划内容

## 结论

**结论：未通过，不可签收。**

本轮已经完成较大范围的结构性迁移：AB Content/Manifest 模型、Address 索引、Summary 复用记录、Hotfix 状态链清理、BuildRequest/BuildResult 重命名、Runner attempt 交付骨架和大量句柄行为均已落地。纯 .NET 验证也全部通过。

但当前实现仍存在两处会直接影响运行时正确性的缺陷，以及多处计划明确要求收口而尚未收口的边界。尤其是：

- Single 场景替换时忽略异步卸载失败，可能在场景仍未卸载时释放 Bundle 和全部 Handle 状态；
- 同 Address 异步加载 follower 在 leader 完成的竞态窗口可能访问已删除的 inflight 字典项；
- RawFile、冻结后的依赖分析输入、Labels 规范化、Generate/Verify Task 职责、Hotfix baseline 窄接口均未按已确认决策完成；
- 活动计划虽标为 Complete，计划索引仍写成 `Approved / Pending execution`，当前 review 索引仍显示无 active review。

因此，当前完成度应表述为：**主干结构已实现，确认边界未闭合，运行时仍有阻断签收的问题。**

## 新鲜验证

本次重新执行：

- `dotnet build XLuaHotfix.sln --no-restore --no-incremental`：exit 0，0 errors，4 个既有 `MSB3277` 引用冲突警告；
- `HotfixRuntimeStateMachineTests`：PASS；
- `S2RuntimeBoundaryTests`：PASS；
- `ab_remediation`：6/6；
- `build_cache`：43/43，3/3 groups；
- `hotfix_flow`：8 个场景全部通过；
- `pipeline_compose`：29/29，4/4 groups；
- `pipeline_realignment`：8/8；
- `publish_diff`：8/8；
- `runtime_resource`：全部场景通过；
- `s3_resource_boundary`：6/6；
- `serialization`：PASS；
- `git diff --check -- .`：exit 0。

这些结果证明现有测试和编译没有回归，但不能证明下面列出的确认边界已经实现，因为其中多项没有对应的行为测试，或现有测试仍为 source-shape 检查。

## 发现

### P1：Single 场景替换忽略卸载失败，提前结算全部状态

**位置**：[ABSceneLoader.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/AB/Runtime/ABSceneLoader.cs:148)

`SettleReplacedSceneAsync` 调用 `WaitForOperationAsync(operation)` 后不检查返回结果；当 `UnloadSceneAsync` 返回 null operation 时也继续执行。随后无条件执行：

- 删除 `_records` 中的旧 SceneRecord；
- `HandleRegistry.ReleaseAllForEntry(record.Address)`；
- `ReleaseContentReference(record.ContentFileName)`。

这违反计划中“卸载失败保留 SceneRecord、Handle 和 Bundle 引用，供重试”的明确决策。真实卸载失败时，场景仍可能存活，但记录和 Bundle 引用已被释放，后续无法重试，且可能造成仍在使用的 Bundle 被卸载。

现有 `test_scene_ownership.cs` 只覆盖 `SceneHandle.ReleaseAsync` 的 fake sink，不覆盖 Single 替换的 `SettleReplacedSceneAsync` 路径，因此当前  runtime_resource 全绿不能排除该缺陷。

**处理要求**：卸载 operation 为 null 或等待结果失败时，保留旧记录、所有 token 和 Bundle 引用；只有确认真实卸载成功后才能删除记录并结算所有权。

### P1：异步同 Address follower 存在 inflight 字典竞态

**位置**：[ABAssetLoader.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/AB/Runtime/ABAssetLoader.cs:61)

leader/follower 判定在锁内保存了 follower Task，但 follower 在锁外没有使用该局部变量，而是再次通过 `_inflightLoads[address]` 读取：

```csharp
if (_inflightLoads.TryGetValue(address, out Task follower))
    leader = null;
...
await _inflightLoads[address];
```

leader 的 `finally` 会先移除字典项，再唤醒 follower。若 follower 恰好在这两个动作之间执行，将收到 `KeyNotFoundException`，而不是共享 leader 的结果。这违反按 Address 共享 inflight 加载的稳定性要求。

现有测试主要覆盖同步 follower 在异步加载期间快速返回 `LoadInProgress`，没有覆盖异步 follower 在 leader 完成和字典移除之间的调度窗口。

**处理要求**：锁内保存并使用 follower Task；同时补充 leader 完成、移除 inflight、唤醒 follower 顺序下的行为测试。

### P1：AnalyzeABDependenciesTask 重新读取可变配置，破坏冻结输入边界

**位置**：[AnalyzeABDependenciesTask.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/AB/Build/Collector/Editor/DependencyAnalysis/AnalyzeABDependenciesTask.cs:20)

计划明确要求依赖分析消费 `BuildContext` 中冻结的 `CollectedAssets`、`SharePolicy`、依赖过滤和 RawFile 规则；不得在 Task 内重新读取 `AssetCollectionSetting`。当前实现：

- 直接通过 `AssetDatabase.LoadAssetAtPath` 重新读取 `AssetCollectionSetting`；
- `SharePolicy` 缺失时回退读取 SO；
- `RawFileRules` 和 IgnorePatterns 始终从 SO 读取。

这使自定义 Task 或上游构造的冻结输入不能成为唯一事实来源。构建过程中配置资产发生变化时，采集结果与依赖分析可能来自不同版本的配置。

**处理要求**：在 Collect Task 一次性写入所有后续需要的冻结配置；Analyze Task 只从 `BuildContext` 读取，并对缺失的必需输入明确失败。

### P1：RawFile 未按确认的独立 owner 边界实现

**位置**：[ABAssetLoader.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/AB/Runtime/ABAssetLoader.cs:8)、[ABPackageManager.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/AB/Runtime/ABPackageManager.cs:112)

计划要求：

- `IABAssetLoader` 只处理已解析的 SerializedObject；
- RawFile 由无状态 `ABRawFileReader` 读取；
- RawFile 不进入资源缓存、Bundle 生命周期和 Handle 协议。

当前 `IABAssetLoader` 仍暴露 `LoadRawBytesAsync`，`ABAssetLoader` 仍直接从 `ActivePackageRoot` 读取 RawFile，`ABPackageManager` 也通过 `_assetLoader.LoadRawBytesAsync` 进入该路径。生产树中没有 `ABRawFileReader`。

这不是命名问题，而是已确认的 ownership/lifetime 边界仍未落地；后续 RawFile 逻辑会继续与 Asset loader 的接口和职责耦合。

### P1：Labels 未执行大小写不敏感的重复拒绝与规范化

**AB 位置**：[AssetCollectionSettingValidator.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/AB/Build/Collector/Editor/AssetCollectionSettingValidator.cs:140)、[CollectionScanner.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/AB/Build/Collector/Editor/CollectionScanner.cs:360)

Validator 只检查空项、首尾空白和非法字符；`CopyLabels` 只复制非空字符串。`UI` 与 `ui` 可以同时进入扫描结果和 Manifest，没有大小写不敏感重复检测。

**AA 位置**：[AAAssetIndexBuilder.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/AA/Build/Release/Editor/AAAssetIndexBuilder.cs:33)、[PackageEntry.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/AA/Runtime/Manifests/PackageEntry.cs:15)

AA 构建侧直接复制 `entry.labels`，同样没有非空、Trim、大小写不敏感唯一性校验。`PackageEntry.Type` 的注释还保留“取第一个 Label”的过期说法，与当前 `AssetTypeKey` 生成逻辑不一致。

这违反 AA/AB 共同的 Labels 决策：保存和扫描拒绝重复，保留首次拼写用于展示和序列化。当前测试没有覆盖 AA/AB 两侧的重复 Label 行为。

### P1：Generate/Verify 仍承担计划明确删除的文件 IO 和重复校验

**Generate 位置**：[GenerateABManifestTask.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/Tasks/GenerateABManifestTask.cs:71)

`GenerateABManifestTask` 仍按 `tempDir + FileName` 检查文件、重新计算 Hash/CRC/Size。计划要求该 Task 只把 `ContentBuildResult` 已完成的构建事实映射为 Manifest，不读取临时文件、不重算摘要。

**Verify 位置**：[VerifyABContentTask.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/Tasks/VerifyABContentTask.cs:243)

`VerifyABContentTask` 仍：

- 重新计算 Hash/CRC/Size；
- 检查 UnityFS 文件头；
- 扫描临时目录并执行文件集合/孤儿文件检查。

这些职责与确认的 Verify 边界冲突：Verify 只应校验 Manifest 与当前 ContentBuildResults 的映射、成员关系和 DependencyIndices。当前实现因此仍将临时目录布局和重复摘要计算耦合到后续 Task，无法声称已完成 Task ownership 收口。

### P2：Framework built-in 仍绕过 CollectionScanner

**位置**：[CollectABAssetsTask.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/Tasks/CollectABAssetsTask.cs:64)

计划明确要求 framework built-in assets 必须由 `CollectionScanner` 统一包含，不能作为 `CollectABAssetsTask` 的后处理 patch。当前仍由 `AppendBuiltinAssets` 使用独立的 `AssetDatabase.FindAssets` 追加，并绕过 CollectionScanner 的地址、Label、分类和规则校验。

这可能造成 Editor preview 与真实构建集合不一致，也使内置资源不受同一套采集规则约束。

### P2：builtin AssetType 使用短类型名，破坏 AA/AB 精确类型键一致性

**位置**：[CollectABAssetsTask.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/Tasks/CollectABAssetsTask.cs:103)

普通 AB 扫描和 AA 使用 `AssetTypeKey.FromType`，生成 `assembly-simple-name:type-full-name`。builtin 追加逻辑却使用：

```csharp
AssetDatabase.GetMainAssetTypeAtPath(path)?.Name
```

因此 builtin AB Manifest 可能写入 `Texture2D`，而同类型普通 AB/AA 写入 `UnityEngine.CoreModule:UnityEngine.Texture2D`，违反共同 exact type identifier 决策。

### P2：ContentBuildItem 未完成确认的字段命名和最小模型收口

**位置**：[BuildABContentTask.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/Tasks/BuildABContentTask.cs:710)

当前 `ContentBuildItem` 已经只有一个物理输出，但字段仍叫 `PhysicalName`。计划明确要求该短生命周期记录只保留单一 `FileName`，并删除多输出表达。当前行为接近目标，但模型契约和确认术语未完成，仍会让后续 Task/custom Task 面对旧的物理命名语义。

### P2：HotfixBaselineResolver 对外暴露完整 Summary DTO

**位置**：[HotfixBaselineResolver.cs](/E:/unity/project/XLuaHotfix/Assets/FYAsset/Scripts/Shared/Build/Editor/Summary/HotfixBaselineResolver.cs:14)

计划要求 resolver 只返回 `packageRoot` 和 `fullBuildId`，不返回 Summary DTO，不拥有额外的 Summary 数据边界。当前 `TryResolve` 仍返回 `out CompleteBuildSummary.SummaryDocument fullSummary`，AB Export 和 AA Prepare 仍接收该 DTO；AB 还使用 `baseSummary.BuildId` 仅用于日志。

这属于已批准的窄接口未真正落地，虽然当前路径结果通常正确，但增加了 resolver 与 Summary 表示层的耦合。

### P2：活动文档仍描述已删除类型和旧运行时边界

**位置**：[ab-runtime-运行时加载.md](/E:/unity/project/XLuaHotfix/docs/FYAsset/ab-runtime-运行时加载.md:5)

当前文档仍描述：

- `ABManifestLoader`；
- `RuntimeAssetEntry`；
- EntryId 索引；
- `IsPublic` 过滤；
- `LoadSceneAsync(..., bool = true)` 等旧 API 语义。

这些内容与当前生产源码和计划确认的运行时边界冲突。虽然计划把文档对齐列为执行范围，且当前纯 .NET 测试不会检测活动文档中的这些过期事实，因此该项降低维护和审查可信度。

### P2：计划和 review 队列状态没有同步

证据：

- 活动计划页首已写 `Status: Complete`；
- `requirements/plan/INDEX.md` 仍写 `Approved / Pending execution`；
- `requirements/review/INDEX.md` 仍写 `No active reviews`；
- `progress.txt` 已记录实现完成并等待审查。

这不改变运行时行为，但会使后续协作误判当前计划尚未执行，或误以为本报告不属于活动 review。应在开发者确认本报告结论后再同步队列状态，不应在未处理 P1 前把计划标记为已签收。

## 已确认未发现明显偏离的部分

以下条款在当前生产代码、测试和新鲜输出中基本成立：

- `ContentBuildResult` 已替代 `BundleBuildInfo`，并表达单个物理 Content 输出事实；
- `ManifestAssetEntry` 已删除 `EntryId`、`IsPublic`、逐资产 `ContentType`，运行时使用 `Manifest-local ContentIndex`；
- `ManifestContentEntry` 已覆盖 FileName、Hash、CRC、Size、ContentType 和 DependencyIndices；
- `ABManifest` 初始化校验 Content 文件名、依赖下标、公共 Address 和 ContentIndex；
- 生产源码中 `HotfixContentState`、`ClientUpdateRequiredInfo`、`ContentDependencyIndexResolver` 已无引用；
- `VersionNumber` 已收敛为 Major、Minor、Patch、Channel，未发现 Build/DailyBuildCount 生产引用；
- AB/AA 的 AssetType 主体已使用 `AssetTypeKey.FromType`；builtin 路径是上述例外；
- Runner 已形成 attempt promotion 与 BuildProjectRunner 交付事实提交的两层结构；
- 纯 .NET 场景矩阵、solution 编译和 diff-check 均有本次新鲜输出。

## 完成度评估

| 计划部分 | 评估 | 说明 |
|---|---|---|
| T1 AB Content Build | 部分完成 | Content 结果模型和一 Content 一文件主体已落地，但 `ContentBuildItem.PhysicalName` 未收口；后续依赖边界仍需复核 |
| T2 Manifest And Reuse | 部分完成 | Manifest/ContentIndex/Summary reuse 已落地，但 Generate/Verify 仍重复读盘和校验 |
| T3 Runtime Loading | 未达到签收 | Address、Bundle owner、Handle 结构已落地，但 RawFile owner、场景替换失败和 async follower 仍有问题 |
| T4 Shared Build And Hotfix | 基本完成但接口未收口 | 状态链、事件载荷和版本字段已删除；baseline resolver 仍暴露 Summary DTO |
| T5 Delivery And Verification | 部分完成 | 纯 .NET 验证充分，但 Labels、builtin、冻结输入和 Task ownership 未闭合 |

综合判断：**结构性完成度较高，目标边界完成度不足；按签收口径不超过“待修正”阶段。** 不建议使用 `Complete` 作为已验收状态。

## 验证边界与未执行项

以下项目在活动计划中明确列为本轮未执行，因此本报告不将其单独判为实现缺陷：

- Unity Editor/Player E2E；
- 真实 AB 构建矩阵；
- 真实远端发布矩阵。

但它们仍是后续签收前必须补做的验证，尤其是场景卸载、Bundle 生命周期、RawFile 实际读取、AssetType/Labels Manifest 产物和真实 AB 包布局。纯 .NET 场景不能替代这些宿主级验证。

## 建议处置顺序

1. 修复 Single 场景替换失败状态丢失和 ABAssetLoader async follower 竞态，并新增对应行为测试。
2. 按确认边界拆出 `ABRawFileReader`，移除 `IABAssetLoader` 的 RawFile 方法。
3. 让 Collect Task 输出完整冻结配置，Analyze Task 停止读取 SO；同时补齐 AA/AB Labels 校验和首次拼写保留规则。
4. 收口 Generate/Verify Task：摘要事实只来自 `ContentBuildResult`，Verify 不再读取临时目录或重复计算摘要。
5. 将 builtin 资源纳入 CollectionScanner，并统一使用 `AssetTypeKey.FromType`。
6. 收窄 HotfixBaselineResolver 返回值，重命名 `ContentBuildItem.PhysicalName` 为 `FileName`。
7. 修正活动运行时文档和计划/review 队列状态；完成后再运行 Unity/Player、真实 AB 和发布矩阵。

本报告不包含 Git commit，也未改变当前工作区的未提交状态。
