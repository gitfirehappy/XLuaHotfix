# 字段术语语义定义表（已确认）

> 确认日期：2026-04-27
> 原则：每个词汇在全代码库中含义唯一，不同概念不同词，同一概念同一词
> **⚠️ 已过时**: Tags/Labels 语义已被 `field-semantics-reference.md` v1.0.0 取代。Tags 现明确为纯下载策略标签，不从 Labels 自动聚合。

---

## 最终术语定义

### Labels（资产级标识标签）

**语义**：用户配置的、用于识别和查找资产的标签。全链路统一使用 `Labels`。

| 位置 | 字段名 | 层级 |
|------|--------|------|
| `CollectorGroup` | `Labels` | 配置层 — Group 级标签 |
| `Collector` | `Labels` | 配置层 — Collector 级标签 |
| `CollectedAssetInfo` | `Labels` | 中间产物 — Group.Labels ∪ Collector.Labels 去重 |
| `ManifestAssetEntry` | `Labels` | 序列化层 — 运行时按标签查询 |
| `RuntimeAssetEntry` | `Labels` | 运行时层 — HasLabel/HasAllLabels |
| `PackRuleContext` | `Labels` | PackByLabel 输入 |

**改名清单**（需执行）：
- `CollectorGroup.Tags` → `CollectorGroup.Labels`
- `Collector.Tags` → `Collector.Labels`

---

### Tags（Bundle 分包策略标签）

**语义**：Bundle 级别的大层面分包/下载策略标识（"必装"/"DLC-1"/"语音包"）。与资产 Labels 是完全不同的概念。

| 位置 | 字段名 | 层级 |
|------|--------|------|
| `ManifestBundleEntry` | `Tags` | Bundle 下载策略标签 |

**保持不变**：`ManifestBundleEntry.Tags` 命名正确，无需修改。

---

### GroupName（资产分组名）

**语义**：资产的构建分组标识。主要用于构建期的分组和打包（不同 Group 打出不同的 Bundle），运行时用途不大（仅诊断）。

| 位置 | 字段名 | 层级 |
|------|--------|------|
| `CollectorGroup` | `GroupName` | 配置层 |
| `CollectedAssetInfo` | `GroupName` | 中间产物 |
| `AddressRuleContext` | `GroupName` | 规则上下文 |
| `PackRuleContext` | `GroupName` | 规则上下文 |
| `GroupRuleContext` | `ParentGroupName` | 规则上下文（Collector直属父Group名） |
| `ManifestAssetEntry` | `GroupName` | 序列化层（改名） |
| `RuntimeAssetEntry` | `GroupName` | 运行时层（改名，仅诊断） |

**改名清单**（需执行）：
- `ManifestAssetEntry.Group` → `ManifestAssetEntry.GroupName`
- `RuntimeAssetEntry.Group` → `RuntimeAssetEntry.GroupName`

---

### BundleLogicalName（逻辑 Bundle 名）★ 暂定

**语义**：不含 Hash 和扩展名的三段式逻辑名（`{pkg}_{group}_{key}`）。

| 位置 | 字段名 | 层级 |
|------|--------|------|
| `CollectedAssetInfo` | `BundleLogicalName` | 中间产物（改名） |
| `BundleNameBuilder` | — | 构建工具（类名不变，方法输出语义明确） |

**★ 暂定，后续可能再讨论**。

---

### BundleName（Bundle 文件名）

**语义**：含 Hash 和 `.bundle` 扩展名的完整文件名（`{pkg}_{group}_{key}_{hash}.bundle`）。

| 位置 | 字段名 | 层级 |
|------|--------|------|
| `ManifestBundleEntry` | `BundleName` | 清单中的完整文件名 |
| `BundleDownloadItem` | `BundleName` | 下载列表中的文件名 |

**保持不变**。

---

### 其他一致性命名的术语（无冲突）

| 术语 | 语义 | 全链路一致性 |
|------|------|------------|
| `Address` | 运行时寻址键 | ✅ 全链路 Address |
| `CollectPath` | 采集根目录路径 | ✅ 全链路 CollectPath |
| `PackageName` | 包标识名 | ✅ 全链路 PackageName |
| `EntryId` | Unity GUID | ✅ 全链路 EntryId |
| `PrimaryType` | 资产主类型名 | ✅ 全链路 PrimaryType |

---

## 改名执行清单

### 立即（影响 E1-3 代码）
| 文件 | 改动 |
|------|------|
| `CollectorSetting.cs` | `CollectorGroup.Tags` → `Labels`, `Collector.Tags` → `Labels` |
| `CollectionScanner.cs` | `MergeTags` → `MergeLabels`, 所有 `.Tags` → `.Labels` |
| `Constants.cs` | 注释更新（RULE_PACK_BY_LABEL 注释从"相同 Labels"对齐） |
| `plan-E1-1.md` | Tags→Labels 术语更新 |

### 后续（独立 PR）
| 文件 | 改动 |
|------|------|
| `ManifestAssetEntry.cs` | `Group` → `GroupName` |
| `RuntimeAssetEntry.cs` | `Group` → `GroupName` |
| 所有引用处 | Group→GroupName 级联 |
| `CollectedAssetInfo.cs` | `BundleName` → `BundleLogicalName`（暂定） |
| 所有引用处 | 级联更新 |

---

## 变更日志

| 日期 | 变更 |
|------|------|
| 2026-04-27 | 初始版本：4 项决策确认 |
