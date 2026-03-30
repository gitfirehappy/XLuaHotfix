# Sub-Plan B: AB 包管理替换 — 总览

> **状态**: 进行中（B1/B2/B3 已完成，B5 待审批，B4 概念阶段）
> **子文件**: plan-B1.md / plan-B2.md / plan-B3.md / plan-B5.md / plan-B4.md

---

## 背景与目标

将运行时 Addressable API 替换为自研 AB 包管理，保持 AAPackageManager 对外 API 不变。

**本次不动**：构建侧 Editor 代码（BuildProjectManager / DifferentialProcessor / HelperBuildDataExporter / SOAddressableTagger / LuaAddressableTagger）仍使用 AddressableAssetSettings API。

---

## 修改思路说明（供开发者理解）

### 为什么现在拆成「已落地阶段 + 运行时合同阶段 + 高风险阶段」？

Addressable 在项目中的使用，当前需要按 5 层理解，每层的替换风险不同：

```
[B1] 数据层 — AddressableLabelsConfig 提供 Label/Type -> Key 映射
     ↓ 依赖
[B2] 加载层 — Addressables.LoadAssetAsync / Release（AAPackageManager 封装）
     ↓ 依赖
[B3] 模块层 — DialogueDataManager 直接调用（设计为可插拔独立模块，保留双模式）
     ↓ 依赖
[B5] 合同层 — Runtime Entry / Resolve / Load / Handle / Validation
     ↓ 依赖
[B4] 热更核心 — CatalogUpdater（Catalog 重定向 + Locator 替换）
     最高风险，独立评估
```

B1 / B2 / B3 已经把“抽象层分离”做出来了，但还没把“运行时资源如何被唯一解析、加载、释放”定稳。
因此 2026-03-29 新增 B5：先稳定运行时合同，再决定是否推进 B4。

---

## 各阶段概览

| 阶段 | 文件 | 核心目标 | 风险 |
|------|------|---------|------|
| B1 | plan-B1.md | IAssetIndex 接口化资源索引层 | 低 |
| B2 | plan-B2.md | IPackageBackend + ABPackageBackend 资源加载 | 中 |
| B3 | plan-B3.md | DialogueDataManager 独立双模式（保留直接调用开关） | 低 |
| B5 | plan-B5.md | 运行时资源索引 / Resolve/Load / Handle / 校验 / 迁移 | 中 |
| B4 | plan-B4.md | Catalog 重定向层替换（高风险，独立评估） | 高 |

---

## 代码规范（所有阶段通用）

- 新文件添加 `///` 文档注释，与现有代码风格一致
- 使用 `#region` 分隔逻辑区块
- 修改复杂逻辑时，在代码注释中说明修改思路和原理

## 执行协议

每个子阶段执行完毕后：
1. 请开发者确认收工（功能验证 + 代码审阅）
2. 收工后询问是否推进下一阶段
3. 开发者有疑问可随时提问，执行方负责讲解
