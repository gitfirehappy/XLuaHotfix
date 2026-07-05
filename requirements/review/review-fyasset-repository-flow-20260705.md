# FYAsset Repository Flow Review

> **Date**: 2026-07-05
> **Reviewer**: Codex
> **Scope**: FYAsset build repository commit, HEAD handling, package publication, local push, and repository status UI
> **Method**: Unity build log review, static flow review, and diff review

## 结论

这次构建链路已经从“边构建边发布指针”收敛到更合理的顺序：先生成包体，再提交 Repository，最后发布 `PackageIndex` / `StreamingAssets`，并且 Full build 可以废弃损坏 HEAD 后以空仓重建。最新日志中最后一次 Full build 已经能导出包体并提交 HEAD，说明主链路可跑通。

但仓库层还没有达到可长期信任的事务模型。当前最需要补的是 push 发布事务、orphan/坏对象治理、以及状态 UI 对“损坏、空仓、废弃重建”的显式表达。

## Findings

### P1 - LocalDirectory push 不是原子发布，失败会破坏目标目录

位置：`Assets/FYAsset/Scripts/Build/Repository/Editor/LocalDirectoryPushTarget.cs:42`

`Push()` 当前顺序是确保发布根目录、删除目标包目录、复制新包目录、写 `PackageIndex`。如果复制中途失败，旧包已经被删，`PackageIndex` 可能仍指向旧状态或后续无法写入，目标发布目录会处于半更新状态。

建议：push 应采用 staging 目录。先复制到临时目录并校验基本文件，再原子替换目标包目录，最后原子写 `PackageIndex`。失败时保留旧包和旧指针。

### P1 - Commit object 写入后 HEAD 写入失败只留下 orphan，没有治理入口

位置：`Assets/FYAsset/Scripts/Build/Repository/Editor/FileBuildRepository.cs:121`

`Commit()` 先写 object，再写 `HEAD.json`。HEAD swap 失败时会留下 orphan object，只打 warning。这个方向比先写 HEAD 安全，但当前没有清理/标记工具，后续 `ListCommits()` 仍可能显示不属于 HEAD 链的对象。

建议：保留当前写入顺序，但增加 orphan 管理策略：commit 失败时尝试删除刚写入的 object；UI 中区分 reachable commits 与 loose/orphan objects；提供清理命令。

### P1 - Full build 废弃坏 HEAD 的行为正确，但审计信息不足

位置：`Assets/FYAsset/Scripts/Build/Repository/Editor/FileBuildRepository.cs:103`

Full build 遇到 malformed/stale HEAD 时会清空 parent 并重建仓库；Hotfix 仍严格失败。这符合“不做历史兼容，错误应废弃重建”的方向。问题是当前只写 warning，不持久化被废弃的原因、旧 HEAD 内容、旧对象清理结果。

建议：Full rebuild 废弃 HEAD 时写入一个简短 `RepositoryRepairLog.json` 或 push history 类似记录；坏 `+Build` 对象应删除或移入 quarantine，不应长期混在 `objects/`。

### P2 - RepositoryStatusPanel 会重建左侧列表，选择和滚动状态容易丢失

位置：`Assets/FYAsset/Scripts/Build/Repository/Editor/RepositoryStatusPanel.cs:430`

`RefreshRepositoryState()` 每次都会 `RenderNavigation()`，后者清空 `_leftList` 并重建。当前能保持选中 commit 的逻辑有限，但对滚动位置没有持久化，和 AssetsCollection 曾出现的“点击靠后项后列表跳动”属于同类体验风险。

建议：左侧导航刷新前保存 selected version / selected artifact key / scroll offset，刷新后恢复。Repository 面板和 AssetsCollection 面板应共享这个 UI 约束：数据刷新不能改变用户的滚动上下文。

### P2 - Package publication 已后移，但不是完整事务

位置：`Assets/FYAsset/Scripts/Build/Release/Editor/Shared/BuildProjectManager.cs:127`

当前顺序是 commit 后调用 `TaskExportLocalBuildData.Publish()` 和 `TaskWritePackageIndex.Publish()`；发布失败会回滚 HEAD 并删除失败包目录。这已经修复了“失败包被 PackageIndex 指向”的主要问题。

剩余缺口是：`StreamingAssets` 文件复制和 `PackageIndex` 写入之间没有共同事务。若 baseline 已复制但 PackageIndex 写入失败，HEAD 会回滚、包目录会删除，但 `StreamingAssets` 可能已经被新 baseline 覆盖。

建议：Full build 的本地 baseline 也走 staging，然后在全部发布步骤成功后统一替换；失败时恢复旧 `StreamingAssets` baseline 或至少写明确失败标记。

## 已确认改善

- 版本字符串已收口为 `Major.Minor.Patch[-Channel]`，`Build` 只保留为数字字段，仓库 object/HEAD/ParentVersion/PushHistory 不再使用 `+Build`。
- 官方构建 DAG 设置 `DeferPackagePublication`，PackageIndex 和 Full baseline 发布移到 Repository commit 之后。
- Full build 失败不再继续弹 Unity Build Settings；失败包目录会删除，删除失败时写 `FAILED_BUILD.json`。
- Full build 对坏 HEAD 采用废弃重建策略；Hotfix 对坏 HEAD 保持严格失败。
- `XLuaLoaderTester` missing script 已补回；最新日志中仍有 `XLuaConfigTester` missing script，需要单独处理。

## 后续建议顺序

1. 先做 LocalDirectory push staging/atomic replacement，避免把坏状态发布到外部目录。
2. 增加 repository cleanup/repair 入口，清理 `+Build` 旧对象和 orphan object。
3. 给 post-commit publication 加 baseline staging 或失败恢复。
4. 优化 RepositoryStatusPanel 左侧列表刷新，保留滚动位置。
5. 单独恢复或删除 `XLuaConfigTester` 场景引用。
