# FYAsset AB 主路线结构与注释整治计划

> 状态：已执行 / 已归档（2026-09-07）
> 批准：2026-09-06
> 范围：AB 主路线结构简化、配置归位、测试边界、构建/发布 UI 整理、全量 C# 注释治理

## 目标

以 AB 为主要维护路线，减少 FYAsset 的目录冗余、无效通用化、测试与日常工具混杂，以及低信息注释。AA 保持可编译、可构建、可发布，不删除、不迁移、不做特殊隔离，也不投入本批额外重构精力。

## 约束

- 不改变 AB/AA 构建产物布局、热更状态转换、发布协议、资源加载语义和既有测试语义。
- 一次性迁移旧 PlayMode 配置；不保留旧字段、旧类型或兼容分支。
- `AB/Runtime/Backends` 直接平铺到 `AB/Runtime`，不新增替代目录。
- 删除 `PushTargetUtility`，不新增替代 Utility、Resolver 或 Factory 类；职责回到配置对象和具体调用点。
- 删除共享 `RepositoryStatusPanel` 与无实际价值的 `IRepository*` 适配层；AB 使用专用面板，AA 保留最小发布能力。
- 本地 Server、E2E 和测试维护工具统一进入 `Tests` 目录。
- 全量优化 `Assets/FYAsset` C# 注释，规则写入 `context/conventions/csharp-comments.md`。
- 不执行真实远端发布、远端热更或可能等待管理员授权的联网测试。
- 不创建 Git commit。

## 执行任务

1. 规则与记录：新增 C# 注释规范，更新 context 索引、progress 和本计划。
2. AB 配置：将 `EPlayMode`/`PlayMode` 转入 `FYAssetABSettings`；执行一次性资产字段迁移；移除 Shared 类型和 AB 硬编码默认 Hotfix URL。
3. 目录整理：`AB/Runtime/Backends` 直接平铺；Editor 内部 `Shared` 改为 `Common`；`SettingsPanel` 归入 UI；测试和 E2E 归入统一 Tests 目录。
4. 发布目标：删除 `PushTargetUtility`；将目录/URL 推导放回 `PushTargetConfig`，本地进程细节放回 Local Server，Wrangler 细节放回 Cloudflare 实现。
5. 构建 UI：拆分 AB Diff/Delivery、Publish Target、测试维护；AA 保留最小发布入口；移除旧共享 Repository 类型和适配器。
6. 热更简化：删除 `HotfixDownloadOptions`；保留 `ClientUpdateRequiredInfo`、`HotfixFatalException`、`HotfixStateDecider` 及状态机行为；仅删除已由内部契约保证的重复检查。
7. 注释治理：覆盖 AA、AB、Shared、Compat 和 Tests；删除代码复述、历史叙述、主观评价、阶段性说明和无意义区域标签；保留职责、约束、所有权、生命周期、顺序和失败边界。
8. 验证：静态引用检查、`git diff --check`、可用的本地 Unity/编译验证和离线窄测试；联网或授权阻塞立即超时记录，不等待重试。

## 后置事项

热更状态机重构、`HotfixFatalException` API 改造、`ClientUpdateRequiredInfo` 收敛、AA 功能重构/隔离、真实远端测试和 Git 提交。

## 执行结果（2026-09-07）

1. 规则与记录：`context/conventions/csharp-comments.md` 新增并登记索引；plan/progress 已更新。
2. AB 配置：`EPlayMode` 移入 `AB/Settings`；`FYAssetABSettings.PlayMode` 落位，资产 YAML 已一次性迁移（`PlayMode: 2`，旧资产行已移除）；`AB/Settings/Editor/ABSettingsMigration.cs` 供外部克隆一次性复检；AA/AB `HotfixUrl` 代码默认改空，`HotfixFlowBase` 在未配 URL 时明确报错。
3. 结构：`AB/Runtime/Backends`（含 Models）平铺；`Editor/Shared`→`Editor/Common`；`SettingsPanel`→`Editor/UI`；`Tests/{Editor/{Build,Hotfix,E2E,Support},Runtime/E2E}` 统一目录，.meta GUID 保留，无孤儿 meta。
4. 发布：`PushTargetUtility` 删除；布局/URL 方法入 `PushTargetConfig`；python/引号归 `LocalHotfixServerController`；Wrangler 参数与 PATH 查找归 `CloudflarePagesPushTarget`（internal 供测试验证）；创建逻辑收拢为 `CompatPushTargetFactory.CreateFull` 的 switch，面板仅 LocalDirectory。
5. UI：`RepositoryStatusPanel`（1733 行）与全部 `IRepository*` 契约删除；新 `ABBuildDiffPanel`（Changes/Delivery）、`PublishTargetPanel`（AA/AB 共用，Apply URL 由各后端窗口注入）、`ABTestMaintenancePanel`（本地服务器/重置/清 channel）；AA 窗口保留最小发布 + Hotfix Groups 恢复面板。
6. 热更：`HotfixDownloadOptions` 删除，五个调用点改为直接传超时/重试参数，钳制收敛到 `NetworkDownloader`；`ClientUpdateRequiredInfo`、`HotfixFatalException`、`HotfixStateDecider` 保持不动；HotfixFlowBase 内部审查未找到可证明冗余的检查（现存均为序列化/网络/文件边界检查），状态机核心留待后续审查。
7. 注释：4 个并行代理完成 `Assets/FYAsset` 约 190 个 .cs 全量治理（AA/AB/Shared/Compat/Tests），统计见代理报告；失实、叙事、主观、复述注释已删改。

## 验证（2026-09-07）

- `dotnet build XLuaHotfix.sln --no-restore`：0 errors；无新增 C# 警告（仅既有 MSB3277 依赖链汇总）。编译在中途节点与完成后各通过一次。
- 删除对象（PushTargetUtility、HotfixDownloadOptions、RepositoryStatusPanel、IRepository*、Shared EPlayMode、Backends 目录）仓内无引用；已删目录/文件复查不存在；无孤儿 .meta。
- `git diff --check`：仅行尾自动转换提示，无冲突标记。
- 资产 YAML：`FYAssetABSettings.asset` 含 `PlayMode: 2`，`FYAssetSettings.asset` 旧行已移除。
- 后续编辑器统一、注释语言纠偏、序列化边界和 HTML 建模由 `plan-fyasset-editor-tools-modeling-20260907.md` 接续；本计划不再作为活动计划。

## 验收

- 删除对象和旧路径无仓内引用。
- AB Runtime 文件直接位于 `AB/Runtime`。
- AB/AA 配置可编译，旧 PlayMode 值完成一次迁移。
- AB 构建窗口职责分离，AA 最小发布能力可达。
- 注释规范文件与索引可达，FYAsset C# 注释完成全量治理。
- 本地验证有新鲜命令输出；阻塞项有明确记录。
