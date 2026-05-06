# Sub-Plan E5-2: Backbone Task Implementations

> **Risk**: Medium (depends on E5-1 core + E1-3 CollectionScanner + E4 DependencyAnalyzer; actual AssetBundle building via Unity API has known edge cases)
> **Dependencies**: E5-1 (IBuildTask + BuildContext + DAGScheduler + BuildPipelineConfig), E1-3 (CollectionScanner — TaskCollectAssets output), E4 (TaskAnalyzeDependencies output)
> **Parent**: [plan-E5.md](plan-E5.md)
> **Status**: Superseded — 已拆分为 [plan-E5-2a.md](plan-E5-2a.md) + [plan-E5-2b.md](plan-E5-2b.md)（因 E6 交叉依赖，TaskVerifyBuildResult 和 TaskOrganizeOutput 需要 ABManifest）

---

## Objective

Implement 5 backbone pipeline Tasks owned by E5: **TaskPrepareContext** (initialize build environment), **TaskCollectBuiltins** (auto-collect engine-required assets: Shaders etc.), **TaskBuildBundles** (group assets by BundleName + build Unity AssetBundles), **TaskVerifyBuildResult** (validate output integrity), **TaskOrganizeOutput** (copy bundles to output + serialize manifest + cleanup). Also define **BundleBuildInfo** as the output data contract for E5-2/E6.

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

### TaskCollectBuiltins (YooAsset Gap #1)

```
ReadKeys:  CollectedAssets
WriteKeys: CollectedAssets (augmented)
DependsOn: [TaskCollectAssets]
RunsBefore: [TaskAnalyzeDependencies]

Logic:
  1. AssetDatabase.FindAssets("t:Shader") → all Shader GUIDs
  2. For each Shader not already in CollectedAssets:
     - Create CollectedAssetInfo entry:
       - CollectorType = Implicit
       - GroupName = SystemIdentifiers.SharedGroupName ("$shared")
       - PackKey = "shaders"
       - BundleName = BundleNameBuilder.Build(pkg, "$shared", "shaders")
       - Address = AssetAddressGenerator.GenerateShortAddress(assetPath, primaryType)
       - Labels = empty, PayloadKind = Serialized
     - Append to CollectedAssets list
  3. (Future extensibility) Other engine-required implicit asset types use same pattern
  4. Write augmented CollectedAssets to BuildContext
```

Rationale: Shaders have Unity-specific build requirements (stripping per-bundle, Shader.Find string references). Collecting them in a dedicated shared bundle as engine-required assets — separate from E4's SharePolicy-governed implicit dependency discovery — keeps both concerns clean. E4 BFS sees them as "already owned" and records bundle dependency edges normally.

### TaskVerifyBuildResult (YooAsset Gap #2)

```
ReadKeys:  ABManifest, BundleBuildResults, OutputRoot
WriteKeys: BuildVerificationResult
DependsOn: [TaskGenerateManifest]
RunsBefore: [TaskOrganizeOutput]

Logic:
  1. FILE EXISTENCE (Error): Every bundle in ABManifest.BundleEntries → corresponding .bundle file exists in build output
  2. FILE INTEGRITY (Error): Each .bundle file size > 0, Unity header readable
  3. ORPHAN CHECK (Warning): Every .bundle file in output → has corresponding ABManifest.BundleEntries entry
  4. HASH RE-VERIFY (Error): Recompute each bundle file MD5 → compare with ABManifest.BundleEntries[n].Hash
  5. SIZE ANOMALY (Warning): Size <1KB (possible corruption) or > threshold (config error, default 500MB)
  6. COUNT CROSS-CHECK (Error): Output bundle count == BundleBuildInfo count == Manifest.BundleEntries count

  BuildVerificationResult = { Passed: bool, Errors: List<BuildMessage>, Warnings: List<BuildMessage> }
  Errors → build aborted (return BuildTaskResult.Failure)
  Warnings → build continues, listed in build summary
```

---

## New Files

| File | Path | Assembly | Lines (est.) | Description |
|------|------|----------|-------------|-------------|
| BundleBuildInfo.cs | Build/Pipeline/Editor/ | Editor | ~25 | BundleName, OutputFileName, Hash, Size, AssetPaths, PayloadKind |
| TaskPrepareContext.cs | Build/Pipeline/Editor/Tasks/ | Editor | ~80 | Backbone node 1: BackendMode + BuildVersion + OutputRoot + TargetPlatform |
| TaskCollectBuiltins.cs | Build/Pipeline/Editor/Tasks/ | Editor | ~50 | Backbone node 2: auto-collect Shaders + engine-required implicit assets |
| TaskBuildBundles.cs | Build/Pipeline/Editor/Tasks/ | Editor | ~180 | Backbone node 4: group by BundleName, build AssetBundles, handle RawFile/Serialized/Scene |
| TaskVerifyBuildResult.cs | Build/Pipeline/Editor/Tasks/ | Editor | ~80 | Backbone node 6: file existence, integrity, orphan check, hash re-verify, size anomaly, count cross-check |
| TaskOrganizeOutput.cs | Build/Pipeline/Editor/Tasks/ | Editor | ~100 | Backbone node 7: copy to output, serialize ABManifest, build summary, cleanup |

All paths relative to `Assets/FYAsset/Scripts/`.

---

## Task Breakdown

| Task | Content | Depends On |
|------|---------|-----------|
| E5-2-T1 | Create `BundleBuildInfo.cs` | — |
| E5-2-T2 | Create `TaskPrepareContext.cs` — implement IBuildTask: read config + CLI → write BackendMode, BuildVersion, OutputRoot, TargetPlatform | E5-1 done |
| E5-2-T2b | Create `TaskCollectBuiltins.cs` — implement IBuildTask: FindAssets(t:Shader), create CollectedAssetInfo with Implicit type + $shared group, append to CollectedAssets | E5-1 done |
| E5-2-T3 | Create `TaskBuildBundles.cs` — implement IBuildTask: group by BundleName, separate by PayloadKind, call BuildPipeline.BuildAssetBundles, record BundleBuildInfo | E5-1 done, E1-3 done, E4 done, T1 |
| E5-2-T3b | Create `TaskVerifyBuildResult.cs` — implement IBuildTask: 6 validation checks (file existence, integrity, orphan, hash re-verify, size anomaly, count cross-check), Error→abort, Warning→continue | E5-1 done, E6 done |
| E5-2-T4 | Create `TaskOrganizeOutput.cs` — implement IBuildTask: copy bundles, serialize ABManifest, build summary, cleanup | E5-1 done, T1 |
| E5-2-T5 | Compilation verification (`dotnet build XLuaHotfix.sln`) | All above |

---

## Invariants (Must Hold After E5-2)

1. All 5 Tasks compile and correctly implement `IBuildTask`
2. `TaskPrepareContext.Execute` writes all 4 keys to BuildContext; BackendMode is locked
3. `TaskCollectBuiltins.Execute` discovers all Shader assets and appends them to CollectedAssets with Implicit type + "$shared" group
4. `TaskBuildBundles.Execute` correctly groups assets by BundleName and calls BuildPipeline.BuildAssetBundles
5. Serialized/Scene/RawFile payload kinds each follow their correct build path
6. `BundleBuildInfo` contains all fields required by TaskGenerateManifest (E6)
7. `TaskVerifyBuildResult.Execute` catches missing/corrupt/hash-mismatched bundles as Error; orphan/size-anomaly as Warning
8. `TaskOrganizeOutput.Execute` creates correct output directory structure
9. Temp build artifacts are cleaned up after successful copy
10. `dotnet build XLuaHotfix.sln` passes with 0 errors

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
