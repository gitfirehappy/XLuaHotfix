# Plan: AB 构建交付所有权收敛（Runner 独占 finalize + attempt staging）

> **Status**: Executed / Verified 2026-09-06（待手动全量审查与 AA 对齐后续，同 Android 同级搁置）
> **Date**: 2026-09-06
> **Source Review**: `requirements/review/review-xluaframework-fyasset-20260905.md`（B01、B02；T01/T03/T04 归测试自治子范围）
> **Confirmed Decisions**:
> - 双出口模型：HotfixOutput = 远端导出（Full + Hotfix）；StreamingAssets = 本地（Full、Standalone）
> - 按构建模式 fan-out 的所有者 = `BuildProjectRunner`（方向 A，不新增 finalize task）
> - AA 管线本轮不做，与 Android 同级搁置，AB 完成后再按同模型对齐

## 目的

1. 任意一次 AB 构建要么是一次完整可见交付，要么对 live 状态零影响；不存在 PackageIndex / StreamingAssets / baseline / VersionRecord 互相矛盾的中间态（B01）。
2. 构建任务链禁止直接写任何 live 目录；所有任务只产出 attempt 目录（B01/B02 前提）。
3. Standalone 构建失败保留上一份可用离线包，成功才原子换入（B02）。
4. 测试恢复改用 per-scope durable 状态：不完整快照禁止破坏性恢复、恢复失败不可标记完成（T01/T03/T04，由执行者自治收敛，不向开发者提问）。

## 约束

- 范围仅限 AB 构建管线 + 测试恢复边界；AA 构建代码只允许为了编译通过做引入性调整，行为不变。
- 不新增通用事务框架、不恢复 repository kernel / DAG。
- 现有 `DeferPackagePublication` 模式保留并在 Runner 层补全；尾部两个 Task 保持 deferred 空转，不删除、不改配置资产，清理留待 AA 对齐轮。
- 不动 B03（manifest 格式矩阵）、B04（preview）、B09/B10、运行时 R01/R02、XLuaFramework 任一项。
- 预览/ Diff Preview 路径提前 stop，不经过 finalize，行为不变。

## 已有事实（实施摘要必须尊重）

- `ABBuildBackend` / `AABuildBackend` 已设 `DeferPackagePublication=true`；`TaskWritePackageIndex`、`TaskExportLocalBuildData` 链内为空转，静态 `Publish` 由 `BuildProjectRunner` 调用。
- `TaskOrganizeOutput` 现在执行“删旧最终目录 → _temp → CopyFile 到 `request.OutputDir`”。
- `TaskWriteABPackageManifest` 断言输出路径必须等于 `request.OutputDir` 并写 `request.OutputDir` 根。
- Standalone：`BuildPackageRequest.Create` 令 `OutputDir = BuildPathManager.StandalonePackageDir`；`HandleFailedPackage` 的 safety check 明确允许删除该目录。
- `RunBuild` 顺序：backend build → PublishBuildArtifacts（Startup data + PackageIndex）→ RecordDeliveredBaseline → 返回后外层再 `ApplyBuiltVersion`（赎回边界之外）。

## 目标契约

1. **Attempt-only tasks**：AB 的 organize / manifest 任务只写 attempt 目录，`request.OutputDir` 语义改为 attempt 路径；最终导出路径仅 Runner 在 finalize 时计算。attempt 与最终根同卷（项目内），保证原子 move。
2. **Runner 独占交付**：`BuildProjectRunner` 的 finalize 顺序为
   `validate attempt → promote 包目录（非 Standalone）→ local build data（Full/Standalone）→ PackageIndex（非 Standalone）→ baseline（非 Standalone）→ VersionRecord`。
   整段在同一补偿边界内；失败时恢复：PackageIndex 旧字节、local build data 备份、删除已 promote 的包目录、VersionRecord 未落盘则无影响。
3. **Standalone swap**：任务写 attempt；成功后 old-aside → new-in → remove-old 的原子序列换入 `StreamingAssets/Standalone`；任何一步失败都自动回退到旧目录。
4. **失败清理只看 attempt**：`HandleFailedPackage` 只允许删除 attempt 目录族，任何 live 根（`HotfixOutput/Packages/Build_*`、`StreamingAssets/Standalone`）不再出现在清理分支。
5. **测试恢复 manifest**：`BuildTestRecoveryRecord` 增加 per-scope 状态（`AbsentBefore | SnapshotIncomplete | SnapshotComplete | Restored | RestoreFailed`）；恢复循环按 scope 独立执行并聚合错误；`Completed` 只在所有 scope `Restored/AbsentBefore` 后才允许为 true。

## 任务拆分

| # | 任务 | 范围 | 验收 |
|---|------|------|------|
| T1 | Request/attempt 语义 | `BuildPackageRequest` 增加 attempt 导出路径；AB/Standalone 的创建分支改指 attempt；`BuildPathManager` 加 attempt 根约定 | compile + 路径单测 |
| T2 | AB organize/manifest 收窄 | `TaskOrganizeOutput`、`TaskWriteABPackageManifest` 只写 attempt；删除 live 目录重建逻辑；合计清单落盘供 finalize 校验 | compile + RED：任务链运行后 live 根零变化 |
| T3 | Runner delivery commit | 新 finalize：validate→promote→local data→index→baseline→version + 补偿；`HandleFailedPackage` 收窄为 attempt-only | RED：在 index 后注入失败，断言 PackageIndex/StreamingAssets/baseline/VersionRecord 全部保持旧值 |
| T4 | Standalone 原子换入 | attempt → `StreamingAssets/Standalone` swap；早期失败保留旧包 | RED：构建前置失败 + 中段失败均保留旧目录与旧 BuildIndex |
| T5 | 测试恢复 manifest（自治） | `BuildTestState` per-scope durable 状态、独立 scope 恢复、恢复失败不标记完成、`E2ETestEngine` 一并收口 | RED 三项 |
| T6 | 回归与矩阵 | AB build full/hotfix/chain + standalone E2E，验证最终出口内容、版本/baseline/index 一致性 | 现有 BuildTest 引擎全绿 |

## 成功条件

1. `dotnet build XLuaHotfix.sln --no-restore` 0 errors（既有 System.Net.Http 警告不变）。
2. 四套 scenario 项目全绿（S2 / S3 / Hotfix state / runtime-resource）。
3. 三个 RED 在三刀实现前失败、实现后通过：index 后注入失败零矛盾、Standalone 中段失败旧包完好、不完整 snapshot 拒绝删除。
4. AB build full + hotfix + chain + standalone E2E 通过，`HotfixOutput/Packages` 与 `StreamingAssets` 双侧内容与 baseline/VersionRecord/PackageIndex 一致。
5. review 中 B01、B02 标注为已修复；T01/T03/T04 标注为已修复；其余 findings 状态不变。

## 实施顺序

T1 → T2 → T3 → T4 → T5 → T6；每个任务独立 compile + 对应 RED 断言后再继续。

## 批准清单

- [ ] 同意本计划范围与任务拆分（AA 搁置、尾部 deferred Task 保留空转）。
- [ ] 同意 attempt-only 语义下 AB 构建的 live 写入唯一入口收敛到 `BuildProjectRunner`。
- [ ] 同意执行 T3/T4/T5 期间按需要进行故障注入 RED（仅项目内临时目录与自有 fixture）。
