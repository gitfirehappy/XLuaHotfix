# ABManifest Format Specification

> **Status**: APPROVED - All design decisions confirmed 2026-03-30
> **Phase**: 3 (Runtime Implementation Layer)
> **Created**: 2026-03-30
> **Dependencies**: B5-1 (RuntimeAssetEntry), B5-2 (Resolve/Load API)
> **Consumers**: B6 (ABAssetIndex), B7 (ABPackageBackend), B4 (Hotfix Catalog), B9 (Incremental Download), E6 (Build Export)

---

## 1. Problems This Manifest Solves

| # | Problem | Description | Consumer |
|---|---------|-------------|----------|
| P1 | Asset-to-Bundle mapping | Given an Address/EntryId, find which Bundle contains it | ABAssetIndex (runtime) |
| P2 | Bundle dependency graph | Before loading a Bundle, know all dependent Bundles to load first | ABBundleLoader (runtime) |
| P3 | Bundle integrity verification | Hash + CRC + FileSize to verify downloaded/cached files | Runtime + Hotfix downloader |
| P4 | Incremental update comparison | Compare two Manifest versions to find changed/added/deleted Bundles | HotfixManager |
| P5 | Download manifest generation | Know each Bundle size for download list + progress estimation | Hotfix downloader |
| P6 | Multi-dimensional asset lookup | Lookup by Address, by TypeKey, by Labels | AssetResolver (runtime) |

---

## 2. Design Decisions (Approved)

| # | Decision | Choice | Rationale |
|---|----------|--------|-----------|
| D1 | Serialization format | JSON first, binary later | Fast iteration in Phase 3; binary optimization deferred to build pipeline phase |
| D2 | Asset-Bundle referencing | int index into BundleList | Compact serialization; manifest is build-once-read-only |
| D3 | Dependency level | Bundle-level only | Simple + sufficient; runtime recursive resolution is lightweight (shallow depth, cached) |
| D4 | EntryId storage | string (Unity GUID) | Consistent with existing RuntimeAssetEntry; no premature optimization |
| D5 | PrimaryType storage | string (direct) | String table optimization deferred to binary phase |
| D6 | Labels storage | string[] (direct) | Same as D5 |
| D7 | Version format | Reuse existing VersionNumber | Already used by DifferentialProcessor |

---

## 3. Data Model

### 3.1 ABManifest (Top-Level)

The root object representing a complete resource manifest for one build.

| Field | Type | Description |
|-------|------|-------------|
| FormatVersion | string | Manifest format version (e.g. "1.0"). Used for forward compatibility |
| PackageName | string | Package identifier (e.g. "MainPackage") |
| PackageVersion | VersionNumber | Semantic version from existing VersionNumber class |
| BuildTimestamp | string | ISO 8601 build time for debugging |
| AssetEntries | ManifestAssetEntry[] | All asset entries |
| BundleEntries | ManifestBundleEntry[] | All bundle entries |

**Runtime-only fields** (not serialized, built on Initialize()):

| Field | Type | Purpose |
|-------|------|---------|
| _addressIndex | Dictionary<string, List<int>> | Address -> AssetEntry indices (supports duplicate addresses) |
| _entryIdIndex | Dictionary<string, int> | EntryId -> AssetEntry index (unique) |
| _typeIndex | Dictionary<string, List<int>> | PrimaryType -> AssetEntry indices |
| _labelIndex | Dictionary<string, List<int>> | Label -> AssetEntry indices |
| _bundleNameIndex | Dictionary<string, int> | BundleName -> BundleEntry index |

### 3.2 ManifestAssetEntry (Per-Asset Record)

Maps directly to RuntimeAssetEntry after deserialization, with added Bundle binding.

| Field | Type | Serialized | Description |
|-------|------|------------|-------------|
| EntryId | string | Yes | Unity GUID, unique identifier |
| Address | string | Yes | Logical name (may duplicate across assets) |
| PrimaryType | string | Yes | Asset type name (e.g. "Texture2D", "GameObject") |
| Labels | string[] | Yes | Classification tags |
| SourcePath | string | Yes | Asset path in project (editor diagnostics) |
| Group | string | Yes | Build group name (editor diagnostics) |
| AutoAddress | bool | Yes | Whether Address was auto-generated |
| BundleIndex | int | Yes | Index into ABManifest.BundleEntries[] |

### 3.3 ManifestBundleEntry (Per-Bundle Record)

| Field | Type | Serialized | Description |
|-------|------|------------|-------------|
| BundleName | string | Yes | Unique bundle file name (e.g. "group_assets_label_hash.bundle") |
| FileHash | string | Yes | Content hash (MD5 or SHA256), used for integrity + update comparison |
| FileCRC | uint | Yes | CRC32 checksum for quick verification |
| FileSize | long | Yes | File size in bytes, used for download progress |
| Encrypted | bool | Yes | Whether bundle is encrypted |
| Tags | string[] | Yes | Classification tags (for selective download by tag) |
| DependBundleIndices | int[] | Yes | Indices into ABManifest.BundleEntries[] for direct dependencies |

**Runtime-only fields** (built on Initialize()):

| Field | Type | Purpose |
|-------|------|---------|
| IncludeAssets | List<ManifestAssetEntry> | Assets contained in this bundle (reverse lookup) |

---

## 4. Initialization Flow

After deserialization, `ABManifest.Initialize()` builds runtime lookup structures:

```
1. Build _entryIdIndex:    for each AssetEntry, map EntryId -> index
2. Build _addressIndex:    for each AssetEntry, map Address -> list of indices
3. Build _typeIndex:       for each AssetEntry, map PrimaryType -> list of indices
4. Build _labelIndex:      for each AssetEntry, for each Label, map Label -> list of indices
5. Build _bundleNameIndex: for each BundleEntry, map BundleName -> index
6. Build IncludeAssets:    for each AssetEntry, add to BundleEntries[BundleIndex].IncludeAssets
```

All lookups use for-loops (no LINQ) to avoid runtime GC pressure.

---

## 5. Key Operations

### 5.1 Asset Resolution (consumed by AssetResolver)

```
ResolveByAddress(address):
  1. _addressIndex.TryGetValue(address) -> indices
  2. If 0 hits -> NotFound
  3. If 1 hit -> return AssetEntries[index]
  4. If N hits -> Conflict (caller decides)

ResolveByEntryId(entryId):
  1. _entryIdIndex.TryGetValue(entryId) -> index
  2. return AssetEntries[index] or NotFound

ResolveByTypeKey(type, address):
  1. _typeIndex.TryGetValue(type) -> type_indices
  2. Filter by address match -> result
  3. Return matched entry or NotFound

ResolveByLabels(labels):
  1. For each label, _labelIndex.TryGetValue(label) -> indices
  2. Intersect all label index sets
  3. Return matching entries
```

### 5.2 Bundle Dependency Resolution (consumed by ABBundleLoader)

```
GetBundleForAsset(assetEntry):
  1. return BundleEntries[assetEntry.BundleIndex]

GetAllDependencies(bundleEntry):
  1. result = []
  2. for each depIndex in bundleEntry.DependBundleIndices:
       result.Add(BundleEntries[depIndex])
  3. return result
  // Note: recursive expansion handled by ABBundleLoader, not Manifest
```

### 5.3 Incremental Update Comparison (consumed by HotfixManager)

```
CompareManifests(localManifest, remoteManifest):
  1. Build localBundleMap: BundleName -> ManifestBundleEntry
  2. For each remote bundle:
     - If not in localBundleMap -> NEW (must download)
     - If in localBundleMap but FileHash differs -> CHANGED (must download)
     - If in localBundleMap and FileHash matches -> UNCHANGED (skip)
  3. For each local bundle not in remote -> DELETED (can clean up)
  4. Return: { toDownload: [], toDelete: [], totalDownloadSize: long }
```

---

## 6. Serialization Format (Phase 3: JSON)

Using Unity `JsonUtility` for serialization. The manifest file is named `ABManifest_{PackageVersion}.json`.

Example JSON structure:

```json
{
  "FormatVersion": "1.0",
  "PackageName": "MainPackage",
  "PackageVersion": { "Major": 1, "Minor": 0, "Patch": 3 },
  "BuildTimestamp": "2026-03-30T14:30:00Z",
  "AssetEntries": [
    {
      "EntryId": "a1b2c3d4e5f6...",
      "Address": "PlayerPrefab",
      "PrimaryType": "GameObject",
      "Labels": ["Character", "Player"],
      "SourcePath": "Assets/Prefabs/Player.prefab",
      "Group": "Characters",
      "AutoAddress": true,
      "BundleIndex": 0
    }
  ],
  "BundleEntries": [
    {
      "BundleName": "characters_assets_all_abc123.bundle",
      "FileHash": "d41d8cd98f00b204e9800998ecf8427e",
      "FileCRC": 3456789012,
      "FileSize": 1048576,
      "Encrypted": false,
      "Tags": ["Character"],
      "DependBundleIndices": [1, 2]
    }
  ]
}
```

---

## 7. Relationship to Existing Data Structures

### 7.1 ManifestAssetEntry vs RuntimeAssetEntry

`ManifestAssetEntry` is the **serialized form** of `RuntimeAssetEntry` with an added `BundleIndex` field.

Conversion on deserialization:
```
ManifestAssetEntry -> RuntimeAssetEntry:
  - EntryId, Address, PrimaryType, Labels, SourcePath, Group, AutoAddress: direct copy
  - BundleIndex: consumed by ABAssetIndex to build Asset->Bundle mapping, not stored in RuntimeAssetEntry
```

### 7.2 ManifestBundleEntry vs BundleInfo (existing)

`ManifestBundleEntry` **supersedes** the existing `BundleInfo` class:

| BundleInfo (current) | ManifestBundleEntry (new) |
|---|---|
| bundleName | BundleName |
| hash | FileHash |
| size | FileSize |
| - | FileCRC (new) |
| - | Encrypted (new) |
| - | Tags (new) |
| - | DependBundleIndices (new) |

### 7.3 ABManifest vs VersionState (existing)

`ABManifest` **supersedes** `VersionState` by containing both version info AND full asset/bundle data:

| VersionState (current) | ABManifest (new) |
|---|---|
| version | PackageVersion |
| hash | (derived from manifest content hash) |
| totalSize | (computed from sum of BundleEntry.FileSize) |
| bundles[] | BundleEntries[] |
| - | AssetEntries[] (new: full asset index) |
| - | FormatVersion (new) |
| - | BuildTimestamp (new) |

---

## 8. File Locations

| File | Location | Purpose |
|------|----------|---------|
| Build output manifest | `HotfixOutput/{version}/ABManifest.json` | Produced by build pipeline |
| StreamingAssets copy | `StreamingAssets/ABManifest.json` | Bundled with initial package |
| Remote manifest | `{RemoteURL}/ABManifest.json` | Downloaded for update check |
| Local cache | `{PersistentDataPath}/ABManifest.json` | Cached after successful update |

---

## 9. Migration Path

### Phase 3 (Current)
- Implement ABManifest.cs, ManifestAssetEntry.cs, ManifestBundleEntry.cs as C# classes
- ABAssetIndex reads from ABManifest instead of AddressableLabelsConfig
- JSON serialization via JsonUtility

### Phase 4 (Hotfix)
- HotfixManager downloads remote ABManifest.json
- CompareManifests() replaces current Catalog-based update check
- ABManifest replaces VersionState for version tracking

### Phase 6 (Build Pipeline)
- Build pipeline exports ABManifest.json alongside AB packages
- DifferentialProcessor generates manifest diff
- **Binary serialization upgrade**: consider Protobuf (protobuf-net or Google.Protobuf) for binary format — auto-generated serializers, native forward/backward compatibility via field numbers, built-in JSON interop via JsonFormatter. Evaluate vs custom binary (YooAsset style) at that time.

---

## 10. Approval Checklist (All Approved 2026-03-30)

- [x] ManifestAssetEntry fields: **current fields sufficient** (RuntimeAssetEntry + BundleIndex)
- [x] ManifestBundleEntry.FileHash: **MD5 first** (consistent with existing HashGenerator); easy to swap to SHA256 later via interface
- [x] ManifestBundleEntry.Tags: **keep field but leave empty** for now; reserved for future selective download by tag
- [x] File naming: **include version** — `ABManifest_1.0.3.json` for CDN cache compatibility
- [x] SourcePath/Group: **keep + configurable strip** — default include, optional `StripEditorFields` build flag
