# Plan: Address Generation Conflict Policy

> **Status**: Signed off / Archived
> **Date**: 2026-06-04
> **Scope**: Replace automatic conflict-driven Address rewriting with explicit project and batch Address styles.
> **Priority**: Execute before AB cumulative hotfix delivery. This plan only changes collector Address generation semantics.

## Goal

Make Address generation deterministic and explicit:

- default automatic Address follows the selected project Address style;
- same Address values remain allowed until the existing resolver identity cannot disambiguate them;
- old automatic `Filename_Type` conflict upgrade is removed;
- explicit asset-level and group-level operations can apply short name, long asset path, or `Name#Type`.

## Non-Goals

- Do not change runtime loading or hotfix flow.
- Do not change `Address + PrimaryType + Labels` conflict semantics.
- Do not implement AB cumulative hotfix delivery.
- Do not make Address globally unique.

## Approved Decisions

- `AssetCollectionSetting` stores project-level `AddressStyle`.
- Supported styles:
  - `ShortName`: filename without extension, for example `Icon`.
  - `LongAssetPathWithoutExtension`: serialized compatibility enum name; current behavior preserves the file extension, for example `Assets/UI/Icon.png`. This was corrected by `assets-collection-followup-20260605`.
  - `NameType`: short name plus type with `#`, for example `Player#Prefab`.
- `#` is allowed in Address but must be blocked from PackageName, GroupName, Labels, and BundleKey.
- `BundleNameBuilder.NormalizeAddressKey()` projects `#` to `-` through the BundleKey blacklist, so PackSeparately bundle names never contain `#`.
- Group batch operations only modify entries with `AutoAddress == true`; manual Address overrides are preserved.

## Dependency Order

1. Data model: add `AssetAddressStyle` and `AssetCollectionSetting.AddressStyle`.
2. Generator: implement style-based generation and remove conflict-upgrade batch logic.
3. Reserved characters: add `#` to segment and BundleKey blacklists.
4. Default callers: route scanner and editor default/reset paths through `AddressStyle`; implicit dependency/builtin fallbacks use `ShortName`.
5. Editor UI: expose project style and explicit asset/group apply buttons.
6. Verification: build, whitespace check, source searches, and manual flow simulation.

## Code Drafts

### T1 - Data Model

File: `Assets/FYAsset/Scripts/Build/Collector/AssetCollectionSetting.cs`

```csharp
public enum AssetAddressStyle
{
    ShortName = 0,
    LongAssetPathWithoutExtension = 1,
    NameType = 2
}

public class AssetCollectionSetting : ScriptableObject
{
    public AssetAddressStyle AddressStyle = AssetAddressStyle.ShortName;
    public List<AssetCollectionPackage> Packages = new();
    public List<string> IgnorePatterns = CreateDefaultIgnorePatterns();
    public List<AssetEntry> AssetEntries = new();
}
```

Editor clone/save paths must copy `AddressStyle`.

### T2 - Address Generator

File: `Assets/FYAsset/Scripts/Build/AssetAddressGenerator.cs`

```csharp
public const char TypeSuffixSeparator = '#';

public static string GenerateAddress(string assetPath, string primaryType, AssetAddressStyle style)
{
    switch (style)
    {
        case AssetAddressStyle.LongAssetPathWithoutExtension:
            return GenerateLongAssetPath(assetPath);
        case AssetAddressStyle.NameType:
            return GenerateNameTypeAddress(assetPath, primaryType);
        case AssetAddressStyle.ShortName:
        default:
            return GenerateShortName(assetPath);
    }
}

public static string GenerateLongAssetPath(string assetPath)
{
    string normalized = FYAssetPathUtility.NormalizeAssetPath(assetPath);
    return normalized;
}

public static string GenerateNameTypeAddress(string assetPath, string primaryType)
{
    return GenerateTypeSuffixAddress(GenerateShortName(assetPath), primaryType);
}

public static void GenerateAddresses(IList<RuntimeAssetEntry> entries, AssetAddressStyle style)
{
    foreach (var entry in entries)
    {
        if (!entry.AutoAddress)
            continue;

        entry.Address = GenerateAddress(entry.SourcePath, entry.PrimaryType, style);
    }
}

public static void GenerateAddresses(IList<RuntimeAssetEntry> entries)
    => GenerateAddresses(entries, AssetAddressStyle.ShortName);
```

Keep `GenerateShortAddress(assetPath, primaryType, bool useTypeSuffix)` for compatibility. It maps `false` to `ShortName` and `true` to `NameType`.

### T3 - Reserved Character Boundary

File: `Assets/FYAsset/Scripts/Build/Collector/Editor/SystemIdentifiers.cs`

```csharp
public static readonly char[] ReservedChars =
    { '/', '\\', ':', '*', '?', '<', '>', '"', '|', '.', ' ', ';', '%', '~', '$', '_', '#' };

public static readonly char[] BundleKeyReservedChars =
    { '/', '\\', ':', '*', '?', '<', '>', '"', '|', '.', ' ', ';', '%', '$', '_', '#' };
```

Reason: Address may contain `#`, but BundleKey, PackageName, GroupName, and Labels must not.

### T4 - Default Call Sites

- `CollectionScanner.TryCollectAsset`: `GenerateAddress(assetPath, primaryType, setting.AddressStyle)`.
- `AssetsCollectionPanel.EnsureAssetEntry`: use `_curateSetting.AddressStyle`.
- `AssetsCollectionPanel` Address `Reset Auto`: use `_curateSetting.AddressStyle`.
- `DependencyAnalyzer.CreateImplicitEntry`: no setting is available, use `ShortName`.
- `TaskCollectBuiltins.Execute`: no setting is available, use `ShortName`.

### T5 - Editor UI

File: `Assets/FYAsset/Scripts/Build/Editor/ABPipeline/AssetsCollectionPanel.cs`

- Add a project-level `Address Style` enum field in the package/setting detail surface.
- Asset Details:
  - `Apply Short`
  - `Apply Path+Ext`
  - `Apply Name#Type`
  - Each operation sets `entry.Address`, sets `entry.AutoAddress = true`, and marks preview dirty.
- Group Details:
  - same three buttons;
  - iterate `GetAssetsForSourceGroup(_curateResult, group.GroupName)`;
  - call `EnsureAssetEntry(asset.AssetGUID, asset)`;
  - modify only entries where `entry.AutoAddress == true`;
  - preserve manual entries silently.

### T6 - Documentation-in-Code

Update comments that still describe automatic conflict upgrade or `Filename_Type`. The new explicit style name is `Name#Type`.

## Acceptance Cases

- `Assets/UI/Icon.png` with `ShortName` generates `Icon`.
- `Assets/UI/Icon.png` with `LongAssetPathWithoutExtension` generates `Assets/UI/Icon.png`.
- `Assets/Characters/Player.prefab` with primary type `Prefab` and `NameType` generates `Player#Prefab`.
- PackSeparately bundle key for `Player#Prefab` normalizes to a key containing `player-prefab`, never `#`.
- Two assets with the same short Address are not automatically rewritten.
- Group batch Apply Short/Long/Name#Type changes auto entries only; manual entries remain unchanged.

## Verification

- `dotnet build XLuaHotfix.sln --no-restore`
- `git diff --check`
- `rg -n "Filename_Type|短名冲突时升级|冲突时升级|Player_Prefab" Assets/FYAsset/Scripts context docs`
- `rg -n "GenerateShortAddress\([^\\n]+true\)" Assets/FYAsset/Scripts -g "*.cs"`
- Static simulation of the three styles and BundleKey projection.
