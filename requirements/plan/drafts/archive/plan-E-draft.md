# Phase 5-6 构建管线 — 方向草案

> 状态：📦 Archived 2026-05-07 — 全部方向决策已落地到 E1-1~E5-2b 精确子计划。保留作为方向收敛历史参考

---

## 🩺 2026-04-26 方向审计记录

| # | 偏移 | 严重度 | 修正 |
|---|------|--------|------|
| 1 | 三规则→两规则（GroupRule 消失） | 🔴 | 恢复 IGroupRule。三规则：IFilterRule→IGroupRule→IPackRule |
| 2 | Bundle 命名格式 `pkg_group_type_labels_hash` → `pkg_group_packKey` | 🟡 | E2 三段 minimal 成立 |
| 3 | E3 取消，归入 E1-3 | 🟡 | 引用全部修正 |
| 4 | 扫描管线缺 GroupRule 步骤 | 🟡 | 修正为 Classify→GroupRule→Address→Pack→Labels |
| 5 | Collector 数据结构缺字段 | 🟡 | 补齐 ForcePayloadKind/IgnorePatterns/Labels/GroupRuleName |

---

## 跨阶段架构决策（方向级，不重复精确计划内容）

### Pipeline Pattern + BuildContext

- 每个 Task 单一职责，新增不破坏现有
- 通过 BuildContext 共享数据（类型安全泛型容器）
- 后端级切换：Legacy Addressable ↔ AB。SO 配置默认 + 构建参数覆盖。启动后锁定

### 文件系统边界

- 5 类职责：统一路径 / 文件读写 / Hash 校验 / 缓存管理 / 解压
- 不包含：网络下载、热更流程、资源加载
- Android StreamingAssets 治理原则待专项讨论

### 规则体系

- **三规则**：IFilterRule（过滤）→ IGroupRule（路由到 Group）→ IPackRule（Bundle 分组键）
- **IAddressRule** 保留
- 所有规则实现并列可选，接口支持自定义扩展

### 统一管线 + 5 标准扩展点（F 系列）

所有资产走同一条管线。差异化通过 5 个扩展点注入：
1. IAssetImportRule（导入期）
2. Classifier→PayloadKind（分类期）
3. PackRule（打包期）
4. 自定义 IBuildTask（构建期）
5. IPackageBackend（运行期）

---

## G 方向收敛看板

| 方向 | 状态 | 已定 | 待定 |
|------|------|------|------|
| G1（E1+E2） | ✅ | 三规则；ECollectorType 与 PayloadKind 正交；AssetClassification 2 字段；PackRule 并列可选；命名 `pkg_group_packKey`；空标签 `$orphan`；Label join `--` | — |
| G2（E1-3+E4） | ✅ | E1-3 管归属（含 GroupRule 路由）、E4 管共享；依赖分析仅补写不覆盖；SharePolicy：AutoShare + NoShare，仅处理 ImplicitDependency | 冲突级别策略归入 E5 |
| G3（E5） | ✅ | DAG topological sort + deterministic batch execution (single-threaded)；IBuildTask 4 字段契约；6 骨干节点+扩展节点；HelperBuildData 已取消 | — |
| G4（E6+E7） | 收敛 | BuildContext 统一版本源；Digest 在快照；ConfirmRelease 固化；version_state 仅旧后端 | 回滚机制 |
| G5（E7） | 非优先 | 旧后端可停放，不强制退场 | — |
| G6（E8） | 延期 | 文件系统 5 类接口；异步统一 Task 语义 | Android StreamingAssets 治理 |

---

## 延期议题池

1. G6：Android StreamingAssets 治理原则
2. 子目录冲突级别策略（Dev 告警 / CI 阻断差异，归入 E5）
3. 回滚机制文档化

---

## 关联的精确子计划

| 子计划 | 文件 | 状态 |
|--------|------|:--:|
| E1-1 | plan-E1-1.md | ✅ Executed |
| E1-2 | plan-E1-2.md | ✅ Executed |
| E1-3 | plan-E1-3.md | ✅ Executed |
| E1-4 | plan-E1-4.md | 📋 Approved |
| E2 | plan-E2.md | ✅ Executed |
| E4 | plan-E4.md | 📋 Draft (12 decisions) |
| E5 | plan-E5.md | 📋 Parent container |
| E5-1 | plan-E5-1.md | 📋 Draft |
| E5-2 | plan-E5-2.md | 📋 Draft |
| E6 | plan-E6.md | 📋 Draft |
| R1 | plan-R1.md | 📋 Draft (error handling) |
| F-ideas | plan-F-ideas.md | 💡 Ideas |
| YooAsset gap | gap-analysis-yooasset.md | 📋 Discussion input |
