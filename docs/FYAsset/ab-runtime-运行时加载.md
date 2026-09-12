# AB 运行时加载

> **关联代码** | [AB/Runtime](../../Assets/FYAsset/Scripts/AB/Runtime/) · [Compat 资源门面](../../Assets/FYAsset/Scripts/Compat/AssetPackageManager.cs)

`ABPackageManager` 是 AB 的唯一 concrete 入口。Editor PlayMode 通过 `EditorPackageBackend` 走 AssetDatabase；Runtime 使用 `ABManifestLoader`、`ABAssetIndex`、`ABPackageBackend`、`ABBundleLoader` 与 `ABSceneLoader`。Compat 门面启动时绑定一次 AA/AB，不另外持有资源缓存。

---

## 单一激活包根

所有运行时读取都相对 `RuntimePathManager.ActivePackageRoot`：

- `ABManifestLoader.LoadAsync` 只读激活包根一个目录，同目录内优先 `ABManifest.bin`，其次 `ABManifest.json`；两者都不可用时返回 null 并输出错误。
- `ABBundleLoader` 的物理路径同样只相对激活包根解析，**不做逐文件回退 StreamingAssets**。
- `ABPackageBackend` 读取 RawFile 时也从激活包根推导内容文件路径。
- 激活根由激活流程显式切换：`RuntimePathManager.SwitchToNewBuild(...)`（本地热更包）或 `ActivateBuiltInPackage()`（内置包），两者互斥，后调用者生效。退化到内置包只改写 `ActivePackageRoot`，不改写 `CurrentGUIDRoot` 与 `Mode`。

不存在“Manifest 来自 Local、单个内容文件回退 BuiltIn”的混合读取。

---

## 初始化与索引

`ABPackageManager.InitializePackageAsync()`：

1. Editor PlayMode 下走 `InitializeEditorPlayMode()`：`EditorVirtualManifestBuilder` 用 Collector 配置在内存里生成 `ABManifest`，经 `EditorPackageBackend` 用 AssetDatabase 加载（没有 BundleLoader）。
2. 否则 `ABManifestLoader.LoadAsync()` → `ABBundleLoader` → `ABPackageBackend`。
3. `InitializeFromManifest` 建立 `ABAssetIndex`；索引不可用（公共 Address 重复）时拒绝初始化。
4. 场景加载与资源加载共用同一个 `ABBundleLoader`，否则两者会各自持有缓存与引用计数。

`ABAssetIndex` 初始化时只对 `IsPublic = true` 且 Address 非空的条目建立索引：

```text
Address → ManifestAssetEntry（大小写不敏感且唯一）
PrimaryType → Address[]
Label → Address[]（大小写不敏感）
EntryId → 条目（含隐式依赖条目，仅供加载与诊断）
```

- 公共 Address 重复时 `BuildError` 非 null、`IsValid` 为 false，且**清空全部查询索引**，不再保留可用的地址集合。
- 隐式依赖条目只保留 EntryId 索引：业务无法用 Address/Type/Label 查询到它们。
- 查询返回的数组是索引持有的内部缓存，调用方不得修改。

---

## 公共 API

| 类别 | 方法 | 返回与释放 |
|---|---|---|
| 查询 | `ContainsAddress(address)` | 公共 Address 是否存在（大小写不敏感） |
| 查询 | `GetAddressesByType<T>()`、`GetAddressesByLabel(label)`、`GetAddressesByTypeAndLabel<T>(label)` | 只返回 `IReadOnlyList<string>`（Address） |
| 单资源 | `LoadByAddress<T>(address)`、`LoadByAddressSync<T>(address)` | `AssetHandle<T>`；失败句柄内联错误，不占 Registry 槽位 |
| 批量 | `LoadByType<T>()`、`LoadByLabels<T>(labels)`、`LoadByTypeAndLabels<T>(labels)` | `IReadOnlyList<AssetHandle<T>>`，**逐项独立成败** |
| RawFile | `LoadRawBytesAsync(address)`、`LoadRawTextAsync(address, encoding = null)` | `byte[]` / `string`；直接读内容文件，不持有 AssetBundle Handle |
| Scene | `LoadSceneAsync(address, LoadSceneMode mode, bool activateOnLoad = true)` | `SceneHandle` |
| 生命周期 | `InitializePackageAsync()`、`Shutdown()` | 见下文 |

解析层 `AssetResolver` 按请求形态校验内容类型：

- `ResolveByAddress` 只接受 `SerializedObject`；
- `ResolveRawByAddress` 只接受 `RawFile`；
- `ResolveSceneByAddress` 只接受 `Scene`；
- 类型不符返回 `INVALID_PAYLOAD_KIND` 结构化错误，不做跨类型回退。

**不做 Type 消歧，也没有 Object/ScriptableObject 回退分支**：公共 Address 唯一，候选集天然只有一个。

`LoadByLabels` 不按类型过滤，可能包含请求类型 T 之外的资源，这些条目的 handle 会携带引用类型不匹配的失败信息；需要限定类型时用 `LoadByTypeAndLabels`。

已删除的 API：`LoadByTypeKey`、无 Handle 的 `LoadAsset*`、按 Address 强制卸载入口。

---

## Handle token

```csharp
AssetHandle<T> handle = await manager.LoadByAddress<T>("Icon");
AssetHandle<T> second = handle.Retain();   // 同一 EntryId 的独立 token
handle.Release();                          // 只消费当前 token 一次
second.Release();
```

- 一次 Load 或一次 Retain 对应一个**独立 token**（Registry 槽位），没有引用计数对象。
- 普通 struct 复制**不增加所有权**：复制体与原件是同一个 token，谁先 Release 谁生效，另一次是静默 no-op。
- `default(AssetHandle<T>)` 永远无效；构造失败的句柄 `HandleId = 0`，只携带内联错误。
- `Release()` 重复调用或对过期 token 调用都是 no-op：不抛异常、不改变计数、不会误扣同 EntryId 其他 token 的释放权。
- Registry 按 EntryId 统计活跃 token，**最后一个 token 释放**时才回调 `_backend.UnloadByEntryId`。
- 槽位回收时 `Generation` 只增不减，过期句柄的 `(tokenId, Generation)` 不会再次命中。

`HandleRegistry` 同时统计 `ActiveCount = AssetActiveCount + SceneActiveCount`，供热更 Apply 门禁使用。

---

## Scene 加载与释放

Unity 2022.3 的官方约束与对应实现：

```text
Address
→ 公共 Scene 资源条目（`ManifestAssetEntry`，ResolveSceneByAddress）
→ Scene ContentEntry
→ 加载内容依赖与 Scene Bundle
→ AssetBundle.GetAllScenePaths() 取得完整 ScenePath（与条目 SourcePath 文件名比对校验）
→ SceneManager.LoadSceneAsync(scenePath, mode)
→ SceneHandle
```

| 规则 | 实现 |
|---|---|
| `SceneManager.UnloadSceneAsync` 不释放 AssetBundle | 内容引用由 `ABSceneLoader` 持有，只在场景确认卸载后调用 `ABBundleLoader.UnloadBundle` |
| Scene 槽位不携带 Registry 释放回调 | 释放时机由场景真正卸载完成决定，不能由 token 释放时机决定 |
| Additive | 调用方通过 `SceneHandle.UnloadAsync()` 卸载；该方法先消费自己的 token，再卸载场景，最后由加载器释放内容引用 |
| Single | 新 Scene 激活后确认旧 Scene 卸载，再结算旧 `SceneHandle`（确认窗口有帧数上限） |
| `activateOnLoad = false` | 有界预载窗口（帧数上限）后激活，不无限等待，避免把 AsyncOperation 长期停在队列里 |
| `Resources.UnloadUnusedAssets` | 只在场景切换这类安全入口显式调用，不放在每次 Handle 释放里 |

`SceneHandle` 与 `AssetHandle<T>` 的所有权语义一致：一次 LoadScene 或一次 Retain 对应一个独立 token，普通复制不增加所有权，重复 Release 幂等，`default(SceneHandle)` 永远无效。

---

## Shutdown 与 Handle 门禁

`ABPackageManager.Shutdown()`：

1. 未初始化时直接返回 null。
2. `HandleRegistry.ActiveCount > 0` 时返回 `ACTIVE_HANDLES_REMAIN` 结构化错误（含 Asset/Scene 计数），并**保持现状**：不释放、不清计数。此时卸载内容会让调用方手里的句柄指向已销毁对象。
3. 成功时先 `UnloadAllContent()` + `_sceneLoader.Clear()`（此时已无活跃 Handle），再 `HandleRegistry.Reset()`，随后清空索引与后端，允许重新 `InitializePackageAsync()`。

`HandleRegistry.Reset()` 自身也拒绝在仍有活跃 token 时执行，并打印按 EntryId + Kind 汇总的泄漏来源（Top N）；成功时递增全部槽位的 Generation，使 Shutdown 之前取得的句柄副本在重新初始化后不会命中新 token。

---

## Bundle 缓存与释放顺序

| 层 | 计数或所有权 |
|---|---|
| `HandleRegistry` 槽位 | 一次 Load 或显式 Retain 的 token |
| EntryId 活跃 token 集合 | 归零才回调后端卸载内容 |
| `ABPackageBackend` 资源缓存 | 每 EntryId 缓存一次提取结果 |
| `ABBundleLoader` | 直接依赖与结构引用计数；归零才 `AssetBundle.Unload(true)` 并递归释放依赖 |

`ABBundleLoader` 对同名物理请求做 single-flight 合并：leader 负责依赖与物理加载，followers 共享同一请求结果；finalize 先完成缓存或失败补偿，再移除进行中记录，最后唤醒等待者。递归路径集合用于防环，不是整个依赖图永久去重。`UnloadAllBundles` 不等于取消所有进行中的请求。

释放顺序：最后一个资源 token 释放 → 后端卸载该 EntryId 的资产与内容引用 → Bundle 引用计数归零 → Unload Bundle 与依赖。Scene 的释放顺序相反：场景卸载完成 → 释放内容引用。

---

## RawFile

`ABPackageBackend.LoadRawBytesAsync` 直接从激活包根读取内容文件，**不经过 BundleLoader**：RawFile 是普通物理文件，不伪装成 Bundle，也不参与 Bundle 卸载。`LoadRawTextAsync` 在字节结果上按 `encoding`（默认 UTF-8）解码。

---

## 失败与平台边界

- 提取为空或异步提取异常会尝试归还所获取的 Bundle；同步提取没有完全相同的异常封装。
- 依赖加载失败需释放本次取得的依赖引用。
- 同步接口不能等待 UnityWebRequest 路径完成，不能把桌面磁盘测试推广为 Android 同步加载支持。
- 公开 API 不是统一“绝不抛异常”的边界：具体 I/O、Unity 提取和 Raw API 仍需按实现处理异常。

完整流程图和文件索引见 [HTML 建模文档](./fyasset-modeling.html)。纯 .NET stub 测试可验证引用与时序规则，不替代真实 Unity AssetBundle、场景切换或 Player 验收。
