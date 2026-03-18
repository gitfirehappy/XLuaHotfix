# Sub-Plan B2: 资源加载层接口化（IPackageBackend）

> **风险**: 中
> **依赖**: B1 完成后执行
> **预计改动文件**: 4 个新文件 + 1 个现有文件
> **状态**: 已完成 (2026-03-18)

---

## 修改思路（为什么要做这一步）

AAPackageManager 当前直接调用 Addressables.LoadAssetAsync / Release，
加载逻辑与 Addressables 绑定在一起。

**方案**：提取 IPackageBackend 接口，
将 Addressables 调用封装进 AddressablesBackend，
新增 ABPackageBackend 实现自研 AB 加载。
AAPackageManager 只通过接口调用，运行时可切换后端。

这一步不影响热更流程（CatalogUpdater / HotfixManager），只替换资源加载部分。

---

## 改动范围

| 文件 | 改动类型 | 说明 |
|------|---------|------|
| 新建 IPackageBackend.cs | 新增 | 资源加载后端接口 |
| 新建 AddressablesBackend.cs | 新增 | 从 AAPackageManager 提取现有 Addressables 实现（含引用计数缓存） |
| 新建 ABPackageBackend.cs | 新增 | 自研 AB 加载：依赖树 + 引用计数 |
| 新建 ABBundleLoader.cs | 新增 | AB 依赖链加载核心逻辑 |
| AAPackageManager.cs | 修改 | 内部加载改为 IPackageBackend，添加 SetBackend() |

---

## IPackageBackend 接口

```csharp
/// <summary>
/// 资源包加载后端接口
/// 隔离 Addressables 或自研 AB 的底层实现，AAPackageManager 通过此接口加载/卸载资源
/// </summary>
public interface IPackageBackend
{
    #region 初始化

    /// <summary> 初始化后端（加载 Manifest 或等待 Addressables.InitializeAsync） </summary>
    Task InitializeAsync();

    #endregion

    #region 资源加载

    /// <summary> 异步加载资源 </summary>
    Task<T> LoadAssetAsync<T>(string key) where T : UnityEngine.Object;

    /// <summary> 同步加载资源（用于 Lua require 等必须同步的场景） </summary>
    T LoadAssetSync<T>(string key) where T : UnityEngine.Object;

    #endregion

    #region 资源卸载

    /// <summary> 卸载资源（实现类负责引用计数管理） </summary>
    void UnloadAsset(string key);

    #endregion

    #region 查询

    /// <summary> 检查资源是否存在 </summary>
    bool ContainsKey(string key);

    #endregion
}
```

---

## AAPackageManager 修改说明

**核心变化**：内部加载/卸载调用改为通过 IPackageBackend，对外 API 完全不变。

```csharp
// 新增后端字段（默认 Addressables 后端）
private IPackageBackend _backend = new AddressablesBackend();

// 新增切换接口（供启动时配置）
public void SetBackend(IPackageBackend backend) { _backend = backend; }

// LoadAssetAsync 内部调用改为：
var result = await _backend.LoadAssetAsync<T>(key);

// UnloadAsset 内部调用改为：
_backend.UnloadAsset(key);
```

**引用计数**：现有 ResourceEntry + ReferenceCount 逻辑移入 AddressablesBackend 内部，
ABPackageBackend 自行实现引用计数。AAPackageManager 不再直接持有 _resourceCache。

---

## ABPackageBackend 核心设计

```csharp
/// <summary>
/// 自研 AB 包加载后端
/// 实现 AB 依赖树加载、引用计数缓存、同步/异步双模式
/// </summary>
public class ABPackageBackend : IPackageBackend
{
    // AB 包缓存（bundle path -> AssetBundle）
    private readonly Dictionary<string, AssetBundle> _bundleCache = new();

    // 引用计数（bundle path -> 引用数）
    private readonly Dictionary<string, int> _refCounts = new();

    // Key -> bundle path 映射（由 ABAssetIndex 提供）
    private readonly ABAssetIndex _index;

    // 说明：加载一个资源时，先查 _index 找到所在 bundle，
    // 再递归加载该 bundle 的所有依赖 bundle（_refCounts 递增），
    // 最后 LoadAsset<T>() 从 bundle 中加载。
    // 卸载时引用计数减 1，为 0 时真正卸载 bundle。
}
```

---

## ABBundleLoader 核心逻辑

ABBundleLoader 负责 AB 包的实际 I/O，同时提供同步和异步两条路径：

- **同步**：`AssetBundle.LoadFromFile(path)` — 用于 Lua require 等必须阻塞的场景
- **异步**：`AssetBundle.LoadFromFileAsync(path)` — 用于 `LoadAssetAsync<T>` 调用链，避免主线程卡顿

**路径解析逻辑**：优先检查 `PathManager.CurrentGUIDRoot`（热更目录），若文件不存在则 fallback 到 `Application.streamingAssetsPath`。

```csharp
// 路径解析示意
string ResolveBundlePath(string bundleName)
{
    var hotfixPath = Path.Combine(PathManager.CurrentGUIDRoot, bundleName);
    if (File.Exists(hotfixPath)) return hotfixPath;
    return Path.Combine(Application.streamingAssetsPath, bundleName);
}
```

---

## 保留项（必须通过）

- [ ] AAPackageManager 所有公开方法签名不变
- [ ] HotfixManager / XLuaLoader / NetworkDownloader 无需修改
- [ ] 默认后端为 AddressablesBackend，不传 SetBackend 时行为与重构前完全一致

---

## 验收标准

- [ ] 编译通过
- [ ] 使用 AddressablesBackend（默认）时，所有资源加载行为与重构前一致
- [ ] 使用 ABPackageBackend 时，能正确加载带依赖的 AB 包
- [ ] 引用计数正确：同一资源 Load 2 次 + Unload 1 次后仍保持加载状态

---

## 审批问题

- [x] ABPackageBackend 的 AB 包路径是读 StreamingAssets 还是热更目录（PathManager.CurrentGUIDRoot）？
  **决定**：热更目录（PathManager.CurrentGUIDRoot）优先，fallback 到 StreamingAssets。当前路径管理和包体隔离机制已经完善，无需修改根路径逻辑。如未来需要写入游戏安装目录，只需修改根路径即可。
- [x] SetBackend 切换时机：GameLauncher 启动时，还是需要运行时动态切换？
  **决定**：GameLauncher 启动时一次性配置，运行期间不支持动态切换。
- [x] ABBundleLoader 是否需要支持异步加载（LoadFromFileAsync），还是同步即可？
  **决定**：需要支持 LoadFromFileAsync。项目中使用了异步 AA 加载 API，AB 后端也需要对应支持异步。ABBundleLoader 需要同时提供 LoadFromFile（同步）和 LoadFromFileAsync（异步）两个方法。
