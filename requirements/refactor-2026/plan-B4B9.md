# Sub-Plan B4+B9: Hotfix Pipeline Interface Separation + AB Backend Implementation

> **Risk**: High (hotfix core pipeline, startup critical path)
> **Dependencies**: Phase S (SerializationUtility) completed; Phase 3 (ABManifest + ABBundleLoader + ABPackageBackend) completed
> **Status**: DONE

---

## Objective

Refactor HotfixManager from a monolithic Addressables-coupled static class into an orchestrator + backend architecture (matching AssetPackageManager's pattern), and implement the AB hotfix backend that replaces Addressables catalog/version_state with ABManifest.

After B4+B9:
- HotfixManager becomes a pure orchestrator (shared steps only)
- IHotfixPipeline interface defines backend-specific hooks
- LegacyHotfixBackend wraps existing Addressables flow (zero behavioral change)
- ABHotfixBackend implements full hotfix flow using ABManifest
- `Constants.USE_AB_BACKEND` controls the entire pipeline (hotfix + index + loading backend)
- NetworkDownloader relocated to shared infrastructure

---

## Background

Current HotfixManager (554 lines, static class) executes a 9-step hotfix flow tightly coupled with Addressables:

```
Step 0: LoadBuildIndex + PathManager init
Step 1: Addressables.InitializeAsync          ← Addressables-only
Step 2: Download manifest.json (package pointer)
Step 3: Download version_state.json + version compare
Step 4: Download bundles (hash-copy optimization)
Step 5: Download catalog.json + save version_state  ← Addressables-only
Step 6: Apply update (manifest pointer + PathManager.Switch)
Step 7: CatalogUpdater.LoadExternalCatalog     ← Addressables-only
Step 8: AssetPackageManager.Initialize
```

Steps 1, 5, 7 are Addressables-exclusive. Steps 3, 4 use VersionState as data source (replaceable by ABManifest). Steps 0, 2, 6, 8 are fully shared.

---

## Confirmed Design Decisions

### D1: Architecture Pattern — Interface + Backend Separation

Matches AssetPackageManager's pattern. HotfixManager stays as static orchestrator; backend-specific logic delegated to IHotfixPipeline implementations.

Rationale: engineering discipline and technical accumulation, even for non-production projects.

### D2: VersionState Retirement

AB backend does NOT use version_state.json. ABManifest contains PackageVersion + BundleEntries (name/hash/size), fully covering VersionState's functionality. VersionState retires naturally with Legacy backend.

### D5: NetworkDownloader Relocation

Move from `LegacyRuntime/` to `Helpers/Helper/`. Pure download utility with no Addressables dependency. Both backends share it.

### D7: Interface Granularity — Fine-Grained Hooks

HotfixManager (orchestrator) controls shared step sequencing, progress callbacks, and error handling. IHotfixPipeline exposes only backend-specific hooks. 5 methods, each one sentence to explain.

### D10: Global Backend Switch

`Constants.USE_AB_BACKEND` (single const bool) controls the entire chain: hotfix pipeline + asset index + loading backend. AssetPackageManager's `USE_AB_INDEX` replaced by reading this constant.

### D11: HotfixManager Stays Static

IHotfixPipeline instance created inside `InitializeAsync()` based on `Constants.USE_AB_BACKEND`. No lifecycle management needed — hotfix runs once at startup.

### D12: Addressables Cache Cleanup Compatibility

`CheckAndCleanIfNewBuild` cleans `com.unity.addressables` cache directory in both backends (one-time migration cleanup for users switching from Legacy to AB). Directory won't exist after first AB run, code becomes no-op.

### D17: Bundle List in HotfixVersionInfo

HotfixVersionInfo includes `IReadOnlyList<BundleDownloadItem> Bundles` — straightforward data adaptation, not over-wrapped. Orchestrator accesses `localInfo.Bundles` directly for hash-copy optimization.

---

## Interface Design

### IHotfixPipeline (5 methods)

```csharp
/// <summary>
/// Hotfix backend interface. Orchestrator handles shared steps;
/// backend implements only its unique steps.
/// </summary>
public interface IHotfixPipeline
{
    /// <summary>
    /// Backend initialization.
    /// Legacy: Addressables.InitializeAsync. AB: no-op (return true).
    /// </summary>
    Task<bool> InitializeBackendAsync();

    /// <summary>
    /// Load local version info from currentGUIDRoot for version comparison.
    /// Legacy: reads version_state.json. AB: reads ABManifest.json/bin.
    /// Returns null if no local version exists (first install).
    /// </summary>
    Task<HotfixVersionInfo> LoadLocalVersionAsync(string currentGUIDRoot);

    /// <summary>
    /// Download and parse remote version info.
    /// Legacy: downloads version_state.json. AB: downloads ABManifest.bin/json.
    /// </summary>
    Task<HotfixVersionInfo> FetchRemoteVersionAsync(string remoteUrlRoot);

    /// <summary>
    /// Extract bundle download list from remote version info.
    /// Converts backend-specific data to unified BundleDownloadItem list.
    /// </summary>
    IReadOnlyList<BundleDownloadItem> GetBundleDownloadList(HotfixVersionInfo remoteInfo);

    /// <summary>
    /// Post-download finalization.
    /// Legacy: download catalog.json + save version_state.json + LoadExternalCatalog.
    /// AB: save cached ABManifest data to target directory.
    /// </summary>
    Task<bool> PostDownloadAsync(HotfixContext ctx);
}
```

### HotfixVersionInfo (unified version view)

```csharp
public class HotfixVersionInfo
{
    public VersionNumber Version;
    public int BundleCount;
    public long TotalSize;
    public IReadOnlyList<BundleDownloadItem> Bundles;
}
```

### BundleDownloadItem (unified bundle entry)

```csharp
public struct BundleDownloadItem
{
    public string BundleName;
    public string FileHash;
    public long FileSize;
}
```

### HotfixContext (shared state)

```csharp
public class HotfixContext
{
    public BuildIndexData BuildIndex;
    public string TargetPackageName;
    public string RemoteUrlRoot;
    public string TargetGUIDRoot;   // download target directory
}
```

---

## Orchestrator Flow (HotfixManager.InitializeAsync)

```
HotfixManager.InitializeAsync()
│
├─  1. LoadBuildIndex + PathManager.Initialize              [shared]
├─  2. CheckAndCleanIfNewBuild (incl. AA cache cleanup)     [shared]
├─  3. pipeline = CreatePipeline(Constants.USE_AB_BACKEND)  [shared]
├─  4. pipeline.InitializeBackendAsync()                    [backend]
├─  5. DownloadManifestPointer (manifest.json)              [shared]
├─  6. localInfo = pipeline.LoadLocalVersionAsync()         [backend]
├─  7. remoteInfo = pipeline.FetchRemoteVersionAsync()      [backend]
├─  8. CompareVersion(localInfo, remoteInfo)                [shared]
├─  9. downloadList = pipeline.GetBundleDownloadList()      [backend]
├─ 10. DownloadBundles(downloadList, hash-copy from localInfo.Bundles) [shared]
├─ 11. pipeline.PostDownloadAsync(ctx)                      [backend]
├─ 12. ApplyUpdate (manifest pointer + PathManager.Switch)  [shared]
└─ 13. AssetPackageManager.Initialize()                     [shared]
```

Shared steps: 1, 2, 3, 5, 8, 10, 12, 13 (8 steps)
Backend steps: 4, 6, 7, 9, 11 (5 steps = IHotfixPipeline's 5 methods)

---

## Backend Implementations

### ABHotfixBackend

```
InitializeBackendAsync    → return true (no-op)
LoadLocalVersionAsync     → read ABManifest.json/bin from currentGUIDRoot via SerializationUtility
                            → parse → build HotfixVersionInfo (PackageVersion + BundleEntries)
                            → return null if file not found
FetchRemoteVersionAsync   → NetworkDownloader.DownloadBytes(remoteUrlRoot + "/ABManifest.bin")
                            → fallback: DownloadText(remoteUrlRoot + "/ABManifest.json")
                            → cache raw bytes (_remoteManifestData) for PostDownload
                            → parse → build HotfixVersionInfo
GetBundleDownloadList     → convert ABManifest.BundleEntries to BundleDownloadItem[]
PostDownloadAsync         → write _remoteManifestData to ctx.TargetGUIDRoot/ABManifest.bin (or .json)
```

Internal state:
- `byte[] _remoteManifestData` — cached raw bytes from FetchRemoteVersion
- `ABManifest _remoteManifest` — parsed object for GetBundleDownloadList

### LegacyHotfixBackend

```
InitializeBackendAsync    → Addressables.InitializeAsync (extracted from current Step 1)
LoadLocalVersionAsync     → read version_state.json from currentGUIDRoot
                            → parse → build HotfixVersionInfo (version + bundles)
FetchRemoteVersionAsync   → NetworkDownloader.DownloadText(remoteUrlRoot + "/version_state.json")
                            → cache raw JSON (_remoteVersionJson) for PostDownload
                            → parse → build HotfixVersionInfo
GetBundleDownloadList     → convert VersionState.bundles to BundleDownloadItem[]
PostDownloadAsync         → download catalog.json to ctx.TargetGUIDRoot
                            → write _remoteVersionJson to ctx.TargetGUIDRoot/version_state.json
                            → CatalogUpdater.LoadExternalCatalog (extracted from current Step 7)
```

Internal state:
- `string _remoteVersionJson` — cached raw JSON from FetchRemoteVersion
- `VersionState _remoteVersionState` — parsed object for GetBundleDownloadList

---

## Remote Directory Structure

### Legacy Backend

```
CDN/{packageName}/
  ├── version_state.json
  ├── catalog.json
  └── bundles/
      └── *.bundle
```

### AB Backend

```
CDN/{packageName}/
  ├── ABManifest.bin (primary) or ABManifest.json (fallback)
  └── bundles/
      └── *.bundle
```

AB backend: 1 metadata file (vs Legacy's 2). One fewer network request.

---

## New Files

| File | Path | Lines (est.) | Description |
|------|------|-------------|-------------|
| IHotfixPipeline.cs | Runtime/Hotfix/ | ~50 | Interface + HotfixVersionInfo + BundleDownloadItem + HotfixContext |
| ABHotfixBackend.cs | Runtime/Hotfix/ | ~120 | AB backend: ABManifest download/parse/save |
| LegacyHotfixBackend.cs | Runtime/Hotfix/ | ~100 | Legacy backend: extracted from HotfixManager Steps 1/3/5/7 |

All paths relative to `Assets/AboutXLua/Scripts/Core/Hotfix_AssetPackageManage/`.

---

## Modified Files

| File | Change | Risk |
|------|--------|------|
| HotfixManager.cs | Refactor to orchestrator: extract backend-specific steps to IHotfixPipeline, add CreatePipeline switch, replace static fields with HotfixContext | Medium — core flow restructure, but logic preserved |
| Constants.cs | Add `USE_AB_BACKEND` const bool | Low — additive |
| AssetPackageManager.cs | Replace `USE_AB_INDEX` with `Constants.USE_AB_BACKEND` | Low — rename only |
| NetworkDownloader.cs | Move from LegacyRuntime/ to Helpers/Helper/ | Low — path change only |

---

## Files NOT Modified

| File | Reason |
|------|--------|
| CatalogUpdater.cs | Referenced only by LegacyHotfixBackend, no changes needed |
| PathManager.cs | Shared infrastructure, no changes needed |
| ABManifest.cs / ManifestLoader.cs | Already implemented in Phase 3, used as-is by ABHotfixBackend |
| ABBundleLoader.cs / ABPackageBackend.cs | Already implemented in Phase 3, used by AssetPackageManager |

---

## Task Breakdown

| Task | Content | Depends On |
|------|---------|-----------|
| B4B9-T1 | Create `IHotfixPipeline.cs` (interface + HotfixVersionInfo + BundleDownloadItem + HotfixContext) | — |
| B4B9-T2 | Add `Constants.USE_AB_BACKEND` + update `AssetPackageManager.USE_AB_INDEX` to read it | T1 |
| B4B9-T3 | Move `NetworkDownloader.cs` from LegacyRuntime/ to Helpers/Helper/ + update .csproj if needed | — |
| B4B9-T4 | Create `LegacyHotfixBackend.cs` — extract Steps 1/3/5/7 from HotfixManager into interface methods | T1 |
| B4B9-T5 | Create `ABHotfixBackend.cs` — implement 5 interface methods using ABManifest + SerializationUtility + NetworkDownloader | T1+T3 |
| B4B9-T6 | Refactor `HotfixManager.cs` to orchestrator — replace inline steps with pipeline.XxxAsync() calls, replace static fields with HotfixContext, add CreatePipeline switch | T1+T2+T4+T5 |
| B4B9-T7 | Compilation verification (dotnet build) | T6 |
| B4B9-T8 | Legacy path verification: USE_AB_BACKEND=false, confirm zero behavioral change vs current code | T7 |

---

## Invariants (Must Hold After B4+B9)

1. With `USE_AB_BACKEND = false`: hotfix flow is byte-identical to current behavior (Legacy path preserved)
2. With `USE_AB_BACKEND = true`: hotfix flow downloads ABManifest instead of version_state + catalog, skips Addressables init
3. HotfixManager orchestrator controls all progress callbacks and error reporting (backends don't touch OnProgress/OnError)
4. Hash-copy optimization works identically in both backends (same Dict<hash, name> → File.Copy logic)
5. `CheckAndCleanIfNewBuild` cleans Addressables cache in both backends (one-time migration safety)
6. NetworkDownloader is accessible from both LegacyRuntime/ and Runtime/ code
7. `Constants.USE_AB_BACKEND` is the single switch controlling hotfix pipeline + asset index + loading backend
8. `dotnet build XLuaHotfix.sln` passes with 0 errors

---

## Not In Scope

- Build-side ABManifest generation (Phase 6 E6)
- Download progress per-bundle granularity improvements (future enhancement)
- Resumable downloads / checkpoint persistence (future enhancement)
- Android StreamingAssets UnityWebRequest adaptation (deferred per existing decision)
- HotfixManager unit tests (manual acceptance as primary, per project decision)
- Removing Legacy backend code (natural retirement, no forced cleanup)

---

## Approval Checklist

- [ ] Agree to IHotfixPipeline interface (5 methods: InitBackend / LoadLocalVersion / FetchRemoteVersion / GetBundleDownloadList / PostDownload)
- [ ] Agree to HotfixVersionInfo + BundleDownloadItem + HotfixContext data structures
- [ ] Agree to HotfixManager staying static, refactored to orchestrator pattern
- [ ] Agree to `Constants.USE_AB_BACKEND` as single global switch
- [ ] Agree to NetworkDownloader relocation to Helpers/Helper/
- [ ] Agree to VersionState retirement (AB backend does not use version_state.json)
- [ ] Agree to Addressables cache cleanup in both backends (one-time migration)
- [ ] Agree to LegacyHotfixBackend preserving exact current behavior (zero change)
