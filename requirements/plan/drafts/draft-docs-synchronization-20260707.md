# Draft: Documentation Synchronization

**Date**: 2026-07-07  
**Status**: Draft  
**Category**: Maintenance / Documentation

## Problem Statement

项目文档（`context/` 目录下的知识库）严重落后于当前实现。经过多轮重构（E5～E11、R1～R2 等 Plan），代码的架构、接口和设计决策已发生大幅变化，但文档未同步更新，导致：

1. **知识库失真**：文档描述的旧架构与代码实际行为不一致，误导后续开发
2. **决策追溯困难**：重构原因、设计取舍未记录在文档中
3. **新成员理解成本高**：缺少准确的当前架构概览
4. **AI 辅助质量下降**：本工具依赖 `context/` 知识库做出判断，过时的知识库会影响建议准确性

## Scope of Outdated Documentation

根据近期实现变更，需要核查以下文档领域：

### 1. Build Pipeline Architecture

**可能过时的内容：**
- DAGScheduler 调度机制（E5 系列重构后）
- Task 依赖关系图（新增/删除 Task 后）
- AB/AA 双后端的切换机制（E10 后）
- BuildContext 数据流描述

### 2. Repository System

**可能过时的内容：**
- FileBuildRepository 内部存储结构（R1/R2 后）
- Commit/Rollback 流程描述
- Channel 概念和多渠道支持
- RepositoryPreviewRunner 工作机制

### 3. Version Management

**可能过时的内容：**
- VersionDataBase + VersionNumber 设计（E9 版本管理重构后）
- Build 字段与 DailyBuildCount 的关系
- Channel 参数的合法值范围

### 4. FYAsset Runtime Architecture

**可能过时的内容：**
- AssetPackageManager 的初始化流程
- ABPackageBackend vs AddressablesBackend 接口差异
- HandleRegistry 引用计数机制
- HotfixManager 热更流程

### 5. Editor Tool Panels

**可能过时的内容：**
- IBuildPipelinePanel 接口和各 Panel 职责
- RepositoryStatusPanel 的 Health Check 逻辑
- BuildProjectManager 的编排入口

## Proposed Approach

### Phase 1: Audit (识别差距)

逐一对比 `context/` 文档与当前代码，记录差距：

```
FOR each document in context/:
  1. 读取文档描述的模块/接口/行为
  2. 检索对应代码的当前实现
  3. 标记差异：
     - STALE: 内容已不准确（旧接口/旧行为）
     - MISSING: 文档未覆盖的重要新机制
     - OUTDATED_EXAMPLE: 代码示例已失效
```

**输出物：** `context/audit-2026-07.md` —— 审计差距清单

### Phase 2: Priority Update (按优先级补全)

根据差距清单，按影响面排序更新：

| Priority | Document Area | Reason |
|----------|---------------|--------|
| P1 | Repository System | R1/R2 大幅重构，最易误导 |
| P1 | Build Pipeline (DAG + Tasks) | E5 系列完全重写 |
| P2 | Version Management | E9 引入 Build 字段 |
| P2 | FYAsset Runtime | 接口变化但影响较小 |
| P3 | Editor Tool Panels | 工具类文档，变化频繁 |

### Phase 3: Design Decision Recording

补充缺失的设计决策说明（记录"为什么"）：

- 为什么 Repository 使用文件系统而非 Git？
- 为什么 DAGScheduler 选择拓扑排序而非线性 Task 链？
- AB 和 AA 双后端共存的设计取舍
- VersionNumber.Build 与 DailyBuildCount 分离的原因

## Document Update Standards

更新文档时遵循以下规范：

```markdown
<!-- 每份文档应包含 -->
# [Module Name]

**Last Updated**: YYYY-MM-DD  
**Code References**: [key files/symbols]  
**Related Plans**: [plan-Xxx, plan-Yyy]

## Current Architecture
[准确描述当前实现]

## Design Decisions
[说明关键设计取舍和原因]

## Known Limitations
[已知限制或待优化点]
```

## Effort Estimate

| Phase | Scope | Effort |
|-------|-------|--------|
| Phase 1: Audit | 扫描全部 context/ 文档 | 0.5 人日 |
| Phase 2: Update (P1 areas) | Repository + Pipeline | 1.5 人日 |
| Phase 2: Update (P2 areas) | Version + Runtime | 1 人日 |
| Phase 3: Design Decisions | 补充关键决策说明 | 0.5 人日 |
| **Total** | | **~3.5 人日** |

## Recommendation

**优先级：P2 (Medium)**  
建议在 E/R 系列 Plan 执行完毕后的稳定窗口期集中补全，而非零散穿插。

**触发条件（任意满足即应启动）：**
- 完成当前轮次所有 Plan 后
- 新成员加入时
- AI 工具反复给出基于旧架构的错误建议时

## Open Questions

1. `context/INDEX.md` 是否需要重组（增加模块分类）？
2. 是否引入文档版本号，便于追踪文档与代码的对应关系？
3. 是否建立"文档更新 Checklist"——每次 Plan 落地后必须同步更新对应文档？
