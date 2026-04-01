# YooAsset Manifest Data Model Reference

> Source: YooAsset source code analysis (Runtime/ResourcePackage/)
> Purpose: Reference for XLuaHotfix ABManifest design comparison and Phase 4/6 alignment
> Language: English (AI consumption)

---

## 1. Architecture Overview

YooAsset's manifest system describes the complete mapping of assets to bundles, with metadata for runtime loading, incremental updates, and integrity verification. The manifest is generated at build time and consumed at runtime.

`
PackageManifest (root)
  +-- List<PackageAsset>    // all actively collected assets
  +-- List<PackageBundle>   // all output bundles
  +-- Runtime dictionaries  // built on deserialization for O(1) lookups
`

---

## 2. Data Model

### 2.1 PackageManifest (Top-Level)

**Serialized Fields:**

| Field | Type | Purpose |
|-------|------|---------|
| FileVersion | string | Manifest format version for forward compat |
| EnableAddressable | bool | Whether addressable location is enabled |
| SupportExtensionless | bool | Support location without file extensions |
| LocationToLower | bool | Case-insensitive location matching |
| IncludeAssetGUID | bool | Include Unity asset GUIDs |
| ReplaceAssetPathWithAddress | bool | Use address as asset path at runtime |
| OutputNameStyle | int | File naming: HashName / BundleName / BundleName_HashName |
| BuildBundleType | int | Bundle build type |
| BuildPipeline | string | Build pipeline name |
| PackageName | string | Package identifier |
| PackageVersion | string | Version string |
| PackageNote | string | Optional description |
| AssetList | PackageAsset[] | All actively collected assets |
| BundleList | PackageBundle[] | All bundle entries |

**Runtime-Only Dictionaries (built on Initialize):**

| Dictionary | Key -> Value | Purpose |
|------------|-------------|---------|
| AssetDic | AssetPath -> PackageAsset | Primary asset lookup |
| AssetPathMapping1 | Location -> AssetPath | Addressable/extensionless/case-insensitive lookup |
| AssetPathMapping2 | AssetGUID -> AssetPath | GUID-based lookup |
| BundleDic1 | BundleName -> PackageBundle | Bundle by name |
| BundleDic2 | FileName -> PackageBundle | Bundle by output filename |
| BundleDic3 | BundleGUID -> PackageBundle | Bundle by GUID (= FileHash) |

### 2.2 PackageBundle

| Field | Type | Purpose |
|-------|------|---------|
| BundleName | string | Original bundle name |
| UnityCRC | uint | Unity engine CRC for AssetBundle.LoadFromFile |
| FileHash | string | Content hash (MD5/CRC32) for integrity + cache key |
| FileCRC | uint | Custom CRC32 for quick verification |
| FileSize | long | File size in bytes |
| Encrypted | bool | Whether bundle is encrypted |
| Tags | string[] | Classification tags for selective download |
| DependBundleIDs | int[] | Indices into BundleList for dependencies |

**Runtime-Only:**

| Field | Type | Purpose |
|-------|------|---------|
| IncludeMainAssets | List | Main assets contained in this bundle |
| ReferenceBundleIDs | List | IDs of bundles that reference this one |
| BundleGUID | string | Property returning FileHash |
| FileName | string | Generated output filename |
| FileExtension | string | Output file extension |

### 2.3 PackageAsset

| Field | Type | Purpose |
|-------|------|---------|
| Address | string | Addressable address (empty if not enabled) |
| AssetPath | string | Unity asset path (e.g. 'Assets/Prefabs/Player.prefab') |
| AssetGUID | string | Unity asset GUID (empty if not included) |
| AssetTags | string[] | Classification tags |
| BundleID | int | Index into BundleList for containing bundle |
| DependBundleIDs | int[] | Framework-collected dependency bundle IDs |

---
## 3. Serialization Formats

### 3.1 Binary Format (Production)

Written by ManifestTools.SerializeToBinary():

`
[FileSign: uint32]           // Magic number for format identification
[FileVersion: UTF8 string]   // Version for forward compat
[Header: 11 fields]          // Booleans, ints, strings (feature flags + metadata)
[AssetCount: int32]
[Asset entries...]            // Address, AssetPath, AssetGUID, AssetTags[], BundleID, DependBundleIDs[]
[BundleCount: int32]
[Bundle entries...]           // BundleName, UnityCRC, FileHash, FileCRC, FileSize, Encrypted, Tags[], DependBundleIDs[]
[Optional: IManifestProcessServices.ProcessManifest() post-processing]
`

Version checking supports manifests >= 2025.8.28, with graceful handling of field additions.

### 3.2 JSON Format (Debug/Development)

Simple JsonUtility.ToJson() with pretty printing. All serialized fields included, runtime dictionaries excluded.

### 3.3 Deserialization Flow

`
1. Read file (binary or JSON)
2. Validate FileSign and version (binary) or parse JSON
3. For each PackageBundle: call InitBundle() to compute FileName/Extension
4. Build runtime dictionaries:
   a. AssetDic: AssetPath -> PackageAsset
   b. AssetPathMapping1: location variants -> AssetPath
   c. AssetPathMapping2: AssetGUID -> AssetPath
   d. BundleDic1/2/3: various bundle lookups
5. For each asset: link to containing bundle, record dependencies
6. Optional: ReplaceAssetPathWithAddress substitution
`

---

## 4. Build-Time Manifest Generation

TaskCreateManifest (build pipeline task) flow:

`
1. Validate: Check for bundle hash conflicts
2. Create PackageManifest with build parameters
3. Process assets: Create PackageAsset list, assign BundleIDs, collect dependencies
4. Process bundles: Create PackageBundle list, process dependencies and tags
5. Tag propagation: Spread asset tags to their dependent bundles
6. Builtin handling: Add references for shaders/scripts/sprite atlases
7. Generate files:
   - JSON manifest: {PackageName}_{PackageVersion}.json
   - Binary manifest: {PackageName}_{PackageVersion}.bytes
   - Hash file: {PackageName}_{PackageVersion}.hash (CRC32 of binary)
   - Version file: {PackageName}.version (version string only)
`

---

## 5. Runtime Lookup Algorithms

### Asset Resolution
`
Location -> AssetPathMapping1 -> AssetDic -> PackageAsset -> BundleID -> PackageBundle
`

### Bundle Dependency Resolution
`
PackageAsset.BundleID -> BundleList[BundleID] -> PackageBundle
PackageAsset.DependBundleIDs -> [BundleList[id] for id in DependBundleIDs]
`

### Incremental Update Strategy

YooAsset uses **version-based manifest switching** rather than field-level diffing:
1. Compare PackageVersion strings (local vs remote)
2. If different, download entire new manifest
3. Individual bundle FileHash determines if bundle content changed
4. NeedDownload() checks if bundle exists locally with matching hash
5. No explicit CompareManifests() function - handled per-bundle at download time

---

## 6. Comparison with XLuaHotfix ABManifest

| Aspect | YooAsset PackageManifest | Our ABManifest |
|--------|------------------------|----------------|
| Format | Binary primary, JSON debug | JSON primary (Phase 3), binary later (Phase 6) |
| Version | String ('1.0.5') | VersionNumber (Major.Minor.Patch) |
| Asset ID | AssetPath (string) | EntryId (Unity GUID string) |
| Asset Address | Optional field | Always present |
| Bundle Deps | int[] indices into BundleList | int[] indices into BundleEntries[] |
| Asset Deps | Asset-level DependBundleIDs[] | Bundle-level only (D3 decision) |
| Feature Flags | Multiple (addressable, extensionless, etc.) | Minimal |
| Encryption field | Per-bundle boolean | Per-bundle boolean |
| Tags | Both asset and bundle level | Labels (asset) + Tags (bundle) |
| Update comparison | Per-bundle hash check | Explicit CompareManifests() method |
| File naming | {PackageName}_{Version}.json/bytes | ABManifest_{Version}.json |
| Runtime fixed name | N/A (uses version in name) | ABManifest.json (fixed local name) |

### Key Differences to Note:
1. **Our manifest is simpler** - fewer feature flags, no dual-format initially
2. **Our dependency model is bundle-level only** - YooAsset tracks asset-level deps too
3. **Our EntryId uses Unity GUID** - YooAsset uses AssetPath as primary key
4. **Our update comparison is explicit** - ABManifest.CompareManifests() vs YooAsset's per-bundle approach
5. **Our version is structured** - VersionNumber class vs plain string
