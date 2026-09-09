# AB 运行时加载

> **关联代码** | [AB/Runtime](../../Assets/FYAsset/Scripts/AB/Runtime/) · [Compat 资源门面](../../Assets/FYAsset/Scripts/Compat/AssetPackageManager.cs)

ABPackageManager 是具体入口。Editor PlayMode 可以通过 EditorPackageBackend 使用 AssetDatabase；Runtime 使用 ABManifest、ABPackageBackend 和 ABBundleLoader。Compat 门面启动时绑定一次 AA/AB，不另外持有资源缓存。

## 初始化与索引

ABManifestLoader 按当前包目录、StreamingAssets 顺序尝试，每个目录优先 Binary 再 JSON；Standalone 的内置 fallback 位于 `StreamingAssets/Standalone`。全部失败返回 null。不要把 Loader 的格式回退与热更后端精确包检查混为一谈，后者可能在存在的 Binary 损坏时直接失败。

ABAssetIndex 将 ManifestAssetEntry 转成 RuntimeAssetEntry，建立 Address、Address/PrimaryType、EntryId 查询缓存。返回的对象或数组由索引持有，不应在调用方修改。RuntimeAssetEntry 不携带 BundleIndex；后端通过 Manifest 查 Bundle 归属。

## 三类 API

| API | 返回与释放 |
|---|---|
| LoadAssetAsync/Sync | `(T asset, RuntimeMessage error)`；与同 Address、T 的 UnloadAsset 配对 |
| LoadByAddress/LoadByTypeKey | AssetHandle<T>；以 Retain/Release 管理所有权 |
| LoadRawBytes/LoadRawText | 字节或文本；读取 RawFile，不持有 Unity AssetBundle Handle |

普通 tuple API 不分配 HandleRegistry slot。不要在仍持有 Handle 时绕过它直接 UnloadAsset，否则可以释放其他持有者仍在使用的缓存资源。公开 API 不是统一“绝不抛异常”的边界，具体 I/O、Unity 提取和 Raw API 仍需按实现处理异常。

## 获取流程

1. AssetResolver 根据 Address、PrimaryType、Labels 和 Payload 收敛候选。
2. Backend 以 EntryId 查缓存；异步加载按 EntryId 合并正在进行的请求。
3. 缓存未命中时通过 Manifest 找到 Bundle，再由 BundleLoader 获取依赖及目标 Bundle。
4. 使用 SourcePath 调用 Unity 的 AssetBundle.LoadAsset/LoadAssetAsync；SourcePath 不只是诊断字段。
5. 成功缓存资源。Handle API 额外创建 registry slot，并登记后端 UnloadByEntryId 回调。

Resolver 的类型匹配主要基于类型名和有限的 Object/ScriptableObject 回退，不是任意 CLR assignability。TypeKey 只有唯一候选时可直接返回；不能假定所有分支都再次应用 Labels。

## 生命周期

| 层 | 计数或所有权 |
|---|---|
| HandleRegistry slot | 一次分配及显式 Retain 的引用数 |
| EntryId 活跃 slot 集合 | 最后一个 slot 释放才调用资源释放回调 |
| Backend 资源缓存 | 每 EntryId 缓存一次提取结果，不是每次命中都重新获取 Bundle |
| BundleLoader | 直接获取与依赖结构引用；归零才 Unload(true) 并释放依赖 |

普通 struct 复制不调用 Retain。复制两个变量后分别 Release，并不自动得到两个独立所有权。Generation 保护普通槽复用，但不能因此宣称任意复制释放或 Reset 后句柄都绝对安全。HandleRegistry.Reset 不执行资源释放回调。

BundleLoader 的 single-flight 合并同名物理请求。Finalize 先完成缓存或失败依赖补偿，再移除进行中记录，最后唤醒等待者。递归路径集合用于防环，不是整个依赖图永久去重。UnloadAllBundles 不等于取消所有进行中的请求。

## 失败与平台边界

提取为空或异步提取异常会尝试归还所获取的 Bundle；同步提取没有完全相同的异常封装。依赖加载失败需释放本次取得的依赖引用。同步接口不能等待 UnityWebRequest 路径完成，不能把桌面磁盘测试推广为 Android 同步加载支持。

完整流程图和文件索引见 [HTML 建模文档](./fyasset-modeling.html)。纯 .NET stub 测试可验证引用与时序规则，不替代真实 Unity AssetBundle、场景切换或 Player 验收。
