# Sub-Plan B5-4: 迁移路径与旧 API 淘汰策略

> **风险**: 中
> **依赖**: B5-1 + B5-2 + B5-3 审批完成
> **状态**: ✅ 审批完成

---

## 目标

把 B5 的落地顺序拆清楚：

- 先加什么
- 后改什么
- 旧接口何时兼容、何时淘汰
- 哪些模块作为首批迁移对象

在不破坏现有运行时行为的前提下，逐步把 `AAPackageManager` 从「字符串 key 驱动」迁到「Resolve + AssetHandle 驱动」。

---

## 背景说明

本项目已经完成 B1 / B2 / B3，说明：

- 抽象层已经分离到位
- 但大多数运行时调用仍然停留在旧的 `LoadAssetAsync<T>(key)` / `UnloadAsset(key)` 心智

如果直接一步切掉旧 API，风险较大；
如果长期双轨并存，又会让调用面持续分裂。

因此本子计划专门定义迁移节奏与淘汰条件。

---

## 已确认规则

1. 新 API 需要先跑通，再开始迁移旧 API
2. 新 API 必须同时支持 Sync / Async
3. 旧 `LoadAssetAsync<T>(key)` 先映射到 `LoadByAddress`
4. `Handle-first` 是目标方向，不能回退到长期字符串卸载
5. B4 不在本轮执行范围，迁移计划不得偷偷夹带热更核心链路改动

---

## 计划任务

### 任务 1: 规划新增阶段

- 定义 `ResolvedEntry` / `AssetHandle<T>` / 新 Resolve / Load API 的引入顺序
- 定义旧 API 包装层应该放在哪一层
- 定义哪些接口先保留兼容外壳

### 任务 2: 规划替换阶段

- 定义首批迁移调用面
- 定义何时迁移 `UnloadAsset(string key)` 相关调用
- 定义批量 `ByLabel(s)` 接口与新批量 API 的衔接时机

### 任务 3: 规划淘汰阶段

- 定义旧 API 标记 `Obsolete` 的时机
- 定义何时允许删除兼容外壳
- 定义验证新 API 稳定的观察标准

---

## 保留项（必须通过）

- [x] 在新 API 未验证前，不删除旧 API
- [x] 迁移阶段不同时推动 B4 高风险链路修改
- [x] 兼容层行为必须透明，不能在失败时做过多隐式猜测
- [x] 迁移计划必须明确"先验证，再替换"而不是直接全量切换

---

## 验收标准

- [ ] 新增、替换、淘汰三个阶段的边界清晰
- [ ] 能说明哪些调用面先迁，哪些后迁，哪些必须等批量 API 定案后再动
- [ ] `LoadAssetAsync<T>(key)`、`LoadAssetSync<T>(key)`、`UnloadAsset(string key)` 的去向明确
- [ ] 迁移计划不把 B4、RawFile 或其他未定范围偷偷混进来

---

## 不在本次范围

- 具体 `ABPackageBackend` / `ABAssetIndex` 实现
- B4 的 catalog / locator 替换
- RawFile API 迁移

---

## 审批清单

- [x] 迁移是否坚持「新 API 跑通后再迁旧 API」？
  **决定**：是。
- [x] 新 API 是否一开始就同时支持 Sync / Async？
  **决定**：是。
- [x] 旧 `LoadAssetAsync<T>(key)` 是否先映射到 `LoadByAddress`？
  **决定**：是。
- [x] 第一批替换调用面，先从 `AAPackageManager` 外壳、`XLuaLoader`，还是其他模块开始？
  **决定**：AAPackageManager 内部先行。它是所有调用方的统一入口，改内部实现外部无感知，是验证新 API 的最佳位置。
- [x] 旧 `LoadAssetByLabel(s)` / `UnloadAssetByLabel(s)` 是否等 B5-2 批量 API 定案后再迁？
  **决定**：是。首批迁移先做单资源路径（ByAddress / ByTypeKey），批量路径等 ResolveMany + LoadMany + LoadByLabels 实现后再迁。
- [x] `UnloadAsset(string key)` 在哪一阶段标记为 `Obsolete`？
  **决定**：同步 B5-2 决定 — 首批调用面迁移完成后标 Obsolete。
