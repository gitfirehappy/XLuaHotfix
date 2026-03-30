# Sub-Plan B5-2: Resolve / Load API 与 AssetHandle 合同

> **风险**: 中
> **依赖**: B5-1 审批完成
> **状态**: ✅ 审批完成

---

## 目标

定义运行时 Resolve / Load API 的最终对外合同，明确：

- 严格查询与便捷查询的边界
- `AssetHandle<T>` 的返回模型
- 同步 / 异步一致性
- 结构化错误模型
- 旧 `LoadAssetAsync<T>(key)` 的兼容映射与后续淘汰方向

---

## 背景说明

一旦 `Address` 允许重复，现有 `LoadAssetAsync<T>(string key)` + `UnloadAsset(string key)` 模型就会出现两个问题：

1. **字符串查询不再等于唯一身份**
2. **释放语义不能再安全地只靠字符串 key**

因此本子计划需要把运行时合同改成：

- 解析（Resolve）先得到唯一条目或结构化错误
- 加载（Load）在唯一条目基础上返回 `AssetHandle<T>`
- 释放（Release）走 Handle-first

---

## 已确认规则

1. Resolve / Load 采用双轨语义：
   - `ByAddress`
   - `ByTypeKey`
2. `LoadByAddress<T>` 默认先 `ResolveByAddress<T>` 再 Load
3. `ResolveByTypeKey<T>` 默认合同：`Type + Key`，`Labels` 可选；不传 `Labels` 时多命中直接报错
4. 类型过滤默认沿用 Addressables 习惯：**assignable**；`Exact` 仅在 `Resolve API` 中提供
5. `LoadByAddressSync<T>` / `LoadByTypeKeySync<T>` 与异步版保持**完全一致**的解析 / 错误合同
6. 核心返回模型是 `AssetHandle<T>`
7. `AssetHandle<T>.Release()` 合同为：**幂等 + 二次警告**
8. 旧 `LoadAssetAsync<T>(key)` 先映射到 `LoadByAddress`

---

## 计划任务

### 任务 1: 定义 Resolve 结果模型

- 定义 `ResolvedEntry` 的字段边界
- 定义 `ResolveByAddress` / `ResolveByTypeKey` 成功与失败返回形态
- 定义冲突时的候选清单与建议过滤信息

### 任务 2: 定义 AssetHandle 合同

- 定义 `AssetHandle<T>` 的最小能力：`Asset / EntryId / Address / PrimaryType / IsValid / Release()`
- 明确 Handle 在释放后的可用性约束
- 明确 Handle 与内部缓存 / 引用计数的关系

### 任务 3: 定义 Sync / Async 与兼容层

- 对齐同步 / 异步入口的 Resolve / Load 顺序
- 定义旧 `LoadAssetAsync<T>(key)` / `LoadAssetSync<T>(key)` 的兼容映射
- 为后续淘汰 `UnloadAsset(string key)` 准备迁移边界

---

## 保留项（必须通过）

- [ ] 仍保留 `ByAddress` 查询入口，不强制所有调用改成 `ByTypeKey`
- [ ] `Exact` 只存在于 `Resolve API`，不把日常 Load API 复杂化
- [ ] 新 API 必须同时支持同步与异步
- [ ] 旧 API 在新 API 未验证前不得直接删除

---

## 验收标准

- [ ] `ResolveByAddress` / `ResolveByTypeKey` 能清晰区分唯一命中、未命中、冲突、类型不符等情况
- [ ] `LoadByAddress` / `LoadByTypeKey` 的成功与失败路径都能对应到结构化错误或唯一条目
- [ ] `AssetHandle<T>` 足以承担释放身份，不再把字符串 key 当作唯一卸载依据
- [ ] 同步 / 异步接口与兼容层关系清晰，可直接进入实现拆分

---

## 不在本次范围

- 批量构建校验与建议 Address 的编辑器实现
- RawFile / 非 Unity 资源加载接口
- B4 的 catalog / locator 替换

---

## 审批清单

- [x] 是否采用 `ByAddress` + `ByTypeKey` 双轨查询语义？
  **决定**：是。
- [x] `LoadByTypeKey<T>` 不传 `Labels` 时，多命中是否直接报错？
  **决定**：是；只有显式传 `Labels` 才参与最终消歧。
- [x] `Exact` 能力是否只放在 `Resolve API`？
  **决定**：是。
- [x] 返回模型是否以 `AssetHandle<T>` 为核心？
  **决定**：是；Handle 调试友好，释放采用幂等 + 二次警告。
- [x] Load 失败的结构化错误，对业务层采用 `Result` 风格、带 `ErrorCode` 的异常，还是两者并存？
  **决定**：Result 风格为主。AssetHandle 承担 Result 角色（IsValid + Error），需要时加 `.ThrowIfFailed()` 扩展方法，不作为主 API。资源加载失败是预期内错误，不用异常表达。
- [x] 批量 `Labels` 查询是否同时提供 `ResolveMany + LoadMany` 与直接 `LoadByLabels` 两层 API？
  **决定**：两套都保留（分层）。ResolveMany + LoadMany 为底层能力（校验工具和高级场景），LoadByLabels 为日常便捷封装（内部调底层）。
- [x] 旧 `UnloadAsset(string key)` 在哪个阶段标记为 `Obsolete`？
  **决定**：首批调用面迁移完成后标 Obsolete。新 API 经过 AAPackageManager 内部等首批调用面验证后再标记，稳妥且有编译器提醒。
