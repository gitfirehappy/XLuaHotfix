# Field Semantics — Naming Constraints

Binding rules for field naming across the FYAsset codebase. Consult before writing or reviewing code that touches data structures.

## Critical Disambiguation Rules

1. **Labels** = asset-level query tags (ManifestAssetEntry.Labels, RuntimeAssetEntry.Labels). Never use "Labels" on a Bundle structure.
2. **Tags** = bundle-level download strategy markers (ManifestBundleEntry.Tags). Never use "Tags" on an asset structure. Labels and Tags do NOT auto-aggregate.
3. **Address** = runtime logical name (allows duplicates, primary query key). **AssetPath/SourcePath** = project-relative path (editor diagnostic only, never a runtime key).
4. **EntryId** = runtime canonical unique ID (reuses Unity GUID). **AssetGUID** = same value, build-time naming. Always map 1:1.
5. **PrimaryType** = single asset's Unity type name. **BundleType** = bundle's dominant type (>80% threshold) or "Mixed".
6. **FileHash** = canonical hash field name (MD5). All code now uses PascalCase.
7. **BundleName** = canonical bundle identifier. All code now uses PascalCase.

## Hierarchy

```
Package (PackageName)
  └─ Group (GroupName)
       └─ Collector (CollectorType)
            └─ Asset (EntryId, Address, PrimaryType, Labels)
                 └─ Bundle (BundleName, Tags, DependBundleIndices)
```

## VersionNumber Comparison Semantics

Fields: Major, Minor, Patch, Build, Channel.
- CompareTo order: Major → Minor → Patch → ChannelRank (alpha=0 < beta=1 < rc=2 < ""=3).
- Build is metadata only — excluded from equality and comparison.

## Enum Quick Reference

- ECollectorType: Main(0)=addressable entry, Static(1)=internal, Depend(2)=explicit dep, Implicit(3)=auto-discovered dep
- EPayloadKind: Serialized(0)=AB, RawFile(1)=copy, Scene(2)=scene bundle
- EAssetRole: Main(0), Static(1), Depend(2), ImplicitDependency(3)

## Naming Convention

- Entire codebase: PascalCase for public fields (`FileHash`, `BundleName`, `LatestPackage`)
- `FYAssetConstants.cs` is the PascalCase exemplar for new code
- Same semantic across structures MUST use same name (e.g., `PrimaryType` everywhere)
