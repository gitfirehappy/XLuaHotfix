# Sub-Plan E5-2: Backbone Task Implementations

> **Risk**: Medium (depends on E5-1 core + E1-3 CollectionScanner + E4 DependencyAnalyzer; actual AssetBundle building via Unity API has known edge cases)
> **Dependencies**: E5-1 (IBuildTask + BuildContext + DAGScheduler + BuildPipelineConfig), E1-3 (CollectionScanner — TaskCollectAssets output), E4 (TaskAnalyzeDependencies output)
> **Parent**: [plan-E5.md](plan-E5.md)
> **Status**: Draft — discussion complete, pending approval

---

## Objective

Implement 3 of the 6 backbone pipeline Tasks that are owned by E5: **TaskPrepareContext** (initialize build environment), **TaskBuildBundles** (group assets by BundleName + build Unity AssetBundles), **TaskOrganizeOutput** (copy bundles to output + serialize manifest + cleanup). Also define **BundleBuildInfo** as the output data contract for E5-2/E6.

The other 3 backbone Tasks (TaskCollectAssets, TaskAnalyzeDependencies, TaskGenerateManifest) are implemented by E1-3, E4, E6 respectively.

---

## Confirmed Design Decisions

### D8: BundleBuildInfo — TaskBuildBundles Output

```csharp
public class BundleBuildInfo
{
    public string BundleName;              // Logical name (without hash/suffix)
    public string OutputFileName;          // Actual file name (with hash + .bundle)
    public string Hash;                    // Content hash (Unity BuildPipeline provides)
    public long Size;                      // File size in bytes
    public List<string> AssetPaths;        // Asset paths in this bundle
    public EPayloadKind PayloadKind;       // Dominant payload kind for the bundle
}
```

---

## Task Specifications

### TaskPrepareContext

```
ReadKeys:  —
WriteKeys: BackendMode, BuildVersion, OutputRoot, TargetPlatform
DependsOn: —

Logic:
  1. Read BuildPipelineConfig.DefaultBackendMode from SO
  2. Check command-line arg --backend (override if present: "LegacyAddressable" | "ABManifest")
  3. Write BackendMode to BuildContext → LOCK (immutable for rest of pipeline)
  4. Resolve BuildVersion:
     - Command-line --version <ver>  →  user override
     - Else: yyyyMMdd-HHmmss timestamp
     - Future: git rev-parse HEAD short hash
  5. Resolve TargetPlatform:
     - Command-line --platform <plat>  →  user override
     - Else: EditorUserBuildSettings.activeBuildTarget
  6. Resolve OutputRoot:
     - Default: {Application.dataPath}/../Build/{TargetPlatform}/
     - Configurable via command-line --output <path>
  7. Write all to BuildContext
```

### TaskBuildBundles

```
ReadKeys:  CollectedAssets, BundleDependencyGraph, OutputRoot, BackendMode
WriteKeys: BundleBuildResults
DependsOn: [TaskAnalyzeDependencies]

Logic:
  1. Group CollectedAssetInfo by BundleName
  2. For each group:
     a. Separate assets by EPayloadKind:
        - Serialized → AssetBundleBuild entry (standard AB packing)
        - Scene → AssetBundleBuild entry (separate Scene AB, Unity requirement)
        - RawFile → copy file directly to output, record BundleBuildInfo (no AB build)
     b. For Serialized + Scene groups:
        - Call BuildPipeline.BuildAssetBundles(outputDir, builds, options, targetPlatform)
        - Options: None (default), ChunkBasedCompression (LZ4)
     c. For each built bundle, collect:
        - BundleName (logical, from grouping key)
        - OutputFileName (from Unity build output: name + hash + .bundle)
        - Hash (from Unity BuildPipeline or compute from file)
        - Size (file size in bytes)
        - AssetPaths (from CollectedAssetInfo in this group)
        - PayloadKind (dominant kind for the group)
     d. Record BundleBuildInfo
  3. Write List<BundleBuildInfo> to BuildContext
```

### TaskOrganizeOutput

```
ReadKeys:  ABManifest, BundleBuildResults, OutputRoot
WriteKeys: OutputPath
DependsOn: [TaskGenerateManifest]

Logic:
  1. Create output directory: {OutputRoot}/{BuildVersion}/
  2. Copy all built bundles from temp build dir to output directory
  3. Serialize ABManifest to output directory (ABManifest.json or binary format)
  4. Generate build summary log ({OutputRoot}/{BuildVersion}/build_summary.txt):
     - Build version, timestamp, platform, backend mode
     - Bundle count, total size, asset count
     - Warnings/Errors summary
  5. Clean up temp build artifacts (Unity's default build output dir)
  6. Write final OutputPath = {OutputRoot}/{BuildVersion}/ to BuildContext
```

---

## New Files

| File | Path | Assembly | Lines (est.) | Description |
|------|------|----------|-------------|-------------|
| BundleBuildInfo.cs | Build/Pipeline/Editor/ | Editor | ~25 | BundleName, OutputFileName, Hash, Size, AssetPaths, PayloadKind |
| TaskPrepareContext.cs | Build/Pipeline/Editor/Tasks/ | Editor | ~80 | Backbone node 1: BackendMode + BuildVersion + OutputRoot + TargetPlatform |
| TaskBuildBundles.cs | Build/Pipeline/Editor/Tasks/ | Editor | ~180 | Backbone node 4: group by BundleName, build AssetBundles, handle RawFile/Serialized/Scene |
| TaskOrganizeOutput.cs | Build/Pipeline/Editor/Tasks/ | Editor | ~100 | Backbone node 6: copy to output, serialize ABManifest, cleanup |

All paths relative to `Assets/FYAsset/Scripts/`.

---

## Task Breakdown

| Task | Content | Depends On |
|------|---------|-----------|
| E5-2-T1 | Create `BundleBuildInfo.cs` | — |
| E5-2-T2 | Create `TaskPrepareContext.cs` — implement IBuildTask: read config + CLI → write BackendMode, BuildVersion, OutputRoot, TargetPlatform | E5-1 done |
| E5-2-T3 | Create `TaskBuildBundles.cs` — implement IBuildTask: group by BundleName, separate by PayloadKind, call BuildPipeline.BuildAssetBundles, record BundleBuildInfo | E5-1 done, E1-3 done, E4 done, T1 |
| E5-2-T4 | Create `TaskOrganizeOutput.cs` — implement IBuildTask: copy bundles, serialize ABManifest, build summary, cleanup | E5-1 done, T1 |
| E5-2-T5 | Compilation verification (`dotnet build XLuaHotfix.sln`) | All above |

---

## Invariants (Must Hold After E5-2)

1. All 3 Tasks compile and correctly implement `IBuildTask`
2. `TaskPrepareContext.Execute` writes all 4 keys to BuildContext; BackendMode is locked
3. `TaskBuildBundles.Execute` correctly groups assets by BundleName and calls BuildPipeline.BuildAssetBundles
4. Serialized/Scene/RawFile payload kinds each follow their correct build path
5. `BundleBuildInfo` contains all fields required by TaskGenerateManifest (E6)
6. `TaskOrganizeOutput.Execute` creates correct output directory structure
7. Temp build artifacts are cleaned up after successful copy
8. `dotnet build XLuaHotfix.sln` passes with 0 errors

---

## Not In Scope

- TaskCollectAssets implementation (E1-3)
- TaskAnalyzeDependencies implementation (E4)
- TaskGenerateManifest implementation (E6)
- Extension node implementations
- Builder panel UI / build trigger button
- Incremental build / asset caching
- BuildPipeline.BuildAssetBundles compression strategy configuration (default LZ4 only)

---

## Approval Checklist

- [ ] Agree to `BundleBuildInfo` 6-field data structure
- [ ] Agree to TaskPrepareContext: command-line override > SO default priority
- [ ] Agree to TaskBuildBundles: 3-way PayloadKind routing (Serialized→AB, Scene→SceneAB, RawFile→copy)
- [ ] Agree to TaskOrganizeOutput: copy + serialize + summary + cleanup
- [ ] Agree to 4 new files + 5 tasks
- [ ] Agree to TaskBuildBundles depending on E1-3 + E4 completion
