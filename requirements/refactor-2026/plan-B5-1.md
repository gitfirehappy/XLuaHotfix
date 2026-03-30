# Sub-Plan B5-1: 运行时条目模型与索引规则

> **风险**: 中
> **依赖**: B1 + B2 完成
> **状态**: ✅ 审批完成

---

## 目标

定义运行时资源条目的最小模型，明确 `Address / PrimaryType / Labels / EntryId / AutoAddress` 的语义边界，
为后续 Resolve / Load / 校验与迁移提供统一数据基础。

---

## 背景说明

当前项目的运行时索引仍偏向 Addressables 的「key 唯一」思维。
但本轮 ask 已确认：

- `Address` 允许重复
- `Group` 不参与运行时过滤
- V1 只保留 `PrimaryType`
- `Labels` 用于筛选与最终消歧，不承担主查询入口职责

因此必须先定义新的条目模型，否则 B5-2 的 Resolve / Load 合同无法稳定。

---

## 设计范围

| 主题 | 说明 |
|------|------|
| `EntryId` | 内部唯一身份，仅用于缓存、诊断、句柄归属 |
| `Address` | 逻辑名，允许重复，不再承担全局唯一身份 |
| `PrimaryType` | V1 唯一公开 Type 字段；默认自动推导，允许兼容性手改 |
| `Labels` | 无序唯一集合；匹配不区分大小写；显示保留原输入 |
| `Group` | 构建元数据；参与编辑器报表，不参与运行时过滤 |
| `SourcePath` | 编辑器定位与冲突报告信息 |
| `AutoAddress` | 标记 Address 是自动生成还是手动覆写 |

---

## 已确认规则

1. `Address` 可跨不同 `PrimaryType` 重名
2. 同一 `Address + PrimaryType` 可依赖不同 `Labels` 区分，但需要警告
3. `PrimaryType` 默认自动推导，允许手动修改，但必须**兼容实际类型**
4. 对 `ScriptableObject`，`PrimaryType` 必须使用**具体类名**，不能退化为 `ScriptableObject`
5. V1 **不做 `AdditionalTypes`**，多分类需求先全部走 `Labels`
6. 自动短名默认来源为**文件名去扩展**
7. 自动项可重建；手动覆写项保持锁定，除非显式切回 Auto
8. 路径只作为编辑器显示与定位信息，不作为正式运行时查询入口

---

## 计划任务

### 任务 1: 定义运行时条目最小字段集

- 明确 `EntryId / Address / PrimaryType / Labels / SourcePath / Group / AutoAddress` 的最小集合
- 明确哪些字段属于运行时必须字段，哪些字段仅用于编辑器诊断
- 明确 `PrimaryType` 与实际资源类型之间的兼容约束

### 任务 2: 定义 Address 自动生成与覆写策略

- 定义自动短名生成规则（短名来源、扩展策略）
- 定义「短名 + 类型后缀升级」的抽象规则
- 定义自动项重建与手动项保留合同

### 任务 3: 定义唯一性、警告与阻断边界

- 明确哪些冲突允许进入构建但必须警告
- 明确哪些冲突在手动扫描与构建阶段必须阻断
- 明确 `LabelSet` 的归一化方式和比较规则

---

## 保留项（必须通过）

- [x] `Group` 不进入运行时 Load / Resolve 查询参数
- [x] V1 不引入 `AdditionalTypes`
- [x] `Address` 允许重复这一核心方向不回退
- [x] 手动覆写 `PrimaryType` 时必须可一键恢复自动推导值

---

## 验收标准

- [ ] 能用一份条目模型完整表达一个运行时资源的逻辑身份、主查询类型、标签与编辑器定位信息
- [ ] 能明确区分阻断项与警告项，不再把 `Address` 当硬唯一键
- [ ] `PrimaryType` 与 `Labels` 的职责边界清晰，不引入 `AdditionalTypes` 冗余设计
- [ ] 自动 Address 与手动覆写、重建的关系可直接落地为编辑器逻辑

---

## 不在本次范围

- RawFile / 非 Unity 资源对象索引
- `AssetHandle<T>`、加载返回值、释放语义
- 批量 `Labels` 查询接口设计

---

## 审批清单

- [x] V1 是否只保留 `PrimaryType`？
  **决定**：是。自动推导 + 允许兼容性手改 + 支持一键重置；多分类先走 `Labels`。
- [x] `PrimaryType` 手改是否必须兼容实际类型？
  **决定**：必须兼容。
- [x] `Labels` 是否为无序唯一集合，且匹配时大小写不敏感？
  **决定**：是。内部归一化匹配，显示保留原始输入。
- [x] `Address` 是否允许跨不同 `PrimaryType` 重名？
  **决定**：允许。
- [x] `EntryId` 直接复用 Unity GUID，还是构建期生成自有内部 ID？
  **决定**：复用 Unity GUID。天然唯一稳定，运行时只做字符串比较，无需自建唯一性保证。
- [x] 自动 Address 升级具体格式，先定 `Filename_Type`、`Type_Filename`，还是继续只保留"短名 + 类型后缀升级"抽象规则？
  **决定**：`Filename_Type`（下划线分隔）。类型后缀在最后一段，从后向前解析。如 `player_idle` 升级为 `player_idle_Sprite`。
