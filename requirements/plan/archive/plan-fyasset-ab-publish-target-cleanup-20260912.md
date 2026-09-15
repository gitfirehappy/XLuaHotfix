# FYAsset AB 发布目标与工具链精简计划

> **Status**: Implementation and cleanup complete; Unity Editor/Player boundary remains pending
> **Type**: Follow-up plan after `plan-fyasset-windows-ab-remediation-20260911.md`
> **Source**: Developer-approved correction of the previous implementation scope
> **Execution gate**: Developer approval received; current worktree changes are retained and this follow-up is now authorized.

## 1. Purpose

收敛 FYAsset 当前 AB 发布与运行时配置的单一所有权，清理已确认的冗余封装和过时文档，整理测试宿主边界，并把当前仓库资产预转换到新配置格式。目标是让 Publish Target 成为 AB 热更地址的唯一来源，同时保持已发布的 Summary、PackageIndex、Manifest、热更包和客户端本地内容可读。

## 2. Current Baseline

- 保留当前工作区已有的 AB Target、Summary Source、Address 和测试重命名改动；本轮不回退。
- 上一版计划错误地把工具集中和注释清理排除在范围外。本次重新打开计划，新增通用文件能力整合、模型内化、目录归属和源码注释清理任务。
- 当前 `FileHelper.cs` 已存在目录大小能力，但摘要、比较和发布扫描仍分散在多个文件；本轮要把通用能力收进唯一 `FileHelper` 类，调用方同步改用新 API。

## 3. Scope And Non-Goals

### In scope

- 只改 AB 的 Publish、Target、AB runtime URL 解析和相关编辑器配置。
- `PushTargets` 数据模型预转换为 `TargetId + Name + Type + Path + PublicBaseUrl`，并新增 `CurrentABTargetId`。
- Publish Source 严格由有效 Summary 驱动，保留无效项但禁用并显示原因。
- 规则编辑统一留在 Assets Collection 面板，保留现有 typed lists 和匹配语义。
- 删除明确的死包装，压缩运行时类型注释和无效成员，但不强行合并职责明确的模型。
- 按行为和宿主整理测试，保留 Unity 发布集成自检，并正式收录热更流程事务场景测试。
- 清理当前有效手写 C# 源码注释；未获单独批准的 HTML 文件移动不在本轮执行。

### Out of scope

- AA 的 Target、URL、Publish 面板和运行时 URL 改造。本轮不增加 `CurrentAATargetId`，不建立双后端通用映射，也不让 Publish 面板耦合 AA。
- 已发布运行时数据格式迁移。Build Summary、PackageIndex、Manifest、热更包和客户端本地内容继续按现有格式读取。
- 完整 `.gitignore` 规则引擎、规则字段重命名和 Publish 面板中的第二套规则编辑入口。
- AA 的 Target、URL 和运行时 URL 归属改造；本轮只因通用文件 Helper 整合修改 AA/AB 的构建调用点。
- 已发布运行时数据格式迁移。
- 历史归档计划、历史 review 和生成代码重写。

## 4. Confirmed Decisions

### 4.1 URL and Target ownership

1. AB `HotfixUrl` 彻底归 Publish Target 所有，不再由 ABConfig 编辑。
2. Publish Target 选择按后端持久化；本轮只实现 `CurrentABTargetId`。
3. Target 选择保存稳定 ID，不复制 URL；运行时通过 Target 的 `PublicBaseUrl` 解析地址。
4. Target 无法解析、列表为空、ID 不存在或 URL 非法时严格阻断 Publish 和 AB runtime，不回退到第一个 Target，也不回退旧 URL。
5. Target URL 只保存一个 `PublicBaseUrl`，统一解析为 `PublicBaseUrl + /AB/`；不拆成 AA/AB 两份 URL。
6. `PublicBaseUrl` 必须是绝对 HTTP/HTTPS URL；不接受查询、片段和本地路径；保存时规范化末尾斜杠。
7. Target 使用 `TargetId` 作为稳定身份，`Name` 作为显示名称；Name 必须非空且唯一，允许重命名，TargetId 不变。
8. 现有仓库配置先预转换：为 `local`、`cloudflare` 生成一次 GUID TargetId，原旧 Id 转为 Name，按旧 AB URL 唯一对应 `cloudflare` 写入 `CurrentABTargetId`。新逻辑不支持旧格式，不实现运行时迁移。
9. 无效测试占位 Target `target3` 删除；最后一个 Target 也允许删除，删除后当前选择为空并严格阻断。
10. 删除 Target 只作用于本地配置，不删除远端内容；当前 Target 必须先切换后才能删除。
11. 删除 `Apply URL` 按钮；选择 Target 后立即持久化当前 AB Target。
12. Publish 面板保持 AB 独立，不增加 AA/AB 后端选择。

### 4.2 Publish and editor UI

13. Publish Source 采用严格 Summary 模式：无 Summary、Summary 损坏、产物缺失或内容校验不一致的来源不能 Push。
14. 无效 Source 保留在列表中，显示失效原因并禁用，不直接静默过滤。
15. Source 候选只从 Summary 声明的产物生成，不再把裸 `Build_*` 目录扫描作为身份来源。
16. 删除 Publish 面板重复的 `Backend: AB` 行，保留标题中的 AB 上下文。
17. 删除静态 Idle 徽标/占位状态，保留操作成功、失败和错误反馈。
18. Push Target 删除操作保留并修正 UI 行构造；当前 Target 不可直接删除，最后一个 Target 可在先清空选择的条件下删除。

### 4.3 Collection rules and configuration migration

19. 规则名单只在 Assets Collection 面板编辑：`IgnorePatterns`、`RawFileRules`、`ForceSharePatterns`、`NoSharePatterns`。
20. 保留现有 typed lists 和匹配语义；统一新增、编辑、删除、去重、空项和首尾空白校验，不引入完整 `.gitignore` 语法。
21. 保留 `CollectorSetting.asset` 中 17 个公共资源的显式短 Address，并保持对应 AssetGUID、Labels 和其他 Override 数据不变；另保留独立 `LuaScriptsIndex` Address。执行期曾按原决定清空公共 Address，但 `AddressStyle=LongAssetPathWithoutExtension` 使其自动解析为长路径，导致 AA/AB 标签地址契约失败；开发者于 2026-09-12 明确短 Address 未废弃并批准恢复。
22. `ABSettingsMigration` 不再保留；当前仓库资产视为最低支持版本，不提供旧 `PlayMode` 自动迁移。
23. 预转换只破坏编辑器配置资产旧格式；不破坏已发布运行时数据。

### 4.4 Type and helper cleanup

24. 通用文件能力只保留在 `Assets/Tools/Scripts/FileHelper.cs` 的 `FileHelper` 类中。`FileDigest`、`FileDiff` 不再是独立文件或顶层类型，改为 `FileHelper` 内部的数据和比较能力；调用方使用 `FileHelper` API。
25. `FileHelper` 提供文件/目录枚举、目录大小、文件摘要、摘要索引、内容比较和差异结果；发布模块只保留包根相对路径、Manifest 语义、来源组装和事务边界。
26. `PackageFileScanner` 如仍需要保留，只能作为发布包路径适配层，委托通用扫描和摘要计算给 `FileHelper`；不得重复实现文件枚举、摘要或排序。若调用方可直接表达包根相对路径，则删除该适配层。
27. 删除 `HotfixPackageSizeGuard`。AA/AB 构建任务直接使用 `FileHelper` 的大小检查结果，并保留各自的构建错误码、日志和 Editor/BatchMode 反馈。
28. `RuntimePathManager`、`HotfixContentState`、`HotfixVersionInfo`、`ClientUpdateRequiredInfo` 按实际调用链保留或收敛；不以文件名为依据删除，不保留重复字段和冗长说明。
29. 当前有效手写 C# 源码中的计划编号、历史任务、阶段编号和执行记录注释全部删除；复杂事务仅保留必要的所有权、顺序、失败补偿和路径安全说明。生成代码、历史计划和历史 review 不改。

### 4.5 Tests, docs and planning

28. 测试按行为和宿主整理为纯 .NET、Unity Editor 和 Runtime/E2E；只删除经过核对的重复、失效或无独立断言测试。
29. `HotfixPublishSelfCheck` 保留为 Unity Editor 发布集成自检，补充统一执行入口、命令和覆盖边界文档。
30. 将已验证的热更流程事务场景测试整理为正式 `tests/scenario/hotfix_flow` 项目，覆盖 8 个失败恢复与事务场景、55 个断言；测试名称按职责命名，不使用历史计划任务编号。
31. `docs/FYAsset/fyasset-modeling.html` 暂不移动；移动需单独获得开发者批准，不把它作为当前架构依据。
32. 只归档明确全文过时的 draft；仍包含有效延期决策或独立主题的 draft 保留并标注，不批量删除。
33. 新计划成为本轮 AB 发布目标与工具链精简的执行依据；上一轮 Windows AB remediation 计划保留其历史实施和审计记录，不改写已完成证据。
34. 设计阶段不授权实施；本计划已于 2026-09-12 收到开发者执行批准。当前实现、Helper 整合、模型审查、注释清理和纯 .NET 验证已完成；Unity Editor/Player、真实 AB 构建和远端发布仍是明确未执行边界。

## 5. Execution Tasks

### P0. Baseline and pre-conversion

- 展示并确认当前工作区提交范围，处理已有 staged/unstaged/untracked 状态。
- 记录当前计划与 `progress.txt` 的状态关系；不把上一轮混合改动误归入本计划。
- 预转换 `FYAssetSettings.asset`：删除 `target3`（已完成）、为有效 Target 写入 GUID TargetId 和 Name、写入 `CurrentABTargetId`。
- 预转换完成后再删除 `FYAssetABSettings.asset` 的 `HotfixUrl` 字段；不改 AA settings。

### P1. AB Target model and runtime resolver

- 修改 `PushTargetConfig`、`FYAssetSettings` 和对应序列化资产，落实 TargetId/Name/CurrentABTargetId。
- 增加集中式 AB Target 解析和 URL 校验，确保编辑器 Push 与 AB runtime 使用同一解析结果。
- 修改 `ABHotfixManager`/共享流程注入点，移除对 `FYAssetABSettings.HotfixUrl` 的读取；解析失败返回可观察的配置错误。
- 删除 `ABSettingsMigration` 及其 `.meta`，确认旧 PlayMode 自动迁移不再编译或执行。

### P2. Publish panel and Summary source

- 重构 `PublishTargetPanel` 的 Target 选择、立即保存、删除保护、最后 Target 清空、错误反馈和 URL 预览。
- 移除 Apply URL 和重复 Backend 行；移除静态 Idle 徽标但保留反馈。
- 以正式 Summary 枚举 Source，验证 ArtifactRelativePath、Summary 身份和包内容；无效项保留且禁用。
- 修复 Target 行控件重复挂载问题，并补面板行为的最小静态/编辑器门禁。

### P3. Rules, helper consolidation and model cleanup

- 在 `AssetsCollectionPanel` 统一规则名单的清理和保存行为，不在 Publish 增加入口。
- 核对并保留 17 个公共资源 Override 的显式短 Address，并保留独立 `LuaScriptsIndex` Address；所有相关 GUID、Labels 和其他字段未改。运行标签地址对等门禁，确认没有自动长路径替换或 Address 冲突。
- 将文件枚举、目录大小、摘要创建、摘要索引和差异比较集中到唯一 `FileHelper` 类；删除独立 `FileDigest.cs`、`FileDiff.cs`，并同步所有生产代码、场景工程和测试。
- 将 `PackageFileScanner` 改为薄的发布路径适配，或在确认无剩余包语义后删除；不得重复通用文件能力。
- 删除 `HotfixPackageSizeGuard`，将大小判断移到 `FileHelper`，由 AA/AB 任务保留后端特定反馈。
- 审查 `RuntimePathManager`、`HotfixContentState`、`HotfixVersionInfo`、`ClientUpdateRequiredInfo` 的实际调用链，删除重复封装或字段，并保留必要数据边界。
- 清理当前有效手写 C# 源码的冗长注释、计划编号和历史任务字眼；复杂事务保留精简边界注释。

### P4. Tests and docs

- 先建立纯 .NET / Unity Editor / Runtime-E2E 分类清单，再移动文件；每次移动核对 `.asmdef`、`.csproj`、菜单入口、GUID 和命令路径。
- 保留并接入 `HotfixPublishSelfCheck` 的统一执行入口，修正其 fixture 契约并记录只使用临时目录/本地 Target 的边界。
- 把热更流程事务场景测试整理进 `tests/scenario/hotfix_flow`，与现有测试项目的宿主、临时目录和退出码约定对齐。
- `fyasset-modeling.html` 暂不移动；文件移动需单独批准。只对明确全文过时的 planning draft 做逐项归档并更新索引。

### P5. Verification and review

- 运行配置序列化、Target ID/Name 唯一性、URL 校验、当前 Target 删除保护和严格阻断测试。
- 运行 Summary Source 有效/无效状态、Publish 面板行为、规则重扫和发布事务回归测试。
- 运行纯 .NET 全矩阵、Unity Editor 编译/自检、热更流程事务场景测试（8 场景、55 断言）和必要的 AB 本地 Build/Hotfix/E2E。
- 检查活动文档链接、`.meta`/GUID、`git diff --check`，并核对 AA 未被本轮改动。
- 形成 review，逐项对照本计划的 34 条决策和执行证据；未验证项不得标记完成。

## 6. Acceptance Criteria

1. 当前仓库配置只包含新 Target 格式；`CurrentABTargetId` 唯一指向有效 Target，Target 名称非空且唯一。
2. AB Config 不再编辑或读取 HotfixUrl；AB runtime 和 Publish 使用同一个 Target URL 解析器；AA 行为不变。
3. 无有效当前 Target、非法 URL、缺失/损坏 Summary 或产物不一致时，Publish/AB runtime 均明确阻断且不静默回退。
4. Publish Source 只来自有效 Summary；无效项可见、禁用并带原因；无 Apply URL、重复 Backend 行和静态 Idle 占位。
5. 17 个公共资源继续使用显式短 Address，另有独立 `LuaScriptsIndex` Address；Override GUID/Labels 保持不变。AA/AB 标签地址对等门禁通过，无自动长路径替换或未解释的 Address 冲突。
6. `ABSettingsMigration` 和明确死包装被移除；保留类型的职责和已发布运行时数据兼容性未被破坏。
7. 测试分类、HotfixPublishSelfCheck、热更流程事务场景测试和文档归档均有路径级证据与可重复命令。
8. 所有验证输出、review 和进度记录与实际工作区一致；未批准的实现不提前发生。

## 7. Explicit Risks

- 本计划是破坏性编辑器配置升级；执行前必须先把当前仓库旧资产预转换并保存，不能依赖运行时迁移。
- 当前工作区混合改动规模大，若不先建立提交边界，后续 diff 无法可靠区分上一轮成果与本计划改动。
- 17 个显式短 Address 是 AA/AB 公共业务键兼容层；在 `AddressStyle=LongAssetPathWithoutExtension` 下清空会改成长路径并破坏运行时短键查询，因此不得作为冗余字段删除。
- Source 严格 Summary 模式会拒绝没有正式 Summary 的历史 Build_* 目录；这符合已确认规则，但需要在面板提示中明确原因。
- AA 暂缓意味着 AA 仍使用自己的 HotfixUrl/Target 语义；任何共享解析器改动都必须证明不改变 AA。
