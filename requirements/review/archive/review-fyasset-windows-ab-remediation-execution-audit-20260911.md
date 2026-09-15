# Review: fyasset-windows-ab-remediation 执行效果审查（签收前）

> **Date**: 2026-09-11
> **Reviewer**: 主 Agent（受开发者委托）
> **Scope**: `../plan/plan-fyasset-windows-ab-remediation-20260911.md` 全部执行记录、disposition、当前工作树与 git 索引
> **Method**: 新鲜门禁复跑 + git 索引/工作树残留扫描 + 计划条文逐项对照；只读扫描，清理动作见「已执行清理」

## 结论

计划 T0–T11 的实现主体成立：新鲜复跑的全部纯 .NET / 静态门禁绿，执行记录、disposition 与代码现状三者一致。**未发现与 Confirmed Decisions 相抵触的实现。** 发现 2 处环境残留（已清理）、1 份过期索引条目（已修复）、4 项实质遗留（deferral 与待开发者决定项，见下文，不阻碍转人工审查）。

## 新鲜验证（本次执行）

```text
dotnet build XLuaHotfix.sln --no-restore --no-incremental   0 error / 4 既有警告
ab_remediation 6/6 · publish_diff 7/7 · build_cache 5/5(59 断言) · runtime_resource 19/19
pipeline_realignment 7/7 · pipeline_compose 4/4(29 断言) · Hotfix state PASS · S2 PASS
S3 6/6 · serialization 3 PASS
git diff --check（工作树与索引）exit 0
Assets 完整性：678 个受检文件 .meta 无缺失、orphan meta=0、919 个 GUID 无重复
活动文档（README/docs/context/requirements 非 archive）本地链接 0 断链
```

说明：broken 链接 25 条全部位于 `requirements/**/archive` 与早期 drafts 中引用已迁移的历史路径，属时点记录的历史文档，不在本轮改动范围，不修。

## 已执行清理（本轮审查中完成）

1. **git 索引 staged 残留清除**：上一轮遗留的 8 条 staged 条目（`BuildRepositoryCLI.cs(.meta)`、`test_build_diff_entry.cs` 删除、`CloudflarePagesPushTarget` 重命名、3 个 docs 重命名）已通过 `git restore --staged` 全部退回工作树。索引现已干净（`git diff --cached --stat` 为空），工作树内容未受任何影响（重命名目标文件存在且为重构后的版本）。提交时可由开发者重新决定分组。
2. **仓库外备份入库**：`/tmp/CollectorSetting.asset.bak`（17 条自定义地址数据对齐的备份）移至 `backlog/CollectorSetting.asset.bak.20260911`，不再依赖易失目录。
3. **`requirements/plan.md` 过期队列条目**：该计划的状态行仍写「Design approved / awaiting explicit execution approval / 暂不执行」，与页首 Status 和实际执行冲突，已更新为 Implemented / awaiting sign-off。

## 与决策对照（无偏差项摘要）

抽查 Confirmed Decisions 与实现的对应关系，disposition 的逐条处置（F01–F16）与代码/门禁现状一致：

- Summary/Index 版本事务、独立 `Build_*` 纯包、`VersionRecord`/固定累计目录/`_build_cache`/`FileNameStyle`/`StandaloneBuild` 退场：18 个应删符号在 `Assets/**` 复核仍 0 命中。
- Hotfix 直接 Diff 最近成功 Full、发布 containment/组装/Cloudflare 补偿、Scene 最后 owner 卸载、sync 遇 inflight 返回 `LoadInProgress`：对应门禁全部绿。
- 延期项（F08 Android、AA 完整矩阵、真实 Cloudflare 部署）在计划、disposition、progress 中标注一致，未被声称通过。

## 实质遗留（转人工审查，不改代码）

1. **`docs/FYAsset/fyasset-modeling.html` 过期**（disposition 记录为「按生成器重跑」，但实际无生成器脚本，该文件为纯手工页面）：内嵌 241 条源码索引仍含已删除类型 `VersionRecord`(4)/`BundleBuildCacheStore`/`HotfixAccumulationDirectory`，且交付事务步骤图仍写「最后应用 VersionRecord 并提交构建缓存」——与现行「Summary → Index 最后提交」直接矛盾；同时缺少 `BuildSummaryStore`/`PublishPathGuard`/`PackageTargetAssembler`/`SceneUnloadResult` 等新核心类型。需要一次实质内容刷新，建议决定由谁、在何时重做。
2. **`HotfixPublishSelfCheck` 无外部调用方**（审计已指出）：保留或移除待开发者定夺。
3. **T8 流程探针未入库**：仓库外 8 场景 46 断言已通过（探针仍在 `/tmp/t8probe*`），建议提升为常驻场景工程。
4. **EOL-only 差异 3 个文件**（`Assets/Test/ConvertTestAssets/Xml/*.meta` 两个、`Assets/Dialogue/Scripts/Dialogue/CharacterConfig.cs.meta` 一个）：均为补末尾换行，按 disposition 明确排除在最终提交之外。注意 disposition 只记录了前两个，第三个同性质，提交时一并排除。
5. **地址数据对齐待确认**：`CollectorSetting.asset` 的 17 条自定义 Address（备份在 `backlog/`），仍需开发者显式确认。

## 边界

- 本审查未重新执行 Windows Unity/Player 四项（`ab build full/hotfix/e2e standalone/e2e full`），沿用 T11 当日的 exit 0 记录；纯 .NET 层全部为本次新鲜证据。
- 未触碰 index 之外的工作树内容；未提交、未建分支。
