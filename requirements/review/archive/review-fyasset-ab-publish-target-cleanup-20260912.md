# FYAsset AB 发布目标与热更流程重命名执行审查

> **Date**: 2026-09-12
> **Reviewer**: Codex
> **Scope**: AB Publish Target 所有权、Summary 发布源、采集 Address 兼容性、FileHelper 能力集中、热更模型定位、测试宿主和源码注释清理
> **Method**: 计划逐项核对、源码与资产静态审计、纯 .NET 场景矩阵、solution 编译、工作区产物检查
> **Status**: Implementation complete, awaiting developer review

## 结论

本轮批准范围内的实现已完成，当前工作区保留源码和文档改动，未提交，供开发者审查。AA 的 Target、URL 和运行时配置未按本轮目标改造；AA 仍保留独立 `HotfixUrl` 入口。

`fyasset-modeling.html` 未移动，因为本轮没有单独的文件移动批准。Unity Editor/Player 本轮未重新运行；上一轮已有的 Windows 本地 Build/Player 证据仍记录在历史 review 中，本报告不将其当作本轮新鲜证据。

## 已完成改动

### AB Target 与 URL

- `PushTargetConfig` 使用 `TargetId + Name + Type + Path + PublicBaseUrl`。
- `FYAssetSettings` 增加 `CurrentABTargetId`，当前配置包含 `local` 与 `cloudflare` 两个 GUID Target。
- 新增 `ABHotfixTargetResolver`，AB runtime 通过 `CurrentABTargetId` 严格解析目标，不回退到列表首项或旧 URL。
- URL 只接受绝对 HTTP/HTTPS 地址，拒绝本地路径、查询字符串和片段，并规范化末尾斜杠。
- AB Publish 面板按 Name 显示、按 TargetId 传递和保存；选择目标立即持久化；当前目标不能直接删除。
- AB Config 移除 `HotfixUrl`；AA 保持原有独立 URL 语义。
- 删除 `ABSettingsMigration` 及其 meta 文件。

### Publish Source 与工具精简

- 新增 `PublishSourceCatalog`，发布源由正式 Summary 枚举。
- Summary 失效、制品缺失、清单缺失或内容摘要不一致时保留为不可发布候选并显示原因。
- 发布面板不再扫描裸 `Build_*` 目录推断构建身份。
- `PackageFileScanner` 只保留包根相对路径适配，扫描和摘要由 `FileHelper` 完成。
- `FileHelper` 内化 `FileDigest` readonly struct；差异比较通过 `ComputeDiff(..., out added, out modified, out unchanged, out removed)` 返回集合，不再保留顶层 `FileDiff` 或差异结果对象。
- 删除 `HotfixPackageSizeGuard`；AA/AB 构建任务直接使用 `FileHelper.IsWithinSizeLimit`，保留各自错误码、日志和 Editor/BatchMode 反馈。
- 移除 `WriteAllTextAtomic` 的无效 `Encoding` 兼容重载及唯一调用点的多余参数。
- AB 页面不显示 `Apply URL`；共享面板的旧 Apply URL 分支只由 AA 构造器启用，AA 行为未改。

### Helper、模型与注释清理

- `RuntimePathManager` 继续作为运行时目录和 `ActivePackageRoot` 的唯一持有者；`CurrentGUIDRoot` 与 `ActivePackageRoot` 分别表示本地包身份路径和当前读取根，不能合并。
- `HotfixContentState` 保留为内容归属枚举；`HotfixVersionInfo` 保留为 AA/AB 共享的远端版本与下载内容载体；`ClientUpdateRequiredInfo` 保留为客户端更新事件的不可变载荷。四类类型均有实际调用点，没有删除纯转发字段。
- 精简 HotfixFlow、RuntimePath、Bundle/Scene/Handle、AA/AB Pipeline、构建升级和 Publish 事务注释，只保留职责、所有权、顺序、失败补偿和路径安全边界。
- 当前有效框架与场景 C# 注释中的计划编号、历史任务和编号阶段已清除；生成代码、历史计划和历史 review 未改。

### Address 与规则

- 经实测确认 `AddressStyle: 1` 是 `LongAssetPathWithoutExtension`。清空显式 Address 会把公共短键变成长路径，导致 AA/AB 地址对等和运行时短键查询失败。
- 按开发者批准恢复 17 条公共业务短 Address：`TalkTest1`、`TalkTest2`、`DialoguePanel`、`Player_LuaBridge`、`Player_AnimeBridge`、`Player_SOBridge`、`UIResourceConfig`、`PlayerControllerSO`、`Core`、`Dialogue`、`LuaText`、`Player`、`StateMachine`、`FYAssetPipelineAsync`、`FYAssetPipelineSync`、`FYAssetPipelineLua`、`FYAssetPipelineRaw`。
- 独立 `LuaScriptsIndex` Address 继续保留；当前显式 Address 共 18 条，Override GUID 数量为 198，未改变 GUID、Labels、顺序和其他 Override 数据。
- 规则编辑仍集中在 `AssetsCollectionPanel`；现有校验器覆盖空项、首尾空白、重复 GUID 和标签约束，未新增 Publish 侧规则入口。

### 测试命名与文档

- 原临时探针整理为 `tests/scenario/hotfix_flow/HotfixFlowScenarioTests.csproj`。
- 入口类改为 `HotfixFlowScenarioTests`，源码、程序集、临时目录和输出均不再使用历史 `T8` 标识。
- 该场景客观验证：损坏包回退、同包 staging 修复、前向切换回滚、指针提交时机、失败诊断物保留，以及 Check/Prepare/Apply 重试。
- 自动化测试管线、README、活动计划、计划索引和 progress 已同步为 8 个场景、55 个断言。
- 历史计划和历史 review 中的 `T8` 作为历史任务编号保留，不做全局替换。

## 新鲜验证

- `dotnet build XLuaHotfix.sln --no-restore --no-incremental`：exit 0，0 个编译错误；存在既有 Unity/MCP `MSB3277` 引用版本警告。
- 纯 .NET 场景矩阵：11 个项目均 exit 0；本轮标准 `dotnet run` 已恢复缺失资产文件。
- `publish_diff`：8/8。
- `s3_resource_boundary`：6/6，包含 Address 对等验证。
- `pipeline_compose`：29/29，4/4 组。
- `build_cache`：59/59，5/5 组。
- `ab_remediation`：6/6。
- `runtime_resource`：全部场景通过。
- `pipeline_realignment`：7/7。
- `serialization`、`S2RuntimeBoundaryTests`、`HotfixRuntimeStateMachineTests`：exit 0。
- `hotfix_flow`：8 个场景、55 个断言，exit 0，最终输出 `Hotfix flow scenarios: ALL PASS`。
- `git diff --check -- . ':!**/*.meta'`：exit 0；Unity `.meta` 的空字段尾随空格未改写。
- 框架/场景 C# 静态审计：生产源码无编号计划注释、无 `FileDiff` 文案、无 `HotfixPackageSizeGuard` 引用、无带 `Encoding` 参数的 `WriteAllTextAtomic` 调用；测试仅保留负向契约对旧类型名称的显式缺失断言。
- 已清理所有 `tests/scenario/**/bin` 与 `tests/scenario/**/obj` 生成目录。
- 当前没有新建的凭据、本地配置或典型构建产物进入工作区；工作区仍保留本轮源码、资产、测试和文档改动。

## 未完成或明确边界

- 未移动 `docs/FYAsset/fyasset-modeling.html`，等待单独的文件移动批准。
- 未重新运行 Unity Editor 编译、Unity 菜单自检、AB 本地 Build/Hotfix、Player E2E 或真实远端发布；不访问 Cloudflare。
- `PackageFileScanner` 仍保留为发布路径适配层，因为发布调用方需要把包目录转换为包根相对路径；它不重复扫描、摘要或排序。
- 工作区不包含本轮提交；保留未提交状态供开发者逐文件审查。

## 审查建议

重点审查以下文件和边界：

- `Assets/FYAsset/Scripts/Shared/Build/Publish/PushTargetConfig.cs`
- `Assets/FYAsset/Scripts/AB/Hotfix/ABHotfixTargetResolver.cs`
- `Assets/FYAsset/Scripts/Shared/Build/Editor/Summary/PublishSourceCatalog.cs`
- `Assets/FYAsset/Scripts/Shared/Build/Editor/UI/PublishTargetPanel.cs`
- `Assets/Resources/FYAssetSettings.asset`
- `Assets/FYAsset/CollectorData/CollectorSetting.asset`
- `tests/scenario/hotfix_flow/HotfixFlowScenarioTests.cs`
- `requirements/plan/plan-fyasset-ab-publish-target-cleanup-20260912.md`

本报告不包含 Git commit。当前工作区保持未提交状态，便于开发者逐文件审查和决定后续签收或继续修正。
