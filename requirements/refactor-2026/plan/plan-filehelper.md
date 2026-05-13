# Plan: FileHelper — Cross-Platform File I/O Utility

> **Risk**: Low (single static utility class, no architectural coupling)
> **Dependencies**: None (standalone utility in Helpers/Helper/)
> **Status**: Realized — FileHelper.cs already landed at Helpers/FileHelper.cs (8 methods: +Exists). All 3 call sites migrated (ManifestLoader/HotfixManager/ABHotfixBackend)
> **Positioning**: Cross-cutting infrastructure, same tier as NetworkDownloader / PathManager / SerializationUtility

---

## Objective

Create `FileHelper`, a general-purpose static utility class for file I/O operations. Fills the gap between path construction (PathManager), download (NetworkDownloader), and serialization (SerializationUtility) — the one missing primitive in the helper layer.

Addresses three problems: Android StreamingAssets read (File.ReadAllBytes fails on APK), partial write safety (process killed mid-write = corrupt file), and inconsistent error handling (delete failures have no single contract).

---

## Design Decisions

### D1: Positioning — Static Utility, Not Facade

FileHelper does NOT aim to replace all `System.IO` calls. It coexists with direct `File.*` / `Directory.*` usage where no added value exists. Callers choose FileHelper when they need cross-platform safety, atomicity, or consistent error semantics.

### D2: Async-First Read, Sync Write

- **Read**: `async Task` — Android StreamingAssets requires UnityWebRequest (async). Non-Android paths use `Task.Run(File.ReadAllBytes)` for main-thread offloading. Uniform async API.
- **Write**: synchronous — `File.WriteAllBytes` + `File.Move` (rename) are inherently synchronous and fast. No platform requires async write.

### D3: Atomic Write = Temp File + Rename

`File.Move` on the same filesystem is an atomic rename operation. Pattern: write temp file → delete target → rename temp to target. Temp filename uses `Guid` suffix to avoid collisions. On failure, temp file is NOT cleaned up (serves as recovery artifact).

### D4: TryDelete Returns bool, Never Throws

Deletion failures (permission, lock) are environmental, not logical errors. `TryDelete` returns false and logs a warning — caller decides whether to care. No try/catch at call sites.

### D5: Android Detection — Compile-Time + Runtime

```
#if UNITY_ANDROID && !UNITY_EDITOR
    if (path.StartsWith(Application.streamingAssetsPath))
        → UnityWebRequest
#endif
    → Task.Run(File.ReadAllBytes)
```

Compile-time `#if` eliminates dead code on non-Android builds. Runtime `StartsWith` check handles Android paths outside StreamingAssets (e.g., persistentDataPath).

### D6: No Synchronous Read API

Sync read (`byte[] ReadAllBytes(string)`) would silently fail on Android StreamingAssets. Async-only API prevents misuse. Editor code that needs sync reads continues using `File.ReadAllBytes` directly.

### D7: Assembly — Runtime

FileHelper lives in Runtime assembly (`Assets/FYAsset/Scripts/Helpers/Helper/`). Editor code can reference it. Only dependency: `UnityEngine.Networking` (UnityWebRequest), already available.

---

## API Specification

```csharp
/// <summary>
/// Cross-platform file I/O utility.
/// Positioning: same tier as NetworkDownloader / PathManager / SerializationUtility.
/// </summary>
public static class FileHelper
{
    // === Cross-Platform Reading ===

    /// <summary>
    /// Read entire file as byte array.
    /// Android StreamingAssets → UnityWebRequest (main thread required).
    /// Other paths / platforms → Task.Run(File.ReadAllBytes).
    /// </summary>
    public static async Task<byte[]> ReadAllBytesAsync(string path);

    /// <summary>
    /// Read entire file as string (UTF-8).
    /// Same platform branching as ReadAllBytesAsync.
    /// </summary>
    public static async Task<string> ReadAllTextAsync(string path);

    // === Atomic Writing ===

    /// <summary>
    /// Write byte array to file atomically.
    /// Writes to temp file first, then renames to target.
    /// Guarantees: target file is either old (complete) or new (complete), never partial.
    /// </summary>
    public static void WriteAllBytesAtomic(string path, byte[] data);

    /// <summary>
    /// Write string to file atomically (UTF-8).
    /// Same atomic pattern as WriteAllBytesAtomic.
    /// </summary>
    public static void WriteAllTextAtomic(string path, string text);

    // === Safe Deletion ===

    /// <summary>
    /// Delete file. Returns false (logs warning) on failure. Never throws.
    /// </summary>
    public static bool TryDelete(string path);

    /// <summary>
    /// Delete directory recursively. Returns false (logs warning) on failure. Never throws.
    /// </summary>
    public static bool TryDeleteDirectory(string path, bool recursive = true);

    // === Directory Helpers ===

    /// <summary>
    /// Create parent directory for a file path if it does not exist.
    /// Null or empty directory component → no-op.
    /// </summary>
    public static void EnsureDirectoryForFile(string filePath);
}
```

---

## Implementation Notes

### ReadAllBytesAsync

```csharp
public static async Task<byte[]> ReadAllBytesAsync(string path)
{
#if UNITY_ANDROID && !UNITY_EDITOR
    if (path.StartsWith(Application.streamingAssetsPath))
    {
        using var request = UnityWebRequest.Get(path);
        await request.SendWebRequest();
        if (request.result != UnityWebRequest.Result.Success)
            throw new IOException(
                $"[FileHelper] Failed to read StreamingAsset: {path}, " +
                $"error: {request.error}");
        return request.downloadHandler.data;
    }
#endif
    return await Task.Run(() =>
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"[FileHelper] File not found: {path}");
        return File.ReadAllBytes(path);
    });
}
```

### WriteAllBytesAtomic

```csharp
public static void WriteAllBytesAtomic(string path, byte[] data)
{
    EnsureDirectoryForFile(path);

    string tempPath = path + ".tmp." + Guid.NewGuid().ToString("N").Substring(0, 8);

    File.WriteAllBytes(tempPath, data);

    if (File.Exists(path))
        File.Delete(path);
    File.Move(tempPath, path);
}
```

Guid suffix prevents temp file collisions when two callers write the same target concurrently (edge case — build pipeline tasks are sequential, but defensive design costs nothing).

---

## New Files

| File | Path | Assembly | Lines (est.) | Description |
|------|------|----------|-------------|-------------|
| FileHelper.cs | Helpers/Helper/ | Runtime | ~120 | 7 static methods: ReadAllBytesAsync, ReadAllTextAsync, WriteAllBytesAtomic, WriteAllTextAtomic, TryDelete, TryDeleteDirectory, EnsureDirectoryForFile |

Path relative to `Assets/FYAsset/Scripts/`.

---

## Modified Files

| File | Change | Risk |
|------|--------|------|
| ManifestLoader.cs | `Task.Run(() => File.ReadAllBytes(path))` → `FileHelper.ReadAllBytesAsync(path)` | Low — same async signature, fixes Android bug |
| HotfixManager.cs | `LoadBuildIndexFromStreamingAssets`: replace `#if UNITY_ANDROID` block with `FileHelper.ReadAllTextAsync` | Low — behavior preserved, code simplified |
| ABHotfixBackend.cs | `File.WriteAllBytes(manifestPath, bytes)` → `FileHelper.WriteAllBytesAtomic(manifestPath, bytes)` (line ~142) | Low — atomic write, same data |

### Not Modified (deferred)

- `BuildProjectManager.cs` manual temp-file pattern — editor-only, works correctly. Replace when BuildProjectManager is refactored for E5 pipeline.
- Other `File.Delete` call sites — replace opportunistically with `FileHelper.TryDelete` when touching those files for other reasons.
- `LegacyHotfixBackend.cs` `File.WriteAllText` for version_state — legacy backend, not worth migration cost.

---

## Task Breakdown

| Task | Content | Depends On |
|------|---------|-----------|
| T1 | Create `FileHelper.cs` with 7 methods | — |
| T2 | Update `ManifestLoader.cs` — `File.ReadAllBytes` → `FileHelper.ReadAllBytesAsync` | T1 |
| T3 | Update `HotfixManager.cs` — `LoadBuildIndexFromStreamingAssets` → `FileHelper.ReadAllTextAsync` | T1 |
| T4 | Update `ABHotfixBackend.cs` — `File.WriteAllBytes` → `FileHelper.WriteAllBytesAtomic` | T1 |
| T5 | Compilation verification (`dotnet build XLuaHotfix.sln`) | T2, T3, T4 |

---

## Invariants (Must Hold After Completion)

1. `FileHelper.ReadAllBytesAsync` returns correct bytes for all platform/path combinations
2. Android StreamingAssets path triggers UnityWebRequest (verified by `#if UNITY_ANDROID` guard + `StartsWith` check)
3. `FileHelper.WriteAllBytesAtomic` produces a complete output file — no partial writes observable
4. `FileHelper.TryDelete` / `TryDeleteDirectory` never throw — all failures return false
5. `ManifestLoader` reads ABManifest correctly on all platforms (existing behavior preserved, Android fixed)
6. `HotfixManager.LoadBuildIndex` reads BuildIndex correctly on all platforms (existing behavior preserved)
7. `dotnet build XLuaHotfix.sln` passes with 0 errors

---

## Not In Scope

- Synchronous read API (intentionally excluded — D6)
- File.Copy / File.Move wrappers (semantics too variable across call sites)
- File.Exists wrapper (one-line System.IO call, no added value)
- Stream-level I/O (FileStream, StreamReader/Writer — belongs in HashGenerator or dedicated streaming utility)
- BuildProjectManager atomic write migration (editor-only, deferred to E5 refactor)
- LegacyHotfixBackend File.WriteAllText migration (legacy backend retirement path)

---

## Change Log

| Date | Change |
|------|--------|
| 2026-04-28 | Initial version: 7 design decisions, 5 tasks, 1 new file, 3 modified files |
