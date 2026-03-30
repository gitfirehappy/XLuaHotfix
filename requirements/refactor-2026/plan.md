# Refactor Plan: XLuaHotfix 资源管理系统全面重构 — 总计划

> **状态**: 进行中（Phase 1 完成，Phase 2 审批中）
> **最终目标**: 全面替换 Addressables，自研运行时 + 构建期资源管理系统（参考 YooAsset 架构）
> **创建**: 2026-03-16
> **更新**: 2026-03-30 — 扩展为完整路线图，覆盖运行时 + 构建期 + 工具链

---

## 核心原则（所有子计划通用）

1. **不改动不必要代码** — 只重构明确列出的部分，其他文件不动
2. **不复杂化代码** — 新抽象层不得引入比现有实现更多的间接层级
3. **保留原有逻辑** — 每个方向都有明确的「保留项」，必须通过
4. **不改变大思路** — XLua 桥接体系 / SO 配置方式保留；热更构建管线渐进替换
5. **渐进替换** — Addressable API 逐步迁移，不做大爆炸式切换
6. **讲解优先** — 重构复杂逻辑时，代码注释中必须说明修改思路和原理
7. **///注释 + #region** — 所有新文件添加文档注释和 region 分隔，与现有代码一致

---

## 执行协议（强制）

```
1. 开发者批准子计划（确认审批清单）
   ↓
2. 执行子计划（按任务逐步实现）
   ↓
3. 执行完毕 → 讲解修改思路 → 请求开发者确认收工
   ↓
4. 开发者可随时提问，执行方负责解释
   ↓
5. 收工确认后 → 询问是否开启下一个子计划
   ↓
6. 不满意 → 调优当前子计划（回到步骤 2）
```

**没有开发者明确批准，不执行任何代码修改。**

---

## 完整路线图

### 阶段总览

```
Phase 1: 运行时抽象层 ✅
  B1 IAssetIndex → B2 IPackageBackend → B3 DialogueDataManager

Phase 2: 运行时合同层 ← 当前焦点
  B5-1 条目模型 → B5-2 Resolve/Load/Handle → B5-3 校验诊断 → B5-4 迁移策略

Phase 3: 运行时实现层
  B6 ABAssetIndex 实现 → B7 ABPackageBackend 实现 → B8 AssetHandle + 引用计数池

Phase 4: 热更核心链路
  B4 Catalog/Locator 替换 → B9 ABManifest 格式 + 增量下载适配

Phase 5: 构建期 — 资源收集与索引（参考 YooAsset）
  E1 收集器框架 → E2 打包规则 → E3 子目录收集器 + 忽略规则

Phase 6: 构建期 — 构建管线
  E4 依赖分析 → E5 构建管线重写 → E6 ABManifest 构建导出 → E7 差异快照适配

Phase 7: 原始文件与特殊资产
  F1 RawFile Bundle → F2 SpriteAtlas 联动 → F3 平台差异化压缩

Phase 8: 编辑器工具
  G1 可视化面板 → G2 依赖关系图 → G3 构建报告与预估

Phase 9: 高级运行时
  H1 AsyncOp 优先级调度器（待定）→ H2 LRU/LFU 缓存策略（延后）

Phase 10: 程序集拆分（最后）
  D0~D4 模块化拆分 + 胶水层
```

### 关键依赖关系

```
Phase 1 ──→ Phase 2 ──→ Phase 3 ──→ Phase 4
  (抽象层)    (合同层)    (实现层)    (热更核心)
                 │                      │
                 │ 条目模型格式          │ ABManifest 格式
                 ↓                      ↓
              Phase 5 ──→ Phase 6 ──→ Phase 7
              (构建收集)   (构建管线)   (特殊资产)
                              │
                              ↓
                          Phase 8 (编辑器工具)
                              │
                              ↓
                          Phase 9 (高级运行时)
                              │
                              ↓
                          Phase 10 (程序集拆分)
```

**注**: Phase 3 和 Phase 5 可部分并行（共享 Phase 2 定义的条目模型格式）。
Phase 4 和 Phase 6 需协同推进（ABManifest 运行时消费 + 构建期产出必须对齐）。

---

## 各 Phase 子计划文件索引

### Phase 1: 运行时抽象层 ✅

| 文件 | 内容 | 状态 |
|------|------|------|
| plan-B1.md | B1: IAssetIndex 资源索引层 | DONE |
| plan-B2.md | B2: IPackageBackend 资源加载层 | DONE |
| plan-B3.md | B3: DialogueDataManager 双模式 | DONE |

### Phase 2: 运行时合同层（当前焦点）

| 文件 | 内容 | 状态 |
|------|------|------|
| plan-B5.md | B5 总览 | ✅ 全部审批完成 |
| plan-B5-1.md | B5-1: 运行时条目模型 | ✅ 审批完成 |
| plan-B5-2.md | B5-2: Resolve/Load API + AssetHandle | ✅ 审批完成 |
| plan-B5-3.md | B5-3: 校验诊断工具 | ✅ 审批完成 |
| plan-B5-4.md | B5-4: 迁移路径与旧 API 淘汰 | ✅ 审批完成 |

### Phase 3: 运行时实现层

| 编号 | 内容 | 状态 |
|------|------|------|
| B6 | ABAssetIndex 实现（自研索引替代 AddressableLabelsConfig 运行时角色）| 待规划 |
| B7 | ABPackageBackend 实现（AB 包加载后端替代 AddressablesBackend）| 待规划 |
| B8 | AssetHandle + 引用计数池（Handle-first 释放、二次警告）| 待规划 |

### Phase 4: 热更核心链路

| 文件 | 内容 | 状态 |
|------|------|------|
| plan-B4.md | B4: Catalog/Locator 替换 | 概念阶段 |
| B9 | ABManifest 格式 + 增量下载适配 | 待规划 |

### Phase 5: 构建期 — 资源收集与索引

| 编号 | 内容 | 参考 | 状态 |
|------|------|------|------|
| E1 | 收集器框架 (Collector: Main/Static/Depend + Classifier) | YooAsset | 待规划 |
| E2 | 打包规则 (Collect/GroupBy/Pack 三规则分离) | YooAsset | 待规划 |
| E3 | 子目录收集器 + 忽略规则 (gitignore 风格) | YooAsset + 初步想法 | 待规划 |

### Phase 6: 构建期 — 构建管线

| 编号 | 内容 | 状态 |
|------|------|------|
| E4 | 依赖分析 + 静态资产 GCRoot | 待规划 |
| E5 | 构建管线重写 (替换 Addressables BuildScript) | 待规划 |
| E6 | ABManifest 构建导出 | 待规划 |
| E7 | 差异快照适配 (DifferentialProcessor 迁移) | 待规划 |

### Phase 7: 原始文件与特殊资产

| 编号 | 内容 | 状态 |
|------|------|------|
| F1 | RawFile Bundle 支持 | 待规划 |
| F2 | SpriteAtlas 联动刷新 | 待规划 |
| F3 | 平台差异化压缩策略 | 待规划 |

### Phase 8: 编辑器工具

| 编号 | 内容 | 状态 |
|------|------|------|
| G1 | 可视化资源管理面板 | 待规划 |
| G2 | 依赖关系图 | 待规划 |
| G3 | 构建报告与预估 | 待规划 |

### Phase 9: 高级运行时

| 编号 | 内容 | 状态 |
|------|------|------|
| H1 | AsyncOperation 优先级调度器 | 待定 |
| H2 | LRU/LFU 缓存策略 | 延后 |

### Phase 10: 程序集拆分

| 文件 | 内容 | 状态 |
|------|------|------|
| plan-D.md | D0~D4: 模块化拆分 + 胶水层 | 待审批（最后执行）|

---

## 已完成事项（非资源管理）

| 文件 | 内容 | 状态 |
|------|------|------|
| plan-C.md | Lua 脚本目录自动管理 | DONE (C1+C2)，C3 待 Plan-B 后 |
| plan-A.md | UI 框架优化 | DONE |

---

## 需求变更记录

| 日期 | 变更内容 |
|------|---------|
| 2026-03-16 | 初始版本：三方向重构 |
| 2026-03-16 | 添加 UIAnimation 可调淡入淡出时间 |
| 2026-03-16 | plan-B 扩展：分组标签 + catalog 机制，拆为 B1-B4 四个阶段 |
| 2026-03-16 | DialogueDataManager 保持独立双模式（Standalone 默认） |
| 2026-03-16 | plan-A 补充多 Canvas 协作说明 + DynamicGroup 职责扩展 |
| 2026-03-16 | 新增规则：重构复杂逻辑必须讲解思路，允许开发者随时提问 |
| 2026-03-17 | 审批完成：Plan-C/A/B1/B2 全部通过。A3 ViewModel 后续按需、DynamicGroup 不扩展只明确职责 |
| 2026-03-17 | Plan-B2 补充：需支持异步加载（LoadFromFileAsync）、路径策略为热更目录优先+fallback StreamingAssets |
| 2026-03-17 | Plan-C 补充：采用方案2（SO 分离+配置映射），LuaAutoSyncConfig 增加 outputDirectory 字段 |
| 2026-03-29 | 新增 Plan-B5：在 B4 前先稳定运行时资源条目模型、Resolve/Load 合同、Handle、校验与迁移策略 |
| 2026-03-30 | **路线图扩展**: 从「三系统重构」升级为「全面自研资源管理系统」。新增 Phase 3~10 覆盖运行时实现、构建期改造(参考YooAsset)、RawFile、编辑器工具、高级运行时、程序集拆分。Plan-D 调整为最后执行。LRU/LFU 延后，AsyncOp 调度器待定 |
