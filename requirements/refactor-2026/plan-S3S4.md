# Sub-Plan S3+S4: ABManifest Binary Serialization + Runtime Integration

> **Risk**: Low-Medium (S3 is mechanical annotation + generation; S4 touches ManifestLoader load path)
> **Dependencies**: S1 (SerializationUtility) + S2 (BinaryCodec + code generator) completed
> **Status**: S3 DONE, S4 DONE — signed off 2026-04-19

---

## Objective

**S3**: Annotate ABManifest data classes with `[BinarySerializable]` / `[BinaryField]`, run the code generator, register Magic value, and verify round-trip correctness.

**S4**: Wire up runtime format auto-detection so ManifestLoader can transparently load both `.bin` and `.json` manifest files. Add binary export option to build side.

After S3+S4, the full serialization pipeline is operational end-to-end:
```
Build: ABManifest → BinaryCodec.Serialize → ABManifest.bin (with ABMF header)
Runtime: ABManifest.bin → SerializationUtility.Deserialize (auto-detect) → ABManifest → Initialize()
Compat: ABManifest.json → same Deserialize path → JSON fallback → ABManifest → Initialize()
```

---

## S3: Data Class Annotation + Code Generation

### Annotation Plan

4 classes annotated. Field order numbers are the serialization contract — once assigned, never reused.

**ABManifest** (top-level, has header):
```csharp
[BinarySerializable(Magic = 0x41424D46, SchemaVersion = 1)]  // 'ABMF'
public class ABManifest
{
    [BinaryField(0)] public string PackageName;
    [BinaryField(1)] public VersionNumber PackageVersion;
    [BinaryField(2)] public string BuildTimestamp;
    [BinaryField(3)] public List<ManifestAssetEntry> AssetEntries;
    [BinaryField(4)] public List<ManifestBundleEntry> BundleEntries;
    // Runtime index fields: NOT annotated (not serialized)
}
```

**ManifestAssetEntry** (nested, no header):
```csharp
[BinarySerializable]
public class ManifestAssetEntry
{
    [BinaryField(0)] public string EntryId;
    [BinaryField(1)] public string Address;
    [BinaryField(2)] public string PrimaryType;
    [BinaryField(3)] public List<string> Labels;
    [BinaryField(4)] public string SourcePath;
    [BinaryField(5)] public string Group;
    [BinaryField(6)] public bool AutoAddress;
    [BinaryField(7)] public int BundleIndex;
}
```

**ManifestBundleEntry** (nested, no header):
```csharp
[BinarySerializable]
public class ManifestBundleEntry
{
    [BinaryField(0)] public string BundleName;
    [BinaryField(1)] public string FileHash;
    [BinaryField(2)] public uint FileCRC;
    [BinaryField(3)] public long FileSize;
    [BinaryField(4)] public bool Encrypted;
    [BinaryField(5)] public int BundleType;
    [BinaryField(6)] public List<string> Tags;
    [BinaryField(7)] public int[] DependBundleIndices;
    // [NonSerialized] fields (IncludeAssets, ReferencedByBundleIndices): NOT annotated
}
```

**VersionNumber** (nested, no header):
```csharp
[BinarySerializable]
public class VersionNumber
{
    [BinaryField(0)] public int Major;
    [BinaryField(1)] public int Minor;
    [BinaryField(2)] public int Patch;
}
```

### Generated Files

Running the S2 code generator produces 4 files in `Utility/Serialization/Generated/`:

| Generated File | Source Class |
|---------------|-------------|
| ABManifest_BinarySerializer.cs | ABManifest (includes WriteWithHeader/ReadWithHeader) |
| ManifestAssetEntry_BinarySerializer.cs | ManifestAssetEntry |
| ManifestBundleEntry_BinarySerializer.cs | ManifestBundleEntry |
| VersionNumber_BinarySerializer.cs | VersionNumber |

### Magic Registration

```csharp
// Called during initialization (e.g., [RuntimeInitializeOnLoadMethod] or explicit setup)
BinaryCodec.Register<ABManifest>(
    magic: 0x41424D46,
    writer: ABManifest_BinarySerializer.WriteWithHeader,
    reader: ABManifest_BinarySerializer.ReadWithHeader
);
```

After registration, `SerializationUtility.DetectFormat` recognizes `0x41424D46` → routes to BinaryCodec.

### Design Notes

- `EBundleType` enum is not annotated — `ManifestBundleEntry.BundleType` is typed as `int`, handled natively
- `[NonSerialized]` fields have no `[BinaryField]` → generator skips them automatically
- `ToRuntimeEntry()`, `ContentEquals()`, `GetVersionString()`, operator overloads — all methods unaffected
- Existing `[Serializable]` attribute stays (needed for JsonUtility compatibility during transition)

---

## S4: Runtime Integration

### ManifestLoader File Search Order

S4 changes ManifestLoader to search for both `.bin` and `.json`, preferring binary:

```
Per directory (hotfix dir, then StreamingAssets):
  1. Try ABManifest.bin → if exists, load + auto-detect (Magic → binary)
  2. Try ABManifest.json → if exists, load + auto-detect (no Magic → JSON fallback)
  3. Neither exists → try next directory
```

This is the only behavioral change in S4. The `SerializationUtility.Deserialize<T>(byte[])` call from S1 already handles format detection — S4 just adds the `.bin` file search.

### ManifestLoader Changes

```csharp
// Constants (add to Constants.cs or ManifestLoader)
private const string ManifestFileNameBin = "ABManifest.bin";
private const string ManifestFileNameJson = Constants.MANIFEST_FILE_NAME; // "ABManifest.json"

// New: search directory for bin then json
private static async Task<ABManifest> TryLoadFromDirectory(string dir)
{
    // Prefer binary
    string binPath = Path.Combine(dir, ManifestFileNameBin);
    var manifest = await TryLoadFromFile(binPath);
    if (manifest != null) return manifest;

    // Fallback to JSON
    string jsonPath = Path.Combine(dir, ManifestFileNameJson);
    return await TryLoadFromFile(jsonPath);
}

// TryLoadFromFile unchanged from S1 (already uses SerializationUtility.Deserialize)
```

### Build-Side Export Option

BuildProjectManager gains a binary export option:

```csharp
// Existing (preserved): JSON export
SerializationUtility.WriteToFile(jsonPath, manifest);  // codecId defaults to "json"

// New (additive): Binary export alongside JSON
SerializationUtility.WriteToFile(binPath, manifest, "binary");
```

Transition strategy:
- Phase 1 (S4): export both `.json` + `.bin` side by side
- Phase 2 (after validation): export `.bin` only, drop `.json`
- Controlled by a simple bool flag in BuildProjectManager (not a feature-flag framework)

### ABManifest API Additions

```csharp
// Existing (kept for compatibility):
public static ABManifest DeserializeFromJson(string json) { ... }
public string SerializeToJson(bool prettyPrint = false) { ... }

// New convenience method:
public static ABManifest DeserializeFromFile(string path)
{
    var manifest = SerializationUtility.ReadFromFile<ABManifest>(path);
    manifest.Initialize();
    return manifest;
}
```

---

## Modified Files

| File | Change | Phase | Risk |
|------|--------|-------|------|
| ABManifest.cs | Add [BinarySerializable] + [BinaryField] on 5 fields + DeserializeFromFile method | S3+S4 | Low |
| ManifestAssetEntry.cs | Add [BinarySerializable] + [BinaryField] on 8 fields | S3 | Low |
| ManifestBundleEntry.cs | Add [BinarySerializable] + [BinaryField] on 8 fields | S3 | Low |
| VersionNumber (in VersionDataBase.cs) | Add [BinarySerializable] + [BinaryField] on 3 fields | S3 | Low |
| ManifestLoader.cs | Add TryLoadFromDirectory with .bin/.json search order | S4 | Low-Med |
| BuildProjectManager.cs | Add binary export alongside JSON (bool flag) | S4 | Low |
| Constants.cs | Add MANIFEST_FILE_NAME_BIN constant | S4 | Low |

---

## Task Breakdown

| Task | Content | Phase | Depends On |
|------|---------|-------|-----------|
| S3-T1 | Add [BinarySerializable] + [BinaryField] to VersionNumber | S3 | S2 done |
| S3-T2 | Add [BinarySerializable] + [BinaryField] to ManifestAssetEntry | S3 | S2 done |
| S3-T3 | Add [BinarySerializable] + [BinaryField] to ManifestBundleEntry | S3 | S2 done |
| S3-T4 | Add [BinarySerializable(Magic, SchemaVersion)] + [BinaryField] to ABManifest | S3 | S2 done |
| S3-T5 | Run code generator → verify 4 serializer files generated | S3 | T1-T4 |
| S3-T6 | Register ABManifest Magic in BinaryCodec | S3 | T5 |
| S3-T7 | Round-trip verification: construct test ABManifest → Serialize(binary) → Deserialize → field-by-field comparison | S3 | T6 |
| S4-T1 | Add MANIFEST_FILE_NAME_BIN constant | S4 | S3 done |
| S4-T2 | ManifestLoader: add TryLoadFromDirectory with .bin/.json search | S4 | T1 |
| S4-T3 | BuildProjectManager: add binary export flag + dual export (.json + .bin) | S4 | S3 done |
| S4-T4 | ABManifest: add DeserializeFromFile(path) convenience method | S4 | S1+S3 done |
| S4-T5 | Compilation verification + end-to-end test: build exports .bin → ManifestLoader loads .bin → Initialize() → query works | S4 | All above |

---

## Invariants (Must Hold After S3+S4)

1. Existing `.json` manifest files remain loadable (JSON fallback path)
2. Binary round-trip is lossless: Serialize → Deserialize produces field-identical ABManifest
3. ManifestLoader prefers `.bin` over `.json` when both exist
4. ManifestLoader falls back to `.json` when `.bin` doesn't exist (backward compat)
5. `ABManifest.Initialize()` is called on both binary and JSON paths
6. Build side can export both formats simultaneously (transition period)
7. `[NonSerialized]` fields (IncludeAssets, ReferencedByBundleIndices) are not serialized in binary
8. `dotnet build XLuaHotfix.sln` passes with 0 errors

---

## Not In Scope

- Binary serialization for VersionState / BuildIndex / Manifest (legacy backend artifacts, natural retirement)
- Compression (Flags field reserved but unused)
- Android StreamingAssets UnityWebRequest adaptation (deferred per existing decision)
- Removing JSON export entirely (keep dual export until validated)

---

## Approval Checklist

- [ ] Agree to field order assignments for all 4 data classes (as shown above)
- [ ] Agree to ABManifest Magic = 0x41424D46 ('ABMF'), SchemaVersion = 1
- [ ] Agree to ManifestLoader .bin-first/.json-fallback search order
- [ ] Agree to build-side dual export (.json + .bin) during transition
- [ ] Agree to keep existing DeserializeFromJson/SerializeToJson as compatibility API
