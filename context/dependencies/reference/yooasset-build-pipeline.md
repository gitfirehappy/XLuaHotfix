# YooAsset Build Pipeline Reference

> Source: YooAsset source code analysis (Editor/AssetBundleBuilder/)
> Purpose: Reference for XLuaHotfix build pipeline architecture study
> Language: English (AI consumption)

---

## 1. Architecture Overview

YooAsset's build system uses a **pipeline pattern** where each build step is an independent IBuildTask. Tasks execute sequentially, sharing data through a typed BuildContext container.

`
AssetBundleBuilder.Run(buildParameters, buildPipeline)
  -> BuildRunner.Run(pipeline, context)
     -> foreach IBuildTask in pipeline:
          task.Run(context)    // sequential, timed, error-handling
`

Four pipeline variants exist, each containing a specific sequence of tasks.

---

## 2. Core Interfaces

### 2.1 IBuildTask

`csharp
public interface IBuildTask
{
    void Run(BuildContext context);
}
`

Single method. Exceptions propagate to BuildRunner which records failure details.

### 2.2 BuildContext

Type-safe container for inter-task data sharing:

`csharp
// Storage: Dictionary<Type, IContextObject>
context.SetContextObject<BuildMapContext>(mapContext);
var map = context.GetContextObject<BuildMapContext>();
`

Single-threaded. Created per build, discarded after completion.

---

## 3. Pipeline Task Sequences

### 3.1 BuiltinBuildPipeline (11 tasks)

Uses Unity's built-in AssetBundle build API.

`
1. TaskPrepare_BBP          - Validate parameters, create output directories
2. TaskGetBuildMap_BBP       - Collect assets, analyze dependencies, create build map
3. TaskBuilding_BBP          - Execute Unity AssetBundle.BuildPipeline
4. TaskVerifyBuildResult_BBP - Validate build output integrity
5. TaskEncryption_BBP        - Apply encryption to built bundles
6. TaskUpdateBundleInfo_BBP  - Compute hashes, sizes, paths for each bundle
7. TaskCreateManifest_BBP    - Generate PackageManifest (JSON + binary)
8. TaskCreateReport_BBP      - Generate build report and statistics
9. TaskCreatePackage_BBP     - Package bundles into deployment format
10. TaskCopyBuildinFiles_BBP - Copy built-in resources to StreamingAssets
11. TaskCreateCatalog_BBP    - Create asset catalog for runtime
`

### 3.2 ScriptableBuildPipeline (11 tasks)

Same sequence as Builtin but uses Unity SBP for the actual build step. Suffix: _SBP.

### 3.3 RawFileBuildPipeline (10 tasks)

For raw files (not AssetBundles). No VerifyBuildResult step.

`
1. TaskPrepare_RFBP
2. TaskGetBuildMap_RFBP
3. TaskBuilding_RFBP          - Raw file copy (no AssetBundle)
4. TaskEncryption_RFBP
5. TaskUpdateBundleInfo_RFBP
6. TaskCreateManifest_RFBP
7. TaskCreateReport_RFBP
8. TaskCreatePackage_RFBP
9. TaskCopyBuildinFiles_RFBP
10. TaskCreateCatalog_RFBP
`

### 3.4 EditorSimulateBuildPipeline (4 tasks)

Lightweight pipeline for editor simulation mode (no actual build).

`
1. TaskPrepare_ESBP
2. TaskGetBuildMap_ESBP
3. TaskUpdateBundleInfo_ESBP
4. TaskCreateManifest_ESBP
`

---
## 4. Context Objects (Inter-Task Data)

### 4.1 BuildParametersContext

Wraps BuildParameters + provides path helpers:
- GetPipelineOutputDirectory() - build output root
- GetPackageOutputDirectory() - per-package output
- CheckBuildParameters() - validation

### 4.2 BuildMapContext

The central data structure produced by TaskGetBuildMap:

`
BuildMapContext
  _bundleInfoDic: Dictionary<string, BuildBundleInfo>  // bundleName -> info
  SpriteAtlasAssetList: List<BuildAssetInfo>           // sprite atlas assets
  IndependAssets: List<ReportIndependAsset>             // unreferenced dependencies
  AssetFileCount: int                                   // total assets
  Command: CollectCommand                               // collection params
`

### 4.3 BuildBundleInfo

Represents one output bundle:

`
BuildBundleInfo
  BundleName: string
  AllPackAssets: List<BuildAssetInfo>    // assets packed into this bundle
  UnityHash / CRC / FileHash / FileSize  // computed after build
  Methods: PackAsset(), CreatePipelineBuild(), GetAllManifestAssetInfos()
`

### 4.4 BuildAssetInfo

Represents one asset in the build process:

`
BuildAssetInfo
  CollectorType: ECollectorType          // Main/Static/Depend
  BundleName: string                     // assigned bundle
  Address: string                        // addressable name
  AssetInfo: AssetInfo                   // Unity asset data (path, GUID, type)
  AssetTags: List<string>               // classification tags
  AllDependAssetInfos: List<BuildAssetInfo>  // all dependencies
  _referenceBundleNames: HashSet<string>     // bundles referencing this asset
`

---

## 5. Dependency Analysis Algorithm

Implemented in TaskGetBuildMap.CreateBuildMap(), this is the most complex build step:

### Phase 1: Asset Collection
- Calls collector system's BeginCollect()
- Retrieves all CollectAssetInfo from configured collectors
- Separates assets by CollectorType (Main / Static / Depend)

### Phase 2: Zero-Reference Removal
- Identifies DependAssetCollector assets not referenced by any Main/Static asset
- Removes unreferenced dependencies
- Logs warnings for removed assets

### Phase 3: Dependency Recording
- Records all collected assets into allBuildAssetInfos dictionary
- Records all dependency relationships between assets
- Links each dependent asset to the set of bundles that reference it

### Phase 4: Dependency List Population
- Fills AllDependAssetInfos for each BuildAssetInfo
- Ensures all dependency chains are complete and resolvable

### Phase 5: Shader Auto-Collection (Optional)
- If AutoCollectShaders enabled, finds all shader dependencies
- Assigns shaders to dedicated 'unityshaders.bundle'

### Phase 6: Shared Bundle Processing (Optional)
- If EnableSharePackRule enabled:
  - Groups shared assets (referenced by 2+ bundles) into shared bundles
  - Applies SingleReferencedPackAlone logic (isolate single-ref shared assets)
  - Uses directory-based naming for shared bundles

### Phase 7: Cleanup
- Removes assets without assigned bundle names
- Packs remaining assets into BuildBundleInfo objects in BuildMapContext

### Phase 8: Finalization
- Creates Unity AssetBundleBuild structures for pipeline consumption
- Prepares data for the actual build step

**Key insight**: The algorithm uses Unity's AssetDatabase.GetDependencies() for dependency discovery, then applies collector rules to determine bundling. Shared dependencies are handled post-collection, not during collection.

---
## 6. Build Parameters

Abstract BuildParameters base class:

### Core Configuration
| Parameter | Type | Purpose |
|-----------|------|---------|
| BuildOutputRoot | string | Root output directory |
| BuildinFileRoot | string | Built-in resources root |
| PackageName | string | Package identifier |
| PackageVersion | string | Version string |
| PackageNote | string | Optional description |
| BuildPipeline | string | Pipeline type name |
| BuildBundleType | enum | AssetBundle or RawBundle |
| BuildTarget | BuildTarget | Unity platform target |
| FileNameStyle | enum | HashName / BundleName / BundleName_HashName |

### Feature Flags
| Flag | Default | Purpose |
|------|---------|---------|
| ClearBuildCacheFiles | false | Clear Unity build cache before build |
| UseAssetDependencyDB | false | Use cached dependency database |
| EnableSharePackRule | false | Create shared bundles for shared dependencies |
| SingleReferencedPackAlone | false | Isolate single-reference shared assets |
| VerifyBuildingResult | true | Validate output after build |

### Services (Injectable)
| Service | Purpose |
|---------|---------|
| EncryptionServices | IEncryptionServices - bundle encryption |
| ManifestProcessServices | IManifestProcessServices - manifest post-processing |
| ManifestRestoreServices | IManifestRestoreServices - manifest restore |
| BuildinFileCopyOption | enum - how to handle built-in files |

---

## 7. Relevance to XLuaHotfix

### What to adopt:
- **Pipeline pattern (IBuildTask)**: Clean separation of build steps, easy to extend/replace individual tasks
- **BuildContext for data sharing**: Type-safe container avoids global state; each task declares its inputs/outputs
- **Dependency analysis algorithm**: The 8-step approach (collect -> remove zero-ref -> record deps -> populate -> shaders -> shared -> cleanup -> finalize) is well-proven
- **Shared bundle logic**: EnableSharePackRule + SingleReferencedPackAlone handles the common 'shared texture' problem

### What to adapt:
- **Our DifferentialProcessor**: YooAsset does full rebuilds; Our diff-based approach (Head/Staged snapshots) is more efficient for hotfix scenarios and should adapt the pipeline pattern without copying YooAsset output semantics directly
- **ABManifest format**: Our manifest format differs from YooAsset's PackageManifest. Export should generate our own runtime-facing format, not YooAsset's
- **Encryption**: YooAsset provides encryption as a pipeline task; we can adopt the pattern but defer implementation
- **Build pipeline type**: We likely only need one pipeline type (Builtin) initially; SBP/RawFile/EditorSimulate can be added later

### What to skip:
- **SBP integration**: Not needed for initial rewrite
- **RawFileBuildPipeline**: deferred to a later dedicated runtime/build step
- **EditorSimulateBuildPipeline**: Our editor already uses Addressables simulation mode
- **AssetArtReporter / AssetArtScanner**: nice-to-have, not core pipeline


