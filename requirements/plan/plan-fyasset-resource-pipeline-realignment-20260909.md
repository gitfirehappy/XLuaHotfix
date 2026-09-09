# FYAsset 资源管理管线纠偏与精简

> 状态：已批准 / 待下次执行（2026-09-09）
>
> 本计划记录经 `grill -> prs-design` 确认的目标。当前轮只提交计划、进度和此前已完成工作区；下次执行必须从任务 T0 开始，不得把本计划状态提前写为已实现。

## Purpose / Constraints / Success

### 目的

纠正资源管线从简单构建、差异和热更逐步扩张成 Repository、baseline、双重索引、后端兼容门面和混合职责的问题。最终保留四段可验证流程：

1. 构建与完整包导出；
2. 无状态差异比较；
3. 本地交付与发布；
4. 运行时热更。

构建和热更是核心；差异与发布是小型辅助流程。AB 需要形成从 Collection、依赖分析、内容构建、Manifest 到 Asset/Scene/RawFile 加载与 Handle 释放的闭环。AA 保留 Addressables 自有机制，只与 AB 共用无后端字段的基础协议和流程语义。

### 约束

- 真实项目只能直接使用 AA 或 AB 中一个后端，严禁混合、兼容、自动切换或失败后尝试另一后端。
- Shared 不引用 Catalog、AAManifest、ABManifest、Bundle 构建结果、Collector 或其他后端字段。
- 框架不引入 Git，不提交、不回滚、不管理外部版本历史；使用方可依据构建信息自行接入 Git、CI 或制品库。
- 不保留开发期数据兼容壳；删除字段、序列化布局和二进制格式可直接不兼容。
- 当前移除 Package 配置和运行时假能力；真正多包作为待定项，未来必须单独设计。
- Full 必定导出 Hotfix 目录；Full 的 Hotfix 目录只有完整 Manifest/索引、没有变化内容文件是正常结果。
- Hotfix 构建仍属于构建管线；运行时 Hotfix 是独立的第四阶段。
- 正式热更包和已发布包按包名隔离且不可变。
- 不实施无缝热更；运行中更新通过回到安全入口、释放 Handle、重启资源管理器完成。
- 编辑已有大量未提交改动时，不回退、不覆盖当前工作区事实；执行开始必须重新记录基线。

### 成功标准

- AA 主干收敛为 5 个固定阶段，AB 主干收敛为 6 个固定阶段；每个阶段之间允许 `0..N` 自定义 Task。
- Runner 不理解后端，不参与差异、发布、PackageIndex、baseline、版本回滚和 UI 操作。
- AB 配置只保留 Group、Collector、Address/Labels 覆盖、Ignore、RawFile 和 SharePolicy。
- ABManifest 只保留运行时逻辑资源和物理内容契约；发布差异、构建报告和配置状态全部移出。
- 公共 Address 大小写不敏感且唯一；只有显式 Collector 资源进入公共索引。
- SerializedObject、Scene、RawFile 三条运行时加载链均闭环；普通 Asset 只能通过 Handle 持有。
- 激活热更包后绝不逐文件回退 StreamingAssets；失败只能整包选择当前包、内置包或阻断。
- `BuildBaselineStore`、Repository 语义和 `Latest/LatestFull` 从正式路径消失。
- 构建缓存、发布缓存、构建结果摘要职责独立；缓存损坏只退化优化，不损害正确性。
- 目标源码、测试、文档和实际 Unity 构建/运行验证全部与本计划一致。

## Confirmed Decisions

### 四段管线

```text
构建与完整包导出
→ 无状态差异比较
→ 本地交付与发布
→ 运行时热更
```

- 构建优化比较上次成功构建信息，减少本次 AB 内容构建；AA 使用 Addressables 自有缓存。
- Hotfix 导出比较上次成功构建 Manifest 和本次 Manifest，持续更新累计 Hotfix 目录；Full 重置该目录。
- 发布优化读取服务器 `PackageIndex` 指向的 Manifest，与本次 Manifest 比较，减少上传。
- 下载优化比较当前本地 Manifest 与远端 Manifest，相同 Hash 文件复制到隔离目标目录，其余下载。

### Full / Hotfix / Standalone

所有模式先得到完整构建结果，再导出：

```text
Full
├─ 完整内置包：BuildIndex(RuntimeMode=Online) + 完整 Manifest + 全部内容
└─ 必定导出 Hotfix 目录：完整 Manifest/索引；初始无变化内容文件

Hotfix
├─ 完整构建结果
├─ 不覆盖安装包 BuildIndex
└─ 累计 Hotfix 目录：完整目标 Manifest + 从 Full 后累计变化内容

Standalone
└─ 完整离线包：BuildIndex(RuntimeMode=Standalone) + 完整 Manifest + 全部内容
```

`BuildIndex.RuntimeMode` 是在线/Standalone 的唯一运行时事实来源。`FYAssetSettings.StandaloneBuild` 不得继续成为 Player 运行时事实来源。

### AA / AB 边界

- AA 和 AB 只因技术开发存在于同一仓库。
- 真实项目直接调用 `AAHotfixManager/AAPackageManager` 或 `ABHotfixManager/ABPackageManager`。
- `BackendMode` 只作为身份校验和诊断字段，不创建、不选择、不切换后端。
- 后端身份不一致一律 Error 严格阻断。
- Compat 只允许作为本仓库测试/宿主胶水，不是正式兼容入口。

### AB 公共资源和查询

- 显式 Collector 收集的资源都是公共资源；依赖分析自动发现的资源全部内部化。
- 公共 Address 在单 AB 包内大小写不敏感且唯一；冲突构建阻断。
- 不自动追加 Type/GUID，不用 Type/Labels 运行时消歧。
- 非加载查询只返回 Address：`GetAddressesByType<T>`、`GetAddressesByLabel`、`GetAddressesByTypeAndLabel<T>`。
- 实际 Asset 加载统一返回 `AssetHandle<T>`；批量加载逐项成功/失败，不做全量原子失败。
- 删除 `LoadByTypeKey`、无 Handle 的 `LoadAsset*` 和按 Address 强制卸载入口。

### Handle / Scene

- 一次 Load 或 Retain 对应一个独立 Handle token；token 的重复 Release 幂等。
- 普通 struct 复制不增加所有权；共享所有权必须显式 `Retain()`。
- 最后一个资源 token 释放时才释放 Asset、主 Bundle 和依赖 Bundle。
- Scene 使用独立 `SceneHandle` 和 `SceneManager.LoadSceneAsync/UnloadSceneAsync`。
- Additive 显式卸载；Single 在新 Scene 激活并确认旧 Scene 卸载后结算旧 SceneHandle。
- 运行中 Apply 前，AssetHandle 和 SceneHandle 必须全部归零，否则 Warning 并拒绝切换。

### AB 依赖和共享

- 构建阶段分析 Asset 级依赖；运行时只消费最终 Content 级 Bundle 依赖。
- 自定义依赖图负责规划；Unity 构建后的 `AssetBundleManifest` 是最终物理依赖事实。
- 单引用隐式依赖随引用方打包；多引用隐式依赖提取共享 Bundle。
- `ForceShare` 强制共享，`NoShare` 禁止共享；同时命中构建阻断。
- 删除 `MinReferenceCount`、`MinAssetSizeBytes`。
- Group 只控制显式资源打包；隐式依赖独立执行共享规则。

### RawFile

分类顺序：

```text
排除规则
→ RawFile 白名单
→ Scene
→ Unity 可有效识别并作为 SerializedObject 构建
→ 其余 RawFile
```

- 判断标准是 Unity 能否有效识别并进入 SerializedObject 构建路线，不把 Importer 当领域规则。
- RawFile 白名单是项目级 `.gitignore` 风格匹配，支持后缀、文件名、文件夹。
- 命中白名单的 Unity 可识别文件也按 RawFile 构建和加载。
- RawFile 不分析文件内部动态引用，业务字符串引用由业务显式加载。
- RawFile 是普通物理文件，不伪装成 Bundle，不进入 BundleLoader 和 Bundle 卸载。

### 发布和热更

- BuildRunner 不写 `PackageIndex`。
- Publisher 本地组装并校验新隔离目录，最后生成并上传 `PackageIndex`。
- 发布时读取服务器 `PackageIndex + Manifest` 一次核对；本地缓存可辅助但不是远端事实。
- 新发布目录不可变；旧包清理与发布解耦，由独立维护操作执行。
- 同 Major 严格前向更新；同版本同包只允许修复；同版本换包和降级 Warning 并拒绝自动切换。
- 远端 Major 更高通知整包更新；Major 更低视为发布/Channel 异常。
- Hotfix 是累计交付，任意同 Major 旧客户端可直接更新到最新包，不安装补丁链。

## Target Data Model

### AB Collection 配置

```csharp
public sealed class AssetCollectionSetting : ScriptableObject
{
    public AssetAddressStyle AddressStyle;
    public List<AssetCollectionGroup> Groups;
    public List<AssetOverride> AssetOverrides;
    public List<string> IgnorePatterns;
    public RawFileRules RawFileRules;
    public SharePolicyConfig SharePolicy;
}

[Serializable]
public sealed class AssetCollectionGroup
{
    public string GroupName;
    public bool Enabled = true;
    public BundlePackingMode BundlePackingMode;
    public List<Collector> Collectors;
}

[Serializable]
public sealed class Collector
{
    public string CollectPath;
    public ECollectPathType CollectPathType;
}

[Serializable]
public sealed class AssetOverride
{
    public string AssetGUID;
    public string Address;
    public List<string> Labels = new();
}

[Serializable]
public sealed class RawFileRules
{
    public List<string> Extensions = new();
    public List<string> FileNames = new();
    public List<string> Folders = new();
}

[Serializable]
public sealed class SharePolicyConfig
{
    public List<string> ForceSharePatterns = new();
    public List<string> NoSharePatterns = new();
}
```

删除 `AssetCollectionPackage`、`Group.Labels`、`CollectorType`、`ForcePayloadKind`、自动 `AssetEntries`、Role/Payload 覆盖和 Rule 名称。

未来多包待定：不保留当前 Package 兼容壳。未来必须另行设计 Package 独立 Manifest、地址空间、跨包依赖、发布、下载和运行时生命周期。

### AB 构建中间模型

```csharp
public enum AssetContentType
{
    SerializedObject,
    Scene,
    RawFile
}

public enum AssetDependencyOrigin
{
    Explicit,
    Implicit
}

public sealed class CollectedAssetInfo
{
    public string AssetPath;
    public string AssetGUID;
    public string Address;
    public string PrimaryType;
    public IReadOnlyList<string> Labels;
    public string GroupName;
    public string ContentName;
    public AssetContentType ContentType;
    public AssetDependencyOrigin DependencyOrigin;
    public bool IsPublic;
}
```

`AssetDependencyOrigin` 只服务构建诊断，不进入运行时 Manifest。

### ABManifest

```csharp
[Serializable]
public sealed class ABManifest
{
    public VersionNumber PackageVersion;
    public List<ManifestAssetEntry> AssetEntries;
    public List<ManifestContentEntry> ContentEntries;
}

[Serializable]
public sealed class ManifestAssetEntry
{
    public string EntryId;
    public string Address;
    public string PrimaryType;
    public List<string> Labels;
    public string SourcePath;
    public bool IsPublic;
    public AssetContentType ContentType;
    public int ContentIndex;
}

[Serializable]
public sealed class ManifestContentEntry
{
    public string FileName;
    public string FileHash;
    public uint FileCRC;
    public long FileSize;
    public AssetContentType ContentType;
    public int[] DependencyIndices;
}
```

删除 Manifest 的 `PackageName`、`BuildTimestamp`、自引用 `FileHash`、`DeliveryBundles`；删除 Asset 条目的 `Group/AutoAddress/BundleIndex/PayloadKind`；删除 Bundle 条目的 `BundleType/Tags/Encrypted`。`ManifestBundleEntry` 改为 `ManifestContentEntry`，`BundleEntries` 改为 `ContentEntries`。

### 构建摘要、缓存和 Diff

```csharp
public readonly struct FileDigest
{
    public string Name;
    public string Hash;
    public uint CRC;
    public long Size;
}

public sealed class CompleteBuildSummary
{
    public string BuildId;
    public string BackendId;
    public BuildType BuildType;
    public RuntimeMode RuntimeMode;
    public VersionNumber Version;
    public string Platform;
    public DateTime StartedAt;
    public TimeSpan Duration;
    public bool Success;
    public List<FileDigest> Files;
    public List<BuildMessage> Messages;
    public BuildStatistics Statistics;
}

public sealed class BundleBuildCacheEntry
{
    public string ContentName;
    public string InputFingerprint;
    public FileDigest Output;
}

public sealed class PublishCache
{
    public string BackendId;
    public string TargetId;
    public List<FileDigest> Files;
}

public sealed class FileDiff
{
    public List<FileDigest> Added;
    public List<FileDigest> Modified;
    public List<FileDigest> Unchanged;
    public List<string> Removed;
}
```

- `CompleteBuildSummary` 是构建结果面板唯一数据源；详细 Task 日志仍独立。
- Build cache、Publish cache 和 Summary 复用 `FileDigest`，但文件和生命周期严格分离。
- Build cache 按后端、平台、压缩、Unity/构建格式和影响输出的设置隔离。
- Diff 只比较两份摘要，不读 Git、PackageIndex、版本库或历史状态。

### BuildIndex

```csharp
public enum RuntimeMode
{
    Online,
    Standalone
}

public sealed class BuildIndexData
{
    public string BuildGUID;
    public string BuildTime;
    public bool IsDebug;
    public string Platform;
    public string BackendMode;
    public VersionNumber Version;
    public RuntimeMode RuntimeMode;
}
```

`BackendMode` 只校验身份；`RuntimeMode` 决定是否联网。

## Target Build Pipelines

### Runner 与 Composer

```csharp
public interface IBuildTask
{
    string TaskName { get; }
    BuildTaskResult Execute(BuildContext context);
}

public readonly struct CustomTaskEntry
{
    public string Slot;
    public string TaskName;
}

public sealed class BuildPipelineComposer
{
    public IReadOnlyList<IBuildTask> Compose(
        IReadOnlyList<CoreTaskSlot> coreTasks,
        IReadOnlyList<CustomTaskEntry> customTasks);
}

public static class BuildPipelineRunner
{
    public static BuildRunResult Run(
        BuildRequest request,
        IReadOnlyList<IBuildTask> tasks);
}
```

- Composer 由 AA/AB 各自定义主干和合法插入位置；每个位置允许 `0..N` 自定义 Task。
- 自定义 Task 通过反射或注册表解析，但反射只服务自定义 Task，不解析主干。
- Runner 创建 Context/attempt，顺序执行全部 Task，首个失败停止，捕获异常，发送状态事件，成功后提升正式输出。
- Runner 不提供 whitelist、stop-after、自由 DAG、可编辑主干、后端工厂、PackageIndex、baseline、发布和版本回滚。
- 预览功能直接调用无副作用的扫描、Compose 或 Diff 服务，不通过截断生产管线实现。

### AA 固定主干

```text
PrepareAAInput
→ [0..N]
BuildAAContent
→ [0..N]
GenerateAAManifest
→ [0..N]
VerifyAAContent
→ [0..N]
ExportAAOutput
```

- `PrepareAAInput` 吸收 AA Hotfix 资源选择；Full/Standalone 不做临时移动。
- Addressables Group 修改必须由 AA 入口 `try/finally` 恢复，不由尾部 Task 恢复。
- `BuildAAContent` 调用 Addressables 原生构建。
- `GenerateAAManifest` 从实际输出建立完整 AA 清单。
- `VerifyAAContent` 校验 Catalog、Manifest 和内容文件。
- `ExportAAOutput` 形成完整结果、模式输出和 BuildIndex。

### AB 固定主干

```text
CollectABAssets
→ [0..N]
AnalyzeABDependencies
→ [0..N]
BuildABContent
→ [0..N]
GenerateABManifest
→ [0..N]
VerifyABContent
→ [0..N]
ExportABOutput
```

- `CollectABAssets` 扫描 Group/Collector，应用排除、RawFile、Address/Labels 覆盖，补框架内置内容。
- `AnalyzeABDependencies` 分析 Asset 依赖、显式/隐式来源和共享策略，生成计划图。
- `BuildABContent` 用输入指纹复用旧内容；SerializedObject/Scene 走 Unity，RawFile 直接复制。
- `GenerateABManifest` 读取 Unity `AssetBundleManifest` 的实际依赖，生成完整 Asset/Content 映射。
- `VerifyABContent` 校验 Address 唯一、Public 边界、成员关系、Content 类型、依赖、文件集合、Hash/CRC/Size。
- `ExportABOutput` 生成完整构建结果，更新 Full/Hotfix/Standalone 输出和构建摘要；成功提升后更新构建缓存。

## Target Runtime

### ABAssetIndex

初始化时仅对 `IsPublic=true` 条目建立：

```text
Address → ManifestAssetEntry（大小写不敏感且唯一）
Type → Address[]
Label → Address[]
```

删除 Address+Type 消歧索引和 `ABPackageManager` 的重复 `_typeToKeys/_labelToKeys/_addressSet`。

### Public API

```csharp
AssetHandle<T> LoadByAddressSync<T>(string address);
Task<AssetHandle<T>> LoadByAddress<T>(string address);

IReadOnlyList<string> GetAddressesByType<T>();
IReadOnlyList<string> GetAddressesByLabel(string label);
IReadOnlyList<string> GetAddressesByTypeAndLabel<T>(string label);

Task<IReadOnlyList<AssetHandle<T>>> LoadByType<T>();
Task<IReadOnlyList<AssetHandle<T>>> LoadByLabels<T>(IReadOnlyList<string> labels);
Task<IReadOnlyList<AssetHandle<T>>> LoadByTypeAndLabels<T>(IReadOnlyList<string> labels);

Task<SceneHandle> LoadSceneAsync(
    string address,
    LoadSceneMode mode,
    bool activateOnLoad = true);

Task<byte[]> LoadRawBytesAsync(string address);
Task<string> LoadRawTextAsync(string address, Encoding encoding = null);
```

批量加载逐项返回 Handle；每项独立携带成功/失败，不因一项失败释放其他成功项。

### Handle token

```csharp
public AssetHandle<T> Retain()
{
    // 为相同 EntryId 分配新的独立 token/slot。
}

public void Release()
{
    // 只消费当前 token 一次；重复调用无效果。
}
```

- Registry 按 EntryId 统计活跃 token，最后一个 token 释放后回调 Backend。
- `ActiveHandleCount` 同时统计 AssetHandle 和 SceneHandle，用于热更 Apply 门禁。
- `Shutdown/Reset` 必须拒绝仍有 Handle 的上下文，不调用无释放回调的静默 Reset。

### Scene

官方 Unity 2022.3 约束已确认：Scene Bundle 用 `GetAllScenePaths` 取得完整路径，使用 `SceneManager.LoadSceneAsync`；`UnloadSceneAsync` 不释放 Asset；`AssetBundle.Unload(true)` 不能早于 Scene 实际卸载。

```text
Address
→ Public Scene AssetEntry
→ Scene ContentEntry
→ 加载 Content 依赖和 Scene Bundle
→ 验证 streamed Scene Bundle / ScenePath
→ SceneManager.LoadSceneAsync
→ SceneHandle
```

- Additive：调用方 `UnloadSceneAsync`，完成后释放 Content 引用。
- Single：新 Scene 激活后，确认旧 Scene 卸载，再结算旧 SceneHandle。
- `allowSceneActivation=false` 不可无限等待，避免阻塞 Unity AsyncOperation 队列。
- `Resources.UnloadUnusedAssets` 作为安全入口/场景切换的显式全局清理，不在每个 Handle 释放时调用。

## Target Hotfix State Machine

### 内容来源

```text
BuiltInPackage：StreamingAssets 内置完整包
LocalPackage：Persistent/Hotfix/Build_xxx 已激活完整包
RemoteTarget：远端 PackageIndex 指向的待准备包
```

激活后一个运行时上下文只读取一个包根目录；禁止 Manifest 来自 Local、单个内容文件回退 BuiltIn 的混合读取。

### 启动流程

```text
读取并校验 BuildIndex
→ 校验 BuiltInPackage
→ RuntimeMode=Standalone ? 直接初始化 : 继续在线流程
→ 读取并检查本地 PackageIndex/LocalPackage
→ 获取远端 PackageIndex
→ 决定保持当前、修复同包、准备前向目标、通知整包更新或阻断
→ Prepare 隔离目标：复制同 Hash 内容 + 下载剩余内容 + 元数据持久化
→ 精确校验目标完整文件集合
→ 激活并初始化具体资源管理器
→ 最后写本地 PackageIndex
→ 清理客户端旧隔离目录
```

### 失败矩阵

| 条件 | 决策 |
|---|---|
| BuildIndex 无效或 BuiltInPackage 不完整 | Error，阻断 |
| Standalone | 只用内置完整包，不联网 |
| Online 且 LocalPackage 完整 | 当前候选为 LocalPackage |
| 本地指针/目录损坏 | 使用 BuiltInPackage 继续远端检查 |
| 远端不可用 | 使用当前完整包；退化内置包时 Warning |
| 同版本同包且本地损坏 | 准备并修复同包 |
| 同 Major 严格前向版本 | 准备新目标 |
| 同版本换包或降级 | Warning，拒绝自动切换 |
| 远端 Major 更高 | 通知整包更新；有完整当前包则继续，否则阻断 |
| 目标下载/校验失败 | 删除目标，保持当前完整包 |
| Apply 时有 Handle/SceneHandle | Warning，拒绝 Apply，不强制释放 |
| 激活/资源管理器初始化失败 | 不写指针，恢复此前完整包；恢复失败则阻断 |

### 运行中 Check / Prepare / Apply

```text
Check：只检查是否存在可接受更新
Prepare：当前包继续运行，隔离下载、复制和完整校验目标
Apply：业务回到安全入口并释放 Handle 后，关闭旧 Manager，切换包根，重新初始化
```

框架不负责弹窗、UI、场景跳转、停止业务协程或强制释放 Handle。

## Target Publish

```text
读取服务器 PackageIndex
→ 读取当前发布 Manifest
→ 与本次 Manifest 做 FileDigest Diff
→ 在新隔离目录复用服务器已有 Hash 内容
→ 上传缺失/变化内容与完整 Manifest
→ 校验新目录逻辑完整性
→ 本地生成并最后上传新 PackageIndex
```

- 服务器查询失败或 Manifest 损坏时退化为完整上传。
- 本地 Publish cache 可辅助/双重检测，但不能替代服务器事实。
- Hotfix 目录是相对 Full 后累计变化内容，服务任意同 Major旧客户端直升。
- 发布不删除旧包；独立维护入口不能删除当前 `PackageIndex` 指向目录。

## Code Change Map

### T0 重新建立事实基线与 RED 门禁

**目的**：执行前锁定当前工作区，先用测试证明旧行为与目标规则的差距。

主要路径：

- `tests/scenario/`
- `Assets/FYAsset/Scripts/Tests/`
- `requirements/progress.txt`

新增/调整检查：

- Shared 不引用 AA/AB 类型；真实后端身份不匹配严格失败。
- Public Address 大小写不敏感唯一；隐式条目不进入索引。
- RawFile 白名单覆盖 Unity 可识别资源，其余不可有效序列化内容自动 RawFile。
- Runner 主干固定、自定义 Task 每槽 `0..N`、失败不提升 attempt。
- Bundle 实际依赖来自 Unity 构建结果。
- Handle token 独立、Release 幂等、SceneHandle 门禁。
- Hotfix 禁止逐文件 StreamingAssets 回退；整包退化产生 Warning。
- PackageIndex 最后发布；baseline/repository 正式入口不存在。

### T1 配置与 Collection 精简

主要修改：

- `Assets/FYAsset/Scripts/AB/Build/Collector/AssetCollectionSetting.cs`
- `Assets/FYAsset/Scripts/AB/Build/Collector/CollectorEnums.cs`
- `Assets/FYAsset/Scripts/AB/Build/Collector/AssetClassification.cs`
- `Assets/FYAsset/Scripts/AB/Build/Collector/SharePolicyConfig.cs`
- `Assets/FYAsset/Scripts/AB/Build/Collector/Editor/CollectionScanner.cs`
- `Assets/FYAsset/Scripts/AB/Build/Collector/Editor/AssetClassifier.cs`
- `Assets/FYAsset/Scripts/AB/Build/Collector/Editor/AssetCollectionSettingValidator.cs`
- `Assets/FYAsset/Scripts/AB/Build/Collector/Editor/CollectedAssetInfo.cs`
- `Assets/FYAsset/Scripts/AB/Build/Collector/Editor/CollectorReverseIndex.cs`
- `Assets/FYAsset/Scripts/AB/Build/Collector/Editor/CollectorMutationUtility.cs`
- `Assets/FYAsset/Scripts/AB/Build/Editor/ABPipeline/AssetsCollectionPanel.cs`
- `Assets/FYAsset/CollectorData/CollectorSetting.asset`

删除：

- `AssetCollectionPackage` 配置层；Setting 直接持有 Groups。
- `Group.Labels`。
- `ECollectorType/EAssetRole/EForcePayloadKind` 及逐资产 Role/Payload 覆盖。
- `AssetEntries` 自动表，改为 `AssetOverrides`。
- `IFilterRule/IGroupRule/RuleResolver/CollectAll/GroupAll/RuleDropdownHelper` 及序列化 RuleName。

必须保留等价行为：默认脚本/程序集/Editor 排除；深层 Collector 优先；同路径冲突；GUID 排除；当前父 Group 归属；场景识别。

### T2 RawFile 与共享依赖

主要修改：

- `CollectionScanner.cs`
- `AssetClassifier.cs`
- `DependencyAnalyzer.cs`
- `TaskAnalyzeDependencies.cs`
- `BundleDependencyGraph.cs`
- `BundleNameBuilder.cs`
- `FYAssetABSettings.cs` / `ABConfigPanel.cs`（仅真正属于后端设置的路径）

实现：

- 项目级 RawFile 后缀、文件名、文件夹规则。
- 验证当前发现机制是否覆盖 Unity 不可识别文件；若 `AssetDatabase.FindAssets` 缺失，再由扫描层补充文件路径，分类原则不依赖 Importer。
- 单引用随行、多引用共享、Force/NoShare 覆盖。
- 共享策略冲突构建阻断。
- Group 只控制显式资源打包。

### T3 Content 构建与缓存

主要修改：

- `Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/Tasks/TaskBuildBundles.cs`
- `Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/BundleBuildInfo.cs`
- `Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/BuildPipelineConfig.cs`
- 新增 AB build cache store（具体路径在 T0 后确定，归 AB/Build，不进 Shared Runtime）

实现：

- `EPayloadKind` 改为 `AssetContentType`。
- 逻辑 Content 名称沿用现有 Group/打包模式/Address/Labels/GUID 规则，移除 Package 前缀和 Hash 后缀。
- SerializedObject、Scene 和 RawFile 产出统一的构建内容结果；RawFile 不成为 AssetBundle。
- 构建前计算 `InputFingerprint`；旧产物存在且输出 Hash 匹配才复用。
- 指纹覆盖成员 GUID/dependency hash、依赖关系、平台、压缩和构建格式。
- 缓存缺失/损坏全量构建；成功提升后原子更新缓存。

### T4 ABManifest 与验证

主要修改：

- `Assets/FYAsset/Scripts/AB/Runtime/Manifests/ABManifest.cs`
- `ManifestAssetEntry.cs`
- `ManifestBundleEntry.cs`（改为 `ManifestContentEntry.cs`）
- `Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/Tasks/TaskGenerateManifest.cs`
- `TaskVerifyBuildResult.cs`
- `TaskWriteABPackageManifest.cs`（职责并入 Generate/Export 后删除）
- `Assets/FYAsset/Scripts/Compat/Serialization/` 对应生成器/生成文件

实现：

- AssetEntry/ContentEntry 一对多映射。
- Public 标记、ContentType、ContentIndex。
- Unity `AssetBundleManifest` 生成最终依赖下标；计划图只作预期诊断。
- 删除 Manifest 自 Hash、DeliveryBundles、Group、AutoAddress、BundleType、Tags、Encrypted。
- JSON/Binary 序列化模型与生成代码重建，不兼容旧数据。
- 完整文件集合和 Content 类型严格校验。

### T5 AB Runtime 与 Handle/Scene

主要修改：

- `Assets/FYAsset/Scripts/AB/Runtime/ABAssetIndex.cs`
- `ABPackageManager.cs`
- `ABPackageBackend.cs`
- `ABBundleLoader.cs`
- `AssetResolver.cs`
- `RuntimeAssetEntry.cs`
- `AssetHandle.cs`
- `HandleRegistry.cs`
- `ABManifestLoader.cs`
- 新增 `SceneHandle` 和 Scene loader/controller
- `Assets/FYAsset/Scripts/Compat/Runtime/FYAssetLuaAssetLoaderAdapter.cs` 及真实调用方

实现：

- 只索引 Public 条目；Address 唯一、大小写不敏感。
- 删除 TypeKey 消歧和无 Handle Asset API。
- 非加载查询返回 Address；批量加载逐项 Handle。
- RawFile 直接读 Content 文件。
- BundleLoader 只按当前激活包根读取，不逐文件回退 StreamingAssets。
- Handle Retain 分配独立 token；SceneHandle 遵循 Unity 官方加载/卸载顺序。
- 增加资源管理器 Shutdown/Reinitialize，服务运行中受控热更。

### T6 Runner、Composer 与模式输出

主要修改：

- `Assets/FYAsset/Scripts/Shared/Build/Pipeline/Editor/BuildPipelineRunner.cs`
- `BuildPipelineConfig.cs`
- `BuildTaskResolver.cs`
- `BuildContext.cs`
- `BuildResult.cs` / `BuildTaskResult.cs`
- `Assets/FYAsset/Scripts/AA/Build/Pipeline/Editor/AAPipelineBackbone.cs`
- `Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/ABPipelineBackbone.cs`
- AA/AB `BuildProjectManager`、`BuildBackend`、构建窗口和 PipelinePanel
- `TaskPrepareContext.cs`（并入 Runner）
- `TaskOrganize*`、`TaskExportLocalBuildData`（归后端 Export 阶段）
- `TaskScanAAHotfixDiff.cs`（收归 AA Prepare）
- `TaskScanABHotfixDiff.cs`（移出核心，逻辑收归 AB Export/Diff）
- `TaskWritePackageIndex.cs`（移出构建）

实现：

- AA 5 段、AB 6 段固定主干。
- `CustomTaskEntry(Slot, TaskName)`，每个位置 `0..N`。
- 自定义 Task 与主干同失败规则、同 attempt 限制。
- 去掉可编辑主干、DAG、whitelist、stop-after 和生产管线截断预览。
- Full/Hotfix/Standalone 按目标契约导出。
- `CompleteBuildSummary` 作为结果面板数据源，删除冗余大型报告状态。

### T7 Diff、Repository/baseline 退出与发布

主要修改/删除：

- `Assets/FYAsset/Scripts/Shared/Build/Baseline/`
- `Assets/FYAsset/Scripts/Shared/Build/Snapshots/ArtifactDelta.cs`
- `Snapshots/Editor/ArtifactDiffer.cs`
- `Assets/FYAsset/Scripts/Shared/Build/Editor/BuildProjectRunner.cs`
- `BuildDeliveryPromoter.cs`
- `Assets/FYAsset/Scripts/Shared/Build/Publish/`
- `Assets/FYAsset/Scripts/Compat/Editor/Repository/BuildRepositoryCLI.cs`
- AA/AB Repository Preview、Diff Panel、Publish Target Panel、维护面板

实现：

- 删除 `BuildBaseline/BuildBaselineStore/Latest/LatestFull/baseline.json`。
- 删除正式 Repository 命名和 HEAD/Commit/Repair/PushHistory 残留。
- `FileDigest + FileDiff` 是唯一无状态比较能力。
- 构建缓存、发布缓存、构建摘要独立存储。
- Hotfix 累计目录由“上次成功构建 Manifest → 本次 Manifest”持续更新，Full 重置。
- Publisher 查询服务器 `PackageIndex + Manifest`，组装不可变新目录，最后上传 PackageIndex。
- 旧包清理单独维护，不属于发布事务。

### T8 热更状态机

主要修改：

- `Assets/FYAsset/Scripts/Shared/Hotfix/HotfixFlowBase.cs`
- `HotfixStateDecider.cs`
- `HotfixContext.cs`
- `HotfixPackageInspection.cs`
- `HotfixPackageValidator.cs`
- `IHotfixPipeline.cs`
- `Assets/FYAsset/Scripts/Shared/Runtime/RuntimePathManager.cs`
- `Assets/FYAsset/Scripts/Shared/Runtime/BuildIndexData.cs`
- AA/AB HotfixManager 与 Backend

实现：

- 移除 baseline 命名；状态只表达 BuiltIn/Local/RemoteTarget/Blocked。
- `BuildIndex.RuntimeMode` 决定 Online/Standalone。
- Shared 只处理通用状态、文件摘要、隔离目录、下载和错误；AA/AB 处理清单、下载项、元数据和激活。
- 禁止热更包激活后的逐文件 StreamingAssets 回退。
- 在线退化到内置完整包必须 Warning，不修改 RuntimeMode，下次启动正常 Check。
- 增加运行中 `Check/Prepare/Apply`；Apply 由业务安全入口触发。

### T9 文档、Editor 和最终验收

同步：

- `docs/FYAsset/资源管理架构文档.md`
- `build-pipeline-构建管线.md`
- `collector-规则系统.md`
- `dependency-analysis-依赖分析.md`
- `ab-runtime-运行时加载.md`
- `hotfix-热更新系统.md`
- `repository-构建仓库.md`（删除或改为发布说明需执行时列出具体处置）
- `snapshots-差异快照.md`（删除或改为无状态 Diff 说明需执行时列出具体处置）
- `字段语义参考表.md`
- `错误处理.md`
- `自动化测试管线.md`
- `使用命令行打包.md`
- `fyasset-modeling.html`
- README、context 稳定事实和 requirements 进度

文档必须明确区分：Asset 加载与 Asset 依赖、Bundle/Content 依赖、构建 Hotfix 模式与运行时 Hotfix、正式 Standalone 与在线临时内置包退化。

## Verification Plan

### 无副作用门禁

- `git diff --check`。
- `dotnet build XLuaHotfix.sln --no-restore`。
- 现有 scenario 全量执行。
- 新增纯 .NET / stub 场景验证 Address、Diff、缓存、Runner Compose、Handle token 和状态决策。
- Shared→AA/AB 反向依赖扫描。
- 删除符号、旧字段、旧 YAML、孤儿 `.meta`、重复路径检查。
- 文档链接、路径、字段和 HTML 索引检查。

### Unity 构建验收

按 AA、AB 分别从干净构建缓存状态执行：

```text
Full
→ Hotfix 1（单内容变化）
→ Hotfix 2（另一内容变化）
→ Standalone
```

验证：

- AA 5 段、AB 6 段主干顺序和 `0..N` 自定义 Task。
- attempt 失败不污染正式输出和缓存。
- Full 必有 Hotfix 目录且初始无变化内容。
- Hotfix 2 累计包含 Hotfix 1 + Hotfix 2 相对 Full 的变化。
- AB 输入指纹命中复用；平台/压缩/依赖变化失效。
- Manifest、Content、Unity 实际依赖、文件摘要一致。
- RawFile 白名单覆盖可识别文件，不可识别文件自动 RawFile。

### Runtime 验收

- AB Address/Type/Label 查询只返回 Public Address。
- 单资源、批量资源、RawFile、Additive/Single Scene 加载和释放。
- 同 Entry 多 Handle、Retain 独立 token、重复 Release、最后释放。
- 启动在线更新、同包修复、降级拒绝、同版本换包拒绝、Major 更新通知。
- 本地包损坏 + 远端失败：整包退化 StreamingAssets 并 Warning。
- 激活包缺文件：严格失败，绝不逐文件 StreamingAssets 回退。
- 运行中 Check/Prepare/Apply；有 Handle/SceneHandle 时拒绝 Apply，释放后成功重启 Manager。
- 目标下载/校验/激活/初始化失败不更新本地 PackageIndex。

### 发布验收

仅在执行时取得明确网络/目标授权后进行；默认先用隔离本地目标模拟：

- 服务器索引缺失/损坏退化完整上传。
- 服务器 Manifest 命中 Hash 时复用，剩余上传。
- 新包目录完整后才写 PackageIndex。
- 中断上传不影响旧 PackageIndex 和旧包。
- 发布不自动清理旧包；维护入口不删当前包。

## Reverse Review

### 是否需要新的总模块

不需要 `BuildArtifact`、Repository、baseline coordinator 或统一 AA/AB Manifest。四段流程由现有具体模块收敛即可。`CompleteBuildSummary` 只是一份构建事实摘要，不拥有生命周期。

### 能否更简单

- Package 当前删除，未来多包另设计划。
- Role/Payload 混合枚举拆成推导出的 Public/Origin/ContentType；Origin 不进 Runtime。
- FilterRule/GroupRule 反射删除，保留可验证的固定扫描行为。
- RawFile 与 Bundle 共用物理摘要，不共用加载生命周期。
- Diff 无状态；缓存按用途拆开。
- Runner 只执行 Composer 结果和管理 attempt。

### 如何证伪

以下任一情况出现即说明边界设计错误：

- Shared 仍需读取 AA/AB 字段才能执行 Runner 或状态机。
- 删除 Repository/baseline 后某个正式流程无法确定自己的输入。
- 同一 Manifest 激活后仍需从另一个包目录逐文件加载。
- 隐式依赖通过公共 Address/Type/Label 被业务加载。
- RawFile 必须经过 BundleLoader 才能工作。
- 自定义 Task 能修改正式输出或越过最终校验。
- 一个 Handle 重复 Release 会消耗另一个 Retain owner 的释放权。
- 最新累计 Hotfix 不能让 Full 客户端直接更新。

## Approval And Execution Gate

2026-09-09：开发者已逐项确认本计划全部方向，并批准写入详细计划、对齐进度、提交当前所有已完成改动。当前提交不实施 T0-T9。

下次执行协议：

1. 读取本计划和最新 `requirements/progress.txt`；
2. `git status --short`，确认无意外外部改动；
3. 从 T0 RED 门禁开始；
4. 按 T1→T9 分批实现，每批具备独立测试和审查证据；
5. 删除、移动、批量重写的具体路径在对应执行批次前列明影响；
6. 未通过新鲜验证不得标记完成。
