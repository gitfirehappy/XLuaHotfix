# FYAsset Infrastructure Consistency Review (2026-05-11)

> **Date**: 2026-05-11
> **Reviewer**: GPT-5.5 Codex (automated grep audit)
> **Processed**: 2026-05-11 → `requirements/refactor-2026/plan/plan-infrastructure-consistency-20260511.md`
> **Status**: Archived · Streamlined 2026-05-11

## Scope

Target: `Assets/FYAsset/Scripts/`

Review focus:

- direct file / directory I/O that bypasses `FileHelper`
- ad-hoc error/result channels that bypass unified infrastructure
- hardcoded build/config paths that bypass centralized constants
- duplicated build/hotfix artifact handling logic that should be unified

Method:

- grep audit over FYAsset scripts for `File.*`, `Directory.*`, `JsonUtility`, `SerializationUtility`, `BuildMessage`, `RuntimeMessage`, `Debug.Log*`, and custom result patterns
- manual verification of hotfix, build backend, build pipeline, and runtime-loading hotspots

## Findings

### 1. [High] Hotfix flow still bypasses `FileHelper` in multiple runtime write/delete/copy paths

**Why this matters**

The project established `FileHelper` as the cross-platform file I/O layer with atomic writes and no-throw deletes, but the hotfix path -- where partial writes and platform-specific path handling are most dangerous -- still uses raw `File.*` / `Directory.*`.

**Evidence**

- `Assets/FYAsset/Scripts/LegacyRuntime/HotfixManager.cs`
  - line 153: `File.Exists(localManifestPath)`
  - line 304: `Directory.CreateDirectory(targetBundleRoot)`
  - line 337: `File.Exists(localPath)`
  - line 341: `File.Copy(localPath, savePath, true)`
  - line 457: `File.Exists(guidFilePath)`
  - line 461: `File.ReadAllText(guidFilePath).Trim()`
  - line 492: `Directory.Delete(aaCachePath, true)`
  - line 500: `File.WriteAllText(guidFilePath, currentGuid)`
- `Assets/FYAsset/Scripts/LegacyRuntime/LegacyHotfixBackend.cs`
  - line 70: `File.Exists(localVersionStatePath)`
  - line 149: `File.WriteAllText(Path.Combine(ctx.TargetGUIDRoot, "version_state.json"), _remoteVersionJson);`
- `Assets/FYAsset/Scripts/LegacyRuntime/PackageCleaner.cs`
  - line 14: `Directory.Exists(PathManager.HotfixRoot)`
  - line 16: `Directory.Delete(PathManager.HotfixRoot, true)`
  - line 42: `Directory.GetDirectories(PathManager.HotfixRoot, "Build_*")`
  - line 74: `Directory.Delete(dirInfo.FullName, true)`

**Risk**

- delete / overwrite semantics differ between sites
- `version_state.json` write is non-atomic while AB path already uses atomic write
- copy/delete error handling is fragmented and partially exception-driven
- future multi-platform behavior will keep drifting because hotfix code does not consistently go through the shared file layer

**Unification direction**

- extend `FileHelper` with the missing operations actually needed by hotfix:
  - `CopyFile`
  - `TryCopyFile`
  - `EnsureDirectory`
  - directory enumeration helpers where needed
- migrate hotfix write/delete/copy/read sites to `FileHelper`
- align `LegacyHotfixBackend` with `ABHotfixBackend` by using atomic write for metadata files

### 2. [High] Build output handling is duplicated across `TaskOrganizeOutput`, `ABBuildBackend`, and `BuildPathCustomizer`

**Why this matters**

Three separate artifact-organization implementations all manually handle path creation, deletion, copy, and manifest/summary writing. This duplication already caused AB package layout drift requiring a follow-up fix.

**Evidence**

- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskOrganizeOutput.cs`
  - line 36: `Directory.CreateDirectory(outputDir)`
  - line 47: `File.Copy(srcPath, destPath, true)`
  - line 54: `File.WriteAllText(manifestPath, manifest.SerializeToJson(), Encoding.UTF8)`
  - line 88: `File.WriteAllText(summaryPath, summary.ToString(), Encoding.UTF8)`
  - line 93: `Directory.Delete(tempDir, true)`
- `Assets/FYAsset/Scripts/Build/BuildManage/Editor/ABBuildBackend.cs`
  - line 62: `Directory.Delete(outputDir, true)`
  - line 65: `Directory.CreateDirectory(outputDir)`
  - line 76: `File.Copy(sourcePath, destinationPath, true)`
  - line 95: `File.WriteAllText(manifestPath, _manifest.SerializeToJson(), Encoding.UTF8)`
  - line 104: `File.Copy(sourcePath, targetPath, true)`
- `Assets/FYAsset/Scripts/Build/BuildManage/Editor/BuildPathCustomizer.cs`
  - line 28: `Directory.Delete(finalOutputDir, true)`
  - line 30: `Directory.CreateDirectory(finalOutputDir)`
  - line 46: `File.Copy(file, targetPath, true)`
  - line 60: `File.Copy(file, targetPath, true)`
  - line 66: `File.Copy(file, targetPath, true)`
  - line 85: `Directory.Delete(serverDataPath, true)`

**Risk**

- same package-layout logic exists in multiple places
- file write/copy/delete semantics are not controlled by one abstraction
- future changes to package layout, manifest export, or cleanup behavior must be manually synchronized in multiple implementations

**Unification direction**

- extract a single Editor-side artifact infrastructure module, for example:
  - `BuildArtifactOrganizer`
  - `BuildArtifactWriter`
- let `TaskOrganizeOutput`, `ABBuildBackend`, and `BuildPathCustomizer` delegate to that shared layer
- route actual writes through `FileHelper` / `SerializationUtility` instead of raw `File.WriteAllText`

### 3. [High] Build and hotfix orchestration still uses ad-hoc `bool` / `string` result channels instead of structured error infrastructure

**Why this matters**

The project introduced `RuntimeMessage`, `BuildMessage`, `BuildTaskResult`, `RuntimeErrorCodes`, and `BuildErrorCodes`, but two orchestration surfaces still expose only `bool` and free-form strings. Top-level flows cannot carry structured diagnostics.

**Evidence**

- `Assets/FYAsset/Scripts/LegacyRuntime/IHotfixPipeline.cs`
  - line 25: `Task<bool> InitializeBackendAsync();`
  - line 49: `Task<bool> PostDownloadAsync(HotfixContext ctx);`
- `Assets/FYAsset/Scripts/LegacyRuntime/HotfixManager.cs`
  - line 58: `private static void ReportError(string message)`
  - line 187: `ReportError("[HotfixManager] 热更后端初始化失败");`
  - line 377: `ReportError("[HotfixManager] 存在下载失败的 bundle，请检查网络！");`
- `Assets/FYAsset/Scripts/Build/BuildManage/Editor/IBuildBackend.cs`
  - line 10: `Task<bool> BuildAsync(VersionNumber version, BuildType buildType);`
  - line 13: `string BuildSummary { get; }`
- `Assets/FYAsset/Scripts/Build/BuildManage/Editor/BuildProjectManager.cs`
  - line 123: `bool buildSucceeded = backend.BuildAsync(version, buildType).GetAwaiter().GetResult();`
  - line 126: `Debug.LogError($"[BuildProjectManager] 后端构建失败: {backend.BuildSummary}");`

**Risk**

- top-level failures lose structured code/severity/source
- orchestration cannot uniformly aggregate diagnostics
- build side and hotfix side each keep inventing side-channel summaries instead of using shared contracts

**Unification direction**

- add a structured orchestration result type for hotfix and build backends
- possible shapes:
  - `HotfixStepResult { bool Success; RuntimeMessage Error; }`
  - `BuildBackendResult { bool Success; BuildMessage Error; IReadOnlyList<BuildMessage> Diagnostics; }`
- stop using `BuildSummary` as a failure transport
- make top-level orchestrators log structured messages, not ad-hoc free-form strings

### 4. [Medium] New code still hardcodes error codes instead of extending `RuntimeErrorCodes` / `BuildErrorCodes`

**Why this matters**

Conventions require centralized error-code constants, but newer code introduces raw string codes directly inside tasks and backends, breaking taxonomy consistency and making code search/aggregation incomplete.

**Evidence**

- `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABPackageBackend.cs`
  - line 100: `RuntimeMessage.Error("INVALID_ARG", ...)`
  - line 120: `RuntimeMessage.Error("INVALID_ARG", ...)`
  - line 143: `RuntimeMessage.Error("INVALID_ARG", ...)`
  - line 162: `RuntimeMessage.Error("INVALID_ARG", ...)`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskPrepareContext.cs`
  - line 30: `BuildTaskResult.Fail("INVALID_BACKEND", ...)`
  - line 58: `BuildTaskResult.Fail("INVALID_PLATFORM", ...)`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskBuildBundles.cs`
  - line 121: `BuildTaskResult.Fail("BUILD_FAILED", ...)`
  - line 240: `BuildTaskResult.Fail("RAWFILE_MULTI_ASSET", ...)`
  - line 251: `BuildTaskResult.Fail("RAWFILE_COPY_FAILED", ...)`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskGenerateManifest.cs`
  - line 46: `BuildTaskResult.Fail("BUNDLE_FILE_NOT_FOUND", ...)`
  - line 73: `BuildTaskResult.Fail("BUNDLE_NOT_FOUND", ...)`
  - line 166: `BuildTaskResult.Fail("MANIFEST_INIT_FAILED", ...)`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskVerifyBuildResult.cs`
  - line 159: `BuildTaskResult.Fail("VERIFICATION_FAILED", ...)`

**Risk**

- error-code namespace is fragmented
- conventions are no longer enforceable by grep
- future error reporting infrastructure cannot reliably group failures

**Unification direction**

- extend `RuntimeErrorCodes` with missing runtime codes such as `InvalidArgument`
- add missing build-side constants to `BuildErrorCodes`
- optionally add semantic factories for `BuildTaskResult` to avoid raw strings at call sites

### 5. [Medium] Build/config asset paths are still hardcoded in several FYAsset files instead of using centralized constants

**Why this matters**

`FYAssetConstants.PIPELINE_CONFIG_ASSET_PATH` exists, but several consumers still embed raw asset paths -- the same category of bypass: independent code instead of shared infrastructure.

**Evidence**

- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskPrepareContext.cs`
  - line 22: `"Assets/Build/BuildPipelineConfig.asset"`
  - line 40: `"Assets/Build/VersionDataBase.asset"`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskBuildBundles.cs`
  - line 33: `"Assets/Build/BuildPipelineConfig.asset"`
- `Assets/FYAsset/Scripts/Build/Editor/PipelinePanel.cs`
  - line 60: `"Assets/Build/BuildPipelineConfig.asset"`
  - line 95: `"Assets/Build/BuildPipelineConfig.asset"`
- `Assets/FYAsset/Scripts/Build/Editor/VersionPanel.cs`
  - line 9: `"Assets/Build/VersionDataBase.asset"`
- `Assets/FYAsset/Scripts/Build/BuildManage/Editor/BuildProjectManager.cs`
  - line 15: `private static string versionDataBasePath => "Assets/Build/VersionDataBase.asset";`

**Risk**

- path changes require manual multi-file edits
- build/editor tools will drift out of sync with backend code

**Unification direction**

- add `FYAssetConstants.VERSION_DATABASE_ASSET_PATH`
- replace raw strings in FYAsset build/editor code with centralized constants

## Suggested Unification Plan

### Priority 1

- unify hotfix file operations behind `FileHelper`
- replace non-atomic metadata writes in Legacy hotfix path
- add missing `FileHelper` capabilities instead of continuing direct `File.*` / `Directory.*`

### Priority 2

- extract a shared build artifact organizer/writer for `TaskOrganizeOutput`, `ABBuildBackend`, and `BuildPathCustomizer`
- make all manifest / summary / package copy writes go through shared infrastructure

### Priority 3

- introduce structured orchestration results for `IHotfixPipeline` and `IBuildBackend`
- stop using `bool + BuildSummary/string` as top-level error transport

### Priority 4

- centralize remaining error codes into `RuntimeErrorCodes` / `BuildErrorCodes`
- centralize remaining FYAsset asset-path constants

## Non-Findings

- direct `Debug.Log*` usage by itself is not a review issue here; project conventions explicitly allow direct logging in lower-level code
- `RuntimeMessage` / `BuildMessage` constructors remain private and are still only used internally by their own factory methods
- `SerializationUtility` is already used in many of the right places; the problem is not absence of the serializer, but inconsistent file-layer and orchestration-layer adoption around it
