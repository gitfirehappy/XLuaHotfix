# FYAsset Windows AB 重构偏差修复计划

> **Status**: In progress 2026-09-11 — execution approved; T0 baseline and RED gates established. T1 starts after the T0 record below.
> **Date**: 2026-09-11
> **Source Plan**: `archive/plan-fyasset-resource-pipeline-realignment-20260909.md`（T0–T9 验收失败后由本计划接管）
> **Source Review**: `../review/review-fyasset-resource-pipeline-realignment-20260911.md`（F01–F16）
> **Primary Scope**: Windows AB 构建、历史构建事实、Hotfix 发布与运行时更新；Shared 只做这些流程必需的中立能力
> **Deferred**: Android AB 内置包提取；AA 完整构建/运行矩阵；真实 Cloudflare 网络部署需另行取得授权

## Purpose / Constraints / Success

### 目的

修复上一轮资源管线重构中“旧 Repository/baseline 内核已删除，但替代状态模型和生命周期实现错误”的问题。恢复原有历史包保留语义，并建立以下可验证闭环：

1. 每次 Full/Hotfix 构建产生独立、不可变、纯发布内容的 `Build_*` 包目录；
2. 构建事实单独存储在 `BuildData/Summaries`，为版本、Hotfix Full 基准、构建复用和发布提供唯一可信入口；
3. Hotfix 每次直接相对最近成功 Full 生成累计变化，不依赖前一个 Hotfix 包；
4. 发布端与客户端下载端都只从各自当前可信完整包复用同 Hash 内容，再组装并校验新完整包；
5. Windows AB 的 Bundle/Scene/RawFile 加载、Handle 生命周期、损坏回退和同包修复满足单一所有权与事务边界；
6. 清除上一轮临时补丁留下的无效配置、固定累计目录、重复缓存、过时测试和文档。

### 约束

- 当前未提交工作树是修复基线；不得回退或覆盖与本计划无关的改动。
- 不恢复 Git Repository、对象库、HEAD/INDEX、提交历史、Repair facade、`BuildBaselineStore`、`Latest/LatestFull` 或旧 Snapshot 模型。
- `FileDiff` 保持纯函数：只比较两份文件事实，不读取 Summary Index、PackageIndex、目录历史或缓存。
- 包目录严格只包含实际发布/运行内容；构建摘要、日志、报告、失败标记不得进入包目录或 `StreamingAssets`。
- Summary/Index 只属于本地构建端，不进入服务器包、客户端包或运行时读取链。
- 不新增人工文件名风格；物理文件名由固定规则生成，Manifest 是 Address、资源归属、依赖和物理映射的唯一运行时权威。
- 不把缓存命中当作正确性条件；任何记录缺失、损坏、配方不兼容或文件校验失败都只退化为重建。
- Windows AB 是本轮行为验收范围。Android 方向已确定为“启动时异步提取完整内置包到 Persistent 后再初始化”，但本轮不实现、不宣称支持。
- AA 只接受 Shared 类型/版本入口变化所需的最小适配和编译门禁；不借本计划重构 AA 构建或运行时。
- 真实 Cloudflare 部署必须另行取得网络和目标授权；无授权时只做本地镜像、故障注入和参数验证。
- 删除、移动、批量改写业务文件须在执行批次前再次列出具体路径和影响；本计划批准不等于执行批准。

### 成功标准

- `HotfixOutput/Packages/Build_*` 恢复为 Full/Hotfix 的唯一历史包集合；不存在固定 `HotfixOutput/Hotfix/{AA|AB}` live 累计目录。
- `BuildData/Summaries/{AA|AB}/Build_*.json` 只记录成功且完成交付的构建；`BuildData/Summaries/index.json` 可由 Summary 重建。
- `VersionRecord.asset`、独立 Bundle artifact cache、`BundleFileNameStyle/FileNameStyle` 和失效的 `StandaloneBuild` 控制面退出正式路径。
- Full/Hotfix 包和 Windows Standalone 目录均不含 Summary、报告、日志或失败标记。
- 无变化二次 Full 能从 Summary 定位的历史包复用内容；内容、成员、依赖、Group、PackingMode、Address、Labels、压缩、Unity/构建格式或命名规则变化时正确失效。
- Hotfix2 直接相对同作用域最近成功 Full 生成，包含 Hotfix1 与 Hotfix2 相对 Full 的累计变化；删除 Hotfix1 不影响 Hotfix2 构建、发布或下载。
- 发布路径无法越出 owner root；服务器当前包可用时只从该包复用；不可用时用本地 Full + 当前累计 Hotfix 组装完整包，任何来源不足即失败且旧 PackageIndex/旧包不变。
- 本地包损坏后立即移除本地指针、回退 BuiltIn 并继续远端检查；同包修复只在隔离目录准备，成功后换入并删除旧目录，失败可保留诊断目录但不得激活。
- Scene 只有最后一个所有者可卸载；卸载失败保留可重试 token。同步加载遇到同 Entry 异步 inflight 时返回 `LoadInProgress`，不得阻塞或重复获取。
- Windows AB Full → Hotfix1 → Hotfix2 → 发布 → 下载 → Apply → 失败回退，以及 Windows Standalone 物理名/Manifest/Player 读取链通过新鲜验收。

## Problem And Evidence

### 已确认的执行偏差

上一轮实现不是简单“尚未验证”，而是发生了以下架构偏差：

1. 为删除 baseline，把“Hotfix 相对 Full 的累计内容语义”错误绑定成固定可变目录，覆盖了原有逐次保留的 `Build_*` 历史包。
2. 为给固定目录补身份，把 `build_summary.json/.txt` 塞进包目录，迫使发布扫描器过滤构建元数据，并污染 Standalone 的 Player 输入目录。
3. `BuildPackageResultsView` 文件和按钮仍在，但只扫描 `Packages/Build_*`；Hotfix 被移出该目录后，Hotfix 历史包管理被实质架空。
4. T9 长路径故障以“无条件 16 位纯 Hash”临时修补，没有先重建 Address、内容分桶名和物理名契约，导致配置、门禁、Manifest 交付和文档分叉。
5. 静态/纯 .NET 门禁在未覆盖真实 Player、目录事务和 Unity 生命周期的情况下被当作完成证据，实际验收才暴露 Player 编译、默认过滤、目录资产和物理名问题。

### Review 发现归并

- 发布安全与补偿：F01、F09。
- 物理命名、缓存门禁和配置残留：F02、F03、F13、F14、F15。
- Hotfix 回退与修复事务：F04、F05。
- Handle/加载所有权：F06、F07、F11。
- 平台路径：F08、F12；F08 Android 本轮延期，F12 仍修复通用路径比较。
- 可复现性和工作区收尾：F10、F16。
- 新增偏差：固定累计目录、Summary 进入包、Hotfix 历史管理失效、`VersionRecord` 与 Summary Index 职责缺失。

### 清理成果边界

以下上一轮成果保留，不回退：

- Repository 生产内核、Facade、Preview、CLI 已删除；
- `BuildBaseline`、`BuildBaselineStore`、`Latest/LatestFull` 已删除；
- `Snapshots`、`ArtifactDelta`、`ArtifactDiffer`、旧 Diff UI 已删除；
- `FileDigest + FileDiff` 已成为中立、无状态比较能力；
- AA/AB 固定主干与 Shared 中立 Runner 的总体方向保留；
- AB Manifest 的公共 Address/内容映射方向和 Handle token 独立所有权方向保留。

不能把固定累计目录或 Summary Index 称为 Repository 恢复：Summary Index 只定位不可变成功事实，没有提交历史、对象库、暂存区、分支或回滚职责。

## Confirmed Decisions

### 1. 构建事实与版本状态

#### Summary

- 每次构建在内存中生成 `CompleteBuildSummary`，供本次结果面板显示成功或失败。
- 只有包交付、必要的本地启动数据和最终校验全部成功后，才写正式 Summary。
- 正式路径：`BuildData/Summaries/{AA|AB}/{BuildPackageName}.json`。
- 正式 Summary 不可变；失败构建不写正式 Summary，只保留内存结果、构建日志和既有诊断报告。
- 删除普通包目录时保留 Summary；UI 动态显示 `ArtifactState=Missing`，该记录仍证明版本曾成功，但不能用于发布、Hotfix 基准或构建复用。
- 删除重复的 `build_summary.txt`；可读信息由 UI、JSON、日志和报告承担。

Summary 至少记录：

```text
SummarySchemaVersion
BuildId / PackageName
BackendId / Platform / Channel
BuildType / RuntimeMode / Version / BuildNumber
StartedAtUtc / FinishedAtUtc
ArtifactRelativePath
BaseFullSummaryId（Hotfix）
BuildRecipeFingerprint
CollectionFingerprint
每个内容桶：ContentIdentity、InputFingerprint、FileName、FileHash、CRC、Size、DependencyFileNames
完整包/累计 Hotfix 的发布文件摘要
Messages / Statistics
```

- `ArtifactRelativePath` 必须是受控根下的相对路径；读取后规范化并验证 containment，不信任任意绝对路径。
- Summary 记录的是构建事实和定位信息，不复制 Manifest 内容，不成为运行时 Manifest 的第二权威。

#### Summary Index

路径：`BuildData/Summaries/index.json`。

职责：

```text
ProjectVersion
  CurrentSuccessfulVersion
  LastBuildDate
  DailyBuildCount

Scopes[Backend, Platform, Channel]
  LatestSuccessfulSummaryId
  LatestFullSummaryId
```

- 项目版本全局唯一，不按后端或平台分别推进。
- Full 默认 Major+1；Hotfix 默认 Patch+1；Channel 默认继承当前值。
- Channel 只能在构建确认界面显式切换为 `alpha/beta/rc/release`，不得被默认参数静默清空。
- 目标版本必须严格高于当前全局成功版本；允许 `beta → rc → release`，禁止自动降级。
- Full/Hotfix 基准按 Backend + Platform + 目标 Channel 隔离。
- Hotfix 缺少同 Major、同作用域的成功 Full 时必须拒绝构建。
- Index 是可重建定位加速；缺失或损坏时枚举、校验所有正式 Summary 后重建。
- 删除历史包或制品缺失不得让 `ProjectVersion` 倒退。
- 删除 `VersionRecord.asset`、`VersionRecord` 类型、设置路径、重置工具和 UI 中对应状态；版本候选计算转为纯服务，提交由 Summary 事务所有者负责。

#### 事务提交顺序

```text
构建 attempt
→ 校验完整构建结果
→ 提升 Full/Hotfix Build_* 包或替换 Standalone
→ 应用 Full/Standalone 本地启动数据
→ 写入 staged Summary
→ 原子写/更新 Summary Index（最后一个可见身份提交点）
→ 提交目录与本地数据补偿 token
```

任一步失败：恢复旧 Index、本地启动数据和被替换目录；删除本次正式 Summary 与新交付包；项目版本不得推进。

### 2. 历史包与 Hotfix 构建

- Full 与 Hotfix 每次都写独立、不可变的 `HotfixOutput/Packages/Build_{yyyyMMddHHmmss}_{version}/`。
- 每个包目录严格只含实际发布内容。Summary、日志、报告、发布缓存、构建缓存和失败标记均在包外。
- Windows Standalone 的运行内容只写 `StreamingAssets/Standalone/`；其 Summary 仍写 `BuildData/Summaries`。
- 删除固定 `HotfixOutput/Hotfix/{AA|AB}`、`HOTFIX_OUTPUT_FOLDER_NAME` 和 `HotfixAccumulationDirectory`。
- `BuildPackageResultsView` 恢复同时展示 Full/Hotfix Summary 与制品状态；用户显式确认后删除选中包。
- 删除 Hotfix 包不影响任何后续 Hotfix；不形成补丁链。
- Full 仍被任何 Hotfix Summary 的 `BaseFullSummaryId` 引用时拒绝删除，列出依赖 Hotfix，不自动级联。
- 普通删除只删制品和可明确关联的大型报告；Summary 长期保留。彻底清空版本历史只能走单独测试重置并明确确认。

每次 Hotfix 直接执行：

```text
Summary Index.LatestFullSummaryId
→ 校验同 Backend/Platform/Channel/Major 的 Full Summary 与 Full 包
→ 读取 Full Manifest
→ 完成本次全量构建与 Manifest
→ 直接比较 Full Manifest 与本次 Manifest
→ 输出完整目标 Manifest + 相对 Full 的全部变化内容
```

- Hotfix2 不读取 Hotfix1 作为正确性基准。
- 若 A 在 Hotfix1 改变、B 在 Hotfix2 改变，则 Hotfix2 包含 A 最新内容 + B 最新内容。
- 若 A 在 Hotfix2 恢复为 Full 内容，则 A 不进入 Hotfix2 累计变化。

### 3. 构建复用

- 删除独立 Bundle artifact cache 和 `{BuildOutputRoot}/_build_cache`；历史包是唯一物理复用来源。
- Summary 是复用索引，不把元数据写回包目录。
- 复用候选只来自作用域匹配、Summary 可读、制品存在且构建配方兼容的明确记录；不得扫描目录猜测身份。
- 优先考虑 `LatestSuccessfulSummary` 与 `LatestFullSummary`；候选包缺少目标物理文件时直接跳过或重建，不建立 correctness 链。
- 复制历史文件后必须重新校验 Hash/CRC/Size；任一不符即重建。
- Summary/Index/历史包缺失或损坏只损失优化，不阻断本可完成的 Full 构建。

`InputFingerprint` 必须覆盖所有影响输出的事实，至少包括：

- 规范化内容身份和命名算法版本；
- Group、PackingMode、ContentType；
- 最终 Address、Labels、显式/隐式成员集合及 GUID；
- Asset dependency hash、成员依赖闭包和最终内容依赖；
- RawFile 规则、SharePolicy 结果；
- BundleCompression、目标平台、Unity 版本、构建格式和后端配方版本；
- 任何会改变 Bundle 字节、成员归属或 Manifest 映射的设置。

不得只用源文件 Hash 或输出 Hash 判断可复用；分组、Labels、Address、PackingMode 或依赖变化必须失效。

### 4. Address、内容分桶与物理文件名

#### 权威边界

- `ManifestAssetEntry.Address` 等于编辑器最终配置的 Address：自动短名、自动完整路径名或人工自定义名。
- 运行时 Address 查询不读取、不解析物理文件名。
- Manifest 的 Asset `ContentIndex`、Content `FileName/DependencyIndices` 是资源归属、物理定位和依赖的唯一运行时权威。
- 物理文件名只服务包内定位与人工粗略识别。
- 删除 `BundleFileNameStyle`、`BuildPipelineConfig.FileNameStyle`、对应 YAML、升级逻辑、UI 和文档；不提供人工命名风格选择。

#### 公共规则

- 固定使用 12 位十六进制内容身份 Hash；不是最终文件字节 Hash。
- 构建期检查最终物理文件名大小写不敏感唯一；碰撞时报告两个内容桶的完整规范化身份并阻断，不随机追加字符。
- 实际文件 Hash/CRC/Size 继续由 Manifest 和 Summary 记录并校验。
- 所有最终文件名执行跨平台非法字符、Windows 设备名、尾部空格/句点和完整路径预算校验。
- 自动短名来自资源文件短名；自动 Address 即使是完整路径，物理名仍只使用资源短名。
- 人工自定义 Address 保留原始大小写和文本作为可读段，不进行静默规范化或截断；因此编辑器保存时必须阻断非法/过长自定义 Address，构建扫描时再次校验以防 YAML 绕过。
- Manifest 中的 Address 始终保留原值，物理名校验不改变查询 Address。

#### `PackTogether`

语义：同一 Group 的全部显式 `SerializedObject` 进入一个 Bundle，不按 `PrimaryType` 二次拆分。

```text
{group}_all_{identityHash12}
```

- Scene、RawFile 不进入整 Group Bundle，仍强制 `PackSeparately`。
- 隐式依赖按共享规则处理，不由 Group 模式控制。

#### `PackSeparately`

语义：每个显式资产独占一个物理内容文件。

```text
SerializedObject: {group}_asset_{readableName}_{identityHash12}
Scene:            {group}_scene_{readableName}_{identityHash12}
RawFile:          {group}_raw_{readableName}_{identityHash12}
```

- 自动 Address 的 `readableName` 为资源短文件名。
- 人工自定义 Address 的 `readableName` 为通过安全/长度校验后的原始 Address。
- Hash 输入至少包括 Group、ContentType、规范化最终 Address、GUID、分桶规则版本及成员身份。

#### `PackTogetherByLabel`

语义：最终 Labels 集合完全相同的显式 `SerializedObject` 进入一个 Bundle。

```text
有 Labels: {group}_labels_{label1-label2}_{identityHash12}
无 Labels: {group}_unlabeled_{identityHash12}
```

- Labels 去空、去重、统一小写、按 ordinal 排序，再用 `-` 连接。
- Label 输入顺序变化不改变内容身份；Label 集合变化必须改变身份和缓存指纹。
- Label 可读投影遵守既有安全规则；不得恢复多份复制式“每个 Label 一个 Bundle”。

#### 隐式共享依赖

- 单引用且未 `ForceShare`：随引用方打包，不产生独立内容。
- 多引用或命中 `ForceShare`：每个共享资产独立一个 Bundle，不再按 `PrimaryType` 合并。
- `NoShare` 多引用冲突继续阻断。

```text
shared_{assetShortName}_{identityHash12}
```

共享条目保持 `IsPublic=false`，业务不能按 Address/Type/Label 直接查询。

### 5. 发布输入、安全与复用

#### 发布身份

- 生产包名只接受 `Build_{yyyyMMddHHmmss}_{VersionNumber}`；时间戳和版本都必须严格解析。
- `PublishRequest` 删除调用方可覆写的 `PackageName/Version`；发布 UI 选择正式 Summary，发布器由 Summary 取得包身份和制品路径。
- Summary 与所指包目录、Manifest 版本/后端必须一致；不一致时拒绝发布。
- `PackagesFolderName` 可配置，但必须是单个安全目录段。

#### 路径边界

所有创建、复制、移动、替换、删除之前必须：

1. 拒绝空值、`.`、`..`、根路径、分隔符、非法字符和设备名；
2. 使用 `Path.GetFullPath` 规范化 owner root、work root、packages root、target package 和 index path；
3. 使用平台正确的路径比较确保子路径仍位于声明 owner root 内；
4. 禁止符号链接/重解析点把操作引出 owner root；
5. 在实际 mutation 边界使用已经验证的路径对象或重复断言，不传回未经验证的字符串。

#### 上传复用与完整组装

三类 Diff 分开：

- 构建 Diff：本次完整构建 vs 最近成功 Full，决定累计 Hotfix 包内容；
- 上传 Diff：本次目标 Manifest vs 服务器当前完整包，决定网络上传内容；
- 下载 Diff：远端目标 Manifest vs 客户端当前完整包，决定网络下载内容。

发布只信任服务器 `PackageIndex` 当前指向且完整校验通过的一个包；不扫描历史服务器目录。

目标新包按目标 Manifest 逐文件组装，来源优先级：

1. 服务器当前包中 Hash/Size（并最终校验 CRC）一致的内容；
2. 本地当前 Hotfix 累计包中的变化内容；
3. 当前 Hotfix Summary 指向的 Base Full 包中的未变化内容。

- 服务器当前包不可用时，不应把稀疏 Hotfix 包直接称为“完整上传”；必须由本地 Hotfix + Base Full 组装完整目标。
- 服务器和本地 Base Full 都无法提供目标文件时发布失败；不得写新 PackageIndex 或留下半成品目标。
- 新目录完整校验通过后才就位；PackageIndex 最后写入。
- 服务器同名同内容重发幂等成功；同名内容不同拒绝覆盖。
- 发布不自动删除旧服务器包；维护入口不能删除当前 PackageIndex 指向包。

#### Cloudflare

- Cloudflare 本地 service root 是 Wrangler 的完整镜像输入，也是本次部署的可回滚本地状态。
- 恢复基线中“发布事务保持未提交直到 Wrangler 成功”的语义；禁止手工删除旧同名包后只恢复索引。
- Wrangler 失败时恢复旧包目录、旧 PackageIndex 和 `_headers`；成功后才提交本地镜像事务。
- 使用可注入进程执行 seam 做失败/超时测试；真实网络部署另行授权。

### 6. Windows AB Hotfix 状态机

#### 损坏 Local 回退

- BuiltIn 完整性先验证。
- 本地 PackageIndex 指向的包损坏时，立即原子移除本地指针；物理损坏目录可保留诊断，但不得激活。
- 当前候选恢复为已验证 BuiltIn 的 identity/root/inspection，再继续远端检查。
- 远端不可用时以 BuiltIn 启动并给出 Warning；`RuntimeMode` 不改变。

#### Prepare

- 当前完整包继续运行；所有复制、下载和元数据写入只发生在目标 sibling staging。
- 当前包中相同 Hash 的内容复制到 staging，其余下载；复用与下载都经临时文件和 Hash/CRC/Size 校验。
- staging 按完整目标 Manifest 校验通过后才进入 Prepared。
- 同包修复也必须使用独立 staging，不能写入当前/损坏正式目录。

#### Apply 与同包替换

```text
确认 AssetHandle + SceneHandle = 0
→ Shutdown 当前 ABPackageManager
→ 旧目标目录 move 到 backup（若存在）
→ staging 原子 move 到正式 Build_* 路径
→ 激活新 ActivePackageRoot
→ 初始化 ABPackageManager 并校验 Manifest
→ 最后原子写本地 PackageIndex
→ 成功后删除 backup/旧目录
```

- 同包修复成功默认删除旧损坏目录。
- 下载、校验或激活失败时可保留旧损坏目录和失败产物用于诊断，但均不得成为活动包。
- 损坏 Local 的同包修复失败后继续使用 BuiltIn，保持无本地指针。
- 从完整 Local 前向更新失败时恢复原完整 Local、原 ActivePackageRoot 和原指针。
- staging/backup 只能位于 `HotfixRoot` 同卷、框架拥有的严格命名目录；清理不扫描任意路径。

### 7. Handle 与加载并发

#### SceneHandle

- 一次 Load/Retain 对应一个独立 Scene token；普通 struct 复制不增加所有权。
- `Release()` 只放弃当前所有者，不卸载 Scene。
- `UnloadAsync()` 只有在当前 token 是最后一个所有者时才允许执行；否则状态不变，返回结构化 `ActiveSceneOwners`。
- Unity 卸载成功后才释放最后 token 和内容 Bundle；卸载失败保留 token，允许重试。
- Single 模式由 Unity 替换旧场景属于外部强制卸载：确认旧 Scene 卸载后，由 Scene record 统一结算该 Scene 的全部 token，使旧 Handle 全部失效。
- Apply 仍要求 Asset/Scene token 全部归零。

#### 同步/异步 Entry single-flight

- Entry 已缓存：同步/异步调用都只分配新的 Handle token，不重复获取 Bundle。
- 无缓存、无 inflight：同步路径可执行同步加载；异步路径成为 leader。
- 同 Entry 存在异步 inflight 时，同步加载立即返回失败 Handle，错误码 `LoadInProgress`；不得 Wait/Result/GetResult，不得重复加载。
- 一个 Entry 缓存创建对应一次 Bundle acquisition；最后一个 Entry Handle token 释放对应一次 backend/Bundle release。
- 增加 async leader + sync follower、失败后重试、最终 Bundle refcount=0 的行为测试。

#### 小型一致性修复

- AB facade lease 地址表使用 `OrdinalIgnoreCase`，与公共 Address 契约一致。
- `FYAssetPathUtility.AreSamePath` 使用平台相关 `FilePathComparison`，Windows 忽略大小写，大小写敏感平台保持区分。

### 8. 延期平台边界

Android AB 方向已确认但不在本轮执行：

```text
APK/JAR StreamingAssets
→ 启动异步读取完整 BuiltIn 包
→ Persistent/BuiltIn/{BuildGUID}.staging
→ 完整校验
→ 原子提升 Persistent/BuiltIn/{BuildGUID}
→ ActivePackageRoot 指向普通文件目录
→ 再初始化 ABPackageManager
```

该后续计划必须覆盖首次准备进度、安装包升级、新旧目录补偿、空间预算和 Android Player 验收。本轮不得以 Windows 通过宣称 Android 已支持。

## Target Call Chains

### Full

```text
SummaryIndex.ProjectVersion
→ 用户确认下一 Full 版本/Channel
→ SummaryIndex scope 定位可兼容历史 Summary 作为复用候选
→ 完整构建 attempt
→ 完整 Manifest/包校验
→ promote 独立 Build_* 完整包
→ 更新 Windows StreamingAssets BuiltIn 数据
→ 写成功 Summary
→ 最后更新 Summary Index / ProjectVersion
```

### Hotfix

```text
SummaryIndex.ProjectVersion + scope.LatestFullSummaryId
→ 用户确认下一 Hotfix 版本/Channel
→ 校验 Base Full Summary/包/Manifest
→ 完整构建 attempt（可从历史包复用）
→ Full Manifest vs current Manifest
→ 输出独立 Build_*：完整目标 Manifest + 相对 Full 的累计变化
→ 校验 Hotfix 交付集合
→ 写成功 Summary(BaseFullSummaryId)
→ 最后更新 Summary Index / ProjectVersion
```

### Publish

```text
用户选择成功 Summary
→ 解析纯包目录与目标 Manifest
→ 读取服务器当前 PackageIndex + 完整包
→ 当前服务器包 / 本地 Hotfix / Base Full 三源组装 staging
→ 逐文件 + Manifest 完整校验
→ 原子就位不可变新目录
→ 最后写服务器 PackageIndex
```

### Runtime Update

```text
检查 BuiltIn
→ 检查 Local；损坏则清指针并回退 BuiltIn
→ Check 远端 PackageIndex
→ Prepare sibling staging（当前包复用 + 下载）
→ Verify 完整目标
→ Handle gate
→ Shutdown
→ 原子换入 / 激活 / Initialize
→ 最后写本地 PackageIndex
→ 清理旧目录
```

## Task Breakdown

每个任务必须先建立能因目标缺失而失败的行为门禁，再修改实现；不得只写源码字符串断言代替行为测试。

| Task | 目的 | 主要范围 | 最小验收 |
|---|---|---|---|
| T0 | 建立修复基线与 RED | 当前 status/diff、旧计划 review、场景工程；新增偏差清单 | RED 能分别证明固定累计目录、包内 Summary、无 Summary Index、旧命名、发布越界、Hotfix 回退、Scene/inflight 问题 |
| T1 | Summary / Index / 版本事务 | `CompleteBuildSummary`、新 Summary store/index、`BuildProjectRunner`、构建确认 UI、设置与 reset 工具 | Index 缺失/损坏重建；失败不推进版本；Channel 继承；提交点故障注入全回滚 |
| T2 | 恢复独立纯包交付 | `BuildPackageRequest`、`BuildPathManager`、AA/AB Export 最小适配、删除固定累计实现、包管理 UI | Full/Hotfix 各自 Build_*；包纯净；Hotfix 可独立删除；被引用 Full 拒删 |
| T3 | 重建三种分桶与命名 | Collection/Dependency/BundleNameBuilder/BuildABContent/Manifest 验证、配置 UI | 三种模式与 shared 规则逐项行为测试；12 位 Hash；自定义 Address 双层阻断；碰撞阻断 |
| T4 | Summary 驱动的构建复用 | 删除独立 cache store/context；Summary 内容记录和历史包复制 | 无变化 Full 命中；内容/依赖/Group/Packing/Address/Labels/压缩/配方变化失效；损坏只重建 |
| T5 | Hotfix 直接 Diff Full | AB Export、Summary BaseFull 引用、Hotfix 交付校验 | Full→H1(A)→H2(B) 得 A+B；恢复 A 后只剩 B；删除 H1 后 H2 仍可构建 |
| T6 | 发布输入与路径安全 | `PublishRequest`、identity resolver、transaction、maintenance、local target | traversal/设备名/symlink 拒绝；严格 Build_*；Summary 唯一身份；owner root 外哨兵不变 |
| T7 | 发布完整组装与 Cloudflare 补偿 | publish planner/transaction、Cloudflare seam、tests | 当前服务器包只读复用；损坏服务器由 Full+Hotfix 组装；来源不足失败；Wrangler 失败完整回滚 |
| T8 | Windows Hotfix 回退与同包修复 | `HotfixFlowBase`、state/context、AB pipeline、RuntimePathManager | 损坏 Local 清指针→BuiltIn；同包 staging；成功删旧；失败保留诊断且不激活；前向失败恢复 Local |
| T9 | Scene 与 Entry 所有权 | `SceneHandle`、`ABSceneLoader`、`ABPackageBackend`、Registry/错误码 | 最后 owner 卸载；失败可重试；Single 全结算；sync/inflight 返回 LoadInProgress；最终 refcount 0 |
| T10 | 小型一致性与清理 | facade comparer、path comparer、死配置、注释、`.csproj` ignore 例外、EOL/垃圾候选 | F10–F16 对应门禁；普通 staging 可纳入 scenario projects；无死配置和无语义 diff |
| T11 | Windows AB 验收与文档 | Windows Unity/Player E2E、docs、review disposition | 下述完整矩阵通过；报告引用/链接/路径索引无误；review finding 逐条有证据关闭或明确延期 |

## Verification Matrix

### 纯 .NET / 静态门禁

- Solution build：0 errors；既有已知 warning 单独列明。
- scenario `.csproj` 必须被 Git 正常跟踪，干净 checkout 可直接执行。
- Summary/Index：重建、损坏、重复 identity、作用域隔离、版本/Channel 递进、事务故障注入。
- Naming：三种 PackingMode、自动短名/长路径 Address、自定义 Address、Labels 规范化、shared 单资产、12 位 Hash、碰撞和路径预算。
- Cache：无变化复用；资源内容、依赖、Group、PackingMode、Address、Labels、压缩、平台、Unity/格式/命名版本逐项失效。
- Publish：路径 traversal、symlink/reparse、服务器当前包复用、本地 Full fallback、稀疏 Hotfix 来源不足、不可变同名包、PackageIndex 最后提交、Cloudflare 失败回滚。
- Runtime：损坏 Local + 远端失败、同包修复、前向失败回滚、Scene 多 owner、卸载失败、sync/async Entry 竞争、大小写 Address。
- `git diff --check`、`.meta` 完整性/GUID 唯一性、活动文档链接与路径索引。

### Windows Unity 构建

从受控干净测试状态执行并保留包/Manifest/Summary 证据：

1. AB Full：独立完整 `Build_*`，Summary 在 BuildData，包内无构建元数据；
2. 同输入第二次 Full：证明历史包复用与产物等价；
3. 修改 A 构建 Hotfix1：完整目标 Manifest + 相对 Full 的 A；
4. 修改 B 构建 Hotfix2：完整目标 Manifest + 相对 Full 的 A+B；
5. 删除 Hotfix1 后重新验证/发布 Hotfix2；
6. 改 Group/PackingMode/Address/Labels/依赖/压缩，逐项证明缓存失效；
7. AB Standalone Player：Manifest `FileName`、交付物和运行时请求路径完全一致，不再出现长逻辑名请求。

### Windows 本地发布与运行时 E2E

- 首次 Full 发布；服务器无索引时由完整 Full 成功建立包和最后索引。
- Hotfix1 发布；只上传变化和新 Manifest，其余从服务器当前包复用。
- Hotfix2 发布；A Hash 与服务器 Hotfix1 一致时从当前服务器包复制，网络上传只包含 B 与必要元数据。
- 破坏服务器当前包/Manifest；由本地 Base Full + Hotfix2 累计内容组装完整新包。
- 同名同内容重发幂等；同名不同内容拒绝；中断不改旧索引/旧包。
- Full 客户端直接更新到 Hotfix2，不经过 Hotfix1。
- 本地完整包损坏 + 远端不可用：清本地指针、退化 BuiltIn、Warning。
- 同包修复：隔离准备、Handle gate、成功换入并删旧；下载/激活失败保持 BuiltIn 且不写指针。
- 从完整 Local 前向更新失败：恢复原 Local 和指针。
- Scene 多 owner 与 sync/async load 竞争在真实 Unity 生命周期下验证。

### 明确延期

- Android APK/JAR StreamingAssets 提取与 Player 验收。
- AA Full/Hotfix/Standalone 完整矩阵；Shared 变化只要求编译和不破坏现有明确门禁。
- 未授权的真实 Cloudflare 网络发布。

延期项不得被写成通过；新 review 必须区分“Windows AB 已验证”和“跨平台/AA 未覆盖”。

## Cleanup And Delivery Rules

- 旧 Repository/baseline/Snapshot 已删除部分只做残留验证，不恢复实现。
- 删除无效 `FileNameStyle`、`StandaloneBuild`、固定累计路径、独立 cache、包内 Summary 过滤逻辑和无消费者发布字段。
- `status_snapshot.txt`、纯 EOL `.meta`、混合换行等候选在执行 T10 前逐项确认归属；不得批量清理用户改动。
- 任何删除的源码/资产必须同步 `.meta`、生成序列化代码、scenario project、docs/HTML 索引和配置 YAML。
- 不提交、不创建分支；提交必须在完整验收后另行展示范围、路径和 message 并取得批准。

## Reverse Review

### 是否恢复了 Repository

没有。Summary 是不可变构建事实；Index 只有可重建引用和项目版本，没有对象历史、提交、暂存、分支、远端同步或修复命令。

### 是否需要独立 Bundle cache

不需要。历史 `Build_*` 已持有可复用字节，Summary 已持有指纹和摘要。重复缓存只增加存储、提交时序和清理所有者。

### 是否需要固定累计 Hotfix 目录

不需要。累计语义由“每次 Hotfix 直接 Diff Base Full”得到；固定目录会覆盖历史、污染包边界并重建隐式 Latest 状态。

### 如何证伪本设计

以下任一成立即说明实现或设计失败：

- 删除 Hotfix1 后无法构建、发布或从 Full 更新到 Hotfix2；
- Summary/Index 缺失使本可完成的 Full 无法全量重建；
- Group/Labels/Packing/依赖变化仍命中旧 Bundle；
- 包目录出现 Summary、报告、日志或失败标记；
- 运行时从文件名推导 Address/依赖，而不是读取 Manifest；
- 服务器当前包损坏时，稀疏 Hotfix 被直接发布为不完整包；
- 同包修复修改活动目录，或失败后指针仍指向损坏/未验证包；
- 任意 Scene owner 能让其他有效 owner 指向已卸载 Scene；
- 同步调用阻塞异步 Unity 请求或产生第二次 Entry acquisition；
- 任何发布/清理路径能触碰 owner root 外哨兵。

## Approval And Execution Gate

2026-09-11：开发者已完成 grill 并确认本计划全部设计决策；同时明确“暂不执行”。

开始执行前必须再次取得明确批准，并按以下顺序：

1. 读取本计划、源 review 和最新 `requirements/progress.txt`；
2. `git status --short`，区分当前重构工作树与新增外部改动；
3. 从 T0 偏差清单和行为 RED 开始；
4. 每个任务独立实现、独立验证、独立更新进度；
5. 临时修复不得先于契约与 RED；真实 Unity/Player 证据不得推迟到全部代码完成后；
6. 未通过 Windows AB 完整矩阵不得关闭源 review、签收或提交。

## T0 Execution Record (2026-09-11)

2026-09-11 开发者批准执行本计划（T0 起分批推进），测试策略按批准的分级方案执行：每批跑受影响门禁 + 新增门禁，全量矩阵在批次收口与 T11 终验运行。

### 基线事实

- 基线提交 `c6e27c18`；工作树保留 T1–T9 全部未提交改动（405 项 status 条目），本批未新增外部改动。
- 本批新鲜自测（T0 前）：solution 构建 exit 0（0 errors / 4 条既有 MSB3277）；pipeline_realignment 7/7、pipeline_compose 4/4、publish_diff 4/4、runtime_resource 16/16、S2、Hotfix state、S3 6/6、serialization 3 PASS 通过；build_cache 67/68（唯一红为 F03 陈旧物理命名断言）。
- 纯 .NET 门禁成本实测：9 个工程强制重编译 8.2s、串行执行 12.6s、solution 冷构建 3.4s，合计约 25s。Unity 实测为耗时主项（T9 记录：单轮 `build full` 约 2–2.5 分钟、`e2e standalone` 约 2.7–4.3 分钟）。

### 已执行的边界修复

- `.gitignore` 增加最小例外 `!tests/scenario/**/*.csproj`（F10）：9 个手维护门禁工程 csproj 现在可被 Git 跟踪，Unity 生成的根级 csproj 仍被忽略。

### 新增 RED 门禁

| 门禁/文件 | 子契约 | 目标批次 | 当前状态 |
|---|---|---|---|
| `tests/scenario/ab_remediation` SummaryOwnershipContract | 6 | T1/T2 | 6 RED |
| `tests/scenario/ab_remediation` PackagePurityContract | 3 | T2 | 3 RED |
| `tests/scenario/ab_remediation` PhysicalNamingContract | 3 | T3 | 3 RED |
| `tests/scenario/ab_remediation` HotfixCandidateContract | 4 | T8 | 4 RED |
| `tests/scenario/publish_diff` PublishContainmentRules | 5 | T6 | 4 RED / 1 绿 |
| `tests/scenario/runtime_resource` SceneOwnershipTests | 2 | T9 | 2 RED |
| `tests/scenario/runtime_resource` EntrySingleFlightTests | 1 | T9 | 1 RED |

七类目标差距的证据映射：

1. 固定累计目录：`HotfixAccumulationDirectory.cs`、`HOTFIX_OUTPUT_FOLDER_NAME`、`HotfixOutputRoot`、`GetHotfixOutputDir`（12 个文件命中）仍存在/被引用。
2. 包内 Summary：Export 阶段仍调用 `WriteSummaryFiles` 写包目录；`PackageBuildIdentity.TryReadFromPackageDir` 仍从包目录读摘要；`PackageFileScanner` 仍过滤构建元数据。
3. 无 Summary Index：`BuildData/Summaries` 与 `LatestFullSummaryId`/`LatestSuccessfulSummaryId` 零命中；`VersionRecord` 类型、资产与重置工具处理仍存在。
4. 旧命名：`BuildPhysicalName` 仍返回 16 位纯 hash（实际 `f171d97df6505d1e`）；`BundleFileNameStyle`/`FileNameStyle` 仍被配置与 UI 引用并写入两份 `.asset`。
5. 发布越界：`PackagesFolderName=".."`、非 `Build_*` 包名、非法时间戳、含分隔符的包集合名均被接受并实际执行发布。
6. Hotfix 回退：`DecideCurrentContent` 签名无法表达本地包完整性；`InspectCurrentPackageAsync` 损坏分支只警告；同包修复仍以 `IsSamePackageRoot(TargetGUIDRoot, CurrentPackageRoot)` 跳过隔离并原地写入。
7. Scene/inflight：卸载失败后 token 被消费不可重试；非最后所有者即可物理卸载；同 Entry 的 async leader + sync follower 产生两次 Bundle acquisition，全部 handle 释放后 `UnloadCount=0`（引用计数泄漏）。

### 取舍与后续补强（透明记录）

- F04/F05 在 T0 以「决策输入契约 + FlowBase 写入根断链」锁定目标形态；完整流程级行为验证（真实 FlowBase + FakePipeline）安排在 T8 实现落地后补强，另由 T11 的 Windows Unity/Player 矩阵作为最终证据。T0 未把静态断言当作流程行为证明。
- build_cache 的 F03 陈旧断言（`BuildTaskUsesContentNameAsBundleName`）保留现状记录，修正归属 T3：命名重建时把断言改写为目标契约并红/绿验证。
- F08（Android）不在本轮范围；F11/F12/F15/F16 的门禁在 T10 批次建立；F14 文档收口在 T11。
- F09（Cloudflare 失败回滚删除旧同名包）在 T7 建立事务级门禁。

### T0 验证命令与结果

```text
dotnet build XLuaHotfix.sln --no-restore --no-incremental   exit 0（0 error）
ab_remediation 0/4 · pipeline_realignment 7/7 · pipeline_compose 4/4 · publish_diff 4/5
build_cache 6/7（既有 F03）· runtime_resource 16/19（新增 3 RED）· Hotfix state PASS
S2 PASS · S3 6/6 · serialization 3 PASS
```

### 下一步

T1：Summary / Index / 版本事务。先实现 `BuildSummaryStore` 与 `BuildData/Summaries/index.json` 契约，再删除 `VersionRecord`，提交顺序与故障回滚按计划「事务提交顺序」执行。

## T1–T10 Execution Record (2026-09-11)

按批准的分级测试策略执行：每个批次先跑受影响门禁与新门禁，全量矩阵在批次收口与 T11 终验运行。以下记录截至 T10 批次收口的实现与新鲜证据。

### T1 Summary / Index / 版本事务（已完成）
- 新增 `Assets/FYAsset/Scripts/Shared/Build/Editor/Summary/{BuildSummaryIndex,BuildVersionPlanner,BuildSummaryStore}.cs`；Summary 落 `BuildData/Summaries/{AA|AB}/{BuildId}.json`（不可变），`index.json` 可重建（作用域记录 `LatestSuccessfulSummaryId` / `LatestFullSummaryId`）。
- `BuildProjectRunner` 提交顺序改为「提升产物 → 本地启动数据 → Summary → Index」，Index 是最后一个可见提交点；失败按逆序回滚（Summary 删除 + Index 字节恢复）。Hotfix 提交前校验基准（同后端/平台/通道/同 Major）。
- `VersionRecord` 类型、资产、设置、UI 与重置工具处理全部删除，版本推进由 `BuildVersionPlanner` 按通道与作用域计算（禁止降级）。
- 证据：`tests/scenario/ab_remediation` SummaryTransactionContract 绿；`pipeline_realignment` 7/7。

### T2 独立纯包交付（已完成）
- Full / Hotfix / Standalone 均写 `Packages/Build_*`；固定累计目录族（`HotfixAccumulationDirectory`/`HotfixAccumulationPlan`/`HOTFIX_OUTPUT_FOLDER_NAME`/`HotfixOutputRoot`/`GetHotfixOutputDir`）与全部调用点删除。
- Hotfix 包 = 完整目标 Manifest + 相对作用域最近成功 Full 的变化内容（`HotfixBaselineResolver`）；包目录只含发布内容，Export 不再写包内摘要；`PackageBuildIdentity` 不再从包目录推断身份，`PublishRequest.Identity` 由发布 UI 从正式 Summary 注入。
- `BuildPackageResultsView` 以 Summary 为数据源，支持 Missing 状态；被 Hotfix 引用的 Full 拒绝删除。
- 证据：PackagePurityContract 绿；`publish_diff` 4/4。

### T3 命名重建（已完成）
- `BundleNameBuilder`：`PhysicalNameHashLength=12`、`PhysicalNameRuleVersion=2`、`BuildPhysicalName(contentName, readableName)` → `{group}_{kind}[_{readable}]_{hash12}`；场景按场景资源逐场景命名（删除 SceneBundleSuffix）；构建期大小写不敏感唯一碰撞阻断（`DuplicateBundleName`）。
- 删除 `BundleFileNameStyle`/`FileNameStyle`（配置/UI/两份 .asset 全部清理，F13 关闭）；`BundleBuildInputFingerprint.FormatVersion` 2→3（IP-66 版本 bump）。
- 证据：PhysicalNamingContract 绿；`build_cache` 命名断言按目标契约改写后全绿。

### T4 Summary 驱动的构建复用（已完成）
- 删除独立 Bundle 制品缓存树（`BundleBuildCacheStore/Document/Entry/Isolation/ReusePolicy/Session`、`ABBuildCacheContext`）与 `IBuildCacheCommitter` 链路；`{BuildOutputRoot}/_build_cache` 不再存在。
- 新增 `BuildArtifactReuseService`：复用唯一来源是「正式 Summary 内容记录 + 历史 `Build_*` 包字节」；按内容身份与输入指纹匹配，复制前后来历校验 Hash/CRC/Size，任何缺失或损坏只退化为重建。
- `CompleteBuildSummary` 增加 `SummaryContentFact`（ContentIdentity/InputFingerprint/FileName/FileHash/FileCRC/FileSize/DependencyFileNames）与 `Contents`；Export 写入 `BuildRecipeFingerprint`；依赖闭合裁剪保留并迁入 `ContentDependencyIndexResolver`。
- 证据：`build_cache` 5 组 / 59 断言全绿。

### T5 Hotfix 直接 Diff Full（已完成）
- 基准恒为作用域最近成功 Full（`HotfixBaselineResolver` + `Summary.BaseFullSummaryId`）；导出集合 = `FileDiff(Full 内容, 本次内容)` 的 Added/Modified。
- 新增 `tests/scenario/ab_remediation/test_hotfix_delivery.cs`（HotfixDeliveryContract，3 子契约）：行为断言 Full→H1 得 A、Full→H2 得 A+B、内容恢复 Full 版本后只剩 B、删除 H1 与 H2 求差无关；静态断言基准来源与不得回退固定累计目录。
- 证据：HotfixDeliveryContract 绿。

### T6 发布输入与路径安全（已完成）
- 新增 `PublishPathGuard`（安全单段名、服务器根 containment、reparse point 检查）；`PackageBuildIdentity` 严格 `Build_yyyyMMddHHmmss_x.y.z` 解析；`PublishRequest.Validate` 校验 `PackagesFolderName` 单段安全；transaction / maintenance 全部派生路径加 containment 与重解析点校验，`TryCreate` 异常不外泄。
- 证据：`publish_diff` PublishContainmentRules 绿（traversal/设备名/严格包名/哨兵不变）。

### T9 Scene 与 Entry 所有权（已完成）
- `SceneHandle.UnloadAsync` 返回结构化 `SceneUnloadResult`（Success/Error/ActiveOwners）；只有同 EntryId 的最后 owner 能物理卸载，卸载失败保留 token 可重试，成功后才结算 token。
- `HandleRegistry` 新增 `GetActiveOwnerCount`/`IsLastOwner`/`ReleaseAllForEntry`；`ABSceneLoader` Single 替换确认旧场景卸载后一次性结算该场景全部存活 token；`ABPackageBackend` 同步路径遇同 Entry inflight 返回 `RuntimeErrorCodes.LoadInProgress`（不阻塞、不二次获取）。
- 证据：`runtime_resource` 19/19（SceneOwnership 2 + EntrySingleFlight 1 转绿）。

### T10 一致性与清理（F10–F13、F15、F16 已关闭，F14 归 T11）
- F11 facade lease 比较器大小写不敏感；F12 `FYAssetPathUtility.AreSamePath` 使用平台文件系统比较（单源 `FilePathComparison`）；S2 门禁新增两条静态断言并全绿。
- F15：`StandaloneBuild` 从 `FYAssetSettings`/设置面板/测试引擎 bake-restore/注释退出；`BundleBuildInfo.OutputFileName` 注释修正；`PushPayload.ServerPackagesRoot` 与 `PackageFileNames` 死常量（`BuildSummaryJson`/`BuildSummaryText`/`FailedBuildMarker`/`IsBuildMetadata`）删除；`PublishCacheStore` 过期注释清理。
- F16：`status_snapshot.txt` 删除；`IBuildTask.cs` 行尾归一为工作区约定 CRLF（保留唯一注释修正）。两份 XML `.meta` 的纯行尾差异保留在工作树，明确排除在最终提交之外（见 T11 review disposition）。
- F10：`.gitignore` 最小例外 `!tests/scenario/**/*.csproj`，9 个门禁工程 csproj 可被跟踪。
- 证据：solution 0 error；`git diff --check` exit 0。

### 批次收口新鲜证据（2026-09-11）
- `dotnet build XLuaHotfix.sln --no-restore --no-incremental`：0 error。
- `ab_remediation` 5/6（仅 HotfixCandidateContract 4 条 = T8）；`build_cache` 5/5；`publish_diff` 4/4；`runtime_resource` 19/19；`pipeline_realignment` 7/7；`pipeline_compose` 4/4；`HotfixRuntimeStateMachine`、`S2`、`S3` 6/6、`serialization` 3 PASS。
- 进行中：T7（发布完整组装 + Cloudflare 补偿）、T8（Hotfix 回退与同包修复）由并行 worker 执行；T11（Windows Unity/Player 矩阵 + 文档收口）待 T7/T8 落地后运行。

## T7 / T8 / T11 Execution Record (2026-09-11)

### T7 发布完整组装与 Cloudflare 补偿（已完成）
- 新增 `PackageTargetAssembler`：目标内容集合 = 本地包目录 ∪ 最终 Manifest 声明而本地缺失的内容；逐内容选源为「服务器当前包只读复用 → 基准 Full」；来源不足或摘要不符即失败，判定与组装在 `CreatePlan` 阶段只读完成（副作用为零）。
- 新增 `IFullPackageBaselineSource` 接缝与编辑器自注册实现（包名 → Hotfix Summary → `BaseFullSummaryId` → `ArtifactRelativePath`）；`BuildPublisher.PushFullUpload` 与非目录目标共用同一组装器。
- `CloudflarePagesPushTarget` 修 F09：旧同名包先备份到服务根外，失败按镜像前状态放回，仅删除本次新建目录；`WranglerCommandRunner` 抽取进程接缝以便行为测试。
- 证据：`publish_diff` 6/6（新增 PublishAssemblyDecisions、CloudflarePublishDecisions 行为套件，均先红后绿）。

### T8 Windows Hotfix 回退与同包修复（已完成）
- `HotfixStateDecider.DecideCurrentContent(localPointerTrusted, localIsBuiltInIdentity, localPackageComplete)`；唯一调用点先做真实本地包检查再决策。
- `InspectCurrentPackageAsync` 损坏分支：清本地指针 → `ctx.CurrentContent = HotfixContentState.BuiltIn` → 继续远端检查。
- 删除「目标根等于当前根就跳过隔离」与直指当前热更包根的写入；新增 `RuntimePathManager.GetHotfixStagingRoot/GetHotfixBackupRoot`，同包修复在独立 staging 完成，成功后换入并删旧，失败退回 staging、旧目录放回、不写指针。
- 证据：`ab_remediation` HotfixCandidateContract 4/4 绿；仓库外 FakePipeline 流程探针 8 场景 46 断言全过（未纳入仓库）。

### T11 Windows AB 验收与文档（已完成主体，遗留见 disposition）
- Unity/Player（local 目标）：`ab build full`、`ab build hotfix`、`ab e2e standalone`、`ab e2e full` 全部 exit 0；测试引擎版本期望对齐新规则（隔离 Full 重置后首个 Full=1.0.0）。
- 地址端到端闭合（数据对齐）：项目 `AddressStyle=LongAssetPathWithoutExtension` 下运行时请求键仍为 AA 短名，已按计划内自定义 Address 机制在 `CollectorSetting.asset` 的 `AssetOverrides` 中为 Addressables 已注册的 17 个公共资源补齐 `Address:`（与 AA group 逐字一致）；不改游戏代码与 AddressStyle。此数据对齐待开发者确认。
- 文档（F14）：12 个 `docs/FYAsset/*.md` 对齐当前实现并校验链接；`fyasset-modeling.html` 判定为生成物，未手改，需按生成器重跑。
- review 处置：`requirements/review/disposition-fyasset-windows-ab-remediation-20260911.md` 逐条记录 F01–F16 状态与证据；F08（Android）与 AA 完整矩阵明确延期。

## 计划偏移审计记录（2026-09-11）

独立 reviewer 只读审计（对照计划任务表、Confirmed Decisions、删除清单与最小验收）结论：除已记录项外发现两处需修复、一处需澄清，均已处理。

- 修复：`PublishTargetPanel.RunPush` 未注入 Summary 身份（生产 Push 不可用）；新增门禁 `PublishIdentitySources`（`publish_diff` 7/7，已验证 RED→GREEN）。
- 修复：`README.md` 4 处过期事实对齐当前实现（F14 闭环）。
- 澄清：`Assets/Build/Bootstrap/BuildIndex.json` 删除由本轮 `fyasset_reset.py` 行为触发，工作树已还原并记录；`build/StandaloneWindows64/_build_cache` 磁盘残留已删除；git 索引中的上一轮 staged 重命名/删除保持原样未动。
- 复核：18 个应删符号在 `Assets/**` 0 命中；92 项删除可归属；计划延期项（F08 Android、AA 完整矩阵）未被当作通过。

审计后终验：solution 0 error；`ab_remediation` 6/6、`publish_diff` 7/7、`build_cache` 5/5、`runtime_resource` 19/19、`pipeline_realignment` 7/7、`pipeline_compose` 4/4、Hotfix state/S2 PASS、S3 6/6、serialization PASS；`git diff --check` exit 0。
