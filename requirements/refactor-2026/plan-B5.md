# Sub-Plan B5: 运行时资源索引与 Resolve/Load 合同重构

> **状态**: ✅ 全部子计划审批完成，待执行
> **依赖**: B1 + B2 + B3 完成；B4 不在本轮执行范围
> **范围**: 仅运行时加载层（Index / Resolve / Load / Handle / 校验）
> **子文件**: plan-B5-1.md / plan-B5-2.md / plan-B5-3.md / plan-B5-4.md

---

## 背景与目标

B1 / B2 已把运行时资源管理抽象为 `IAssetIndex` 与 `IPackageBackend`，
但当前运行时仍沿用「单 key -> 单资源 -> 字符串卸载」的 Addressables 心智。

本轮讨论后，资源管理的目标已经明确偏离 Addressables 的默认假设：

- `Address` **允许重复**，不再承担全局唯一身份
- `Group` 只服务构建与收集，不进入运行时查询
- 运行时需要同时支持**严格查询**与**便捷查询**
- 释放语义需要从「按字符串 key 卸载」转向 **Handle-first**
- 后续 `ABPackageBackend` / `ABAssetIndex` 的实现，需要稳定的上层 Resolve / Load 合同作为地基

因此在进入 B4（Catalog / Locator / 热更核心链路）之前，先新增 B5：
把**运行时条目模型、Resolve/Load 合同、Handle 模型、编辑器校验与迁移路径**定清楚。

---

## 本轮范围

- 运行时资源条目索引模型
- `Address / PrimaryType / Labels / EntryId` 规则
- `ResolveByAddress` / `ResolveByTypeKey` / `LoadByAddress` / `LoadByTypeKey` 合同
- `AssetHandle<T>` 与结构化错误模型
- 手动扫描校验、构建硬拦、冲突报告、建议 Address
- 旧 API 兼容包装与迁移顺序

## 本轮明确不做

- 不改 `HotfixManager` / `CatalogUpdater` / `NetworkDownloader` 执行逻辑
- 不把 `Group` 引入运行时过滤
- 不把 RawFile / 非 Unity 资源加载接口纳入本轮执行
- 不直接进入 B4 的 catalog / locator 重写

---

## 已收敛的设计共识

1. `Address` 允许重复；内部唯一身份使用 `EntryId`
2. `Group` 只服务构建与收集，不进入运行时 Resolve / Load API
3. V1 Type 模型仅保留 `PrimaryType`；`ScriptableObject` 资源使用**具体类名**而不是 `ScriptableObject`
4. `Labels` 为**无序唯一集合**，匹配时大小写不敏感，展示保留原始输入
5. Resolve / Load 采用双轨语义：
   - 严格：`ByAddress`
   - 便捷：`ByTypeKey`（`Labels` 可选）
6. Load API 以 `AssetHandle<T>` 为核心；释放采用 **Handle-first**
7. 旧 `LoadAssetAsync<T>(key)` 先映射到 `LoadByAddress`，待新 API 稳定后再迁移旧接口
8. 校验策略为：**手动扫描 + 构建硬拦**；同一 `Address + PrimaryType` 靠 `Labels` 区分时允许但警告

---

## 子计划总览

| 子计划 | 文件 | 目标 | 风险 |
|------|------|------|------|
| B5-1 | plan-B5-1.md | 定义运行时条目模型、Address/Type/Label/EntryId 规则 | 中 | ✅ 审批完成 |
| B5-2 | plan-B5-2.md | 定义 Resolve/Load API、AssetHandle、错误模型与兼容层 | 中 | ✅ 审批完成 |
| B5-3 | plan-B5-3.md | 定义手动扫描、构建校验、冲突报告与建议 Address 工具 | 中 | ✅ 审批完成 |
| B5-4 | plan-B5-4.md | 定义迁移路径、旧 API 淘汰条件与落地顺序 | 中 | ✅ 审批完成 |

---

## 推荐顺序

`B5-1 -> B5-2 -> B5-3 -> B5-4`

待 B5 全部稳定后，再决定是否推进 B4。

---

## 与 B4 的关系

B4 解决的是 `catalog / locator / 热更核心链路` 替换，风险高。

B5 先把「运行时如何识别一个资源、如何解析查询、如何加载和释放」定义清楚。
这样无论底层继续走 `AddressablesBackend`，还是未来切到 `ABPackageBackend`，
`AAPackageManager` 上层合同都不会再反复摇摆。

---

## 已解决的细化问题（2026-03-30 审批完成）

- ✅ 自动 Address 升级格式：`Filename_Type`（下划线分隔，类型后缀在最后）
- ✅ 批量 Labels 查询：两套都保留（分层）— ResolveMany + LoadMany 底层 + LoadByLabels 便捷封装
- ✅ 结构化错误：Result 风格为主 — AssetHandle 承担 Result 角色，需要时加 `.ThrowIfFailed()` 扩展
- ✅ 编辑器建议 Address：首期仅生成候选列表，一键写回后续增强
- ✅ EntryId：复用 Unity GUID
- ✅ 构建硬拦：先独立 precheck，等构建管线重构后接入主流程
- ✅ 首批迁移调用面：AAPackageManager 内部
- ✅ UnloadAsset Obsolete 时机：首批调用面迁移完成后
