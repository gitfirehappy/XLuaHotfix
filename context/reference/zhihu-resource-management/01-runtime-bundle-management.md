---
title: Runtime Bundle Management System
source: Zhihu Column "游戏资源管理" by 伽蓝之洞, Chapters 8-11
status: verified
---

# Runtime Bundle Management System

This document covers the complete runtime resource management pipeline: concurrent bundle loading with reference counting, an asset-level handle system with automatic lifecycle binding, editor simulation mode via AssetDatabase, and exception handling with timeout control.

---

## Chapter 8: BundleManager -- Concurrent Loading, Deduplication, and Reference Counting

### Problem Statement

Direct use of `LoadFromFileAsync` in business logic is impractical. Three concrete problems must be solved:

- **Dependency resolution**: loading model A must automatically load its dependent material bundle B, or materials go missing.
- **Frame-rate stability**: loading 100 files simultaneously causes mobile frame drops. A queuing system is required.
- **Duplicate loading prevention**: two code paths requesting the same bundle must not trigger two disk reads. The second requestor should await the first.

### Data Structure: BundleInfo (Reference-Counted Wrapper)

`BundleInfo` pairs an `AssetBundle` with a refcount. The `Release()` method returns `true` when the count reaches zero, signaling it is ready for physical unload.

```csharp
namespace YY
{
    public class BundleInfo
    {
        public string Name;
        public AssetBundle Bundle;
        public int RefCount;

        public BundleInfo(string name, AssetBundle bundle)
        {
            Name = name;
            Bundle = bundle;
            RefCount = 0;
        }

        public void Retain() => RefCount++;

        // Returns true when count drops to zero -- caller should unload
        public bool Release() => --RefCount <= 0;
    }
}
```

**Design rationale**: The struct-like simplicity is deliberate. `BundleInfo` is not responsible for the actual unload -- it only answers "should I be unloaded?" The caller (`BundleManager.UnloadBundle`) decides what to do with that signal. This single-responsibility separation prevents entangled lifecycle logic.

### Data Structure: RequestScheduler (Concurrency Limiter)

`RequestScheduler` enforces a maximum concurrency cap (typically 10). It uses `TaskCompletionSource<bool>` as a lightweight queuing primitive -- lighter than coroutine locks and integrates naturally with async/await.

```csharp
namespace YY
{
    public class RequestScheduler
    {
        private int _maxConcurrency;
        private int _currentRunning;
        private Queue<TaskCompletionSource<bool>> _queue = new Queue<TaskCompletionSource<bool>>();

        public RequestScheduler(int max) => _maxConcurrency = max;

        public async Task WaitSlot()
        {
            if (_currentRunning < _maxConcurrency)
            {
                _currentRunning++;
                return;
            }
            var tcs = new TaskCompletionSource<bool>();
            _queue.Enqueue(tcs);
            await tcs.Task;
        }

        public void ReleaseSlot()
        {
            if (_queue.Count > 0)
            {
                var nextTask = _queue.Dequeue();
                nextTask.SetResult(true);
            }
            else
            {
                _currentRunning--;
            }
        }
    }
}
```

**Key insight -- `TaskCompletionSource` as a semaphore gate**: When slots are full, `WaitSlot` creates a TCS and enqueues it. The caller `await`s the TCS, freezing at that line. `ReleaseSlot` dequeues the next waiter and calls `SetResult(true)`, which unblocks the awaiting coroutine. This is essentially a cooperative semaphore built on tasks rather than OS primitives.

**Why not a SemaphoreSlim?** SemaphoreSlim requires the same thread or async context to pair Wait/Release correctly. With deeply nested async calls (dependencies loading dependencies), the TCS-queue pattern gives explicit control over who gets unblocked next, avoiding priority inversion.

### Core Manager: BundleManager

Three key internal dictionaries:

- `_loadedBundles`: Dictionary of loaded `BundleInfo` -- the hot cache.
- `_inflightTasks`: Dictionary of in-progress load `Task<BundleInfo>` -- critical for deduplication.
- `_manifest`: `AssetBundleManifest` for dependency resolution.

#### Entry Point: LoadBundleAsync

This is the primary public API. The logic follows a strict three-step check:

```csharp
public static async Task<BundleInfo> LoadBundleAsync(string bundleName)
{
    // Step 1: Hot cache hit -- increment refcount and return immediately
    if (_loadedBundles.TryGetValue(bundleName, out BundleInfo info))
    {
        info.Retain();
        return info;
    }

    // Step 2: In-flight deduplication -- if another caller is already loading,
    // await their task instead of starting a duplicate
    if (_inflightTasks.TryGetValue(bundleName, out var task))
    {
        info = await task;
        if (info != null) info.Retain();
        return info;
    }

    // Step 3: Initiate actual load, register in-flight task
    var tcs = LoadBundleInternalAsync(bundleName);
    _inflightTasks.Add(bundleName, tcs);

    try
    {
        info = await tcs;
        if (info != null) info.Retain();
        return info;
    }
    finally
    {
        // Always clean up the in-flight record, success or failure
        _inflightTasks.Remove(bundleName);
    }
}
```

**Critical design element -- `_inflightTasks`**: This single dictionary solves the "N callers, 1 disk read" problem. In scenarios like scene transitions or UI initialization where dozens of components request the same shared resource (e.g., a UI font atlas) within milliseconds, only one IO operation executes. All subsequent callers await the same task and receive the same result. The `finally` block ensures the dictionary entry is removed even on failure, preventing permanent deadlock for that bundle name.

#### Internal Load: LoadBundleInternalAsync

This method handles dependency resolution and concurrency queueing. The use of `Task.WhenAll` for parallel dependency loading is a significant simplification over coroutine patterns.

```csharp
private static async Task<BundleInfo> LoadBundleInternalAsync(string bundleName)
{
    // A. Load all dependencies first (recursive)
    if (_manifest != null)
    {
        string[] deps = _manifest.GetAllDependencies(bundleName);
        if (deps.Length > 0)
        {
            var depTasks = new List<Task<BundleInfo>>(deps.Length);
            foreach (var dep in deps)
            {
                depTasks.Add(LoadBundleAsync(dep));
            }
            // Parallel dependency load -- all deps load concurrently
            await Task.WhenAll(depTasks);
        }
    }

    // B. Queue for IO slot (blocks here until concurrency allows)
    await _scheduler.WaitSlot();

    try
    {
        // Double-check: a concurrent task may have completed while we were queued
        if (_loadedBundles.TryGetValue(bundleName, out var info))
            return info;

        // C. Perform actual disk read
        string path = Path.Combine(_basePath, bundleName);
        AssetBundle bundle = await AssetBundle.LoadFromFileAsync(path);

        if (bundle == null) return null;

        var newInfo = new BundleInfo(bundleName, bundle);
        _loadedBundles[bundleName] = newInfo;
        return newInfo;
    }
    finally
    {
        // E. Always release the IO slot
        _scheduler.ReleaseSlot();
    }
}
```

**Why a double-check after `WaitSlot`?** While waiting in the scheduler queue, another task loading the same bundle name may complete and insert into `_loadedBundles`. Without the double-check, the second task would re-read the file redundantly. This is the classic double-checked locking pattern adapted for async cooperative concurrency.

**Why `Task.WhenAll` for dependencies?** Dependencies are independent of each other -- material bundle B and texture bundle C both need to load before model bundle A, but B and C can load concurrently. `Task.WhenAll` enables this parallelism without manual coroutine bookkeeping. Each dependency recursively calls `LoadBundleAsync`, so transitive dependencies are automatically resolved through the same mechanism.

#### Unload: UnloadBundle

Unloading propagates recursively through dependencies. When a bundle's refcount reaches zero, the system attempts to cascade-unload its dependencies as well.

```csharp
public static void UnloadBundle(string bundleName)
{
    if (_loadedBundles.TryGetValue(bundleName, out BundleInfo info))
    {
        if (info.Release())
        {
            info.Bundle.Unload(true);
            _loadedBundles.Remove(bundleName);
            Debug.Log($"Unloaded: {bundleName}");

            // Cascade: check if dependencies can also be unloaded
            if (_manifest != null)
            {
                string[] deps = _manifest.GetAllDependencies(bundleName);
                foreach (var dep in deps)
                {
                    UnloadBundle(dep);
                }
            }
        }
    }
}
```

**The `Unload(true)` call**: Passing `true` to `AssetBundle.Unload` means "also destroy all loaded asset objects." This is the aggressive cleanup path. The recursive cascade through dependencies ensures no orphaned bundles remain. However, this approach assumes callers have already nullified their references to assets from these bundles -- if not, Unity objects will become missing references.

### Chapter 8 Summary

| Concern | Mechanism | Key Type |
|---------|-----------|----------|
| Refcounting | `BundleInfo.Retain()` / `Release()` | `BundleInfo` with `int RefCount` |
| Deduplication | Await inflight task instead of re-loading | `Dictionary<string, Task<BundleInfo>> _inflightTasks` |
| Concurrency | TCS queue with capacity cap | `RequestScheduler` with `Queue<TaskCompletionSource<bool>>` |
| Dependencies | Recursive `LoadBundleAsync` + `Task.WhenAll` | `AssetBundleManifest.GetAllDependencies` |
| Unload cascade | Recursive `UnloadBundle` on dependency chain | Propagates through manifest dependency edges |

---

## Chapter 9: AssetSystem -- Handle Pattern and Automatic Lifecycle Binding

### Design Philosophy: Why a Handle Instead of Raw T?

Returning a raw `T` (e.g., `Texture2D`) to business code means the resource system loses control over that object. It cannot know when the caller is done, so it cannot safely reclaim memory. The handle pattern solves this through three roles:

- **Holder**: grants access to the real asset via `handle.Asset`.
- **Identity proof**: represents a single "borrow." Each call increments the refcount.
- **Return contract**: implements `IDisposable`. Calling `Dispose()` decrements the refcount.

### Data Structure: AssetHandle

`AssetHandle<T>` is a lightweight wrapper with an internal constructor (only `AssetSystem` can create it) and an injected release callback.

```csharp
using System;
using UnityEngine;

namespace YY
{
    public interface IAssetHandle : IDisposable
    {
        UnityEngine.Object RawAsset { get; }
    }

    public class AssetHandle<T> : IAssetHandle where T : UnityEngine.Object
    {
        public T Asset { get; internal set; }
        public UnityEngine.Object RawAsset => Asset;

        private string _key;
        private Action<string> _onRelease;
        private bool _disposed = false;

        internal AssetHandle(string key, T asset, Action<string> onRelease)
        {
            _key = key;
            Asset = asset;
            _onRelease = onRelease;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _onRelease?.Invoke(_key);
            Asset = null;
            _disposed = true;
        }
    }
}
```

**Why `internal` constructor and `Action<string>` callback?** Business code should never create handles directly, only receive them from `AssetSystem`. The callback injection pattern decouples the handle from the manager -- the handle does not know or care about `AssetSystem._nodes`; it only knows "when I'm disposed, call this function with my key." This is dependency inversion at the object level.

**Why an `IAssetHandle` interface?** It allows generic container operations (e.g., `List<IAssetHandle>` in the binding listener) without knowing the specific asset type. The `RawAsset` property provides untyped access for debug/tooling purposes.

### Internal Node: AssetInternalNode

Where `BundleInfo` tracks bundle-level refcounts, `AssetInternalNode` tracks asset-level refcounts for individual assets within bundles.

```csharp
namespace YY
{
    internal class AssetInternalNode
    {
        public UnityEngine.Object TargetAsset;
        public int RefCount;
        public string BundleName;
        public string AssetName;
        public Task LoadingTask; // Deduplication at the asset level

        public void Release()
        {
            RefCount--;
            if (RefCount <= 0)
            {
                TargetAsset = null;
                // Notify BundleManager that the owning bundle can try to unload
                BundleManager.UnloadBundle(BundleName);
            }
        }
    }
}
```

**Two-tier refcounting architecture**: `AssetInternalNode.RefCount` tracks how many handles are using this specific asset. When it reaches zero, the node calls `BundleManager.UnloadBundle`, which decrements the bundle-level `BundleInfo.RefCount`. The bundle unloads only when ALL assets from it have released their references. This is a cascade: handle dispose -> asset node release -> bundle release -> physical unload.

### Facade: AssetSystem

`AssetSystem` is the only class business code interacts with. It maintains `_nodes`, a `Dictionary<string, AssetInternalNode>` that serves as the asset cache pool.

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace YY
{
    public static class AssetSystem
    {
        private static Dictionary<string, AssetInternalNode> _nodes =
            new Dictionary<string, AssetInternalNode>();

        public static async Task<AssetHandle<T>> LoadAsync<T>(
            string bundleName, string assetName) where T : UnityEngine.Object
        {
            string key = $"{bundleName}/{assetName}";

            // 1. Get or create node
            if (!_nodes.TryGetValue(key, out var node))
            {
                node = new AssetInternalNode
                {
                    BundleName = bundleName,
                    AssetName = assetName
                };
                _nodes.Add(key, node);

                // 2. Start loading -- LoadingTask serves as dedup key
                node.LoadingTask = LoadInternal(node);
            }

            // 3. Await loading
            await node.LoadingTask;

            if (node.TargetAsset == null)
            {
                _nodes.Remove(key);
                return null;
            }

            // 4. Increment asset-level refcount
            node.RefCount++;

            // 5. Return handle with injected release callback
            return new AssetHandle<T>(key, node.TargetAsset as T, ReleaseNode);
        }

        private static async Task LoadInternal(AssetInternalNode node)
        {
            node.TargetAsset = await BundleManager.LoadAssetAsync<UnityEngine.Object>(
                node.BundleName, node.AssetName);
        }

        private static void ReleaseNode(string key)
        {
            if (_nodes.TryGetValue(key, out var node))
            {
                node.Release();
                if (node.RefCount <= 0)
                {
                    _nodes.Remove(key);
                }
            }
        }
    }
}
```

**Deduplication at the asset level**: `node.LoadingTask` serves the same purpose as `_inflightTasks` in `BundleManager`, but at the asset granularity. Two concurrent calls to `LoadAsync<Texture2D>("ui/icons.b", "icon_gold")` will share the same `LoadInternal` task.

**Key composition**: The composite key `$"{bundleName}/{assetName}"` is used as the cache index. This works because asset names within a bundle are unique, and the bundle name disambiguates assets with the same name in different bundles.

### Automatic Lifecycle Binding: AssetBindingListener

The weakest point of manual `Dispose()` is that developers forget to call it. Since most Unity resources are tied to `GameObject` lifecycles, the system can automate disposal via `OnDestroy`.

```csharp
namespace YY
{
    internal class AssetBindingListener : MonoBehaviour
    {
        private List<IDisposable> _handles = new List<IDisposable>();

        public void AddHandle(IDisposable handle)
        {
            _handles.Add(handle);
        }

        private void OnDestroy()
        {
            foreach (var handle in _handles)
            {
                handle.Dispose();
            }
            _handles.Clear();
        }
    }
}
```

**Why a dedicated MonoBehaviour instead of extending a base class?** This is a component-based approach rather than inheritance-based. Any `GameObject` can gain binding behavior by having this component added. It does not force UI or entity classes to inherit from a specific base. The component is added imperatively by `LoadAndBind` when needed.

### Convenience API: LoadAndBind

This is expected to be the most-used API in production business code.

```csharp
public static async void LoadAndBind<T>(
    UnityEngine.Object binder,
    string bundleName,
    string assetName,
    Action<T> callback) where T : UnityEngine.Object
{
    if (binder == null) return;

    var handle = await LoadAsync<T>(bundleName, assetName);
    if (handle == null || handle.Asset == null) return;

    GameObject targetGo = null;
    if (binder is GameObject go) targetGo = go;
    else if (binder is Component comp) targetGo = comp.gameObject;

    if (targetGo != null)
    {
        var listener = targetGo.GetComponent<AssetBindingListener>();
        if (listener == null)
            listener = targetGo.AddComponent<AssetBindingListener>();

        listener.AddHandle(handle);
    }

    callback?.Invoke(handle.Asset);
}
```

**Usage example -- the end-state developer experience:**

```csharp
void Start()
{
    // Load a UI panel; auto-unload when this GameObject is destroyed
    AssetSystem.LoadAndBind<GameObject>(this, "uilogin.b", "UILogin", (prefab) =>
    {
        var panel = Instantiate(prefab);
        // ... initialization logic
    });
}
// When UILogin GameObject is Destroyed:
// -> AssetBindingListener.OnDestroy fires
// -> Handle.Dispose fires
// -> AssetInternalNode refcount decrements to 0
// -> BundleManager.UnloadBundle triggers
// -> BundleInfo refcount decrements to 0
// -> AssetBundle.Unload(true) triggers
```

**The double `async void` concern**: `LoadAndBind` is `async void` rather than `async Task`. This is intentional -- it is meant to be fire-and-forget from `Start()` or `Awake()`. Exceptions within are silently swallowed by the async state machine. This is acceptable because the callback pattern handles success/failure at the call site, and `AssetSystem.LoadAsync` already catches and logs internal exceptions.

### Chapter 9 Summary

| Layer | Type | Responsibility |
|-------|------|---------------|
| Handle | `AssetHandle<T>` / `IAssetHandle` | IDisposable wrapper; owns refcount increment; delegates release to callback |
| Node | `AssetInternalNode` | Asset-level refcount; holds the `LoadingTask` for dedup; bridges to `BundleManager` |
| Facade | `AssetSystem` | Cache pool (`_nodes` dict); `LoadAsync` and `LoadAndBind` APIs |
| Binding | `AssetBindingListener` | MonoBehaviour that disposes all registered handles on `OnDestroy` |

---

## Chapter 10: Editor Simulation Mode and Integration Testing

### Problem

Building AssetBundles for every resource change cripples iteration speed. The system must support two modes:

- **Real mode** (device/build): load from AssetBundle files via `LoadFromFileAsync`.
- **Simulation mode** (editor): load directly from project source files via `AssetDatabase`, bypassing the build pipeline entirely.

### Mode Toggle with EditorPrefs Persistence

The toggle persists across Unity restarts using `EditorPrefs`. A cached `int` field avoids repeated `EditorPrefs` reads.

```csharp
#if UNITY_EDITOR
    static int m_SimulateAssetBundleInEditor = -1;
    const string kSimulateAssetBundles = "SimulateAssetBundles";

    public static bool SimulateInEditor
    {
        get
        {
            if (m_SimulateAssetBundleInEditor == -1)
                m_SimulateAssetBundleInEditor = EditorPrefs.GetBool(kSimulateAssetBundles, true) ? 1 : 0;

            return m_SimulateAssetBundleInEditor != 0;
        }
        set
        {
            int newValue = value ? 1 : 0;
            if (newValue != m_SimulateAssetBundleInEditor)
            {
                m_SimulateAssetBundleInEditor = newValue;
                EditorPrefs.SetBool(kSimulateAssetBundles, value);
            }
        }
    }
#endif
```

**Why `-1` as the uninitialized sentinel?** `EditorPrefs.GetBool` returns `false` for missing keys. Without the sentinel, a legitimate `false` value would be indistinguishable from "not yet read." The tri-state encoding (`-1` = unread, `0` = false, `1` = true) avoids this ambiguity.

### Initialization: Bypassing Manifest in Simulation Mode

In simulation mode, Unity Editor internally tracks asset-to-bundle mappings. No `AssetBundleManifest` loading is needed.

```csharp
public static async Task InitializeAsync(string manifestName)
{
    _strategy = new LocalFileLoadStrategy();
    _scheduler = new RequestScheduler(max: 10);

#if UNITY_EDITOR
    if (SimulateInEditor)
    {
        Debug.Log("[BundleManager] Editor Simulation Mode: ON");
        return; // Skip Manifest loading entirely
    }
#endif
    var req = await AssetBundle.LoadFromFileAsync(
        GetAssetBundleBaseDownloadingURL(manifestName) + manifestName);
    if (req != null)
    {
        _manifest = req.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
    }
    else
    {
        Debug.LogError("[BundleManager] Failed to load Manifest!");
    }
}
```

### Asset Loading Redirection

The key bridge: `AssetDatabase.GetAssetPathsFromAssetBundleAndAssetName` translates a (bundleName, assetName) pair to the project-relative file path, then `AssetDatabase.LoadAssetAtPath` loads the source asset.

```csharp
public static async Task<T> LoadAssetAsync<T>(string bundleName, string assetName)
    where T : UnityEngine.Object
{
#if UNITY_EDITOR
    if (SimulateInEditor)
    {
        string[] paths = AssetDatabase.GetAssetPathsFromAssetBundleAndAssetName(
            bundleName, assetName);
        if (paths.Length == 0)
        {
            Debug.LogError($"[Simulate] Asset not found: {assetName} in {bundleName}");
            return null;
        }

        // Simulate async to expose logic bugs that sync loading would hide
        await Task.Yield();

        return AssetDatabase.LoadAssetAtPath<T>(paths[0]);
    }
#endif
    // Real-mode path
    BundleInfo info = await LoadBundleAsync(bundleName);
    if (info == null || info.Bundle == null) return null;
    var req = await info.Bundle.LoadAssetAsync(assetName, typeof(T));
    return req as T;
}
```

**Why `await Task.Yield()` in simulation mode?** Editor-mode loads are synchronous under the hood. If the entire pipeline runs synchronously, bugs related to ordering or uninitialized state at await boundaries might be masked. `Task.Yield()` forces a one-frame yield, making the simulation path closer to the async real path in behavior, surfacing timing-dependent issues early.

### Scene Loading Redirection

Scene loading in the editor uses `EditorSceneManager.LoadSceneAsyncInPlayMode` instead of `SceneManager.LoadSceneAsync`.

```csharp
public static async Task LoadSceneAsync(string bundleName, string sceneName, bool isAdditive)
{
#if UNITY_EDITOR
    if (SimulateInEditor)
    {
        string[] levelPaths = AssetDatabase.GetAssetPathsFromAssetBundleAndAssetName(
            bundleName, sceneName);
        if (levelPaths.Length == 0)
        {
            Debug.LogError($"[Simulate] Scene not found: {sceneName} in {bundleName}");
            return;
        }

        var mode = isAdditive ? LoadSceneMode.Additive : LoadSceneMode.Single;
        var param = new LoadSceneParameters(mode);
        await EditorSceneManager.LoadSceneAsyncInPlayMode(levelPaths[0], param);
        return;
    }
#endif
    BundleInfo info = await LoadBundleAsync(bundleName);
    if (info == null) return;

    var loadMode = isAdditive ? LoadSceneMode.Additive : LoadSceneMode.Single;
    await SceneManager.LoadSceneAsync(sceneName, loadMode);
}
```

### Editor Menu Tooling

Menu items for toggling simulation mode and triggering builds. The validation method provides the checkmark UI state.

```csharp
using UnityEditor;
using UnityEngine;
using YY;

public class MenuToolsUtils
{
    const string kSimulationMode = "GameTools/AssetBundles/Simulation Mode";

    [MenuItem(kSimulationMode)]
    public static void ToggleSimulationMode()
    {
        BundleManager.SimulateInEditor = !BundleManager.SimulateInEditor;
    }

    [MenuItem(kSimulationMode, true)]
    public static bool ToggleSimulationModeValidate()
    {
        Menu.SetChecked(kSimulationMode, BundleManager.SimulateInEditor);
        return true;
    }

    [MenuItem("GameTools/AssetBundles/Build")]
    public static void BuildAssetBundles()
    {
        string outputPath = "StreamingRes/android";
        if (!Directory.Exists(outputPath))
            Directory.CreateDirectory(outputPath);

        BuildPipeline.BuildAssetBundles(outputPath,
            BuildAssetBundleOptions.ChunkBasedCompression,
            BuildTarget.StandaloneWindows64);

        AssetDatabase.Refresh();
        Debug.Log("Build AssetBundles Complete.");
    }
}
```

**Why `ChunkBasedCompression` (LZ4)?** LZ4 compression allows partial reads -- you can load individual assets from a bundle without decompressing the entire file. `UncompressedAssetBundle` loads fastest but wastes disk space. `LZMA` (default) provides the best compression but requires full decompression on access. LZ4 is the recommended sweet spot for runtime loading.

### Path Management: FileUtils

Path resolution differs by environment: editor simulation (no bundles), editor real-mode (bundles in project `StreamingRes/`), and device (bundles in APK `StreamingAssets/`).

```csharp
using System.IO;
using UnityEngine;

public static class FileUtils
{
    public static string NativePath { get; private set; }

    public static void ResetPath()
    {
        // Default: StreamingAssets (mobile builds)
        NativePath = Application.streamingAssetsPath + "/assets/";

#if UNITY_EDITOR
        // Editor: point to project StreamingRes directory
        string rootPath = Application.dataPath.Replace("/Assets", "");
        string platform = "android";
        NativePath = $"{rootPath}/StreamingRes/{platform}/";
#endif
    }
}
```

### Integration Test: ApplicationLauncher

A MonoBehaviour that wires everything together for end-to-end validation.

```csharp
using UnityEngine;
using YY;

public class ApplicationLauncher : MonoBehaviour
{
    async void Start()
    {
        FileUtils.ResetPath();
        BundleManager.overrideBaseDownloadingURL = (bundleName) => FileUtils.NativePath;
        await BundleManager.InitializeAsync("android");

        // Load scene
        await BundleManager.LoadSceneAsync("scenes/uiscene.b", "uiscene", true);

        // Load UI asset
        AssetHandle<GameObject> uilogin = await AssetSystem.LoadAsync<GameObject>(
            "ui/uiloginview.b", "UILoginView");

        if (uilogin != null)
        {
            var uiroot = GameObject.Find("UIRoot/main");
            if (uiroot != null)
            {
                GameObject view = Instantiate(uilogin.Asset, uiroot.transform);
                Debug.Log($"Load Success: {view.name}");
            }
        }
        else
        {
            Debug.LogError("Failed to load UILoginView.");
        }
    }
}
```

### Chapter 10 Summary

- Simulation mode uses `#if UNITY_EDITOR` blocks to intercept load calls and redirect to `AssetDatabase` APIs.
- `AssetDatabase.GetAssetPathsFromAssetBundleAndAssetName` is the bridge function that maps (bundle, asset) to project paths.
- `await Task.Yield()` in simulation prevents masking async-ordering bugs.
- `EditorPrefs` persists the simulation toggle across Unity sessions.
- Two validation paths: simulation (no build needed) and real bundle mode (build via menu, then run).

---

## Chapter 11: Exception Handling and Timeout Mechanisms

### Problem

Files can be corrupt, networks can disconnect, disks can be full. Without proper error handling, the system can deadlock or exhibit undefined behavior. Loading failure is a normal business state that must be handled gracefully, and internal state (particularly the `_inflightTasks` dictionary) must be correctly reset to prevent permanent deadlock for affected bundles.

### Custom Exception: BundleLoadException

A typed exception carrying bundle context for debugging. The `BundleName` property enables error aggregation and routing.

```csharp
using System;

namespace YY
{
    public class BundleLoadException : Exception
    {
        public string BundleName { get; }

        public BundleLoadException(string bundleName, string message)
            : base($"[Bundle: {bundleName}] {message}")
        {
            BundleName = bundleName;
        }

        public BundleLoadException(string bundleName, string message, Exception innerException)
            : base($"[Bundle: {bundleName}] {message}", innerException)
        {
            BundleName = bundleName;
        }
    }
}
```

**Why a custom exception instead of `System.Exception`?** The bundle name in the exception message is essential for debugging dependency chains. When a deeply nested dependency fails, the call stack alone does not reveal which specific bundle was being loaded at which level. The `BundleName` property enables structured error reporting and potential retry logic scoped to a specific bundle.

### Hardening BundleManager: From Null Returns to Exceptions

#### File Existence Check and Validation

The internal load method is refactored to throw on failure rather than silently returning null.

```csharp
private static async Task<BundleInfo> LoadBundleInternalAsync(string bundleName)
{
    // ... dependency loading logic unchanged ...

    await _scheduler.WaitSlot();

    try
    {
        if (_loadedBundles.TryGetValue(bundleName, out var info))
            return info;

        string path = Path.Combine(
            GetAssetBundleBaseDownloadingURL(bundleName), bundleName);

        // File existence check before disk access
        if (!_pathProvider.Exists(path))
        {
            throw new BundleLoadException(bundleName, $"File not found at path: {path}");
        }

        AssetBundle bundle = await _strategy.Load(path);

        // Validate load result
        if (bundle == null)
        {
            throw new BundleLoadException(bundleName,
                "AssetBundle.LoadFromFileAsync returned null. File might be corrupted.");
        }

        var newInfo = new BundleInfo(bundleName, bundle);
        _loadedBundles[bundleName] = newInfo;
        return newInfo;
    }
    catch (Exception ex)
    {
        // Re-throw already-wrapped exceptions; wrap unknown ones
        if (ex is BundleLoadException) throw;

        throw new BundleLoadException(bundleName,
            "Unknown error during loading.", ex);
    }
    finally
    {
        _scheduler.ReleaseSlot();
    }
}
```

**Why not catch-and-null here?** `BundleManager` is the execution layer. It does not know whether a file-not-found error should be retried, ignored, or reported to the user. Its responsibility is to report the failure accurately. The mid-layer (`AssetSystem`) decides how to translate that failure for business logic.

### Path Abstraction: IBundlePathProvider

Hardcoding `File.Exists` breaks on Android where `Application.streamingAssetsPath` points inside the APK (compressed archive). `File.Exists` always returns false for paths inside an APK. Therefore path checking must be abstracted behind an interface.

```csharp
namespace YY
{
    public interface IBundlePathProvider
    {
        string GetBundlePath(string bundleName);
        bool Exists(string bundleName);
    }
}
```

#### Default Implementation

```csharp
using System.IO;
using UnityEngine;

namespace YY
{
    public class DefaultBundlePathProvider : IBundlePathProvider
    {
        public string GetBundlePath(string bundleName)
        {
            // Check sandbox (persistent) first for hot-updated bundles
            string sandboxPath = FileUtils.PersistentPath + bundleName;
            if (File.Exists(sandboxPath))
            {
                return sandboxPath;
            }
            // Fall back to streaming assets
            return FileUtils.NativePath + bundleName;
        }

        public bool Exists(string bundleName)
        {
            string sandboxPath = FileUtils.PersistentPath + bundleName;
            if (File.Exists(sandboxPath)) return true;
            // Default: assume bundles in streaming assets always exist
            // (since File.Exists doesn't work inside APK)
            return true;
        }
    }
}
```

**The APK problem**: On Android, `Application.streamingAssetsPath` resolves to `jar:file:///data/app/.../base.apk!/assets/`. `File.Exists` on this path returns `false` because the Unity APK packaging merges files into a compressed archive. The only reliable check is to attempt loading and catch the failure. The default provider returns `true` for non-sandbox bundles as a deliberate simplification -- the real validation happens when `LoadFromFileAsync` either succeeds or fails.

**Sandbox-first path resolution**: Persistent data path is checked first to support hot-update scenarios where updated bundles are downloaded to writable storage and should take precedence over the built-in versions.

#### Updated BundleManager Initialization

```csharp
public static async Task InitializeAsync(string manifestName,
    IBundlePathProvider provider = null)
{
    _pathProvider = provider ?? new DefaultBundlePathProvider();
    _strategy = new LocalFileLoadStrategy();
    _scheduler = new RequestScheduler(max: 10);

#if UNITY_EDITOR
    if (SimulateInEditor) { return; }
#endif

    var req = await AssetBundle.LoadFromFileAsync(
        _pathProvider.GetBundlePath(manifestName));
    if (req != null)
    {
        _manifest = req.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
    }
    else
    {
        Debug.LogError("[BundleManager] Failed to load Manifest!");
    }
}
```

### AssetSystem: Catching and Timeout

#### Timeout via Task.WhenAny

Unity's `AsyncOperation` does not support `CancellationToken`. `Task.WhenAny` provides a soft timeout -- business logic gets prompt feedback, though the underlying IO continues in the background.

```csharp
public static async Task<AssetHandle<T>> LoadAsync<T>(
    string bundleName, string assetName,
    int timeoutSeconds = 10) where T : UnityEngine.Object
{
    string key = $"{bundleName}/{assetName}";

    AssetInternalNode node = null;
    try
    {
        if (!_nodes.TryGetValue(key, out node))
        {
            node = new AssetInternalNode
            {
                BundleName = bundleName,
                AssetName = assetName,
                RefCount = 0
            };
            _nodes.Add(key, node);
            node.LoadingTask = LoadInternal(node);
        }

        // Race: loading task vs timeout
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
        var completedTask = await Task.WhenAny(node.LoadingTask, timeoutTask);

        if (completedTask == timeoutTask)
        {
            Debug.LogError($"[AssetSystem] Load Timeout: {key} ({timeoutSeconds}s)");
            _nodes.Remove(key);
            return null;
        }

        // Re-await to capture any exception from the loading task
        await node.LoadingTask;

        if (node.TargetAsset == null)
        {
            _nodes.Remove(key);
            return null;
        }

        node.RefCount++;
        var handle = new AssetHandle<T>(key, (h) => ReleaseNode(key));
        handle.Asset = node.TargetAsset as T;
        return handle;
    }
    catch (BundleLoadException ex)
    {
        Debug.LogError($"[AssetSystem] Failed to load asset '{key}': {ex.Message}");
        _nodes.Remove(key);
        return null;
    }
    catch (Exception ex)
    {
        Debug.LogError($"[AssetSystem] Unexpected exception: {ex}");
        _nodes.Remove(key);
        return null;
    }
}
```

**Critical detail -- the double await**: After `Task.WhenAny` returns with the loading task as winner, the code calls `await node.LoadingTask` a second time. This is necessary because `WhenAny` only tells you which task finished first, not whether it completed successfully or faulted. The second `await` re-throws any exception that occurred inside `LoadInternal`, which would otherwise be silently swallowed.

**The orphaned IO problem**: When a timeout fires, the underlying `AssetBundle.LoadFromFileAsync` is still running in the Unity engine layer. There is no way to cancel it. The system removes the node from `_nodes` so the timed-out request does not leave a stale entry. When the orphaned IO eventually completes, it will attempt to set `node.TargetAsset` on a node that is no longer in the dictionary -- this is a memory leak of the result but not a functional bug because no one holds a reference to that node.

**Why clear `_nodes[key]` on every error path?** Each error branch (timeout, null asset, BundleLoadException, generic Exception) removes the key from the cache. Failing to do so would leave a node with `LoadingTask` in a terminal (faulted) state. Future calls to `LoadAsync` with the same key would find the node, `await node.LoadingTask`, and immediately receive the same exception -- effectively a permanent error for that asset. Clearing the node allows retry on the next request.

### Error Propagation Architecture

The system uses a deliberate two-tier error strategy:

- **`BundleManager` (bottom layer)**: Throws exceptions. It is the executor -- it does not make policy decisions about recovery.
- **`AssetSystem` (middle layer)**: Catches exceptions, logs them, returns `null`. It is the service facade -- it protects the game loop from crashing.

This separation means business code calling `AssetSystem.LoadAsync` does not need try-catch blocks. A `null` return means "loading failed, check the console."

### Chapter 11 Summary

- Custom `BundleLoadException` carries bundle name for debugging dependency chain failures.
- `IBundlePathProvider` abstracts file existence checks away from `BundleManager` to handle platform differences (APK on Android).
- `DefaultBundlePathProvider` uses sandbox-first path resolution for hot-update support.
- `Task.WhenAny` provides soft timeout without requiring Unity API cancellation support.
- Double-await after `Task.WhenAny` is necessary to capture exceptions from the winning task.
- Every error path in `AssetSystem` removes the cache key to allow retry on subsequent requests.
- Two-tier error strategy: bottom layer throws, middle layer catches and logs.

---

## Key Design Patterns and Architectural Insights

### 1. Three-Tier Architecture

```
Business Code (MonoBehaviour, UI, Gameplay)
    |
    v
AssetSystem (Facade: handles, binding, timeout, error swallowing)
    |
    v
BundleManager (Core: refcounting, dedup, scheduling, dependency resolution)
    |
    v
Unity Native API (AssetBundle.LoadFromFileAsync, AssetDatabase)
```

Each tier has a distinct responsibility and failure mode. This clean separation means lower tiers can be swapped (e.g., changing the load strategy from local file to web request) without affecting upper tiers.

### 2. Two-Level Deduplication

The system prevents duplicate work at both levels:

- **Bundle level**: `BundleManager._inflightTasks` prevents re-reading the same bundle file.
- **Asset level**: `AssetInternalNode.LoadingTask` prevents re-extracting the same asset from a bundle.

This is necessary because multiple different assets may reside in the same bundle. Without bundle-level dedup, loading two assets from the same bundle would load the bundle twice. Without asset-level dedup, two requests for the same asset would extract it twice.

### 3. Two-Level Reference Counting with Cascade

```
AssetHandle.Dispose()
  -> AssetInternalNode.Release() (RefCount--)
    -> if zero: BundleManager.UnloadBundle(bundleName)
      -> BundleInfo.Release() (RefCount--)
        -> if zero: AssetBundle.Unload(true)
          -> recursive UnloadBundle on dependencies
```

The refcount cascade ensures bundles stay loaded as long as any asset from them is in use, and unload only when all assets from all direct and indirect consumers are released.

### 4. Cooperative Concurrency via TaskCompletionSource Queue

Instead of OS-level synchronization primitives (SemaphoreSlim, locks), the system uses a `Queue<TaskCompletionSource<bool>>` to implement FIFO-gated concurrency. This is compatible with Unity's single-threaded async model and avoids thread-pool starvation issues that could arise with `SemaphoreSlim.WaitAsync` under deep async call chains.

### 5. Compile-Time Mode Switching via Platform Compilation

`#if UNITY_EDITOR` blocks guard all simulation-mode code. This ensures zero overhead in builds -- the simulation branches are physically absent from compiled IL. The `SimulateInEditor` runtime flag only matters within the editor; in builds, all `#if UNITY_EDITOR` blocks are stripped.

### 6. Dependency Injection via Interface for Platform Abstraction

`IBundlePathProvider` is injected into `BundleManager.InitializeAsync`. This follows the strategy pattern: the manager does not need to know about Android APK internals or sandbox paths. It delegates path resolution to an injected provider. In tests, a mock provider can supply known paths without touching the filesystem.

### 7. Handle Pattern with Callback Injection (RAII for Unity Assets)

`AssetHandle<T>` implements `IDisposable` and accepts an `Action<string>` release callback at construction time. The handle knows nothing about what "release" means -- it only knows to invoke the callback. This is a portable pattern: the same handle class could be used with different backends (editor simulation, real bundles, addressables wrapper) by injecting different callbacks.

### 8. Fire-and-Forget Binding via OnDestroy

`AssetBindingListener` is a MonoBehaviour that accumulates handles and disposes them all in `OnDestroy`. This transforms explicit resource management into implicit lifecycle binding -- the developer only loads; the system tracks and releases. This is Unity's equivalent of RAII (Resource Acquisition Is Initialization), achieved through component-based composition rather than inheritance.

### 9. Soft Timeout as a Practical Compromise

Since Unity's async asset APIs do not accept `CancellationToken`, the timeout is a "soft" one: `Task.WhenAny` unblocks the caller, but the IO operation continues in the background. The system accepts this tradeoff because the alternative (blocking indefinitely on a stuck IO) is worse than a small memory leak from an orphaned load result. The cache entry is cleaned up so the system can retry.
