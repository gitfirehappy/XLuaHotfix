# FYAsset AB 发布目标与工具链精简计划

> **Status**: Design approved / execution not approved 2026-09-12
> **Type**: Follow-up plan after `plan-fyasset-windows-ab-remediation-20260911.md`
> **Source**: 2026-09-12 grill decisions 1–37 and current-tree review
> **Execution gate**: This plan records approved design decisions, but does not authorize code, asset, test, document, move, or delete changes. Execution requires a separate explicit approval.

## 1. Purpose

收敛 FYAsset 当前 AB 发布与运行时配置的单一所有权，清理已确认的冗余封装和过时文档，整理测试宿主边界，并把当前仓库资产预转换到新配置格式。目标是让 Publish Target 成为 AB 热更地址的唯一来源，同时保持已发布的 Summary、PackageIndex、Manifest、热更包和客户端本地内容可读。

## 2. Current Baseline

- 当前工作区不是干净基线：约 251 个已跟踪文件有差异，约 196 个未跟踪文件，索引中还有 13 条上一轮遗留 staged 重命名/删除。
- 这些改动来自上一轮 Windows AB remediation，不能回退、覆盖或混入本计划的业务修改，必须先由开发者决定提交范围。
- 当前 `FYAssetSettings.asset` 的空占位 Target `target3` 已按开发者明确授权删除；`backlog/` 中的离线备份目录也已删除。当前有效 Target 为 `local` 和 `cloudflare`。
- 当前 `CollectorSetting.asset` 的 17 条自定义 Address 尚未按本计划重扫处理；已确认的做法是清空 Address 字段而保留 AssetGUID、Labels 和其他 Override 数据。
- 当前 AB 仍从 `FYAssetABSettings.HotfixUrl` 读取 URL，Publish 仍存在旧的 Apply URL/目录扫描等路径；这些是本计划的待实施内容。

## 3. Scope And Non-Goals

### In scope

- 只改 AB 的 Publish、Target、AB runtime URL 解析和相关编辑器配置。
- `PushTargets` 数据模型预转换为 `TargetId + Name + Type + Path + PublicBaseUrl`，并新增 `CurrentABTargetId`。
- Publish Source 严格由有效 Summary 驱动，保留无效项但禁用并显示原因。
- 规则编辑统一留在 Assets Collection 面板，保留现有 typed lists 和匹配语义。
- 删除明确的死包装，压缩运行时类型注释和无效成员，但不强行合并职责明确的模型。
- 按行为和宿主整理测试，保留 Unity 发布集成自检并正式收录 T8 流程探针。
- 归档确认过时的 `fyasset-modeling.html`；更新相关计划索引和进度记录。

### Out of scope

- AA 的 Target、URL、Publish 面板和运行时 URL 改造。本轮不增加 `CurrentAATargetId`，不建立双后端通用映射，也不让 Publish 面板耦合 AA。
- 已发布运行时数据格式迁移。Build Summary、PackageIndex、Manifest、热更包和客户端本地内容继续按现有格式读取。
- 完整 `.gitignore` 规则引擎、规则字段重命名和 Publish 面板中的第二套规则编辑入口。
- 把 `FileDigest`、`FileDiff`、`PackageFileScanner` 或 `HotfixPackageSizeGuard` 整体并入 `FileHelper`。
- 无证据的生产代码目录重排、全量测试删除、历史 review 删除和旧计划历史改写。

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
21. 只清除 `CollectorSetting.asset` 中已确认的 17 个自定义 Address 字段，保留每个 Override 的 AssetGUID、Labels 和其他数据，然后按现有规则重扫。
22. `ABSettingsMigration` 不再保留；当前仓库资产视为最低支持版本，不提供旧 `PlayMode` 自动迁移。
23. 预转换只破坏编辑器配置资产旧格式；不破坏已发布运行时数据。

### 4.4 Type and helper cleanup

24. 保留职责明确的 `HotfixContentState`、`HotfixVersionInfo`、`ClientUpdateRequiredInfo`、`RuntimePathManager`、`FileDigest`、`FileDiff`、`HotfixPackageSizeGuard` 和 `PackageFileScanner`。
25. 删除死成员、纯转发和冗长注释；不把版本数据、状态、通知和文件 I/O 强行合并。
26. 删除 `PackageFileScanner.IndexByName()` 这个无调用方的转发包装，调用方直接使用 `FileDiff.IndexByName()`。
27. `FileHelper` 只承担通用文件 I/O/大小能力；`FileDiff` 保持纯摘要集合比较；`PackageFileScanner` 保留包根相对路径、摘要生成、排序和不可读文件失败语义；`HotfixPackageSizeGuard` 保留编辑器/批处理阻断策略。

### 4.5 Tests, docs and planning

28. 测试按行为和宿主整理为纯 .NET、Unity Editor 和 Runtime/E2E；只删除经过核对的重复、失效或无独立断言测试。
29. `HotfixPublishSelfCheck` 保留为 Unity Editor 发布集成自检，补充统一执行入口、命令和覆盖边界文档。
30. 将仓库外已验证的 T8 流程探针整理为正式 `tests/scenario` 项目，保留 8 个场景和 46 个断言。
31. 将过期的 `docs/FYAsset/fyasset-modeling.html` 移入文档归档区，不在本轮重写，不把它作为当前架构依据。
32. 只归档明确全文过时的 draft；仍包含有效延期决策或独立主题的 draft 保留并标注，不批量删除。
33. 新计划成为本轮 AB 发布目标与工具链精简的执行依据；上一轮 Windows AB remediation 计划保留其历史实施和审计记录，不改写已完成证据。
34. 本计划本身不授权实施；执行前需要开发者明确批准。

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

### P3. Rules, scanner and model cleanup

- 在 `AssetsCollectionPanel` 统一规则名单的清理和保存行为，不在 Publish 增加入口。
- 清空 17 个 Override Address，保留 GUID/Labels/其他字段，执行重扫并记录 Address 数量、冲突和排除结果。
- 删除 `PackageFileScanner.IndexByName()`，压缩 `RuntimePathManager` 等目标类型的重复注释，移除确认无调用方的死成员。
- 保持 FileHelper/FileDiff/PackageFileScanner/HotfixPackageSizeGuard 的职责边界并补对应回归断言。

### P4. Tests and docs

- 先建立纯 .NET / Unity Editor / Runtime-E2E 分类清单，再移动文件；每次移动核对 `.asmdef`、`.csproj`、菜单入口、GUID 和命令路径。
- 保留并接入 `HotfixPublishSelfCheck` 的统一执行入口，修正其 fixture 契约并记录只使用临时目录/本地 Target 的边界。
- 把 T8 探针整理进 `tests/scenario`，与现有测试项目的宿主、临时目录和退出码约定对齐。
- 归档 `fyasset-modeling.html`；只对明确过时的 planning draft 做逐项归档并更新索引。

### P5. Verification and review

- 运行配置序列化、Target ID/Name 唯一性、URL 校验、当前 Target 删除保护和严格阻断测试。
- 运行 Summary Source 有效/无效状态、Publish 面板行为、规则重扫和发布事务回归测试。
- 运行纯 .NET 全矩阵、Unity Editor 编译/自检、T8 8 场景 46 断言和必要的 AB 本地 Build/Hotfix/E2E。
- 检查活动文档链接、`.meta`/GUID、`git diff --check`，并核对 AA 未被本轮改动。
- 形成 review，逐项对照本计划的 34 条决策和执行证据；未验证项不得标记完成。

## 6. Acceptance Criteria

1. 当前仓库配置只包含新 Target 格式；`CurrentABTargetId` 唯一指向有效 Target，Target 名称非空且唯一。
2. AB Config 不再编辑或读取 HotfixUrl；AB runtime 和 Publish 使用同一个 Target URL 解析器；AA 行为不变。
3. 无有效当前 Target、非法 URL、缺失/损坏 Summary 或产物不一致时，Publish/AB runtime 均明确阻断且不静默回退。
4. Publish Source 只来自有效 Summary；无效项可见、禁用并带原因；无 Apply URL、重复 Backend 行和静态 Idle 占位。
5. 17 个自定义 Address 被清空但 Override GUID/Labels 保留；重扫结果无未解释的 Address 冲突或范围回归。
6. `ABSettingsMigration` 和明确死包装被移除；保留类型的职责和已发布运行时数据兼容性未被破坏。
7. 测试分类、HotfixPublishSelfCheck、T8 探针和文档归档均有路径级证据与可重复命令。
8. 所有验证输出、review 和进度记录与实际工作区一致；未批准的实现不提前发生。

## 7. Explicit Risks

- 本计划是破坏性编辑器配置升级；执行前必须先把当前仓库旧资产预转换并保存，不能依赖运行时迁移。
- 当前工作区混合改动规模大，若不先建立提交边界，后续 diff 无法可靠区分上一轮成果与本计划改动。
- 删除 17 个 Address 会改变 AB 重扫结果；必须保留 Override 数据并记录重扫前后差异。
- Source 严格 Summary 模式会拒绝没有正式 Summary 的历史 Build_* 目录；这符合已确认规则，但需要在面板提示中明确原因。
- AA 暂缓意味着 AA 仍使用自己的 HotfixUrl/Target 语义；任何共享解析器改动都必须证明不改变 AA。
