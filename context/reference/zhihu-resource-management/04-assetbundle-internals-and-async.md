---
title: AssetBundle Internals, Runtime Behavior, Loading Approaches, and Native Async/Await
source: Zhihu Column "游戏资源管理" by 伽蓝之洞, Chapters 4-7
status: verified
---

# AssetBundle Internals, Runtime Behavior, Loading Approaches, and Native Async/Await

This document consolidates four chapters covering the full spectrum of AssetBundle knowledge: binary file structure, runtime lifecycle, five loading paradigms, and a zero-dependency native async/await implementation that outperforms UniTask.

---

## Chapter 4: AssetBundle Core Mechanisms and File Structure

### 1. Definition and Purpose

An AssetBundle is a platform-specific archive file format used by Unity to store non-code assets (models, textures, prefabs, audio clips, scenes). At runtime it fulfills three core functions:

- **Dynamic content distribution**: Separates assets from the install package, enabling on-demand download (DLC) or reduced initial download size.
- **Runtime memory mapping**: Through specific compression formats, supports mapping assets into virtual memory with low memory overhead.
- **Platform compatibility**: At build time, compiles assets into formats required by specific graphics APIs (OpenGL ES, Metal, Vulkan).

### 2. Internal File Structure

An AssetBundle is a binary container consisting of a file header and two categories of internal files.

#### 2.1 Serialized Files

The core component storing serialized Unity object data.

- **Contents**: GameObject hierarchies, Component property data, and inter-object references.
- **Generation rules**:
  - **Asset AssetBundles** (regular bundles): Typically contain **one** serialized file.
  - **Scene AssetBundles** (scene bundles): Typically contain **two** serialized files -- one for the scene hierarchy, one for objects referenced by the scene.

#### 2.2 Resource Files

File segments storing large binary data blocks.

- **Contents**: Texture pixel data, AudioClip PCM data, and other large binary blobs.
- **Technical significance**: Unity separates these large data blocks from serialized files to optimize loading performance. This separation allows the engine to efficiently read binary blocks from disk on background threads, supporting multi-threaded loading.

### 3. Compression Formats and Loading Behavior

AssetBundle supports three compression modes. The choice of algorithm directly determines runtime I/O behavior and memory footprint.

#### 3.1 LZMA (Stream-Based Compression)

- **Algorithm characteristics**: Stream-based; the entire AssetBundle is treated as a single continuous compressed stream.
- **Loading behavior**: Does **not** support random access. When loaded via `AssetBundle.LoadFromFile`, the engine must decompress and rewrite the entire bundle to memory or disk cache first.
- **Use case**: Suitable only for network transfer (download phase), because it achieves the highest compression ratio. After download, convert to LZ4 via `AssetBundle.RecompressAssetBundleAsync`.

#### 3.2 LZ4 (Chunk-Based Compression)

- **Algorithm characteristics**: Chunk-based; the file is divided into fixed-size chunks, each independently compressed.
- **Loading behavior**: Supports **random access**.
  - `AssetBundle.LoadFromFile` reads only the file **header**.
  - When an internal asset is actually loaded (e.g., `LoadAsset`), the engine reads and decompresses only the corresponding chunk based on offset.
- **Advantage**: Achieves "zero-copy" loading with extremely low memory footprint; no need to decompress the entire bundle.
- **Use case**: The standard choice for all runtime-loaded local AssetBundles.

#### 3.3 Uncompressed

- **Characteristics**: No compression overhead; largest file size.
- **Loading behavior**: Direct disk I/O read; lowest CPU overhead.

### 4. Script Support and Limitations

AssetBundles do **not** contain C# code or assemblies.

#### 4.1 Serialization Matching Mechanism

AssetBundles store serialized instance data for ScriptableObjects or MonoBehaviours. When loading an AssetBundle, Unity locates the corresponding class in the current application domain using three identifiers:

- **Assembly Name**
- **Namespace**
- **Class Name**

Example: When packing a Prefab with a `Monster.cs` script, the AB stores: "This object has a script called `Monster`, namespace is `Game.Logic`, its `hp` field value is `100`."

#### 4.2 Limitations

Since AssetBundles do not contain code, you cannot distribute new C# classes or modify existing class logic via AssetBundles. If serialized data references a class that does not exist in the main program, a **Script Missing** warning is thrown at load time.

### 5. TypeTree and Compatibility

#### 5.1 TypeTree Function

TypeTree is metadata describing the structure of serialized data. It defines the data type and layout of every field in an object.

- **Function**: When Unity engine versions change and serialization formats shift, the engine uses TypeTree to perform **Safe Binary Read**, attempting to map old-version data onto new-version objects.

#### 5.2 Build Option: `DisableWriteTypeTree`

In `BuildAssetBundleOptions`, the `DisableWriteTypeTree` flag can be enabled.

- **Effect**: Strips TypeTree data from the AssetBundle.
- **Benefits**: Smaller bundle size, faster loading, lower runtime memory.
- **Constraint**: The Unity version used to build the AssetBundle **must exactly match** the runtime Unity version. If versions mismatch without TypeTree, serialization layout misalignment causes crashes or data corruption.

### Summary (Chapter 4)

- **File structure**: Composed of Serialized Files (logical data) and Resource Files (binary data), supporting efficient multi-threaded loading.
- **Compression strategy**: LZ4 chunk-based compression enables random access and low memory footprint -- the standard for runtime loading.
- **Code separation**: AssetBundles contain only data; code logic must exist in the main program assemblies.
- **TypeTree**: With guaranteed engine version consistency, disabling TypeTree is an effective optimization for bundle size and memory.

---

## Chapter 5: AssetBundle Runtime Behavior

### 1. Local Loading: Memory Allocation and I/O Strategy

Unity provides three core local loading methods in `UnityEngine.AssetBundle`. Although all return an `AssetBundle` object, they differ fundamentally in underlying memory allocation and I/O mechanisms.

#### 1.1 `LoadFromFile`

```csharp
[MethodImpl(MethodImplOptions.InternalCall)]
[FreeFunction("LoadFromFile")]
internal static extern AssetBundle LoadFromFile_Internal(string path, uint crc, ulong offset);
```

- **Mechanism**: Calls the operating system's filesystem API directly via the native layer.
- For LZ4 or uncompressed AssetBundles, the engine reads only the **file header** and builds a virtual file index -- no full-file memory copy.
- Actual resource data (data blocks) is read on demand during subsequent `LoadAsset` calls.
- **Memory footprint**: Extremely low (limited to header and index data). This is the most efficient loading method.
- **Best practice**: Always use this as the first choice for local storage scenarios (StreamingAssets or bundles already downloaded to the sandbox).
- **`offset` parameter**: Can be used for a header offset to implement simple encryption, or to support combining multiple bundles. Requires manual code to manage the combination; limited practical use.

#### 1.2 `LoadFromMemory`

```csharp
[MethodImpl(MethodImplOptions.InternalCall)]
[FreeFunction("LoadFromMemory")]
internal static extern AssetBundle LoadFromMemory_Internal(byte[] binary, uint crc);
```

- **Mechanism**: Accepts a `byte[]` array. Before the API call, the file content has already been fully loaded into the **Managed Heap**.
- After passing to the native layer, the Unity engine typically allocates a new buffer in the **Native Heap** to store or decompress the data.
- **Double memory footprint**. For a 10 MB AssetBundle, peak memory consumption is at least 10 MB (managed heap) + 10 MB (native heap).
- **Best practice**: Avoid whenever possible. Use only when you cannot obtain a file path and must construct from memory (e.g., very specific encryption/decryption flows).

#### 1.3 `LoadFromStream`

```csharp
public static AssetBundle LoadFromStream(Stream stream, uint crc, uint managedReadBufferSize)
{
    ValidateLoadFromStream(stream); // Validates CanRead and CanSeek
    return LoadFromStreamInternal(stream, crc, managedReadBufferSize);
}
```

- **Mechanism**: Accepts a C# `Stream` object. The source explicitly checks `stream.CanSeek`, indicating the engine requires random access to the stream.
- Unity calls the stream's `Read` and `Seek` methods via managed/native interop, reading data blocks on demand.
- **Memory footprint**: Depends on `managedReadBufferSize` (typically small by default), avoiding the full-copy problem of `LoadFromMemory`.
- **Best practice for encryption**: The developer can subclass `Stream` to implement a custom decryption stream, decrypting data in real-time within the `Read` method. This maintains security while keeping a low memory watermark.
- **Android note**: On Android, recommend the **BetterStreamingAssets** plugin, as C# `Stream` cannot access `streamingAssetsPath` inside APK/AAB.

### 2. `UnityWebRequestAssetBundle`: Loading and Caching

For scenarios requiring AssetBundle download from a server, Unity provides the dedicated API `UnityWebRequestAssetBundle`. It integrates download, caching, and decompression. It can load both network and local resources (local URIs must use the `file://` prefix).

#### 2.1 `UnityWebRequestAssetBundle.GetAssetBundle`

```csharp
public static UnityWebRequest GetAssetBundle(string uri, CachedAssetBundle cachedAssetBundle, uint crc = 0);
```

- **Mechanism**: Creates a `UnityWebRequest` and automatically attaches a `DownloadHandlerAssetBundle`.
- **Streaming write**: Unlike `UnityWebRequest.Get`, it does not cache downloaded data entirely in memory. The data stream is written directly to disk cache or processed as a stream.
- **Automatic caching**: Based on the provided Hash or Version, the engine automatically checks the `Caching` system.
  - **Cache Hit**: Loads directly from local disk cache (behavior equivalent to `LoadFromFile`).
  - **Cache Miss**: Downloads from network, writes to cache, then loads.

#### 2.2 LZMA Automatic Transcoding

To save bandwidth, servers typically deploy LZMA-format AssetBundles (smallest package). However, LZMA does not support random access and has poor runtime loading performance.

`DownloadHandlerAssetBundle`, during LZMA download, automatically decompresses and recompresses the bundle to **LZ4 format** using background threads, then stores it in the local cache.

- **Transport layer**: Enjoys LZMA's low bandwidth.
- **Storage/runtime layer**: Enjoys LZ4's random access and low memory footprint.

#### 2.3 Memory Considerations

- **WebStream Buffer**: During download, a small amount of native memory buffer is still consumed.
- **Retrieving the object**: After download completes, call `DownloadHandlerAssetBundle.GetContent(UnityWebRequest)` to obtain the `AssetBundle` object reference. This operation is analogous to `LoadFromFile` and is low-overhead.

### 3. Addressing: Platform Path Differences and Loading Strategy

When using `LoadFromFile`, path parameter handling differs significantly across platforms, especially Android.

#### 3.1 Key Path Definitions

| Path | Description |
|------|-------------|
| `Application.streamingAssetsPath` | Corresponds to `Assets/StreamingAssets`. Read-only. Content shipped with the package. |
| `Application.persistentDataPath` | Corresponds to the OS sandbox storage directory. Read-write. Used for storing hot-update downloaded resources (i.e., `UnityWebRequest` cache location). |

#### 3.2 Android Platform Specifics

On Android, `StreamingAssets` resides inside the APK (Zip archive).

- **`System.IO` limitation**: C# `File.Exists` or `FileStream` cannot directly access paths inside the APK (e.g., `jar:file:///.../assets/bundle`).
- **Unity API privilege**: `AssetBundle.LoadFromFile` has special handling at the native layer. It can **directly read data from inside the APK** without needing to decompress or copy files to the sandbox.

#### 3.3 Runtime Loading Strategy: Dual-Path Fallback

```csharp
public AssetBundle LoadBundle(string bundleName)
{
    // 1. Check sandbox directory first (hot-update version)
    string hotPath = Path.Combine(Application.persistentDataPath, bundleName);
    if (File.Exists(hotPath))
    {
        return AssetBundle.LoadFromFile(hotPath);
    }

    // 2. Fall back to built-in directory (initial version)
    string builtInPath = Path.Combine(Application.streamingAssetsPath, bundleName);
    return AssetBundle.LoadFromFile(builtInPath);
}
```

### 4. Extraction: Deserialization Behavior

After loading an `AssetBundle` object, content must be extracted via the `LoadAsset` family of methods.

#### 4.1 `LoadAsset`

```csharp
public T LoadAsset<T>(string name) where T : Object
```

- Finds an object in the AssetBundle's serialized data by name or path and performs **deserialization**.
- This is a **synchronous blocking** operation. For Prefabs with many components or complex hierarchies, deserialization may take milliseconds to tens of milliseconds, causing main-thread frame drops.
- **Recommendation**: For larger assets, use `LoadAssetAsync` to spread deserialization work across multiple frames.

#### 4.2 `LoadAllAssets`

```csharp
public Object[] LoadAllAssets()
```

- Iterates through all objects in the AssetBundle and loads them all. This causes all resources in the bundle to enter memory simultaneously.
- **Unless the AssetBundle is a purpose-built atlas or Shader collection**, avoid this API to prevent unnecessary memory spikes.

### 5. Unloading: The Core of Lifecycle Management

The `Unload` method is the most critical and error-prone part of resource management. Its `bool unloadAllLoadedObjects` parameter determines fundamentally different memory behaviors.

```csharp
[MethodImpl(MethodImplOptions.InternalCall)]
[NativeMethod("Unload")]
public extern void Unload(bool unloadAllLoadedObjects);
```

#### 5.1 `Unload(true)` -- Complete Release

- **Behavior**:
  - Releases the AssetBundle object's header information and file handles.
  - **Force-destroys all Assets** loaded and instantiated from this AssetBundle (e.g., Texture, Mesh, GameObject).
- **Result**: Memory is fully reclaimed. But if any GameObject in the scene still references the destroyed resources, resource loss occurs (e.g., pink materials, Missing Reference).
- **Use only when** you are certain the resources are no longer referenced by any logic.

#### 5.2 `Unload(false)` -- Header-Only Release

- **Behavior**:
  - Releases the AssetBundle object's header information and file handles.
  - **Preserves** currently loaded Assets in memory.
- **Hidden danger -- Resource Orphaning**:
  - **Reference break**: The preserved Assets lose their link to the AssetBundle.
  - **Memory redundancy**: If the same AssetBundle is loaded again later, Unity treats it as a new Bundle instance. Calling `LoadAsset` again creates a **new copy** of the resource in memory, resulting in multiple copies of the same data.
  - Remediation requires `Resources.UnloadUnusedAssets`, which can cause hitches.
- **In strict resource management architectures, avoid `Unload(false)`.** It makes resource lifecycles uncontrollable.

### Summary (Chapter 5)

- **Use `LoadFromFile`** for local loading to achieve optimal memory and I/O performance.
- **Use `UnityWebRequestAssetBundle`** for network loading, leveraging automatic caching and LZMA-to-LZ4 transcoding.
- **For encryption**, use `LoadFromStream` to avoid the memory overhead of `LoadFromMemory`.
- **Build a reference-count-based lifecycle management system**, ensuring `Unload(true)` is called only when reference counts reach zero. Eliminate the resource redundancy and leaks caused by `Unload(false)`.

---

## Chapter 6: Five Approaches to AssetBundle Loading

When building a resource management framework, the choice of async driving mechanism determines the entire project's foundation. Each approach is evaluated not just on code aesthetics, but on how it affects Bundle manager encapsulation and business-layer logic.

### Approach 1: Polling (`Update`)

State-machine polling in `Update`.

```csharp
public class PollingExample : MonoBehaviour
{
    enum LoadState { None, LoadingBundle, LoadingAsset, Done }

    private LoadState _state = LoadState.None;
    private AssetBundleCreateRequest _bundleReq;
    private AssetBundleRequest _assetReq;
    private AssetBundle _loadedBundle;
    private string _path;

    void Start()
    {
        _path = Application.streamingAssetsPath + "/hero.bundle";
        _bundleReq = AssetBundle.LoadFromFileAsync(_path);
        _state = LoadState.LoadingBundle;
    }

    void Update()
    {
        if (_state == LoadState.Done) return;

        if (_state == LoadState.LoadingBundle)
        {
            if (_bundleReq.isDone)
            {
                _loadedBundle = _bundleReq.assetBundle;
                if (_loadedBundle == null) { Debug.LogError("Bundle load failed"); return; }
                _assetReq = _loadedBundle.LoadAssetAsync<GameObject>("HeroPrefab");
                _state = LoadState.LoadingAsset;
            }
        }
        else if (_state == LoadState.LoadingAsset)
        {
            if (_assetReq.isDone)
            {
                var prefab = _assetReq.asset as GameObject;
                Instantiate(prefab);
                Debug.Log("[Polling] Task complete");
                _state = LoadState.Done;
            }
        }
    }
}
```

| Dimension | Assessment |
|-----------|------------|
| **Encapsulation merit** | Precise control over how many requests to process per frame (frame-split loading), retry-on-error, dynamic load-order adjustment. Keeping all requests in a `List` is well-suited for batch management and priority sorting. Fits cleanly into independent C# classes for ECS or custom Update systems. |
| **Encapsulation flaw** | High state-maintenance cost. Even when I/O is not done, the CPU runs `if` checks every frame. Hard to debug. |
| **Raw usage** | Extremely poor developer experience. Should only exist inside `BundleManager` internals, never exposed directly to business-layer code. After encapsulation, usage can be simplified. |

### Approach 2: Callback (`AsyncOperation.completed`)

Event-driven via Unity's built-in `completed` event on `AsyncOperation`.

```csharp
public class CallbackExample : MonoBehaviour
{
    void Start()
    {
        string path = Application.streamingAssetsPath + "/hero.bundle";

        var bundleReq = AssetBundle.LoadFromFileAsync(path);

        bundleReq.completed += (op1) =>
        {
            var bundle = bundleReq.assetBundle;
            if (bundle == null) return;

            var assetReq = bundle.LoadAssetAsync<GameObject>("HeroPrefab");

            assetReq.completed += (op2) =>
            {
                var prefab = assetReq.asset as GameObject;
                Instantiate(prefab);
                Debug.Log("[Callback] Task complete");
            };
        };
    }
}
```

| Dimension | Assessment |
|-----------|------------|
| **Encapsulation merit** | Implementation is extremely simple. Unity schedules at the native level; the Manager only needs to store the user's `Action` and invoke it on completion. No "polling" CPU overhead. |
| **Encapsulation flaw** | **Exception handling is difficult.** If a null-reference exception occurs inside the callback, stack traces are often broken, making it hard to trace which request triggered it. |
| **Use case** | Handy for single-resource loads. **Disastrous for sequential logic** -- logic becomes fragmented. Originally linear business logic is severed by callback nesting; `try-catch` cannot span across callbacks. Callbacks suit "fire-and-forget" logic (e.g., playing a sound effect), but not complex load flows. |

### Approach 3: Coroutine

Appears linear but constrained by `yield return` syntax limitations.

```csharp
public class CoroutineExample : MonoBehaviour
{
    void Start()
    {
        StartCoroutine(LoadFlow());
    }

    IEnumerator LoadFlow()
    {
        string path = Application.streamingAssetsPath + "/hero.bundle";

        var bundleReq = AssetBundle.LoadFromFileAsync(path);
        yield return bundleReq;

        var bundle = bundleReq.assetBundle;
        if (bundle == null) yield break;

        var assetReq = bundle.LoadAssetAsync<GameObject>("HeroPrefab");
        yield return assetReq;

        var prefab = assetReq.asset as GameObject;
        Instantiate(prefab);
        Debug.Log("[Coroutine] Task complete");
    }
}
```

| Dimension | Assessment |
|-----------|------------|
| **Encapsulation merit** | Simple dependency handling. Inside the Manager, loading a dependency is just `yield return LoadDependency()` -- much clearer than polling. |
| **Encapsulation flaw 1** | **Difficult to return data.** `IEnumerator` cannot directly return values. The architect must design a `CoroutineRequest` wrapper class, or force the business layer to pass callbacks to receive results. Despite looking synchronous, it is still half-callback underneath. |
| **Encapsulation flaw 2** | **Tight coupling to `MonoBehaviour`.** If the Manager GameObject is unexpectedly destroyed, all running coroutines are **silently lost without errors**, causing logic to fail silently. |
| **Infectious pattern** | For the business layer to call the Manager, their own functions are often forced to be `IEnumerator`. This is infectious -- the entire project's code style becomes coroutine-style. Unity's early compromise solution: functional but not robust. |

### Approach 4: UniTask (Async/Await)

Concise, linear, strongly-typed.

```csharp
public class UniTaskExample : MonoBehaviour
{
    async void Start()
    {
        try
        {
            string path = Application.streamingAssetsPath + "/hero.bundle";

            var bundle = await AssetBundle.LoadFromFileAsync(path);

            if (bundle == null) return;

            var prefab = await bundle.LoadAssetAsync<GameObject>("HeroPrefab");

            Instantiate(prefab);
            Debug.Log("[UniTask] Task complete");
        }
        catch (Exception e)
        {
            Debug.LogError(e);
        }
    }
}
```

| Dimension | Assessment |
|-----------|------------|
| **Merit** | Perfect glue layer. Handles sequential logic like coroutines, is as efficient as callbacks, and supports concurrency control like polling (`UniTask.WhenAll`). |
| **Flaw 1** | **External dependency.** As a foundational framework, introducing a non-trivial third-party library is sometimes unacceptable. |
| **Flaw 2** | UniTask abstracts away too many details. Steep learning curve. |
| **Overall** | A modern solution, but the learning cost is also high. |

### Approach 5: Custom Awaiter (Native Async)

Via extension methods, enables `await` on `AssetBundleRequest` -- providing UniTask's elegant syntax without any third-party dependency.

```csharp
// Full implementation in Chapter 7; API usage preview:
public class NativeAsyncExample : MonoBehaviour
{
    async void Start()
    {
        string path = Application.streamingAssetsPath + "/hero.bundle";

        // Compiler calls our custom GetAwaiter()
        AssetBundleCreateRequest bundleReq = await AssetBundle.LoadFromFileAsync(path);
        var bundle = bundleReq.assetBundle;

        if (bundle == null) return;

        // Compiler calls AssetBundleRequest's GetAwaiter()
        AssetBundleRequest assetReq = await bundle.LoadAssetAsync("HeroPrefab");
        var prefab = assetReq.asset as GameObject;

        Instantiate(prefab);
        Debug.Log("[NativeAsync] Task complete");
    }
}
```

| Dimension | Assessment |
|-----------|------------|
| **Merit 1** | **Full control over internals.** Can embed monitoring code, performance instrumentation, or bridge callbacks into a custom `BundleRequest` queue inside `GetAwaiter`. |
| **Merit 2** | **Zero dependencies.** Requires no plugin; leverages pure C# language features. |
| **Merit 3** | **High performance.** Can use `struct` for the Awaiter, achieving **0 GC** allocation. |
| **Flaw** | Implementation complexity is high. Requires understanding `INotifyCompletion`, and must handle `SynchronizationContext` -- otherwise callbacks may not fire on the main thread. |
| **Overall** | Best demonstrates architectural capability -- wrapping complex low-level logic into an extremely simple high-level API. |

### Comparative Summary

| Approach | Linearity | Exception Handling | GC Pressure | Dependency | Coupling | Encapsulation Score |
|----------|-----------|-------------------|-------------|------------|----------|---------------------|
| Polling (Update) | Low | Good | None | None | None | Medium |
| Callback | Low | Poor | None | None | None | Low |
| Coroutine | Medium | Poor | Low | None | MonoBehavior | Medium |
| UniTask | High | Good | Low | UniTask DLL | None | High |
| Custom Awaiter | High | Good | Zero | None | None | Highest |

The custom Awaiter (Chapter 7) combines all advantages: linear code, full `try-catch` support, zero GC via struct, no external dependencies, no MonoBehaviour coupling.

---

## Chapter 7: Zero-Dependency Native Await Support

### Motivation

Async/await syntax is clearly superior to all other approaches. Developers typically achieve this by importing UniTask. But async/await is a **C# language feature**, not exclusive to the `Task` class. We can enable native `await` for `AssetBundle.LoadFromFileAsync` with ~50 lines of code and zero external dependencies.

### 1. Core Principle: Compiler Duck Typing

The C# compiler does **not** require the awaited object to be a `Task`. When you write `await obj;`, the compiler only checks whether `obj` satisfies these conditions:

1. It has a method called `GetAwaiter()`.
2. That method returns an object (the **Awaiter**).
3. The Awaiter implements `INotifyCompletion`.
4. The Awaiter has a `bool IsCompleted { get; }` property.
5. The Awaiter has a `GetResult()` method.

As long as `AssetBundleCreateRequest` is extended to satisfy these conditions, the compiler automatically generates the async state machine.

### 2. Implementation: Three Steps

#### Step 1: Define the Awaiter Struct

For extreme performance (**Zero GC**), use `struct` rather than `class` for the Awaiter. This avoids heap allocations during the await process.

```csharp
using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace YY.AsyncSupport
{
    // Awaiter must implement INotifyCompletion
    public struct AssetBundleCreateRequestAwaiter : INotifyCompletion
    {
        private AssetBundleCreateRequest _asyncOp;

        // Constructor: holds the Unity async operation object
        public AssetBundleCreateRequestAwaiter(AssetBundleCreateRequest asyncOp)
        {
            _asyncOp = asyncOp;
        }

        // 1. Status query: tells the compiler whether the task is already done.
        //    If true, compiler skips suspension and runs synchronously (performance optimization).
        public bool IsCompleted => _asyncOp.isDone;

        // 2. Get result: when task completes, compiler calls this to get the return value.
        //    Here we return AssetBundle directly rather than the Request itself, for ergonomics.
        public AssetBundle GetResult()
        {
            return _asyncOp.assetBundle;
        }

        // 3. Register callback: if not done, compiler wraps subsequent logic into an Action and passes it here.
        //    We attach this Action to Unity's native completed event.
        public void OnCompleted(Action continuation)
        {
            _asyncOp.completed += _ => continuation();
        }
    }
}
```

**Key design decisions in this struct:**

- **`struct` not `class`**: Allocated on the stack, zero GC. The compiler's generated state machine is also a struct, so the await infrastructure produces no heap allocations.
- **`GetResult()` returns `AssetBundle`**: Provides a clean `AssetBundle bundle = await LoadFromFileAsync(...)` syntax, hiding the intermediate `AssetBundleCreateRequest` from the caller.
- **`OnCompleted` wires to `_asyncOp.completed`**: Bridges the C# async state machine to Unity's native completion event. Because `completed` fires on the main thread, `continuation()` resumes on the main thread -- no `SynchronizationContext` required.

#### Step 2: Extension Method

The `GetAwaiter()` extension method attaches the Awaiter to `AssetBundleCreateRequest`.

```csharp
namespace YY.AsyncSupport
{
    public static class UnityAsyncExtensions
    {
        // Extension method name MUST be GetAwaiter (compiler convention)
        public static AssetBundleCreateRequestAwaiter GetAwaiter(this AssetBundleCreateRequest asyncOp)
        {
            return new AssetBundleCreateRequestAwaiter(asyncOp);
        }
    }
}
```

#### Step 3: Usage

```csharp
async void Start()
{
    AssetBundle bundle = await AssetBundle.LoadFromFileAsync(FileUtils.ToNativePath("tmps.b"));
    var strs = bundle.GetAllAssetNames();
    foreach (var s in strs)
    {
        Debug.LogError(s);
    }
}
```

### 3. Deep Dive: Decompiled IL State Machine Analysis

The C# compiler lowers `async` methods into a state machine struct. Below is the decompiled IL (via ILSpy) annotated with analysis.

```csharp
[StructLayout(LayoutKind.Auto)]
[CompilerGenerated]
private struct <Start>d__23 : IAsyncStateMachine
{
    public int <>1__state;
    public AsyncVoidMethodBuilder <>t__builder;
    private AssetBundleCreateRequestAwaiter <>u__1;

    private void MoveNext()
    {
        int num = <>1__state;
        try
        {
            AssetBundleCreateRequestAwaiter awaiter;
            if (num != 0)
            {
                awaiter = AssetBundle.LoadFromFileAsync(FileUtils.ToNativePath("tmps.b")).GetAwaiter();
                if (!awaiter.IsCompleted)
                {
                    num = (<>1__state = 0);
                    <>u__1 = awaiter;
                    <>t__builder.AwaitOnCompleted(ref awaiter, ref this);
                    return;
                }
            }
            else
            {
                awaiter = <>u__1;
                <>u__1 = default(AssetBundleCreateRequestAwaiter);
                num = (<>1__state = -1);
            }
            string[] allAssetNames = awaiter.GetResult().GetAllAssetNames();
            for (int i = 0; i < allAssetNames.Length; i++)
            {
                Debug.LogError((object)allAssetNames[i]);
            }
        }
        catch (Exception exception)
        {
            <>1__state = -2;
            <>t__builder.SetException(exception);
            return;
        }
        <>1__state = -2;
        <>t__builder.SetResult();
    }

    void IAsyncStateMachine.MoveNext() { this.MoveNext(); }

    [DebuggerHidden]
    private void SetStateMachine(IAsyncStateMachine stateMachine)
    {
        <>t__builder.SetStateMachine(stateMachine);
    }

    void IAsyncStateMachine.SetStateMachine(IAsyncStateMachine stateMachine)
    {
        this.SetStateMachine(stateMachine);
    }
}

[AsyncStateMachine(typeof(<Start>d__23))]
private void Start()
{
    <Start>d__23 stateMachine = default(<Start>d__23);
    stateMachine.<>t__builder = AsyncVoidMethodBuilder.Create();
    stateMachine.<>1__state = -1;
    stateMachine.<>t__builder.Start(ref stateMachine);
}
```

#### 3.1 Verification: Zero GC -- Struct State Machine

```csharp
[StructLayout(LayoutKind.Auto)]
[CompilerGenerated]
private struct <Start>d__23 : IAsyncStateMachine  // <--- struct, not class
```

The compiler-generated `d__23` is a **struct** (value type). When `Start()` is called, this state machine is allocated directly on the **stack**, not on the heap. By contrast, native `Task`-based async involves `Task` object heap allocation. This struct approach drastically reduces memory pressure.

#### 3.2 Verification: Fast Path -- Synchronous Shortcut

```csharp
if (num != 0)
{
    awaiter = AssetBundle.LoadFromFileAsync(...).GetAwaiter();

    // Fast Path check!
    if (!awaiter.IsCompleted)
    {
        // ... only enters slow path if not done ...
    }
}
```

The compiler-generated code checks `!awaiter.IsCompleted` first. If `LoadFromFileAsync` completes instantly (e.g., due to caching, or the resource was already in memory), the `if` block is skipped entirely. No suspension occurs, no callback is registered. The code flows straight to `GetResult()`. This is **fully equivalent to synchronous code with zero async overhead**.

#### 3.3 Verification: Slow Path -- Suspension and Callback Registration

```csharp
if (!awaiter.IsCompleted)
{
    num = (<>1__state = 0);  // 1. Save state: mark as "waiting on first await"
    <>u__1 = awaiter;        // 2. Save context: store awaiter in struct field to prevent
                              //    local variable from being lost

    // 3. Suspension logic
    //    Internally calls awaiter.OnCompleted(MoveNext)
    <>t__builder.AwaitOnCompleted(ref awaiter, ref this);

    return;                  // 4. Yield control!
}
```

- **State save**: `<>1__state = 0`. When `MoveNext` is called again later, it knows "I woke up from step 0."
- **Callback registration**: `AwaitOnCompleted` internally calls our `AssetBundleAwaiter.OnCompleted`, which we implemented as `req.completed += _ => continuation()`. The `continuation` is the `MoveNext` method itself (wrapped as a delegate).
- **`return`**: The method ends here. The main thread continues to other work (rendering the next frame).

#### 3.4 Verification: Resumption -- Back on the Main Thread

When Unity's native I/O completes, the `completed` event fires, which invokes `MoveNext()`:

```csharp
// num (state) is now 0
else
{
    awaiter = <>u__1;                              // 1. Restore context: retrieve awaiter from struct field
    <>u__1 = default(AssetBundleCreateRequestAwaiter); // Clean up field reference
    num = (<>1__state = -1);                       // 2. Reset state: mark as non-waiting (-1)
}

// 3. Get result
//    Both Fast Path and Slow Path converge here
string[] allAssetNames = awaiter.GetResult().GetAllAssetNames();

// 4. Execute subsequent logic
for (int i = 0; i < allAssetNames.Length; i++) { ... }
```

**This verifies why no `SynchronizationContext` is needed**: Unity's `req.completed` fires on the **main thread**, so `MoveNext()` is also called on the main thread. The code resumes from the `else` branch, restores the `awaiter` variable, and continues with `GetResult()` as if it was never interrupted.

#### 3.5 Summary of State Machine Mechanics

1. **Minimal structure**: Just an `if/else` state machine wrapped in a `struct`.
2. **Simple flow**:
   - If `IsCompleted` -> call `GetResult` directly (fast path).
   - If not -> save state -> register callback -> `return` (slow path).
   - Callback fires -> read state -> restore variable -> `GetResult` (resumption).
3. **Zero heap allocation**: Throughout the process, aside from the optional exception object in `SetException`, there are no `new Class()` operations.

### 4. Generalizing: Supporting All Unity Async Operations

Beyond loading bundles, Unity also has asset loading (`AssetBundleRequest`) and scene loading (`AsyncOperation`). A generic Awaiter can support all of them.

```csharp
using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace YY.AsyncSupport
{
    public static class ExtensionMethods
    {
        // 1. Support loading Bundle files
        public static UnityAsyncAwaiter<AssetBundleCreateRequest, AssetBundle> GetAwaiter(this AssetBundleCreateRequest op)
        {
            return new UnityAsyncAwaiter<AssetBundleCreateRequest, AssetBundle>(op);
        }

        // 2. Support loading Assets from Bundle
        public static UnityAsyncAwaiter<AssetBundleRequest, UnityEngine.Object> GetAwaiter(this AssetBundleRequest op)
        {
            return new UnityAsyncAwaiter<AssetBundleRequest, UnityEngine.Object>(op);
        }

        // 3. Support generic operations (e.g., scene loading, ResourceRequest)
        public static UnityAsyncAwaiter<AsyncOperation, AsyncOperation> GetAwaiter(this AsyncOperation op)
        {
            return new UnityAsyncAwaiter<AsyncOperation, AsyncOperation>(op);
        }
    }

    // Generic Awaiter struct
    // TRequest: Unity's async operation type
    // TResult: The result type we want await to return
    public struct UnityAsyncAwaiter<TRequest, TResult> : INotifyCompletion where TRequest : AsyncOperation
    {
        private TRequest _op;

        public UnityAsyncAwaiter(TRequest op) { _op = op; }

        public bool IsCompleted => _op.isDone;

        public void OnCompleted(Action continuation) => _op.completed += _ => continuation();

        public TResult GetResult()
        {
            // Return different results based on type
            if (_op is AssetBundleCreateRequest bundleReq)
                return (TResult)(object)bundleReq.assetBundle;

            if (_op is AssetBundleRequest assetReq)
                return (TResult)(object)assetReq.asset;

            // Default: return the operation itself
            return (TResult)(object)_op;
        }
    }
}
```

**Design notes on the generic version:**

- The double-generic `<TRequest, TResult>` allows each extension method to declare its own return type, while the constraint `where TRequest : AsyncOperation` ensures type safety.
- `GetResult()` uses runtime type checks (`is`) to return the appropriate result. Since these checks execute only at completion time (not in a hot path), the performance cost is negligible.
- The `(TResult)(object)` double-cast is a C# pattern for converting between unrelated generic types when the runtime type is guaranteed. The intermediate `object` cast satisfies the compiler.

### 5. Complete Integration Example

```csharp
using UnityEngine;
using YY.AsyncSupport;

public class HeroLoader : MonoBehaviour
{
    async void Start()
    {
        string bundlePath = Application.streamingAssetsPath + "/hero.bundle";

        Debug.Log("1. Starting bundle load...");

        AssetBundle bundle = await AssetBundle.LoadFromFileAsync(bundlePath);

        if (bundle == null)
        {
            Debug.LogError("Bundle load failed!");
            return;
        }

        Debug.Log("2. Bundle loaded, loading prefab...");

        var assetReq = await bundle.LoadAssetAsync("HeroPrefab");
        GameObject prefab = assetReq as GameObject;

        if (prefab != null)
        {
            Instantiate(prefab);
            Debug.Log("3. Instantiation successful!");
        }

        // Unload (demo; in production, managed by the Manager)
        bundle.Unload(false);
    }
}
```

### 6. Why This Beats UniTask

| Dimension | UniTask | Custom Awaiter (This Chapter) |
|-----------|---------|-------------------------------|
| **External dependency** | Requires UniTask DLL (~200 KB+) | **Zero** -- ~50 lines in-project |
| **GC pressure** | Low (UniTask uses structs internally) | **Zero** -- Awaiter and state machine are both structs |
| **Build size** | Adds a library | No impact |
| **Debugging** | Must step into library code | Code is yours -- breakpoints anywhere |
| **Upgrade safety** | UniTask version must track Unity version | No external version coupling |
| **API surface** | Large (UniTask has many features) | Minimal -- exactly what you need |
| **Future refactoring** | If you later decide to use UniTask, all business-layer `await` code **requires zero changes** -- the pattern is compatible | Same |

The custom Awaiter approach demonstrates that the C# compiler's duck-typed `await` allows a minimal, zero-dependency implementation that outperforms UniTask in both simplicity and performance while maintaining full compatibility with the async/await ecosystem.

### Key Takeaways (Chapter 7)

- **`async`/`await` is a C# language feature**, not tied to `Task` or `UniTask`. Any type with `GetAwaiter()` returning a type that implements `INotifyCompletion` + `IsCompleted` + `GetResult()` is awaitable.
- **Use `struct` for the Awaiter** to achieve zero GC -- the compiler-generated state machine is also a struct, so the entire await infrastructure is stack-allocated.
- **The compiler generates a fast path**: If `IsCompleted` is true, suspension and callback registration are skipped entirely, making already-complete operations run synchronously with zero overhead.
- **No `SynchronizationContext` needed** because Unity's `AsyncOperation.completed` fires on the main thread, so resumption naturally occurs on the main thread.
- **Business-layer code is future-proof**: Whether backed by a custom Awaiter, UniTask, or any other async mechanism, business code written with `await` requires no changes when the underlying implementation is swapped.
