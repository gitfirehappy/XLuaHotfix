# Draft: 新旧管线并存策略

> **Status**: ~~Draft~~ → **Archived 2026-05-18** — 第一层执行为 E13（plan-E13-legacy-sidebar.md），剩余内容被 `draft-build-repository-20260518.md` 吸收
> **目的**: 抬升旧管线（Legacy Addressables）在编辑器中的地位，与 AB 新管线长期并存，通过开关切换而非淘汰旧管线
> **与 E12 关系**: 独立草稿，与 E12（AB 管线编辑器工具）并行，互不阻塞
>
> ## 2026-05-15 讨论结论
>
> **分层执行策略**:
> - **第一层（可并行 E7）**: 侧边栏重组（LEGACY PIPELINE + AB PIPELINE 分组）+ LegacyPipelinePanel 骨架
> - **第二层（延后）**: 旧管线 Task 化（5 个薄 Task 包装现有逻辑走 DAGScheduler）——旧管线改动困难，留后期
> - **第三层（等 E7）**: Diff 面板 + 产物查看（IDiffPipeline 统一可视化）
> - **第四层（延后）**: 旧管线优化回填（SerializationUtility / FileHelper）
>
> **旧管线 Task 化决策**: 确认方案 A（引入 IBuildTask），好处是统一管理和可视化，但执行时机延后。
> **HelperBuildData**: 旧管线独有（AA 缺失数据的补充），新管线自研已包含。

---

## 核心原则

1. **旧管线代码不删除、不废弃** — `LegacyAddressableBuildBackend`、`DifferentialProcessor`、`BuildPathCustomizer` 等完整保留
2. **新工具面板兼容旧管线** — 编辑器面板不为 AB 独占，Legacy 管线也有对应的可视化和管理入口
3. **切换不混用** — `UseABBackend` 保持全局开关语义，不同时跑两条管线，产物格式各自独立
4. **旧管线参照新管线优化** — 新管线设计的优秀部分（序列化、文件 I/O、产物格式）回填到旧管线，但不动核心逻辑

---

## 决策记录

| # | 决策 | 结果 | 日期 |
|---|------|------|------|
| D1 | 侧边栏改组 | 拆分为 LEGACY PIPELINE + AB PIPELINE 两个独立分组，手风琴折叠导航 | 2026-05-14 |
| D2 | Collector 归属 | AB 独有，不在 Legacy 组出现 | 2026-05-14 |
| D3 | Legacy 面板组成 | 嵌入 Addressables Groups 原生窗口 + Diff 面板（新管线落地后） + 产物文件查看 | 2026-05-14 |
| D4 | 面板继承策略 | 暂定独立面板（LegacyPipelinePanel 与 PipelinePanel 平级），不做共享基类 | 2026-05-14 |
| D5 | 产物格式统一 | 参照新管线优化旧管线输出，但不改动 version_state（高风险区域） | 2026-05-14 |
| D6 | 旧管线工具迁移 | SerializationUtility 替换旧序列化；FileHelper 替换 System.IO 直调 | 2026-05-14 |
| D7 | Diff 面板 | 等新管线落地后再确定 Diff 面板的具体功能范围 | 2026-05-14 |
| D8 | 草稿定位 | 独立草稿，与 E12 计划并行不互相阻塞 | 2026-05-14 |

---

## 侧边栏重组方案

```
当前:                              目标:

SETTINGS                           SETTINGS
   Settings                           Settings
AB PIPELINE                        LEGACY PIPELINE  ← 新增（手风琴折叠）
   Collect Config                     Pipeline       ← 嵌入 Addressables Groups + 产物查看
   Collector                          Builder        ← 产物查看（兼容 version_state.json）
   Pipeline                           Diff           ← 新管线落地后
   Builder                         AB PIPELINE
MANAGE                                Collect Config
   Version                            Collector
                                      Pipeline       ← DAG + 构建触发
                                      Builder        ← 产物查看（ABManifest.json）
                                   MANAGE
                                      Version
```

- 侧边栏导航改为**手风琴折叠**（一次只有一个分组展开），适配更多面板按钮
- `UseABBackend=false` 时 AB PIPELINE 组灰出（保持现有 gating）
- `UseABBackend=true` 时 LEGACY PIPELINE 组灰出
- 面板实现 `IBuildPipelinePanel` 接口，通过 `BuildPipelineWindow.InitPanels()` 注册

---

## Legacy 管线面板规划

### LegacyPipelinePanel（占位名）

- 嵌入 Unity 原生 `Addressables Groups` 窗口（通过 `EditorWindow.GetWindow` 嵌入或 dockable 引用）
- 只读展示 Addressables Groups 列表、资源计数、Schema 配置摘要
- 不重复造轮子——直接引用原生窗口，改动画最小

### LegacyBuilderPanel（占位名）

- 复用 BuilderPanel 的目录扫描逻辑
- 自动检测 `version_state.json` → 用 Legacy 报告格式渲染
- 与 AB BuilderPanel 共享 `HotfixOutput/Packages/Build_*` 扫描入口

### LegacyDiffPanel（占位名）

- 新管线落地后再确定具体功能范围（D7）
- 预期范围：差异摘要视图、快照状态对比、ConfirmRelease / RestoreOriginalGroups 操作按钮、快照历史浏览

---

## 旧管线代码优化范围

### 已完成（E10 重构）

- `LegacyAddressableBuildBackend` 实现 `IBuildBackend` 接口
- `BuildProjectManager` 通过 `CreateBackend()` 路由到对应后端
- `ConfirmReleaseHotfix` / `ResetGroupsToOriginal` 在 AB 模式下跳过

### 待执行优化

| 优化项 | 范围 | 风险 |
|--------|------|------|
| SerializationUtility 替换旧序列化 | `DifferentialProcessor`、`BuildPathCustomizer` 中手写的 JSON 序列化改为 `SerializationUtility.WriteToFile` / `ReadFromFile` | 低——Serializer 已稳定 |
| FileHelper 替换 System.IO | `LegacyAddressableBuildBackend.OrganizeOutput`、`GenerateVersionState` 中的 `File.Exists`/`File.Delete`/`Directory.CreateDirectory` 改为 FileHelper API | 低——原子写入、跨平台 |
| 产物格式对齐 | 旧管线 `OrganizeOutput` 追加生成 `build_summary.txt`（不改动 version_state.json 逻辑） | 低——增量追加 |
| 死代码清理 | `BuildPathCustomizer` 中已不再调用的方法、`DifferentialProcessor` 中 E7 已覆盖的逻辑 | 中——需逐方法确认引用 |
| Editor UI 抽象 | 提取 `IBuildPipelinePanel` 的重复脚手架逻辑（顶栏布局、宿主生命周期）到工具类，但不强制共享基类 | 低 |

### 明确不动

- `version_state.json` 格式 —— 风险高，改动影响运行时热更下载链
- `DifferentialProcessor.PrepareHotfix` / `ReBuildSnapShots` 核心逻辑
- `Addressables BuildPlayerContent` 调用路径

---

## 产物格式对照

| 产物 | Legacy 当前 | Legacy 目标 | AB 管线 |
|------|------------|------------|---------|
| Bundle 清单 | `version_state.json`（BundleInfo 列表） | 保持不变 + 追加 `build_summary.txt` | `ABManifest.json` |
| 构建摘要 | 无独立摘要文件 | 追加 `build_summary.txt`（版本、平台、Bundle 数、总大小、验证计数） | `build_summary.txt` |
| Manifest | 无 | 可选追加 `ABManifest.json`（让 BuilderPanel 统一格式读取） | `ABManifest.json` |
| Hash | `BundleInfo.FileHash`（CRC32） | 保持不变 | `ManifestBundleEntry.Hash` |

---

## 与 E12 计划的边界

| 事项 | 归属 |
|------|------|
| PipelinePanel DAG 可视化 | E12（AB 独有） |
| PipelinePanel 构建触发 | E12-2（AB 独有，通过 `BuildProjectManager` 路由） |
| BuilderPanel 产物报告 | E12-3 → 扩展到本草稿（双格式兼容） |
| Legacy 面板创建 | 本草稿 |
| 旧管线代码优化 | 本草稿 |
| 侧边栏手风琴折叠 | 本草稿（`BuildPipelineWindow` 修改） |
| 产物格式统一 | 本草稿 |

---

## 待讨论/待定

1. LegacyPipelinePanel 嵌入 Addressables Groups 窗口的技术验证（`EditorWindow.GetWindow` 是否可嵌入到自定义面板区域）
2. Legacy Diff 面板的具体功能范围——等新管线（E5-2b/E6/E7）完全落地后再确定
3. 手风琴折叠导航的 IMGUI 实现细节
4. Legacy Pipeline 面板是否需要自己的 `Reload` / `Validate` 操作（对应 Addressables 的 `BuildPlayerContent` 校验）

---

## 变更日志

| 日期 | 变更 |
|------|------|
| 2026-05-14 | 初始建立——讨论新旧管线并存策略，确立 8 项决策 |
