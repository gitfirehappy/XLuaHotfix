# 管线自定义 Task 分槽 + LuaScriptsIndex 归位 Compat（2026-09-04 议定）

## Spec

### 目的
1. FYAsset 管线获得通用能力：**骨架名单与外部自定义名单分槽**，框架认识、解析、装配外部自定义 task（lua 无关的纯机制）。
2. `LuaScriptsIndex` 的构建/打包/校验语义全部迁出 FYAsset 三树，作为 Compat 的业务 task 插入管线；FYAsset 树 lua 词汇清零。
3. XLF 保持零构建逻辑（`LuaScriptsIndexBuilder` 只做数据读写，被 Compat 调用方）。

### 约束
1. 骨架名单不变（`AA/ABPipelineBackbone.BackboneTaskNames`）；自定义名单槽位 = `BuildPipelineConfig.CustomTaskEntries`（`typeName`+`insertBefore` 显式锚，空=骨架后追加）。
2. 装配违约（锚名不存在/类型无法实例化/非 IBuildTask）→ 明确 Fail，含具体名字，不静默跳过。
3. preview 语义简化：lua index diff 反映"上次构建落盘状态"，preview 不临时改写资产（现有 snapshot/回滚协议与 BuildPreviewRunner 特判随迁移删除）；代价记入风险。
4. 机制是通用的，不过度为 lua 特化：自定义 task 只能拿到中性 `BuildContext`/config 能带来的东西。
5. Compat 内的业务 task 代码可合法同时认识 FYAsset 与 XLuaFramework（胶水层定义）。
6. 不加 asmdef/package.json。

### 成功判据
- FYAsset 三树（AA/AB/Shared）grep `LuaScriptsIndex|LuaScriptContainer|LuaScriptsIndexBuilder|LuaScriptsIndexBuildException` 全零命中（新增门禁条款或并入现有 gate）。
- `BuildPipelineConfig.CustomTaskEntries` 空列表时管线行为与现状完全一致（白名单 preview / CLI 输出路径全绿）。
- AA config 将 `LuaScriptsIndexBuildTask` 放在 `TaskScanAAHotfixDiff` 之前、AB config 锚 `insertBefore=TaskBuildBundles` 后：构建窗口/direct backend API 两次构建产出与现状同构（SO 内容与 manifest/包内落点不变）。
- 违约装配的明确报错有演示性测试或真值演练（至少一个 scenario 测试覆盖）。
- 场景测试 4 套全绿；S3 XLuaFrameworkBoundary / ExportBoundary 不回头。

### 非目标
- XLF 内部重构（Builder 不动，exception 类型不动）。
- 运行时 lua 加载面（`ILuaAssetLoader` seam 已完成）。
- index 资产的 per-container 粒度化（此前已否决）。

## 现状事实
- 管线装配：`BuildPipelineRunner.Execute(config, context, options, expectedBackboneTasks)`，名单由 backbone 提供，实例由 `BuildTaskListUtility.CreateTasks(names)` switch 构造。
- lua 引用面（全部 Editor 段）：AA 4（`AALuaScriptsIndexExporter` 全文件、`AABuildBackend.ExportData` 调用、`TaskWriteAAPackageManifest.Validate` 调用、`AARepositoryPreview.RebuildDataOnly`）；AB 4（`TaskCollectAssets.TryAdd*`、`TaskCollectBuiltins` index 收编、`TaskGenerateManifest` Validate、`ABRepositoryPreview` snapshot 相关）；Shared 1（`BuildPreviewRunner.CaptureLuaScriptsIndexSnapshot` 特判）。
- `LuaScriptsIndexBuilder.Rebuild(containerAddresses)`/`ValidatePublishedAssets(publishedAssets)` 是中性的（DDictionary/string -address 参数）。
- AA exporter 只用 Addressables API + XLua builder，文件本身不含 FYAsset 引用，纯业务。

## Tasks

| # | Task | 验证 |
|---|------|------|
| T1 | **修正（开工勘探发现）**：机制已存在——`BuildTaskResolver` 全程序集扫描 IBuildTask 按名解析 + `BuildPipelineConfig.Tasks` 列表顺序即执行顺序 + 骨架校验只查漏不拒外。T1 降为：明文契约（骨架名单=南北 backbone 校验基线、自定义名单=config.Tasks 中超出骨架的条目、位置自由）+ scenario 契约测试防回归 | scenario 契约测试 |
| T2 | `LuaScriptsIndexBuildTask` 落 `Compat/Editor/Build/`（rebuild via XLF builder / AA 注册 / AB 收编 bootstrap raw / 双端 ValidatePublishedAssets 并 fatal 失败）| 单元手审 + 构建演练 |
| T3 | 移除 FYAsset 树 lua 引用：AA 4 点位（exporter 文件删除，backend/preview/task 引用去除）、AB 4、Shared 1（BuildPreviewRunner 去 snapshot 特判与 LuaScriptsIndex 特判）| 门禁条款（见 T4）+ grep 零 |
| T4 | 门禁升级：ExportBoundary 或新条款直接禁 FYAsset 树出现 lua index 相关词表 | s3 PASS |
| T5 | config 资产挂接：`AABuildPipelineConfig.asset` 将 task 置于 `TaskScanAAHotfixDiff` 之前（**开工修正**：原文 `insertBefore=TaskWriteAAPackageManifest` 会晚于 `TaskBuildAAContent`，Addressables 打到旧索引）；AB `insertBefore=TaskBuildBundles` | 资产内容审查 + 构建演练（编辑器侧由开发者回归）|
| T6 | 全场景回归 + docs/context 更新（导出集规范·管线自定义 task 说明）；plan 归档 | exit=0 ×4 |

## 风险与降级
- **preview 语义降级**：container -only 变更在未重建时 preview 低估 lua 差异（真实构建永远准）；记入 plan 且 preview 文案如实说明。
- **锚名重命名漂移**：骨干 task 重命名会无声破坏 config 锚——由装配违约的明确 Fail 兜底，纳入约定文档。
- **Compat task 的排序语义**：两个 config 分别挂 task，本机制不支持/custom 任务间相互插入（当前用例无此需求，记为非目标）。
- **runner whitelist preview**：`expectedBackboneTasks == null` 时 custom 是否参与 preview —— 设计为**参与**（preview 跑完整管线至 whitelist 终点，task 有 preview-意识 prepare-only 对称实现；无写资产副作用）。

## 执行顺序
T1 → T4（门禁先行）→ T2 → T3 → T5 → T6；每步对齐 plan 状态与 progress。
