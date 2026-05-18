# Draft: BuildGraph 可视化（BuilderPanel 升级）

> **Date**: 2026-05-13
> **Status**: Promoted → [plan-E12-buildgraph-editor.md](../plan-E12-buildgraph-editor.md) (2026-05-13)
> **Depends on**: E10 (IBuildBackend 已落地), E5-1 (DAGScheduler 已落地)
> **Scope**: AB 新管线专属。Legacy 模式下 AB PIPELINE 组整组灰显，后续独立设计 Addressable 区
> **技术方案**: Unity GraphView API（嵌入式）

> **Promotion note (2026-05-13)**: The executable scope was narrowed during approval. `plan-E12-buildgraph-editor.md` authorizes only E12-1: read-only DAG visualization + Validate. Drag-line editing, task mutation, build triggers, and real-time build status remain future separately approved slices.

---

## 目标

将 BuilderPanel 从占位符升级为 DAG 可视化编辑器，类似 UE 蓝图 / NodeCanvas 风格：
- 节点表示 IBuildTask，连线表示依赖和数据流
- 可视化编辑 Task 启用状态和 SO 级依赖
- 实时显示构建进度和节点状态
- 触发构建（Full / Hotfix）

---

## 已收敛设计决策

| # | 决策 | 结论 |
|---|------|------|
| D1 | 渲染技术 | **嵌入式 GraphView**（VisualElement 嵌入 BuilderPanel 区域） |
| D2 | Port 设计 | **双 Port**：执行流 Port（左入右出）+ 数据流 Port（下入上出） |
| D3 | 连线类型 | **3 种**：不可变执行流（骨干 Task 代码级 DependsOn）/ 可编辑执行流（SO 级 DependsOn）/ 只读数据流（WriteKeys→ReadKeys 自动匹配） |
| D4 | 编辑能力 | **完整编辑**：Enable/Disable Task + 拖线添加/删除 SO 级依赖 + 拖线时实时环路/冲突检测 |
| D5 | 运行时状态 | **协程实时**：DAGScheduler 新增 IEnumerator ExecuteAsync()，EditorApplication.delayCall 驱动，每 Task 完成后刷新节点着色 |
| D6 | 适用范围 | **仅 AB 新管线**。UseABBackend=false 时整组灰显 |
| D7 | Legacy 行为 | 灰显，后续独立设计 Addressable 区 |

---

## 连线类型详细设计

| 连线类型 | 语义 | 来源 | 可编辑 | 视觉样式 |
|---------|------|------|--------|---------|
| 执行流（不可变） | 骨干 Task 间固定顺序 | `IBuildTask.DependsOn`（代码） | ❌ | 粗实线，灰色 |
| 执行流（可编辑） | 用户自定义 Task 依赖 | `TaskEntry.DependsOn`（SO） | ✅ 拖拽添加/右键删除 | 粗实线，蓝色 |
| 数据流（只读） | 数据生产→消费 | WriteKeys→ReadKeys 自动匹配 | ❌ | 细虚线，按 Key 着色 |

**拖线验证逻辑**：
- 环路检测：拖线前实时检查是否形成循环，禁止则红色提示
- Write-Write 冲突：两个 Task 写同一 Key → 红色高亮
- Read-before-Write 警告：读的 Key 无前置 Task 写入 → 黄色警告

---

## 节点设计

```
┌─────────────────────────────────────┐
│ ● (exec-in)   TaskBuildBundles   (exec-out) ● │
├─────────────────────────────────────┤
│ 📥 Reads:                           │
│   ○ BuildConfig                     │ ← data-in ports
│   ○ CollectedAssets                 │
│   ○ BundleDependencyGraph           │
│ 📤 Writes:                          │
│   ○ BundleBuildResults              │ ← data-out ports
├─────────────────────────────────────┤
│ Status: ● Success  Duration: 12.3s  │ ← 运行时状态
└─────────────────────────────────────┘
```

**节点状态着色**：

| 状态 | 颜色 | 触发条件 |
|------|------|---------|
| Pending | 灰色 | 构建未开始 / 未轮到 |
| Ready | 黄色 | 入度为 0，即将执行 |
| Running | 蓝色 | 正在执行 |
| Success | 绿色 | 执行成功 |
| Failed | 红色 | 执行失败 |
| Skipped | 橙色 | 因前置 Fatal 失败跳过 |
| Disabled | 半透明 | TaskEntry.Enabled = false |

---

## DAGScheduler 协程化改动

```csharp
// 新增方法（保留原 Execute 作为同步兼容入口）
public IEnumerator<BuildTaskResult> ExecuteAsync(BuildPipelineConfig config, BuildContext ctx)
{
    // ... 验证逻辑同 ExecuteInternal ...
    while (remaining > 0)
    {
        var ready = GetReadyTasks(indegree);
        foreach (var task in batch)
        {
            var result = task.Execute(ctx);
            taskResults.Add(result);
            UpdateIndegrees(task, indegree, successors);
            yield return result;  // ← 每个 Task 完成后 yield
        }
    }
}

// 原方法保留为同步入口
public BuildResult Execute(BuildPipelineConfig config, BuildContext ctx)
{
    var async = ExecuteAsync(config, ctx);
    while (async.MoveNext()) { }
    return BuildResultFrom(taskResults);
}
```

改动量：~15 行。

---

## 文件结构

```
Assets/FYAsset/Scripts/Build/Editor/BuildGraph/
├── BuildGraphView.cs           ← GraphView 主体（缩放/平移/背景网格/节点管理）
├── BuildGraphWindow.cs         ← 嵌入 BuilderPanel 的 VisualElement 容器
├── BuildTaskNode.cs            ← 节点渲染（双 Port + 状态着色）
├── BuildGraphEdge.cs           ← 3 种连线样式
├── BuildGraphToolbar.cs        ← 工具栏（Validate / Build Full / Build Hotfix）
├── BuildGraphLayoutEngine.cs   ← 分层拓扑自动布局
└── BuildGraphSerializer.cs     ← 节点位置持久化
```

改动文件：
- `DAGScheduler.cs` — 新增 ExecuteAsync()
- `BuilderPanel.cs` — 替换占位符为 GraphView 容器

---

## 分期实施

### Phase 1：基础图形化 + 自动布局

| Task | 内容 | 依赖 | 估算 |
|------|------|------|------|
| P1-T1 | `BuildGraphView.cs` — GraphView 基础框架（缩放/平移/背景网格） | — | ~60 行 |
| P1-T2 | `BuildTaskNode.cs` — 节点渲染（TaskName + 执行流 Port + 数据流 Port） | T1 | ~100 行 |
| P1-T3 | `BuildGraphLayoutEngine.cs` — 分层拓扑布局（按 DAG 层次自动排列） | T2 | ~80 行 |
| P1-T4 | `BuildGraphWindow.cs` — 嵌入 BuilderPanel 的 VisualElement 容器 | T1 | ~40 行 |
| P1-T5 | 从 BuildPipelineConfig + BuildTaskResolver 读取 Task 列表，生成节点和连线 | T2, T3 | ~60 行 |
| P1-T6 | 3 种连线视觉区分（不可变执行流/可编辑执行流/数据流） | T5 | ~40 行 |

**交付物**：打开 BuilderPanel 能看到 DAG 图，节点自动布局，连线正确显示。只读。

### Phase 2：交互编辑 + 验证

| Task | 内容 | 依赖 | 估算 |
|------|------|------|------|
| P2-T1 | 右键菜单 Enable/Disable Task → 同步到 BuildPipelineConfig SO | P1 | ~40 行 |
| P2-T2 | 拖线添加 SO 级依赖 + 环路检测实时反馈 | P1 | ~60 行 |
| P2-T3 | 删除 SO 级依赖连线 | P2-T2 | ~20 行 |
| P2-T4 | `BuildGraphToolbar.cs` — Validate 按钮 → DAGScheduler.Validate() → 冲突高亮 | P1 | ~50 行 |
| P2-T5 | Write-Write 冲突红色高亮 + Read-before-Write 黄色警告 | P2-T4 | ~30 行 |
| P2-T6 | `BuildGraphSerializer.cs` — 节点位置持久化 | P1 | ~40 行 |

**交付物**：可编辑 DAG 图，拖线有实时验证，Validate 按钮显示冲突。

### Phase 3：构建触发 + 实时状态

| Task | 内容 | 依赖 | 估算 |
|------|------|------|------|
| P3-T1 | DAGScheduler 新增 `ExecuteAsync()` IEnumerator 方法 | — | ~15 行 |
| P3-T2 | 工具栏 Build Full / Build Hotfix 按钮 → BuildProjectManager | P2 | ~30 行 |
| P3-T3 | EditorApplication.delayCall 驱动协程，每步更新节点状态着色 | P3-T1, P3-T2 | ~40 行 |
| P3-T4 | 节点状态着色（6 种状态）+ 耗时显示 | P3-T3 | ~30 行 |
| P3-T5 | 构建完成后结果摘要 | P3-T3 | ~20 行 |

**交付物**：点击 Build 触发构建，节点实时变色，构建完成显示结果。

---

## 总估算

| Phase | 新文件 | 改动文件 | 估算行数 |
|-------|--------|---------|---------|
| P1 | 5 | 1 (BuilderPanel) | ~380 行 |
| P2 | 1 | 2 (BuildGraphView, BuildTaskNode) | ~240 行 |
| P3 | 0 | 3 (DAGScheduler, Toolbar, Node) | ~135 行 |
| **合计** | **6** | — | **~755 行** |

---

## 不变量

1. `UseABBackend = false` 时 BuilderPanel 灰显，不显示 GraphView
2. 骨干 Task 的代码级 DependsOn 连线不可编辑
3. SO 级依赖编辑实时同步到 BuildPipelineConfig.asset
4. 拖线时环路检测阻止非法连接
5. DAGScheduler.Execute() 同步入口行为不变（向后兼容）
6. `dotnet build XLuaHotfix.sln` 0 errors

---

## Out of Scope

- Legacy Addressables 管线可视化（后续独立设计 Addressable 区）
- 自定义 IBuildTask 的代码生成/模板
- 节点内参数编辑（Task 参数通过 BuildPipelineConfig SO 编辑）
- MiniMap / 搜索框（后续增强，GraphView 内置支持）
- 多选批量操作

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-13 | Initial draft — 7 design decisions, 3 phases, 17 tasks, 6 new files |
| 2026-05-13 | Promoted to `plan-E12-buildgraph-editor.md`; first executable slice narrowed to read-only graph visualization and validation. |
