# Sub-Plan A: UI 框架优化

> **状态**: 已审批，可执行
> **依赖**: 无（可独立执行）
> **子任务**: A1 UIAnimation 参数化 | A2 DynamicGroup 职责扩展 | A3 UIViewModel（可选）

---

## 现状分析

### 多 Canvas 协作（已有机制，需理解）

当前 UIManager 通过 UIResourceConfigSO 支持多 Canvas：

```
UIResourceConfigSO
  └─ UIRegistrationGroup[]          （每个对应一个场景 Canvas，如 MainCanvas / PopupCanvas）
       ├─ parentCanvasName           场景中 Canvas GameObject 的名称
       └─ UIGroupDefinition[]        Canvas 下的分组
            ├─ groupID               分组标识符（用于动态面板挂载）
            ├─ manualUIForms         静态面板预制体（直接注册）
            └─ additionalPreloadForms  模板面板预制体（预加载但不立即注册）
```

**当前多 Canvas 的限制**：
- 静态面板在初始化时通过 formToCanvasMap 绑定 Canvas，后续无法切换
- 动态面板通过 groupID -> canvasGroups 查找父 Canvas，但 groupID 当前只用于 buff 卡牌等「批量生成同类面板」场景
- 两套概念（静态 formToCanvasMap + 动态 canvasGroups）职责边界不够清晰

---

## 任务 A1: UIAnimation 参数化

### 修改思路

UIAnimation 的方法已有 duration 可选参数（如 FadeIn(form, callback, duration=0.5f)），
但 UIFormBase 调用时写死了，不会从 UIFormConfigSO 读取。

只需在 UIFormConfigSO 添加参数字段，UIFormBase 调用时传入即可。
UIAnimation.cs 基本不需要改动（方法签名已有参数）。

### 改动范围

| 文件 | 改动 |
|------|------|
| UIFormConfigSO.cs | 新增 [Header("动画参数")] 下 4 个字段 |
| UIFormBase.cs | OpenAnim / CloseAnim 中调用 UIAnimation 时传入对应参数 |
| UIAnimation.cs | 基本不动（方法已有参数）；FadeSlideIn 补充 fromOffset 参数化 |

### 新增字段（UIFormConfigSO）

```csharp
[Header("动画参数（0 = 使用内置默认值）")]
[Tooltip("淡入/淡出时长（秒）")]
public float fadeInDuration = 0f;
public float fadeOutDuration = 0f;

[Tooltip("缩放/弹入目标倍率（Pop/Zoom 动画）")]
public float zoomScale = 0f;

[Tooltip("Slide/FadeSlide 的初始偏移量（像素，0 = 使用屏幕宽/高）")]
public float slideOffset = 0f;
```

### 保留项

- [ ] 现有 UIFormConfigSO.asset 文件无需修改（新字段默认值 0 回退硬编码）
- [ ] UIAnimation 静态方法对外签名不变（调整内部参数传递）

---

## 任务 A2: DynamicGroup 职责扩展与明确

### 修改思路

当前 DynamicGroup 的设计出发点是「批量生成同类面板」（如 buff 卡牌），
但其机制本质上是「一组动态实例化面板，共享同一个 Canvas，由 groupID 标识」。
这个机制可以承载更广泛的场景，需要把职责说清楚并适度扩展。

### 当前 DynamicGroup 能力梳理

```
UIManager
  ├─ dynamicFormGroups: Dictionary<groupID, List<UIFormBase>>
  │   管理同一组内的所有动态面板实例
  ├─ canvasGroups: Dictionary<groupID, Canvas>
  │   每个 groupID 对应的父 Canvas
  ├─ CreateDynamicForm<T>()      实例化 + 注册到分组
  ├─ ShowDynamicForm()           显示（不入 showFormStack）
  ├─ HideDynamicForm()           隐藏
  ├─ ClearDynamicFormsInGroup()  批量清除分组
  └─ SetGroupPanelsAlpha()       批量设置透明度（高亮选中项）
```

**已有但不够明确的设计语义**：
- groupID 同时充当「Canvas 绑定键」和「实例分组键」，初始化配置时容易混淆
- additionalPreloadForms 字段名不直观（其实是「动态面板模板」）

### 改动范围

| 改动 | 说明 |
|------|------|
| UIResourceConfigSO.cs | 将 additionalPreloadForms 重命名为 dynamicFormTemplates（更语义化） |
| UIManager.cs | 添加 GetDynamicFormCount(groupID) 查询方法 |
| UIFormBase.cs | 添加 /// 注释明确 IsDynamicForm / DynamicGroupID 的使用场景 |

**注意**：字段重命名会影响现有 .asset 文件。方案是保留旧字段 + 添加 [FormerlySerializedAs] 特性，
Unity 序列化系统会自动迁移，不需要手动修改任何 .asset 文件。

### DynamicGroup 使用场景文档（写入代码注释）

```
场景 1（原有）: 批量生成同类面板
  如 buff 卡牌：一个 BuffCardGroup，对应 BuffCanvas，
  每张卡牌是一个 UIFormBase 实例，由 SO 注入数据

场景 2（扩展）: 列表/网格 UI
  如背包格子、技能栏图标，批量生成 + 统一管理显隐

场景 3（扩展）: Toast / 提示弹窗队列
  同一个 groupID 管理同类提示，ClearDynamicFormsInGroup 一键清除
```

### 多 Canvas 协作说明（明确边界，不改逻辑）

当前多 Canvas 支持已经完整，本次只补充文档和注释：
- **静态面板** → 使用 UIRegistrationGroup.parentCanvasName 绑定到指定 Canvas
- **动态面板** → 使用 groupID 在 canvasGroups 中查找父 Canvas（groupID 在 UIResourceConfigSO 中注册）
- 同一个 Canvas 可以有多个 groupID（UIGroupDefinition[] 是数组）

> **后续可扩展方向（不在本次范围，单独提 issue/需求）**：
> - 分组内排序（SortGroup）
> - 跨 Canvas 面板迁移
> - 分组容量限制
>
> 本次只做职责明确化 + 字段重命名 + 补文档注释。

### 保留项

- [ ] 动态面板创建/显隐/清除逻辑不变
- [ ] SetGroupPanelsAlpha 等现有方法不变
- [ ] 字段重命名使用 FormerlySerializedAs，.asset 文件自动迁移，无需手动修改

---

## 任务 A3: UIViewModel

本次不执行。后续有具体 ViewModel 需求时再添加。

---

## 审批清单

- [x] A1: slideOffset 字段是否需要分 X/Y 两个方向，还是统一一个值？**决定：统一一个 float 值，不分 X/Y。**
- [x] A2: additionalPreloadForms 重命名是否确认（影响已有 .asset，FormerlySerializedAs 自动迁移）？**决定：确认重命名为 dynamicFormTemplates，使用 FormerlySerializedAs 自动迁移。**
- [x] A2: 是否需要扩展 DynamicGroup 的其他能力（如分组内排序、跨 Canvas 迁移面板）？**决定：本次不扩展额外能力。明确职责、补注释即可。可扩展优化部分后续提单。**
- [x] A3: 是否纳入本次重构，还是后续按需添加？**决定：不纳入本次重构，后续按需添加。**
