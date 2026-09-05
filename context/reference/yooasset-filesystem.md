# YooAsset FileSystem Abstraction Reference

> Source: YooAsset source code analysis (Runtime/FileSystem/)
> Purpose: Reference for XLuaHotfix B7 ABPackageBackend and future cache system design
> Language: English (AI consumption)

---

## 1. Architecture Overview

YooAsset isolates all storage I/O behind an `IFileSystem` interface. Multiple file system implementations handle different deployment scenarios (editor, built-in, cached, web). A ResourcePackage can compose multiple file systems.

```
ResourcePackage
  -> PlayModeImpl
       -> List<IFileSystem>  (ordered by priority)
            [0] DefaultBuildinFileSystem   (StreamingAssets)
            [1] DefaultCacheFileSystem     (downloaded/cached)
       
Bundle lookup: iterate file systems, first one where Belong(bundle)==true handles it
```

---

## 2. IFileSystem Interface Contract

```csharp
public interface IFileSystem
{
    // Lifecycle
    void OnCreate(string packageName, string packageRoot);
    void OnDestroy();
    
    // Configuration (injected via parameters)
    void SetParameter(string name, object value);
    
    // Initialization
    FSInitializeFileSystemOperation InitializeFileSystemAsync();
    
    // Manifest operations
    FSLoadPackageManifestOperation LoadPackageManifestAsync(string packageVersion, int timeout);
    FSRequestPackageVersionOperation RequestPackageVersionAsync(bool appendTimeTicks, int timeout);
    
    // File status queries
    bool Belong(PackageBundle bundle);     // Does this FS own this bundle?
    bool Exists(PackageBundle bundle);     // Is bundle available locally?
    bool NeedDownload(PackageBundle bundle);  // Must download before use?
    bool NeedUnpack(PackageBundle bundle);    // Must unpack from StreamingAssets?
    bool NeedImport(PackageBundle bundle);    // Must import from external source?
    
    // File operations
    FSDownloadFileOperation DownloadFileAsync(PackageBundle bundle, DownloadFileOptions options);
    FSLoadBundleOperation LoadBundleFile(PackageBundle bundle);
    
    // File access
    string GetBundleFilePath(PackageBundle bundle);
    byte[] ReadBundleFileData(PackageBundle bundle);
    string ReadBundleFileText(PackageBundle bundle);
    
    // Cache management
    FSClearCacheFilesOperation ClearCacheFilesAsync(PackageManifest manifest, ClearCacheFilesOptions options);
}
```

### Key Design: Strategy + Composition

- **Strategy**: Each IFileSystem implementation encapsulates a storage strategy
- **Composition**: PlayModeImpl holds a list of file systems, queries in priority order
- **Factory**: `FileSystemParameters.CreateFileSystem()` uses reflection to instantiate by class name string

---

## 3. Built-in File System Implementations

| Implementation | Directory | Scenario | Storage |
|---------------|-----------|----------|---------|
| DefaultEditorFileSystem | Editor/ | Editor simulation | AssetDatabase direct |
| DefaultBuildinFileSystem | Buildin/ | Offline/Host mode | StreamingAssets (read-only) |
| DefaultCacheFileSystem | Cache/ | Host mode (remote) | PersistentDataPath (read-write) |
| DefaultUnpackFileSystem | Unpack/ | Unpack mode | Unpack from StreamingAssets to writable |
| DefaultWebServerFileSystem | WebServer/ | WebGL local | Web server assets |
| DefaultWebRemoteFileSystem | WebRemote/ | WebGL remote | Remote download |

### Play Mode -> File System Mapping

| Play Mode | File Systems Used |
|-----------|-------------------|
| EditorSimulateMode | 1x EditorFileSystem |
| OfflinePlayMode | 1x BuildinFileSystem |
| HostPlayMode | BuildinFileSystem + CacheFileSystem |
| WebPlayMode | WebServerFileSystem + WebRemoteFileSystem |
| CustomPlayMode | User-configured list |

---

## 4. DefaultCacheFileSystem Internals

The most complex implementation, handling download, verification, and caching.

### 4.1 Directory Structure

```
{PackageRoot}/
  +-- CacheBundleFiles/       (verified bundle data + info files)
  |     +-- {hash_prefix}/
  |           +-- {BundleGUID}.data    (actual bundle content)
  |           +-- {BundleGUID}.info    (metadata: CRC, size, hash)
  +-- CacheManifestFiles/     (cached manifest files)
  +-- TempFiles/              (in-progress downloads)
```

### 4.2 Core Data Structures

```
_records: Dictionary<string, RecordFileElement>
  Key = BundleGUID (= FileHash)
  Value = { DataFilePath, InfoFilePath, CRC, Size }
  
_bundleDataFilePathMapping: BundleGUID -> data file path
_bundleInfoFilePathMapping: BundleGUID -> info file path
_tempFilePathMapping: BundleGUID -> temp download path
```

### 4.3 Verification Levels

| Level | Check | Speed |
|-------|-------|-------|
| Low | File exists | Fastest |
| Middle | File exists + size matches | Fast |
| High | File exists + CRC32 matches | Slowest, most reliable |

### 4.4 Cache Operations

- **WriteCacheBundleFile()**: Move from temp -> cache, record in _records
- **DeleteCacheBundleFile()**: Remove from _records, delete files
- **ClearAllCacheBundleFilesOperation**: Full cache wipe
- **ClearUnusedBundleFilesOperation**: Remove bundles not in current manifest

### 4.5 Download Management

DownloadCenterOperation coordinates all downloads for a package:

```
Configuration:
  DownloadMaxConcurrency: int      (max parallel downloads)
  DownloadMaxRequestPerFrame: int  (max new requests started per frame)
  
Features:
  - Resume: ResumeDownloadMinimumSize for large files
  - Retry: FailedTryAgain count per download
  - Watchdog: DownloadWatchDogTime to abort stuck requests
  - Fallback URLs: GetRemoteFallbackURL() for CDN failover
  - Deduplication: Multiple requests for same bundle share one download
```

### 4.6 Bundle Loading

```
LoadBundleFile(bundle):
  1. Get cached file path from _records
  2. If encrypted:
     a. Try IDecryptionServices.LoadAssetBundle() (sync)
     b. Try IDecryptionServices.LoadAssetBundleAsync() (async)
     c. Fallback: IDecryptionServices.LoadAssetBundleFallback() (LoadFromMemory)
  3. If not encrypted:
     AssetBundle.LoadFromFile(path)
  4. Return BundleResult wrapping the AssetBundle
```

---

## 5. Relevance to XLuaHotfix

### What to adopt:
- **IFileSystem concept**: Clean abstraction for "where do bundles live" - maps to our hotfix dir vs StreamingAssets fallback strategy
- **Cache verification levels**: Low/Middle/High is a practical trade-off pattern
- **Download concurrency control**: Max concurrency + per-frame limits prevent flooding
- **Resume + retry**: Essential for mobile hotfix downloads

### What differs in our design:
- **Our IPackageBackend is higher-level**: It abstracts the entire load path, not just file I/O. Consider whether B7 needs a lower-level file system abstraction beneath IPackageBackend
- **Our path strategy is simpler**: Hotfix dir first, fallback StreamingAssets (per B2 decision). No need for 6 file system types
- **Our download system exists**: NetworkDownloader already handles download + verification. May need concurrency improvements but not a full rewrite

### Future consideration:
If the project grows to need multiple package types (main game + DLC + mods), YooAsset's multi-package + multi-filesystem composition becomes more relevant. For now, our single-package approach is sufficient.
