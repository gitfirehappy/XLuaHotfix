# FYAsset 编辑器、工具与建模文档统一

> 状态：已执行 / 已验证 / 开发者签收，归档（2026-09-09）

## Purpose / Constraints / Success

目的：精简编辑入口和通用工具边界，统一注释与人类文档，交付解释实际核心流程的离线 HTML。
约束：保留开始时的 208 项未提交改动；基准 HEAD `567089a45292477289a0f6c30d53baaee0082744`。不提交、不改历史、不清空输出、不执行真实发布。不改变 AA Type 推导、Diff 判断、VersionNumber 的解析和比较规则。旧二进制数据不兼容已获批准。
成功标准：实现与引用正确；序列化和 struct 有可运行验证；编辑器和 HTML 布局检查；223 个初始 C# 文件及新增/删除项逐项追踪；所有现存范围内 C# 都有文档职责索引；文档路径和图可核验；未验证项明确保留。

## Approval

2026-09-09 开发者批准当前全部已完成改动提交，并签收本计划；后续资源管线纠偏由 `plan-fyasset-resource-pipeline-realignment-20260909.md` 接续。

1. AB Labels 归入 Collection 候选配置，统一 Save/Cancel；AA 使用 Addressables 原生编辑器。
2. 删除 AA/AB Project Labels 页面和无其他用途的共享展示类。
3. 编辑器统一间距（组内 4–6、组间 8–12 为起点）、紧凑工具栏、伸缩列表；删除重复标题和常驻教程，保留状态、错误与破坏性操作确认。
4. 英文术语与简短英文描述；复杂原因中文。FYAsset 与 Serialization 手写 C# 全量语义核查，不强制 XML/region；生成代码改模板。
5. 删除 Serialization 的 ABManifestRoundTripTest、Smoke 菜单与专用模型/生成类；业务生成类与注册归 FYAsset；工具无业务依赖；保留通用验证。
6. VersionNumber 转 struct，保留既有比较忽略 Build 的规则；不兼容旧二进制，核查默认值与调用处复制。
7. BuildIndexData 直接移动到 Shared/Runtime，删除空 Bootstrap 目录；保留 .meta GUID。
8. ArtifactDigest 改为 BuildDiffEntry，保留字段及比较规则。
9. 保留 HotfixOutput / HotfixPublish 的构建与发布职责隔离。
10. 治理 FYAsset、Serialization 及直接集成文档；旧 requirements 按完成和替代关系归档，不丢未决事项。
11. HTML 反映调整后的实现；SVG 离线核心流程图、局部结构图和全部 C# 文件职责索引，涵盖 Editor/Tests/生成文件。区分源码事实与运行验证。
12. 文档删除、合并、归档需先提交具体路径与去向清单，单独批准后才执行。

## Latest Correction Scope

2026-09-07 开发者要求重审前一轮偏移并继续自动化验收：HTML 不再以文件清单作为正文，改为系统地图、关键流程和按需源码索引；Editor 恢复分割线与层级；归档已完成的 20260906 旧计划。随后开发者明确取消 GUI 布局验收，要求用 AA/AB 实际构建与热更 Chain 证明核心功能；只允许 Local target，不执行外部发布和远端请求。

成功标准补充：流程图必须表达分支、泳道、提交点和失败回退，不能只是竖向步骤列表；217 个 C# 职责索引通过搜索弹窗按需访问，页面结尾不展示机械性长列表；桌面/移动渲染及交互自测有新鲜证据。

## Tasks

| ID | Task | Status |
|---|---|---|
| T1 | 基线、规则、逐文件清单 | 完成：基线与规则记录，无新增 review |
| T2 | Labels 与编辑器统一 | 完成：页面删除、candidate Labels、PlayMode 归 AB Config、共享分割线恢复；按最新要求不验收 GUI |
| T3 | Serialization 通用边界和验证 | 完成：工具解耦、业务归 Compat/Serialization、JSON presence 回归通过 |
| T4 | struct、BuildIndexData、BuildDiffEntry | 完成：类型/路径/调用点落地，比较规则回归通过 |
| T5 | 全量注释语义核查 | 完成：208 个手写 C# 旧术语/阶段叙述扫描，复杂契约中文，生成代码走模板 |
| T6 | 文档更新及归档清单 | 完成：当前文档对齐，20260906 旧计划已批准归档；snapshots 文档按未删除决定保留 |
| T7 | HTML 流程与全量文件索引 | 完成：系统地图、泳道流程、身份/事务边界；217 项按需 dialog；桌面/移动/交互自测 PASS |
| T8 | 集成验证、进度对齐 | 完成：AA/AB Chain、4 scenario、Serialization、solution build、diff-check 均通过 |

## Verification Safety

优先隔离场景；开发者随后明确授权 AA/AB 实际 Build Test Chain。实联只使用 LocalDirectory target，测试引擎在修改前快照项目与目标并在结束后恢复。仍禁止 Cloudflare、远端 URL、Player E2E 和清空既有输出。

## Evidence

- 开始时 `git status --short`：208 项；初始 FYAsset + Serialization C# 文件 223 个。
- `dotnet build XLuaHotfix.sln --no-restore -v:q`：0 errors，4 个 MSB3277 依赖冲突警告；本轮同步本地生成 csproj 的移动路径和新增入口，不把编译当运行验收。
- `node tests/scenario/serialization/run.mjs`：exit 0，复制、解析/default/比较/Build、class/struct嵌套空值共3组通过。
- `dotnet run --project tests/scenario/s3_resource_boundary/S3ResourceBoundaryTests.csproj --no-restore`：exit 0，S3 6/6 含 ExportBoundary。PublishTargetPanel 改为窗口注入 Apply URL 回调；AB PlayMode 从 Shared Settings 挪到 ABConfigPanel。
- HTML 重写验收：正文 6 个主题 section，无 `<details>` 文件墙；217 条唯一全路径全部存在，源码索引在 dialog 中按需查询。内置 selftest 驱动流程切换、VersionNumber 搜索与 dialog 开关，Chrome `--dump-dom` 得到 `data-selftest=pass`；1440×4200 与 390×5200 截图均 exit 0。源码 dialog 单独截图 `Temp/FYAssetModelingVerification/modeling-source-dialog.png`，搜索结果与详情区无重叠。
- 文档路径：README、docs、计划索引共检查 64 个本地 Markdown 链接，0 broken。
- Unity：强制 scripts refresh 后 Console 0 error。内存构建 AA/AB EditorWindow VisualTree，AA=331 elements/19 separators/5 ScrollView，AB=616/29/9。Unity 2022.3 batchmode EditMode 与 PlayMode 均生成 XML，`result=Passed`、`total=0`；只证明编译/发现完成，不是业务测试覆盖。
- 实际核心链：`AB/build/chain/20260907_133441_fdb8abbd` PASS，版本 2.0.1、51 artifacts；`AA/build/chain/20260907_133947_2098494b` PASS，版本 2.0.1、17 artifacts。两者 Full/Hotfix、Local publish/probe、project/target restore 全部成功，result/recovery 均记录 Completed/Restored。
- 实联修复 1：Player 脚本编译发现 PushTargetConfig 的 Editor 路径方法引用 `#if UNITY_EDITOR` 的 BuildPathManager；四个操作方法收进 UNITY_EDITOR，运行时仍保留序列化字段。
- 实联修复 2：AA BinaryOnly 构建只生成 AAManifest.bin，但 AABuildBackend 静态要求 JSON+Binary，导致 PublishFull 失败。AA/AB RequiredManifestFileNames 现按各自 ManifestOutputFormat 返回实际必需文件；AA Chain 重跑通过且失败轮 RestorationSucceeded=true。
- 最终回归：HotfixRuntimeStateMachine、S2RuntimeBoundary、RuntimeResource、S3ResourceBoundary、Serialization、SolutionBuild、DiffCheck 均 exit 0。
- 最新集成编译（Compat注册归属修正后）：exit 0，4 warnings / 0 errors；git diff --check exit 0。
- 注释门禁：208 个手写 C# 文件扫描阶段叙述、TODO/FIXME、旧类型和旧路径，只命中仍实际执行一次性 YAML 迁移的 `ABSettingsMigration`；该注释与实现一致。6 词以上纯英文注释为 0。
- 开发者确认：JSON 缺少 Version 对象字段拒绝，显式 0.0.0 合法。已在 PackageIndex/BuildIndex/AAManifest/ABManifest/baseline 读取边界检查字段存在。
- 已删除本轮 6 份多余 review 文件：fyasset-{build,diff,document-disposition,editor,runtime,serialization}-*20260907.md。保留 review/INDEX.md、archive/ 与 review-xluaframework-fyasset-20260905.md。
- 决策漂移门禁：Labels 页面/候选 Save-Cancel、Editor separator、Tools 业务隔离、FYAsset serializer 归属、旧 smoke/roundtrip 删除、VersionNumber struct、BuildIndexData/BuildDiffEntry 路径、输出/发布根隔离、当前文档术语、HTML 索引、review 范围和旧计划归档全部 PASS（15/15）。
