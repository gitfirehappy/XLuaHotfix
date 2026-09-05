# Build/E2E Reset Isolation Review (2026-07-23)

> **Date**: 2026-07-23  
> **Reviewer**: Claude Code (reset-isolation CLI batch)  
> **Scope**: clean-slate batch（每用例 reset）+ AA e2e Player persistent 隔离 + Hotfix seed  
> **Method**: Serial reset-per-case Build/E2E matrix with retained-Player marker verification  
> **Class rule**: 细节直接改；大方向/架构记本 review
> **Status**: Completed / Archived — 2026-07-24

## 问题与处置

| 现象 | 分类 | 处置 |
|------|------|------|
| 每用例 reset 后 standalone Hotfix exit 7（无 HEAD） | 测试细节 / 契约张力 | `BuildTestEngine.RunHotfix`：baseline 不可用时在同一事务内 seed Full（keep Targets），再 Hotfix；结束后统一 Restore。**不是**产品 `BuildHotfix` 静默 Full |
| AA e2e full Player：`LocalLow/.../fyasset` 脏包导致「本地需修复但远端非前向」 | 测试细节 | 假 CLI `-persistentDataPath` 无效；改为 bake 隔离 `ProjectName` 到 Player Resources |
| Full 用例 always-restore 后磁盘无 Full | 契约（既有） | 仍成立；standalone Hotfix 依赖 seed 或外部 baseline |
| e2e hotfix/chain 无完整 retained-Player 前向冒烟 | **架构/范围** | 记残差：当前多复用 Build 引擎；完整 E2E continuity 待后续 plan |
| FYAsset 独立程序集 asmdef | **架构** | 未在本轮落地：182 cs、AA Addressables 依赖、Editor/Runtime 混目录。建议 Runtime/Editor/AA/AB 分层 asmdef，需单独 plan |
| 注释/日志中英 | 卫生 | 本轮改动保持「中文描述 + 英文术语」；全库扫未发现 plan id 污染 |
| 过度改动风险 | 约束 | 未抽象新框架；seed 复用 Chain 前半路径；batch 仍只 reset+串行 |

## 本轮代码改动（最短）

1. `BuildTestEngine.cs` — Hotfix seed Full；删除无意义 `PublishSuccess = PublishSuccess`
2. `E2ETestEngine.cs` — ProjectName 隔离 LocalLow；去掉无效 persistent CLI
3. `run_fyasset_test_batch.py` — 每用例 `fyasset_reset all --keep-testruns --reports --clear-publish`
4. `VersionDataBase.asset` — 修复损坏的 `LastBuildTime` 字段名
5. AA 夹具组：`FYAssetPipelineSync` 归入永久 `FYAssetPipelineTest`（符合 plan 表）

## 文档

- 新增 `docs/FYAsset/自动化测试管线.md`
- `docs/FYAsset/使用命令行打包.md` / `docs/README.md` 索引对齐

## Mistake 多维对照（节选）

| ID | 应用 |
|----|------|
| IP-01/02 | Player 失败写 result + exit；coordinator timeout 显式 Fail |
| IP-04 | 禁止吞异常；batch 记录 EXCEPTION 后继续 |
| IP-06 | 删除 noop `PublishSuccess = PublishSuccess` |
| IP-08 | reset/seed 后路径与 PackageIndex 必须可重建 |
| IP-11 | 测试后端显式 aa/ab，不半跟随 UseABBackend |
| IP-16 | 隔离目录删除走 FileHelper |

## 最终矩阵（reset-per-case + retained-Player 热更，2026-07-23 晚）

| 用例 | Exit | 约秒 | Player 证明 |
|------|-----:|-----:|-------------|
| aa/ab × build × full/hotfix/chain | 0 | 17–23 | n/a |
| aa/ab × e2e × full | 0 | 45–48 | Full smoke |
| aa/ab × e2e × hotfix | 0 | 74–81 | Full then Hotfix relaunch |
| aa/ab × e2e × chain | 0 | 65–70 | Full v1 → Hotfix v2 markers |

**12/12 PASS** — `HotfixOutput/TestRuns/cli-batch-3/summary.json`

Player 前向热更证据：
- AA chain：`MarkerSync` v1 → v2
- AB chain：`MarkerRaw` v1 → v2

## 第二轮落地

1. `E2ETestEngine` retained-Player：`BuildPlayerSession` + `LaunchPlayer` 双相位  
2. CLI 硬化：Unity 退出等待；VersionDataBase write retry  

## 建议后续（不本轮做）

1. FYAsset Runtime/Editor asmdef 拆分 plan  
2. 多 Target / Cloudflare batch  

