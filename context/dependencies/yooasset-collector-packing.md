# YooAsset Collector & Packing Rules Reference

> Source: YooAsset source code analysis (Editor/AssetBundleCollector/)
> Purpose: Reference for XLuaHotfix Phase 5 (E1-E3) build-time asset collection & indexing
> Language: English (AI consumption)

---

## 1. Architecture Overview

YooAsset's asset collection system determines **which assets are collected, how they are addressed, and which bundles they belong to**. It uses a hierarchical configuration with pluggable rule interfaces.

`
AssetBundleCollectorSetting (ScriptableObject, top-level)
  +-- List<AssetBundleCollectorPackage>     (one per package, e.g. 'DefaultPackage')
        +-- List<AssetBundleCollectorGroup>  (logical grouping, e.g. 'UITextures')
              +-- List<AssetBundleCollector>  (one scan path with rules)
`

Each Collector scans a directory path and applies four rule types to produce a set of CollectAssetInfo objects.

---

## 2. Rule Interfaces

### 2.1 IAddressRule

Generates the runtime-loadable address for collected assets.

`csharp
public interface IAddressRule
{
    string GetAssetAddress(AddressRuleData data);
}
// AddressRuleData contains: AssetPath, CollectPath, GroupName, UserData
`

**Only applies to MainAssetCollector type.** StaticAsset and DependAsset collectors do not generate addresses.

### 2.2 IPackRule

Determines which bundle an asset belongs to (bundle name + extension).

`csharp
public interface IPackRule
{
    PackRuleResult GetPackRuleResult(PackRuleData data);
}
// PackRuleData contains: AssetPath, CollectPath, GroupName, UserData
// PackRuleResult contains: BundleName, BundleExtension
`

### 2.3 IFilterRule

Controls which assets are collected from a scan path.

`csharp
public interface IFilterRule
{
    string FindAssetType { get; }  // Unity search filter (e.g. 't:Texture2D')
    bool IsCollectAsset(FilterRuleData data);
}
// FilterRuleData contains: AssetPath, CollectPath, GroupName, UserData
`

### 2.4 IIgnoreRule

Excludes assets entirely from collection (applied before filter rules).

`csharp
public interface IIgnoreRule
{
    bool IsIgnore(AssetInfo assetInfo);
}
`

### 2.5 IActiveRule

Controls whether a collector group participates in the build.

`csharp
public interface IActiveRule
{
    bool IsActiveGroup(GroupData data);
}
`

---

## 3. Built-in Rule Implementations

### 3.1 Address Rules

| Class | Behavior | Example Output |
|-------|----------|----------------|
| AddressDisable | Returns empty (no addressing) | '' |
| AddressByFileName | Filename without extension | 'MainPanel' |
| AddressByGroupAndFileName | 'GroupName_FileName' | 'UI_MainPanel' |
| AddressByFolderAndFileName | 'ParentFolder_FileName' | 'Panels_MainPanel' |

### 3.2 Pack Rules

| Class | Behavior | Bundle Granularity |
|-------|----------|--------------------|
| PackSeparately | One bundle per asset | Fine (path-based name) |
| PackDirectory | One bundle per directory | Medium |
| PackTopDirectory | One bundle per first subdirectory | Coarse |
| PackCollector | One bundle for entire collector | Single |
| PackGroup | One bundle for entire group | Single |
| PackRawFile | Raw file packing (ext: 'rawfile') | Per-file |
| PackVideoFile | Video file packing (ext: file ext) | Per-file |
| PackShader | All shaders in 'unityshaders.bundle' | Global singleton |
| PackShaderVariants | Shader variants in shared bundle | Global singleton |

### 3.3 Filter Rules

| Class | FindAssetType | Behavior |
|-------|---------------|----------|
| CollectAll | 'Object' | Collects everything |
| CollectScene | 'Scene' | Only .unity/.scene files |
| CollectPrefab | 'Prefab' | Only .prefab files |
| CollectSprite | 'Texture2D' | Only textures with Sprite import |
| CollectShader | 'Shader' | Only .shader files |
| CollectShaderVariants | 'ShaderVariantCollection' | Only .shadervariants |

### 3.4 Ignore Rules

| Class | Ignores |
|-------|---------|
| NormalIgnoreRule | Folders, editor resources, lighting data, unrecognized assets, extensions: .so .cs .js .boo .meta .cginc .hlsl |
| RawFileIgnoreRule | Similar but allows more asset types for raw file pipelines |

### 3.5 Active Rules

| Class | Behavior |
|-------|----------|
| EnableGroup | Group always participates |
| DisableGroup | Group never participates |

---
## 4. Collector Types (ECollectorType)

| Type | In Manifest | Loadable | Purpose |
|------|-------------|----------|---------|
| MainAssetCollector | Yes | Yes | Primary assets that game code loads directly |
| StaticAssetCollector | Yes | No | Assets included in bundles but not exposed for loading |
| DependAssetCollector | Conditional | No | Dependencies only included if referenced by main/static assets |
| None | No | No | Invalid / disabled |

**Key distinction**: Only MainAssetCollector generates addresses via IAddressRule. DependAssetCollector assets are automatically pulled in by the dependency analysis phase.

---

## 5. Collection Flow (End-to-End)

`
1. AssetBundleCollectorSetting.BeginCollect()
   |-- Creates CollectCommand with package settings
   |
2. Package.GetCollectAssets(command)
   |-- Iterates active groups (filtered by IActiveRule)
   |
3. Group.GetAllCollectAssets(command)
   |-- Iterates all collectors in the group
   |
4. Collector scans assets:
   a. EditorTools.FindAssets(filterRule.FindAssetType, collectPath)
      -> Unity AssetDatabase search
   b. For each found asset:
      - ignoreRule.IsIgnore(assetInfo) -> skip if true
      - filterRule.IsCollectAsset(data) -> skip if false
      - addressRule.GetAssetAddress(data) -> generate address
      - packRule.GetPackRuleResult(data) -> determine bundle name
      - Collect tags from group and collector settings
   c. Create CollectAssetInfo for each valid asset
   |
5. Dependency collection:
   - AssetDependencyCache.GetDependencies(assetPath)
   - Uses Unity AssetDatabase.GetDependencies()
   - Dependencies NOT in any collector are flagged as shared
   |
6. Results aggregated into CollectResult
   -> List<CollectAssetInfo> with: address, bundleName, tags, dependencies
`

---

## 6. Key Design Decisions

### 6.1 Three-Rule Separation
Collection, addressing, and packing are separate concerns. This allows:
- Same collection path with different addressing strategies
- Shared packing rules across different content types
- Filter rules that leverage Unity's asset type system

### 6.2 Dependency Handling Strategy
- Dependencies are collected via Unity's AssetDatabase.GetDependencies()
- DependAssetCollector provides explicit control over dependency bundling
- Shared dependencies (referenced by multiple bundles) are handled in the build pipeline phase, not collection phase

### 6.3 Tag Propagation
- Tags are assigned at the Group and Collector level
- During build, asset tags propagate to their containing bundles
- Tags enable selective download (e.g., download only 'Level1' tagged bundles)

---

## 7. Relevance to XLuaHotfix Phase 5

### What to adopt:
- **Three-rule interface pattern** (IAddressRule / IPackRule / IFilterRule) maps well to our E1 collector framework
- **Collector hierarchy** (Setting > Package > Group > Collector) provides good organizational granularity
- **CollectorType enum** (Main/Static/Depend) cleanly separates asset roles
- **Strategy pattern for rules** enables custom extensions without framework changes

### What to adapt:
- **IIgnoreRule**: Consider merging with IFilterRule or using a gitignore-style approach (per E3 plan)
- **Sub-directory collector**: YooAsset uses PackTopDirectory as a pack rule; our E3 may need a dedicated sub-directory collector concept
- **Tag system**: Our RuntimeAssetEntry.Labels maps to YooAsset's Tags but with case-insensitive matching (per B5-1 decision)
- **Address generation**: Our AssetAddressGenerator already handles filename + type suffix format; align with IAddressRule pattern

### What to skip:
- **Shader-specific rules** (PackShader/PackShaderVariants) - not needed initially
- **RawFile-specific rules** - deferred to Phase 7 (F1)
- **IActiveRule** - simpler enable/disable can be a boolean flag
