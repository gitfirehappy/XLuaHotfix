# Sub-Plan B2: Asset Loading Layer Interface (IPackageBackend)

> **Risk**: Medium
> **Dependencies**: Execute after B1 completion
> **Estimated file changes**: 4 new files + 1 existing file
> **Status**: Completed (2026-03-18)

---

## Design Rationale (Why This Step Is Needed)

AAPackageManager currently calls Addressables.LoadAssetAsync / Release directly,
tightly coupling loading logic to Addressables.

**Approach**: Extract an IPackageBackend interface,
encapsulate Addressables calls inside AddressablesBackend,
and add ABPackageBackend implementing custom AB loading.
AAPackageManager calls through the interface only, enabling runtime backend switching.

This step does not affect the hotfix pipeline (CatalogUpdater / HotfixManager) — only the asset loading portion is replaced.

---

## Scope of Changes

| File | Change Type | Description |
|------|------------|-------------|
| New: IPackageBackend.cs | New | Asset loading backend interface |
| New: AddressablesBackend.cs | New | Extracted existing Addressables implementation from AAPackageManager (including ref-count cache) |
| New: ABPackageBackend.cs | New | Custom AB loading: dependency tree + ref counting |
| New: ABBundleLoader.cs | New | AB dependency chain loading core logic |
| AAPackageManager.cs | Modified | Internal loading changed to IPackageBackend, added SetBackend() |

---

## IPackageBackend Interface

```csharp
/// <summary>
/// Asset package loading backend interface
/// Isolates Addressables or custom AB underlying implementation; AAPackageManager loads/unloads assets through this interface
/// </summary>
public interface IPackageBackend
{
    #region Initialization

    /// <summary> Initialize backend (load Manifest or await Addressables.InitializeAsync) </summary>
    Task InitializeAsync();

    #endregion

    #region Asset Loading

    /// <summary> Async asset loading </summary>
    Task<T> LoadAssetAsync<T>(string key) where T : UnityEngine.Object;

    /// <summary> Sync asset loading (for scenarios requiring synchronous access like Lua require) </summary>
    T LoadAssetSync<T>(string key) where T : UnityEngine.Object;

    #endregion

    #region Asset Unloading

    /// <summary> Unload asset (implementation handles ref counting) </summary>
    void UnloadAsset(string key);

    #endregion

    #region Query

    /// <summary> Check whether an asset exists </summary>
    bool ContainsKey(string key);

    #endregion
}
```

---

## AAPackageManager Modification Notes

**Core change**: Internal load/unload calls changed to go through IPackageBackend; external API remains completely unchanged.

```csharp
// New backend field (defaults to Addressables backend)
private IPackageBackend _backend = new AddressablesBackend();

// New switching interface (for startup configuration)
public void SetBackend(IPackageBackend backend) { _backend = backend; }

// LoadAssetAsync internal call changed to:
var result = await _backend.LoadAssetAsync<T>(key);

// UnloadAsset internal call changed to:
_backend.UnloadAsset(key);
```

**Reference counting**: Existing ResourceEntry + ReferenceCount logic moved into AddressablesBackend internally.
ABPackageBackend implements its own reference counting. AAPackageManager no longer directly holds _resourceCache.

---

## ABPackageBackend Core Design

```csharp
/// <summary>
/// Custom AB package loading backend
/// Implements AB dependency tree loading, ref-count caching, sync/async dual-mode
/// </summary>
public class ABPackageBackend : IPackageBackend
{
    // AB bundle cache (bundle path -> AssetBundle)
    private readonly Dictionary<string, AssetBundle> _bundleCache = new();

    // Reference counting (bundle path -> ref count)
    private readonly Dictionary<string, int> _refCounts = new();

    // Key -> bundle path mapping (provided by ABAssetIndex)
    private readonly ABAssetIndex _index;

    // Note: When loading an asset, first query _index to find the containing bundle,
    // then recursively load all dependency bundles for that bundle (_refCounts incremented),
    // finally LoadAsset<T>() from the bundle.
    // On unload, decrement ref count by 1; when reaching 0, actually unload the bundle.
}
```

---

## ABBundleLoader Core Logic

ABBundleLoader handles the actual AB bundle I/O, providing both sync and async paths:

- **Sync**: `AssetBundle.LoadFromFile(path)` — for scenarios requiring blocking (e.g., Lua require)
- **Async**: `AssetBundle.LoadFromFileAsync(path)` — for `LoadAssetAsync<T>` call chain, avoiding main thread stalls

**Path resolution logic**: Prioritizes `PathManager.CurrentGUIDRoot` (hotfix directory); falls back to `Application.streamingAssetsPath` if file doesn't exist.

```csharp
// Path resolution example
string ResolveBundlePath(string bundleName)
{
    var hotfixPath = Path.Combine(PathManager.CurrentGUIDRoot, bundleName);
    if (File.Exists(hotfixPath)) return hotfixPath;
    return Path.Combine(Application.streamingAssetsPath, bundleName);
}
```

---

## Preservation Requirements (Must Pass)

- [ ] All AAPackageManager public method signatures unchanged
- [ ] HotfixManager / XLuaLoader / NetworkDownloader require no modifications
- [ ] Default backend is AddressablesBackend; behavior identical to pre-refactoring without SetBackend

---

## Acceptance Criteria

- [ ] Compiles successfully
- [ ] Using AddressablesBackend (default): all asset loading behavior identical to pre-refactoring
- [ ] Using ABPackageBackend: correctly loads AB bundles with dependencies
- [ ] Ref counting correct: same asset Load x2 + Unload x1 still remains loaded

---

## Approval Checklist

- [x] Should ABPackageBackend read AB bundle paths from StreamingAssets or hotfix directory (PathManager.CurrentGUIDRoot)?
  **Decision**: Hotfix directory (PathManager.CurrentGUIDRoot) prioritized, fallback to StreamingAssets. Current path management and bundle isolation mechanisms are well-established; no need to modify root path logic. If future write access to game install directory is needed, only the root path needs modification.
- [x] SetBackend switching timing: at GameLauncher startup, or support runtime dynamic switching?
  **Decision**: One-time configuration at GameLauncher startup; no runtime dynamic switching.
- [x] Does ABBundleLoader need async loading support (LoadFromFileAsync), or is sync sufficient?
  **Decision**: Must support LoadFromFileAsync. The project uses async AA loading APIs; the AB backend also needs corresponding async support. ABBundleLoader needs both LoadFromFile (sync) and LoadFromFileAsync (async) methods.