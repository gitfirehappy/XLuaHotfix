# Disposition: fyasset resource pipeline realignment findings (F01–F16)

> **Date**: 2026-09-11
> **Owner**: `../plan/plan-fyasset-windows-ab-remediation-20260911.md` execution (T0–T11)
> **Scope**: 逐条处置 `review-fyasset-resource-pipeline-realignment-20260911.md` 的 F01–F16
> **Method**: 新鲜门禁输出 + Windows Unity/Player 实测 + 源码复核；证据命令与结论见下表与文末矩阵

## 结论总览

| Finding | 级别 | 状态 | 证据 |
|---|---|---|---|
| F01 发布路径段可逃出服务器包根 | P0 | 已关闭 | `PublishPathGuard`（安全单段名 / containment / reparse 检查）接入 `PublishRequest`/transaction/maintenance；`publish_diff` PublishContainmentRules 绿（含 `PackagesFolderName=".."`、`nested/dir`、`Build_2026_1.0.0`、sentinel 不变、reparse 拒绝） |
| F02 短物理名未端到端接受；Standalone 仍读长逻辑名 | P1 | 已关闭 | T3 命名重建（`{group}_{kind}[_{readable}]_{hash12}`、逐场景名、碰撞阻断）；Windows `ab e2e standalone` exit 0（上一轮保留的 Player 启动失败已消失），`ab e2e full` exit 0 |
| F03 缓存门禁在应急命名后回归 | P1 | 已关闭 | `build_cache` 场景按 Summary 复用契约重写，5/5 组、59 断言绿；旧 `BuildTaskUsesContentNameAsBundleName` 陈旧断言已改写 |
| F04 本地包损坏不回退内置包 | P1 | 已关闭 | T8：`DecideCurrentContent(pointerTrusted, isBuiltInIdentity, packageComplete)`；`InspectCurrentPackageAsync` 损坏分支清指针并 `ctx.CurrentContent = BuiltIn`；ab_remediation HotfixCandidateContract 4/4 绿；仓库外 FakePipeline 流程探针 8 场景 46 断言过；`ab e2e full` 覆盖运行期链路 |
| F05 同包修复写入活动包而非隔离 staging | P1 | 已关闭 | T8：`RuntimePathManager.GetHotfixStagingRoot/GetHotfixBackupRoot`；删除 `IsSamePackageRoot(...)` 前提与直指当前包根的写入；成功换入+删旧、失败退回 staging 并放回旧目录且不写指针；同名门禁 4/4 绿 |
| F06 `SceneHandle` 所有权与 Retain/失败重试不一致 | P1 | 已关闭 | T9：`SceneUnloadResult`（Success/Error/ActiveOwners）、仅最后 owner 可物理卸载、失败保留 token；`runtime_resource` SceneOwnershipTests 2/2 绿 |
| F07 async/sync 混用泄漏一次 Bundle acquisition | P1 | 已关闭 | T9：同步路径遇同 Entry inflight 返回 `RuntimeErrorCodes.LoadInProgress`；EntrySingleFlight 门禁绿（最终 `UnloadCount==1`） |
| F08 Android StreamingAssets 内置包加载 | P1 | 明确延期 | 计划批次范围外（`F08 Android 本轮延期`）；未在本轮修改 Android 路径 |
| F09 Cloudflare 回滚删除旧同名包 | P1 | 已关闭 | T7：旧同名包先备份到服务根外，失败按镜像前状态放回，仅删除本次新建目录；`publish_diff` CloudflarePublishDecisions 行为测试绿（断言失败后旧包仍在、索引字节恢复） |
| F10 新增场景工程被 ignore | P1 | 已关闭 | `.gitignore` 最小例外 `!tests/scenario/**/*.csproj`；10 个门禁 csproj 均可被跟踪（`git check-ignore` 全部通过） |
| F11 facade lease 表大小写敏感 | P2 | 已关闭 | `AssetPackageManager` AB lease 使用 `StringComparer.OrdinalIgnoreCase`；S2 门禁新增断言并 PASS |
| F12 跨平台路径比较忽略大小写 | P2 | 已关闭 | `FYAssetPathUtility.AreSamePath` 使用平台 `FilePathComparison`；S2 门禁断言单一来源并 PASS |
| F13 应急命名遗留无效配置面 | P2 | 已关闭 | `BundleFileNameStyle`/`FileNameStyle` 从代码、UI、两份 `.asset` 删除；`grep FileNameStyle Assets` 0 命中 |
| F14 文档与实现矛盾 | P2 | 已关闭（HTML 生成物除外，见遗留项） | 12 个 `docs/FYAsset/*.md` + `README.md` 4 处过期块对齐；`grep BundleBuildCache/VersionRecord/HotfixAccumulation/FileNameStyle/_build_cache docs/ README.md` 仅 `fyasset-modeling.html` 命中（生成物，需重跑生成器）；49 条改动文档相对链接 0 断链 |
| F15 死状态与过期注释 | P3 | 已关闭 | 删除 `StandaloneBuild`（字段/面板/E2E bake-restore/设置资产键）、`VersionRecordPath` 键、`PackageFileNames` 死常量、`PushPayload.ServerPackagesRoot`、`CumulativeSourceLabel`；修正 Export/Request/PublishCache/AA/Runner/面板/测试引擎的过期注释 |
| F16 工作区噪音与行尾残留 | P3 | 已关闭（1 项显式排除） | `status_snapshot.txt` 删除；`IBuildTask.cs` 行尾归一；两份 XML `.meta` 的纯行尾差异保留在工作树并明确排除在最终提交之外 |

## 收口矩阵（新鲜执行）

纯 .NET / 静态门禁：

- `dotnet build XLuaHotfix.sln --no-restore --no-incremental` → 0 error
- `ab_remediation` 6/6；`publish_diff` 6/6；`build_cache` 5/5（59 断言）；`runtime_resource` 19/19；`pipeline_realignment` 7/7；`pipeline_compose` 4/4（29 断言）；`HotfixRuntimeStateMachine` PASS；`S2RuntimeBoundary` PASS；`S3ResourceBoundary` 6/6；serialization PASS
- `git diff --check` exit 0；Assets 受检 534 文件 `.meta` 无缺失；918 个 `.meta` GUID 无重复

Windows Unity / Player（local 目标，无外网）：

- `ab build full` exit 0（`Build_20260911064628_1.0.0`，Expected=Actual=1.0.0，RestorationSucceeded=true）
- `ab build hotfix` exit 0
- `ab e2e standalone` exit 0
- `ab e2e full`（在线 Player + localhost）exit 0

## 遗留项与明确边界

1. **地址端到端闭合是数据对齐，待开发者确认**：项目 `AddressStyle=LongAssetPathWithoutExtension`（已批准）后自动 Address 为长路径，运行时请求键仍是 AA 时代的短名。已在 `CollectorSetting.asset` 的 `AssetOverrides` 中为 Addressables 已注册的 17 个公共资源补 `Address:`（与 AA group 地址逐字一致），使用计划内自定义 Address 机制，不改游戏代码/AddressStyle/运行时键。备份在仓库外 `/tmp/CollectorSetting.asset.bak`。
2. **`docs/FYAsset/fyasset-modeling.html` 未手改**：判定为生成物（内嵌 241 条源码索引与自校验不变量），需按生成器重跑以同步已删类型与门禁计数。
3. **F08 Android 延期**：不在本轮范围。
4. **AA 完整矩阵延期**：`ExportAAOutputTask`/AA Hotfix 路径本轮只做编译与门禁级验证，未跑 AA Unity/Player 矩阵。
5. **T8 流程探针未入库**：真实 `HotfixFlowBase` + FakePipeline 的 8 场景 46 断言在仓库外执行通过；建议后续提升为常驻场景工程（本轮未改测试工程边界）。
6. **Unity 矩阵未逐项重跑的场景**：同输入第二次 Full 的复用命中、逐维度（Group/Packing/Address/Labels/依赖/压缩/配方）缓存失效、删除 Hotfix1 后重建 Hotfix2、Full 客户端直接更新到 Hotfix2 —— 上述语义由 `build_cache`/`ab_remediation`/`pipeline_compose` 纯门禁与 `ab e2e full` 链路覆盖；未再单独做 Unity 端重复验证。

## 独立审计与审计后修复（2026-09-11）

由独立 reviewer 对照计划文本与工作树做只读审计（`git status`、删除清单、Confirmed Decisions、任务最小验收），结论与处置：

1. **发现并修复（计划要求未落地）**：`PublishTargetPanel.RunPush` 未把正式 Summary 身份注入 `PublishRequest`（`request.TryResolveIdentity` 必然失败 → 编辑器 Push 按钮不可用；自动化路径已注入故门禁未暴露）。已改为从 `BuildSummaryStore` 解析身份并注入 `Identity` 与 `BaseFullSummaryId`；新增门禁 `tests/scenario/publish_diff/test_publish_identity_source.cs`（`PublishIdentitySources`），并实际验证 RED→GREEN（移除注入即失败）。`publish_diff` 现 7/7。
2. **发现并修复（文档闭环）**：`README.md` 仍有 4 处过期事实（包内 `build_summary.json/.txt`、`{BuildOutputRoot}/_build_cache`、固定累计 Hotfix 目录、VersionRecord/交付事务顺序）。已按当前实现改写（Summary/Index、历史包复用、独立 `Build_*` 包、提交顺序与回滚）。
3. **记录澄清（非未记录改动）**：`Assets/Build/Bootstrap/BuildIndex.json(.meta)` 的删除来自本轮 T1 改造的 `fyasset_reset.py`（“首次启动前删除更干净”），由 E2E/reset 运行触发；工作树文件已还原，reset 工具行为保留并记录于此（该文件按构建导出产物对待）。磁盘残留 `build/StandaloneWindows64/_build_cache`（15MB，gitignore 内）已删除。
4. **工作树状态澄清**：git 索引中存在上一轮遗留的 staged 重命名/删除（`CloudflarePagesPushTarget` 重命名、`BuildRepositoryCLI` 与 `test_build_diff_entry.cs` 删除、3 个 docs 重命名）。本轮未 stage、未提交、未改写索引。
5. **审计确认的计划内/已记录项**：固定累计目录族、`FileNameStyle`、`StandaloneBuild`、`_build_cache` 代码、`VersionRecord`、`CumulativeSourceLabel` 等 18 个符号在 `Assets/**` 中 0 命中；92 项删除均可归属（仅上述 Bootstrap 文件需要澄清，已处理）；计划明确延期项未被误报为通过。
6. **审计指出的残余**：`HotfixPublishSelfCheck` 无外部调用方（仅自用），保留待开发者决定是否移除；`docs/FYAsset/fyasset-modeling.html` 仍需按生成器重跑。
