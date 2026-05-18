# Sub-Plan B1: Asset Index Layer Abstraction (IAssetIndex)

> **Risk**: Low
> **Dependencies**: None
> **Estimated file changes**: 3 new files + 1 existing file
> **Status**: Completed and signed off

---

## Design Rationale (Why This Step Is Needed)

AssetPackageManager.Initialize() directly stores AddressableLabelsConfig as a private field _config,
and all GetKeysByLabel / GetKeysByType queries go through it. This hardcodes 'where the index data comes from'
inside AssetPackageManager, making it impossible to swap data sources when switching to custom AB.

**Approach**: Extract an IAssetIndex interface so AssetPackageManager depends only on the interface.
Whether the underlying implementation is AddressableLabelsConfig or custom ABManifest, the upper-layer code remains unchanged.

---

## Scope of Changes

| File | Change Type | Description |
|------|------------|-------------|
| New: IAssetIndex.cs | New | Query interface definition |
| AddressableLabelsConfig.cs | Modified | Implements IAssetIndex (adds interface, no structural changes) |
| New: ABAssetIndex.cs | New | Reads index from custom ABManifest (used in B4 phase; B1 creates skeleton only). **In B1, methods throw NotImplementedException; ABManifest format and parsing logic designed in B4.** |
| AssetPackageManager.cs | Modified | _config type changed to IAssetIndex, Initialize accepts IAssetIndex |

---

## IAssetIndex Interface Design

```csharp
/// <summary>
/// Asset index interface
/// Abstracts Label/Type -> Key query capability, decoupling from specific data source (Addressables or custom AB)
/// </summary>
public interface IAssetIndex
{
    /// <summary> Get all asset keys under a given label </summary>
    List<string> GetKeysByLabel(string label);

    /// <summary> Get all asset keys of a given type </summary>
    List<string> GetKeysByType(string type);

    /// <summary> Get all registered labels </summary>
    IEnumerable<string> GetLabels();

    /// <summary> Check whether a key is registered </summary>
    bool ContainsKey(string key);
}
```

---

## ABAssetIndex.cs Notes

ABAssetIndex in B1 is a skeleton class only (methods throw NotImplementedException). The actual ABManifest format and parsing logic will be designed in B4.

---

## AssetPackageManager Modification Notes

**Before** (hardcoded):
```csharp
private AddressableLabelsConfig _config;  // concrete type

public async Task Initialize()
{
    var handle = Addressables.LoadAssetAsync<AddressableLabelsConfig>(...);
    _config = await handle.Task;
    ...
}
```

**After** (interface-dependent):
```csharp
private IAssetIndex _index;  // depends on interface only

// Preserves original initialization method (backward compatible)
public async Task Initialize()
{
    var config = await LoadAddressableConfig();
    _index = config;  // AddressableLabelsConfig implements IAssetIndex
    ...
}

// New: supports injecting custom index (for ABPackageBackend use)
public void SetIndex(IAssetIndex index) { _index = index; }
```

All original `_config.GetKeysByLabel(...)` calls changed to `_index.GetKeysByLabel(...)`,
**AssetPackageManager external API remains completely unchanged**.

---

## Preservation Requirements (Must Pass)

- [ ] AddressableLabelsConfig existing serialization format unchanged (Unity .asset file compatibility)
- [ ] AssetPackageManager.GetKeysByLabel / GetKeysByType and other public methods unchanged
- [ ] Without calling SetIndex, defaults to original AddressableLabelsConfig initialization logic

---

## Acceptance Criteria

- [ ] Compiles without CS errors
- [ ] After AssetPackageManager.Initialize(), GetKeysByLabel returns same results as before refactoring
- [ ] Device testing: asset loading works normally (test with a scene containing multiple label types)

---

## Approval Checklist

- [x] Does IAssetIndex need additional methods (e.g., GetAllEntries)?
  **Decision**: Current four methods are sufficient (GetKeysByLabel, GetKeysByType, GetLabels, ContainsKey); GetAllEntries not needed.
- [x] Does ABAssetIndex have a reference ABManifest format, or design it during B4?
  **Decision**: Design during B4. B1's ABAssetIndex is a skeleton framework class only, no Manifest parsing logic.