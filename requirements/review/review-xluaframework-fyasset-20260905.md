# XLuaFramework And FYAsset Current-Tree Audit

> **Date**: 2026-09-05
> **Reviewer**: Codex parent review with independent XLuaFramework, build/publication, docs/tests, and editor reviewers; FYAsset runtime findings below were directly verified by the parent
> **Scope**: Actual working tree over HEAD `e5de777a541d9a024035d26870643624f8e08e71`, including pre-existing uncommitted backend-selection changes; history inventory covers `68b75a5..e5de777` only
> **Method**: Source/call-chain review, historical decision comparison, independent perspective reviews, fresh .NET scenario and solution builds, parent verification of candidate findings. Codegraph timed out. No Unity/Player/publish/network acceptance was executed.
> **Status**: Detailed review delivered with open findings. Findings are not implementation approval. No production code changed; Unity acceptance and history rewrite remain open.

## Evidence Boundary

- A green build proves the current generated solution compiles, not that AA+Shared, AB+Shared and XLuaFramework independently export and compile in a clean Unity project.
- Source-text boundary tests prove their lexical predicates. Stub-based tests prove only the production code actually linked plus the modeled engine behavior.
- Static failure-path reasoning is labeled separately from a runnable reproduction. No Unity runtime failure is claimed reproduced unless a run is recorded here.
- The working tree contains pre-existing staged/unstaged/untracked work. This report is not a review of HEAD alone and its verification cannot authorize rewritten historical HEAD.
- The source inventory has 247 C# and 6 Lua files under the two framework roots. Inventory is exhaustive for those extensions; semantic review is risk-directed, not an assertion that every line was exhaustively audited.

## Current Responsibility Model

```text
Host GameLauncher
  -> serialized Compat FYAssetBackendSettings
  -> Compat HotfixManager -> concrete AAHotfixManager / ABHotfixManager
  -> LuaAssetRuntime.SetLoader(FYAssetLuaAssetLoaderAdapter)
  -> LuaEnvManager -> XLuaLoader -> LuaModuleRegistry -> upper UI startup

XLuaFramework consumer
  -> ILuaAssetLoader (neutral Unity object / string-error contract)
  -> Compat FYAssetLuaAssetLoaderAdapter
  -> Compat AssetPackageManager facade
  -> concrete AA / AB resource owner

Concrete AA / AB build entry
  -> shared runner + ordered config.Tasks
  -> backend-specific tasks and baseline handler
  -> package/startup publication + Latest/LatestFull baseline
  -> publish target -> runtime hotfix package consumers
```

The diagram groups responsibilities, not exact statement ordering: GameLauncher registers the Lua resource adapter before validating backend settings or initializing hotfix. Strict export independence is the latest intended contract; transient historical XLF prerequisites, a DAG scheduler, repository objects/HEAD, and slot-based CustomTaskEntries are not current design authority.

## Findings

Evidence labels: **REPRO** means a runnable check in this round demonstrated the failure. **STATIC** means the stated failure follows from parent-inspected source under the specified input/failure; no Unity execution is implied. P1 blocks the affected delivery/safety contract, P2 is a concrete correctness or coverage defect, and P3 is narrower documentation/maintenance debt. All findings remain open.

### T01 - P1 - Incomplete recovery snapshots can delete original data [STATIC]

- Evidence: `Assets/FYAsset/Scripts/Compat/Editor/Tests/BuildTestEngine.cs:54`, `BuildTestState.cs:435`, `BuildTestState.cs:555`, `BuildTestState.cs:570`, and `E2ETestEngine.cs:109` / `:205`.
- Chain: write durable recovery record -> partially copy project/targets -> interruption or copy exception -> recover every recorded scope. There is no durable per-scope snapshot-complete flag. Missing backup means "original absent" in RestorePath; RestoreDirectory deletes the source before testing whether a usable backup exists.
- Impact: packages, BuildData, StreamingAssets or a target service root can be emptied during supposed recovery. E2E's exception path can enter this logic immediately, even without a restart. The Build-only in-memory flags do not protect durable recovery.
- Direction: distinguish absent-at-snapshot from incomplete-snapshot; record scope completion before permitting destructive restore. Test interruption after each snapshot stage with owned temporary trees, never the developer's real roots.

### B01 - P1 - Build publication, baseline and version have separate commit points [STATIC]

- Evidence: `Assets/FYAsset/Scripts/Shared/Build/Editor/BuildProjectRunner.cs:126`, `:128`, `:155`, `:193`, `:212`; `Build/Baseline/BuildBaselineStore.cs:92`.
- Chain: startup export -> PackageIndex publication -> baseline atomic write -> later VersionRecord save. If baseline writing fails, catch deletes the new package but does not restore the already published pointer or startup bytes. A PackageIndex failure can leave startup data advanced; a version-save failure is outside RunBuild compensation entirely.
- Impact: a reported failed build can leave mismatched visible identities, including a pointer to a deleted package. Atomicity of one baseline JSON file does not make the whole operation atomic.
- Direction: one transaction owner across these mutations, with compensation retained until the final identity commit. Fault-inject each boundary and assert old bytes/identities survive. Do not restore a repository kernel merely to solve this.

### B02 - P1 - Early Standalone failure deletes the previous good package [STATIC]

- Evidence: `Assets/FYAsset/Scripts/Shared/Build/Release/Editor/BuildPackageRequest.cs:45`, `Build/Editor/BuildProjectRunner.cs:112`, `:212`, `:224`.
- Every Standalone request targets the shared Standalone directory. A backend failure before producing any new output, such as missing config, still calls HandleFailedPackage; the safety check deliberately accepts that shared directory and removes it.
- Direction: distinguish attempt-owned staging from previously published offline content; atomically replace offline content and BuildIndex only after success. Test old-good package plus early and late failures.

### B03 - P1 - Single-format manifests conflict with Push requirements [STATIC]

- Evidence: `Assets/FYAsset/Scripts/AA/Build/Release/Editor/AABuildBackend.cs:60`, `AA/Build/Pipeline/Editor/Tasks/TaskWriteAAPackageManifest.cs:76`, `Shared/Build/Publish/PackagePublishTransaction.cs:133`; `Assets/Resources/FYAssetAASettings.asset:21` has BinaryOnly (2).
- The producer intentionally omits/removes JSON in BinaryOnly, but baseline metadata always requires both JSON and binary. Push rejects a package that the current AA configuration is designed to produce. AB's analogous fixed manifest list also needs format-matrix coverage.
- Direction: persist the actual required output set and AA catalog requirement from the build result. Verify JsonOnly/BinaryOnly/Both through build-to-push, without publishing to a real service for the test.

### B04 - P1 - A malformed preview endpoint expands into real build tasks [STATIC]

- Evidence: `Assets/FYAsset/Scripts/Shared/Build/Repository/Editor/BuildPreviewRunner.cs:39`, `Shared/Build/Pipeline/Editor/BuildPipelineRunner.cs:84`, `Shared/Build/Editor/UI/PipelinePanel.cs:427`, and `Shared/Build/Pipeline/Editor/Tasks/TaskWritePackageIndex.cs:19`.
- Missing/unknown HotfixDiffTaskName adds the entire list to the whitelist; the runner does not reject that endpoint. CreateConfig fills Tasks but not the endpoint. Preview does not set DeferPackagePublication, and publish tasks trust the stop boundary instead of rejecting preview.
- Impact: a request presented as preview can move AA groups, build real output, and reach pointer publication if intervening tasks succeed. AB may fail at an output-path guard after doing work; that does not establish a read-only preview contract.
- Direction: validate a present, valid endpoint before executing any task and independently prohibit publication in preview. Keep ordered lists, not a new DAG.

### E01 - P1 - Clear Channel can delete other channels' and backends' packages [STATIC]

- Evidence: `Assets/FYAsset/Scripts/Shared/Build/Editor/UI/RepositoryStatusPanel.cs:1025`, `:1094`, `:1107`.
- The dialog names one channel/backend, but DeleteLocalPackageFolders enumerates every Build_* under the shared PackagesDir. Path containment only prevents escaping that directory; it does not prove channel ownership.
- Reproduction sequence: retain AA and AB packages, choose one Repository channel, enable Delete local package folders, and Clear. Other packages match the same deletion loop while their baseline records remain.
- Direction: resolve exact owned packages or expose a separately named global cleanup with an affected-path preview. Test two backends and two channels using temporary roots.

### E02 - P1 - Saving a Curate draft overwrites persisted external edits [STATIC]

- Evidence: `Assets/FYAsset/Scripts/AB/Build/Editor/ABPipeline/AssetsCollectionPanel.cs:212`, `:1002`, `:1032`; `AB/Build/Collector/Editor/CollectorMutationUtility.cs:90` onward.
- A dirty draft receives an external-change event but only rescans itself. Save then replaces persisted Packages, AssetEntries, Ignore and ExcludedAssets wholesale.
- Reproduction sequence: edit a label in Curate, exclude an asset from its Inspector, then save Curate. The older draft drops the saved exclusion or membership change without conflict detection.
- Direction: revision-check the saved owner before committing a draft and merge or explicitly reject conflicts. Do not fix this by silently throwing away either editor's work.

### R01 - P1 - AB common load/unload bypasses the advertised lifetime owner [STATIC]

- Evidence: `Assets/FYAsset/Scripts/AB/Runtime/ABPackageManager.cs:159`, `:175`, `:300`; `Backends/ABPackageBackend.cs:98`, `:485`; `Backends/ABBundleLoader.cs:145`.
- Common tuple loads cache by EntryId without allocating a HandleRegistry owner. Cache-hit loads add no bundle reference. One common UnloadAsset calls ReleaseEntry directly, which drops the cache and calls UnloadBundle; zero bundle references execute Unload(true).
- Impact: two ordinary consumers loading the same entry do not have two owners. The first unload can destroy the second consumer's asset, and a common unload can bypass a still-active handle for that same entry. The AA equivalent explicitly retains one ticket per acquisition, so the neutral facade does not have backend-independent ownership semantics.
- Direction: establish one acquisition/release contract shared by common and handle APIs; test two common loads, common+handle mixed use, and both release orders. Existing runtime-resource tests cover AA tickets and AB bundle single-flight, not this manager/backend composition.

### T02 - P1 - E2E Lua and Raw markers are assigned, not measured [STATIC]

- Evidence: `Assets/FYAsset/Scripts/Compat/Runtime/E2E/FYAssetE2ECoordinator.cs:95` and `:97`.
- The coordinator really loads/checks Async and Sync assets, then assigns MarkerLua to a constant and MarkerRaw from the requested backend/expectHotfix flag. It neither executes the fixture Lua module nor reads the RawFile.
- Impact: a missing/corrupt Lua fixture or an unchanged AB Raw hotfix can still be reported as expected marker success. GameLauncher readiness proves some bootstrap ran, not those fixture contents.
- Direction: derive every reported marker from the real resource/Lua consumer path. Introduce negative fixtures that must fail. Historical E2E PASS records must not be treated as proof of these two payload checks.

### D01 - P1 - The declared independent export sets are not self-contained [STATIC]

- Evidence: `docs/FYAsset/export-sets-导出集.md:11` / `:17`; `Assets/FYAsset/Scripts/Shared/Hotfix/HotfixPackageValidator.cs:93` calls external HashGenerator; Shared also uses external FileHelper/serialization helpers. Definitions are under Assets/Tools.
- XLuaFramework inherits external Singleton at `Scripts/EventCentre/EventCentre.cs:6`, waits on excluded host LaunchSignal at `Scripts/Bridge/Utils/LuaBehaviourBridge.cs:101`, and imports Input System at `Scripts/Bridge/InputBridge.cs:5`, although the documented prerequisite is only XLua.
- Impact: FYAsset-name exclusion is not dependency closure. Importing only the documented trees into an empty project leaves unresolved dependencies.
- Direction: decide the minimum utility/package prerequisites and remove/inject the host startup dependency. Compile the exact three export manifests in clean projects; do not silently redefine independence as "works in this full project". No clean-export build was executed here.

### R02 - P2 - Default handles alias live slots; Reset also erases generation identity [REPRO]

- Evidence: `Assets/FYAsset/Scripts/AB/Runtime/Backends/Models/AssetHandle.cs:31`, `:98`, `:140`; `HandleRegistry.cs:85`, `:252`.
- A default struct is (0,0); the first allocated slot also begins at (0,0). Default IsValid becomes true, and default.Release releases somebody else's owner. An in-memory probe compiled the actual two source files and demonstrated this.
- Reset zeroes slot generations, so a stale old handle can become valid again when slot 0 is reallocated and can release the new callback. No current production caller of HandleRegistry.Reset was found in the targeted runtime scan; that part is a latent internal API hazard, not a demonstrated scene-reset failure.
- Direction: reserve an invalid zero identity and keep generation/session identity monotonic across resets. Add production-source tests for default, copied, released, retained, and post-reset handles.

### R03 - P2 - AB RawFile fallback ignores the Standalone subdirectory [STATIC]

- Evidence: `Assets/FYAsset/Scripts/AB/Runtime/Backends/ABPackageBackend.cs:385` / `:413`; `Backends/ABBundleLoader.cs:310`; `Shared/Hotfix/HotfixFlowBase.cs:87`.
- Bundle fallback chooses StreamingAssets/Standalone/bundles in Standalone mode, but Raw bytes fallback always uses StreamingAssets/bundles. Standalone hotfix skips online activation and does not map CurrentGUIDRoot to that offline directory.
- Impact: an offline raw asset present only in the supported isolated output location cannot be read by the raw API, or an unrelated normal baseline is consulted. T02 masks this in current coordinator evidence.
- Direction: share the existing mode-aware physical-root policy across bundle and raw I/O; verify RawFile contents in a Standalone package with no online baseline.

### T03 - P2 - Failed restoration is marked completed and excluded from retry [STATIC]

- Evidence: `Assets/FYAsset/Scripts/Compat/Editor/Tests/BuildTestState.cs:75`, `:96`, `:101`; `BuildTestEngine.cs:450` / `:462`.
- Completed becomes true regardless of restored. Stale recovery skips completed records. A target failure also aborts the restore loop before later targets and project restoration.
- Direction: track unfinished recovery independently of test completion, attempt independent scopes and aggregate errors, retain backups until every required scope restores. Inject one failed target and confirm later scopes still run.

### T04 - P2 - Test preflight mutates assets before snapshots [STATIC]

- Evidence: `Assets/FYAsset/Scripts/Compat/Editor/Tests/BuildTestEngine.cs:42`; `BuildTestFixtures.cs:16`, `:106`; `BuildTestState.cs:153`, `:175`.
- EnsurePermanentFixtures changes fixture markers, Lua container/index and both backend groups before target validation and snapshot. Async fixture is snapshotted but deliberately not restored; grouping/index are not comprehensively covered.
- Impact: even an invalid-target run can alter the checkout, and restore does not mean return to pre-run bytes. Permanent fixture provisioning can be valid, but must not be hidden inside a supposedly reversible run.
- Direction: explicit provisioning command, read-only preflight, snapshot-before-mutation, and exact scope restoration tests.

### T05 - P2 - AB address acceptance and restore self-check test substitutes for the contract [STATIC]

- Evidence: `Assets/FYAsset/Scripts/Compat/Editor/Tests/BuildTestAcceptance.cs:478`, `:506`; `BuildTestSelfCheck.cs:25`.
- AB acceptance scans manifest bytes for strings and accepts fixture-named files, including when JSON exists; it does not verify AssetEntries membership. The restore self-check copies a marker with File.Copy rather than calling production snapshot/recovery.
- Impact: unaddressable or malformed AB data and broken restore logic can coexist with green acceptance.
- Direction: parse with the production codec and check address/payload/artifact relations; test real recovery functions under fault injection. AA address acceptance already demonstrates structured parsing and is a useful local pattern.

### T06 - P2 - Three scenario project files are absent from Git [REPRO]

- Evidence: `.gitignore:37`; `git ls-files tests/scenario/*.csproj tests/scenario/*/*.csproj` contains only HotfixRuntimeStateMachineTests.csproj.
- git check-ignore identifies S2RuntimeBoundaryTests.csproj, s3_resource_boundary/S3ResourceBoundaryTests.csproj and runtime_resource/RuntimeResourceScenarioTests.csproj as ignored by *.csproj. Their local builds passed here, but a clean checkout lacks those entry projects. No checked-in generator for these exact projects was found in the searched tooling/tests.
- Direction: narrowly track the hand-maintained scenario projects while continuing to ignore Unity-generated root projects, or supply a checked-in deterministic generator and invoke it in CI. Do not use force-added local files as the only undocumented setup.

### T07 - P2 - Export scans report PASS after scanning zero types [REPRO]

- Evidence: `tests/scenario/s3_resource_boundary/test_export_boundary.cs:268`, `test_xluaframework_boundary.cs:80`; RepoSource.Root already exists in scenario_support.cs:42.
- EnumerateSources resolves relative to process CWD and yields nothing for missing roots. Running the same built DLL from its scenario directory reports all type counts as zero while all six scenarios pass; exact output is below.
- Direction: use RepoSource.Root and fail on missing/empty required source sets. Test root and subdirectory invocation before relying on the gate for exports.

### T08 - P2 - Promised malformed-task and transaction gates do not execute those implementations [STATIC]

- Evidence: `requirements/plan/archive/2026-09-04-pipeline-custom-tasks.md:25`; `tests/scenario/s3_resource_boundary/test_export_boundary.cs:120`; all four inspected scenario project Compile lists.
- Task checks inspect source/YAML rather than compile BuildPipelineRunner/BuildTaskResolver. Hotfix tests compile decision/validation helpers, not HotfixFlowBase; restore/transaction orchestration is likewise absent. This is an unclosed success criterion, distinct from ACCEPT-01's native artifact equivalence.
- Direction: add actual runner/resolver execution with missing/duplicate/throwing tasks and validate fault behavior at the real finalization boundaries. Track this as ACCEPT-03; archive housekeeping did not complete it.

### X01 - P2 - LuaToLua dispatch calls a record table as a function [REPRO]

- Evidence: `Assets/XLuaFramework/LuaScripts/Framework/EventCentre/EventCentre.lua:48`, `:80`, `:84`.
- on stores {handler, closure}; trigger iterates the records as functions. A registered zero-argument event immediately fails with "attempt to call a table value" in the real Lua file.
- Direction: dispatch the stored callback and specify listener mutation behavior. Add actual Lua on/trigger/off tests; C# event tests do not cover this route.

### X02 - P2 - Coroutine identity remains on a global stack while suspended [REPRO]

- Evidence: `Assets/XLuaFramework/LuaScripts/Core/CoroutineScheduler/coroutineBridge.lua:20`, `:113`, `:130`; analogous lifetime-wide stack in `Scripts/CoroutineScheduler/CSharpCoroutineScheduler.cs:44`.
- A starts/yields, B starts/yields, A resumes: current ID is B. The real Lua algorithm with mocked C# notification boundaries printed expected A=1, observed=2.
- Direction: identify the executing coroutine, not the most recently started unfinished coroutine. Test interleaved/out-of-order completion and cross-language waits. The Lua reproduction uses local Lua 5.4.8, not Unity's embedded VM; C# Unity scheduling remains static evidence.

### X03 - P2 - Bridge Start/OnEnable delivery depends on load timing [STATIC]

- Evidence: `Assets/XLuaFramework/Scripts/Bridge/Utils/LuaBehaviourBridge.cs:227`, `:288`, `:299`.
- Initialization manually invokes Lua Start, and Unity Start invokes it again when readiness was reached synchronously. If loading is delayed, initial OnEnable is ignored and never replayed.
- Direction: one exactly-once lifecycle state with explicit activation reconciliation. Compare completed and delayed loader tasks, disable/re-enable during loading, and repeated initialization traces.

### X04 - P2 - Bridge async initialization can resume after destruction and lacks rollback [STATIC]

- Evidence: `Assets/XLuaFramework/Scripts/Bridge/Utils/LuaBehaviourBridge.cs:101`, `:126`, `:204`, `:218`, `:354`.
- There is no destroyed/session check after awaits. OnDestroy clears only already registered instances. After CacheFunctions, a bridge or Lua Awake exception disposes only the table, not the cached functions/previously initialized components. Some fatal setup failures return normally and allow isInitialized=true.
- Direction: one initialization ownership transaction with a lifetime fence and explicit failure result; commit an instance only after every acquisition succeeds. Test destroy-at-each-await and partial bridge/Lua-Awake failure.

### X05 - P2 - Input unbind never removes subscribed delegates [STATIC]

- Evidence: `Assets/XLuaFramework/Scripts/Bridge/InputBridge.cs:69`, `:88`, `:146`.
- Anonymous InputAction delegates capture a LuaFunction. Unbind disposes that function and deletes bookkeeping but never unsubscribes the delegate; rebind overwrites bookkeeping and adds another subscription.
- Direction: retain exact normalized action/phase delegate identities, unsubscribe before disposing, define replacement. Verify bind/unbind/fire and duplicate bind/destroy/fire with a controlled input source.

### X06 - P2 - Bridge resource acquisitions have no matching releases [STATIC]

- Evidence: `Assets/XLuaFramework/Scripts/Bridge/ScriptObjectBridge.cs:34` / `:47`, `Bridge/Anime/AnimBridge.cs:27`, `Bridge/Utils/LuaBehaviourBridge.cs:126`; Compat adapter and `Assets/FYAsset/Scripts/AA/Runtime/AAPackageManager.cs:166`.
- Config/SO loads are not balanced on destroy or partial initialization. Clearing a Unity component/dictionary does not release AA tickets. Repeated spawn/destroy accumulates retained resource ownership.
- Direction: record every successful acquisition against its original loader/address/type and release exactly once, including rollback. A counting ILuaAssetLoader is the minimum test substitute.

### X07 - P2 - Process-global Lua cache/index ignores environment and mode ownership [STATIC]

- Evidence: `Assets/XLuaFramework/Scripts/XLuaLoader/XLuaLoader.cs:29`, `:49`, `:61`, `:75`; `Scripts/LuaEnvManager.cs:9`; `Scripts/Resource/LuaAssetRuntime.cs:15` / `:22`.
- New VM or replacement loader keeps old index and cached bytes. Cache lookup precedes mode filtering; EditorOnly can consume old package bytes and falls through to the retained package index.
- Direction: a loader-session owner for cache/index/acquisition lifetime and mode checks before lookup. Test package A -> new session/package B, and EditorOnly after PackageOnly.

### X08 - P2 - Pointer callbacks abandon fresh LuaFunction wrappers [STATIC]

- Evidence: `Assets/XLuaFramework/Scripts/Bridge/UIEventBridge.cs:29`; `Scripts/LuaEnvManager.cs:31`; inspected host boot path.
- Each pointer/drag event obtains a new function wrapper and never disposes it. No .Tick/.GC maintenance call was found under the inspected XLuaFramework/Global/Game roots. This creates repeated unmanaged-reference retention pressure; no memory profile is claimed.
- Direction: scoped disposal or cached callbacks with teardown, plus an explicit VM-maintenance owner. Verify sustained pointer dispatch and cleanup with the embedded VM before making leak-size claims.

### X09 - P2 - The Lua creation template is incompatible with both bridge modes [STATIC]

- Evidence: `Assets/XLuaFramework/Scripts/Editor/LuaFileCreatorWithName.cs:7`; `Bridge/Utils/LuaBehaviourBridge.cs:160`.
- The generated script defines global Start and returns no table. require returns true, while Module and Class bridge modes both require a LuaTable, and Class also needs New.
- Direction: emit a valid chosen Module/Class template. Test an untouched created script through require and bridge initialization.

### B05 - P2 - Implicit dependency placement conflicts with enforced physical bundle types [STATIC]

- Evidence: `Assets/FYAsset/Scripts/AB/Build/Collector/Editor/DependencyAnalysis/DependencyAnalyzer.cs:335`; `AB/Build/Pipeline/Editor/Tasks/TaskBuildBundles.cs:298`.
- A below-threshold uncollected material is copied as a Serialized/Material entry into its collected prefab's bundle; validation requires one exact PrimaryType. Scene plus copied serialized dependency violates the single PayloadKind rule too.
- Direction: reconcile dependency embedding with the later physical-bundle invariant. Keep Unity-implicit dependencies implicit or place explicit dependency entries in compatible physical buckets. Test one prefab and one scene referencing assets outside collector roots.

### B06 - P2 - NoShare is overridden by the normal sharing threshold [STATIC]

- Evidence: `Assets/FYAsset/Scripts/AB/Build/Collector/Editor/DependencyAnalysis/DependencyAnalyzer.cs:307` / `:326`.
- Threshold/size sharing executes before else-if(noShare), without excluding noShare. A NoShare-only match with two references is shared anyway.
- Direction: make the explicit prohibition win after conflict validation. Cover ForceShare, NoShare, neither, both, and size/reference thresholds.

### B07 - P2 - Rollback failure deletes its own recovery backups [STATIC]

- Evidence: `Assets/FYAsset/Scripts/Shared/Build/Publish/PackagePublishTransaction.cs:97`, `:115`, `:171`.
- Rollback's finally marks completion and cleans the work root even if restoring a package or pointer throws. MoveDirectory also ignores a failed destination deletion before trying the move.
- Direction: retain the backup/work record on unsuccessful compensation and expose recoverable failure. A successful explicit rollback test is insufficient; inject a lock/restore exception in an owned temporary target.

### B08 - P2 - Version creation silently clears the current channel [STATIC]

- Evidence: `Assets/FYAsset/Scripts/Shared/Build/Versioning/VersionRecord.cs:19`, `:39`, `:64`; runner calls omit channel.
- BuildNextVersion copies CurrentVersion.Channel then unconditionally replaces it with the optional empty argument. A beta version's next build goes to the default channel, including baseline-key selection.
- Direction: distinguish omitted/preserve from explicit stable selection; test Full/Hotfix/Standalone from every supported channel.

### B09 - P2 - AB preview drops Lua index membership, not merely regeneration [STATIC]

- Evidence: `Assets/FYAsset/Scripts/Compat/Editor/Build/LuaScriptsIndexBuildTask.cs:21`, `:80`; normal AB task configuration and collector exclusion of Assets/Build.
- The preview early-return skips both asset rewriting and transient injection into CollectedAssets. Real builds include that bootstrap entry, so an unchanged post-build preview can describe its bundle as removed.
- Direction: preserve read-only collection membership without rebuilding the asset; separately flag stale index-content uncertainty. This is beyond the accepted "last built index contents" compromise.

### B10 - P2 - Missing dependency graph is accepted as no dependencies [STATIC]

- Evidence: `Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/Tasks/TaskGenerateManifest.cs:20`, `:74`, `:112`; shared runner validates presence of backbone names but not this producer-before-consumer relation.
- Reorder dependency analysis after manifest generation with already explicitly collected cross-bundle references. Generation can use null graph and keep all dependency arrays empty rather than fail. Runtime consumes those arrays.
- Direction: require the graph and validate the essential list-order constraint before mutation. Test reordered tasks without reintroducing topology scheduling.

### E03 - P2 - Scan navigation silently discards an unsaved Curate draft [STATIC]

- Evidence: `Assets/FYAsset/Scripts/AB/Build/Editor/ABPipeline/AssetsCollectionPanel.cs:161`, `:286`.
- ShowScanStage calls EnterScan, which nulls the draft and clears its dirty flag without asking. Returning to Curate reloads saved state.
- Direction: retain drafts across read-only navigation or require explicit discard. Test edit -> Scan -> Curate and cancellation.

### E04 - P2 - Scene normalization can enable disabled content [STATIC]

- Evidence: `Assets/FYAsset/Scripts/AB/Build/Editor/ABPipeline/AssetsCollectionPanel.cs:2223`, `:2257`, `:2265`, `:2377`.
- A scene-only folder is removed from its original group and re-created in a name-derived group with default Enabled=true, without preserving the original ownership/labels/rules. A disabled custom group can therefore gain enabled replacement content when Curate normalizes and saves.
- Direction: preserve the source group's semantics while changing collector shape. Test disabled/custom-labeled scene-only groups, not only the default Scenes group.

### E05 - P2 - Apply Address writes a value that the scanner immediately ignores [STATIC]

- Evidence: `Assets/FYAsset/Scripts/AB/Build/Editor/ABPipeline/AssetsCollectionPanel.cs:2761`; `AB/Build/Collector/Editor/CollectionScanner.cs:490`.
- ApplyAddressStyleToAsset computes the chosen style but sets AutoAddress=true. Scanner chooses the setting-wide generated address whenever AutoAddress is true. Group Apply has the same effective-value issue for automatic entries.
- Direction: save an explicit override or define a real per-entry style contract. Test effective preview/build addresses after applying a non-global style.

### E06 - P2 - Scanner adds persistent entries during nominally read-only scans [STATIC]

- Evidence: `Assets/FYAsset/Scripts/AB/Build/Collector/Editor/CollectionScanner.cs:491`; `Collector/AssetCollectionSetting.cs:171`.
- GetOrCreateAssetEntry appends into the supplied setting. When that is the loaded SO rather than an isolated draft, preview discovers new metadata by mutating the authoritative asset, which a later SaveAssets can persist.
- Direction: separate transient default resolution from explicit persistent mutation. Verify serialized settings before/after preview with pre-existing unsaved edits.

### D02 - P2 - Public AB loading documentation describes the wrong return type [STATIC]

- Evidence: `docs/FYAsset/ab-runtime-运行时加载.md:199` says common loads return objects directly; `Assets/FYAsset/Scripts/AB/Runtime/ABPackageManager.cs:159` / `:167` return (T, RuntimeMessage).
- Direction: show tuple/error handling and distinguish handle APIs. Correct consumer examples against compiled signatures, not old interface names.

### D03 - P3 - Current operational docs still advertise retired entry points and repository state [STATIC]

- Evidence: `docs/FYAsset/自动化测试管线.md:3`, `:20`, `:64`, `:131` reference an absent run_fyasset_test_batch.py, an extra Tests/Editor path, and removed embedded Test panels. Actual window is Compat/Editor/Tests/BuildTestWindow.cs with FYAsset/Build/Test Matrix.
- `docs/FYAsset/资源管理架构文档.md:42` assigns facades to Shared; `:55` describes objects/HEAD atomic commits even though current code uses Compat and Latest/LatestFull baseline.json.
- Direction: one current ownership diagram and executable operator entry list; keep earlier designs in archives. Do not resurrect removed scripts/classes merely to make old prose true.

## Candidates Requiring Further Validation

These independent-review candidates are retained rather than silently dropped, but are not counted as parent-confirmed findings or implementation-ready fixes:

| Candidate | Evidence Anchor | Remaining Question |
|-----------|-----------------|--------------------|
| AA successive hotfixes may omit prior changes after group reset | TaskScanAAHotfixDiff.cs:75; TaskMoveAAHotfixGroups.cs:85 | Execute Full -> change A -> Hotfix1 -> restore groups -> change B -> Hotfix2 and inspect catalog reachability from original Full, including clients skipping Hotfix1 |
| AA dependency-only changes are invisible to source hashes | TaskScanAAHotfixDiff.cs:94 | Confirm remote/local Addressables bundle delivery with unchanged prefab and changed unaddressable dependency; source-file hashing demonstrably omits dependency state |
| AB prefix matching can misassociate valid similar bundle names | TaskBuildBundles.cs:197 | Validate exact generated names/ui vs ui~hd fixture and both insertion orders through native output |
| Platform override may disagree with baseline/bootstrap target | TaskPrepareContext.cs:33; BuildBaselineStore.cs:18 | Exercise supported CLI target selection differing from Editor active target; reject or freeze identity consistently |
| Failed AB attempt may retain stale _temp output | TaskBuildBundles.cs:114 | Confirm retry cleanup owned by all entry points, then reproduce changed bundle-set retry |
| Parseable but semantically corrupt baseline is accepted | BuildBaselineStore.cs:49 | Establish required serialized invariants and test Hotfix record in LatestFull, empty fields and identity mismatches |
| Shared Push validation only checks manifest presence | PackagePublishTransaction.cs:127 | Define minimum validation without violating whole-package-copy ownership; test missing catalog/bundle and corrupt metadata |
| Multi-script bridge configuration overwrites one-instance components | LuaBehaviourBridge.cs:204; InputBridge.cs:17; UIEventBridge.cs:18 | Exercise two real scripts sharing those components; supplied configurations inspected by reviewer are single-script |
| TypeMemberListSO editor configuration is disconnected from generation | TypeMemberListSO.cs:26; LabelBatchManagement/Editor/XLuaConfig.cs:34 | Confirm all generation providers and AOT output; no generation/player run authorized |
| Undo/import/raw Inspector mutations do not notify Curate | CollectorReverseIndex.cs:61; AssetsCollectionPanel.cs:85 | Validate Unity event ordering and revision propagation with Undo/import/deletion |
| Confirming old Project Scan can replace newer saved filters | AssetsCollectionPanel.cs:322 / :398 / :1032 | Reproduce Ignore/exclusion edit between scan and confirmation and decide separate filter ownership |
| Membership lookup can disagree with actual scene eligibility | CollectorReverseIndex.cs:232; CollectionScanner.cs:407 | Exercise mixed prefab/scene folder through Inspector/context menu/Curate and scanner |
| Empty final-package deletion cannot be saved | AssetsCollectionPanel.cs:1050 | Decide whether empty configs are intentionally invalid; UI must not offer an unpersistable deletion silently |
| Preview delta/report bodies can stay stale after identity or load failure | RepositoryStatusPanel.cs:618 / :892; ABReportPanel.cs:205 | Unity UI reproduction and request-keyed model/render ordering |
| Publish self-check omits required ManifestFileNames | Compat/Editor/Repository/HotfixPublishSelfCheck.cs:84 | Inspect/repair fixture contract and rerun only against owned local roots |

## Design Assessment

The AA/AB/Shared/Compat split and a linear task list are reasonable current boundaries; another manager, DAG or repository emulation would not solve the verified failures. The accumulated complexity is concentrated in five missing contracts:

1. **Mutation ownership:** build publication, baseline and version each treat local success as final; rollback can erase its own evidence. Define one operation's commit point and exactly which previous bytes it can restore.
2. **Acquisition ownership:** AA tickets, AB cached entries, AB handles and Lua component loads have different release semantics behind a common facade. Identify one owner per successful acquisition and make mixed API usage explicit.
3. **Session ownership:** Lua VM, module bytes/index, bridge instances, input delegates and coroutine identities have mismatched lifetimes. Group state by an actual startup/session boundary rather than adding global reset calls piecemeal.
4. **Editor state authority:** saved SO, Curate draft, scan result and reverse index can each be treated as current truth. Keep saved state authoritative, draft revisions explicit, and derived results keyed by the revision they describe.
5. **Verification authority:** textual presence, filename guesses and assigned markers have repeatedly been used as substitutes for observable behavior. Negative tests must make false success impossible; a green declaration scan cannot establish package closure.

Recommended review-driven sequence, not implementation approval: protect destructive/transaction paths first; repair verification credibility; stabilize runtime/bridge ownership; reconcile editor state; then update current docs and retire only abstractions proven unused. Historical plans, progress and backups retain decision provenance throughout.

## Coverage And Limitations

- XLuaFramework independent pass reported full reads of all 59 C#/Lua files; parent reread lifecycle/loader/input/event/coroutine/resource integration paths and directly reproduced X01/X02/R02.
- Build pass covered complete main Shared/AA/AB producer-consumer chains, publication and collector dependencies. Parent validated listed B findings against the corresponding producers and consumers.
- Editor pass read all 3,022 lines of AssetsCollectionPanel plus RepositoryStatusPanel, PipelinePanel, collector mutation/reverse-index UI, and AB report UI. Parent confirmed E01-E06; other UI candidates remain explicitly unverified.
- Docs/tests pass compared FYAsset docs, current/historical plans, scenario project inputs and test orchestration. Parent checked recovery, fixture, acceptance and coordinator code and reproduced T06/T07.
- One separately launched runtime review result could not be retrieved from the agent registry and is not used as evidence. Parent directly reviewed AAPackageManager, ABPackageManager, ABPackageBackend, HandleRegistry/AssetHandle, relevant ABBundleLoader and HotfixFlowBase/ABHotfixManager paths. A full fresh native hotfix/download audit remains outside the verified coverage; no claim of exhaustive all-file runtime review is made.
- No Unity Editor workflow, native Addressables/AB build, Player/IL2CPP, Cloudflare publish, memory/performance profiling, or clean-export compilation was run. Those limits remain material even with the green generated solution.


## Size And Ownership Signals

| Area | Source Files | Lines |
|------|--------------|-------|
| XLuaFramework | 59 | 7,144 |
| FYAsset Shared | 70 | 9,987 |
| FYAsset Compat | 27 | 6,108 |
| FYAsset AA | 26 | 2,903 |
| FYAsset AB | 71 | 15,121 |

Largest concentration points: AssetsCollectionPanel 3,022 lines; RepositoryStatusPanel 1,730; HotfixFlowBase 1,229; E2ETestEngine 905; CollectionScanner 796; ABBundleLoader 714. Size is a navigation/risk signal, not a bug or sufficient reason to introduce another abstraction. Boundary or decomposition recommendations must name the scenario and state owner they improve.

## Fresh Verification

Working directory for every command: `E:/unity/project/XLuaHotfix`. Test processes use `TEMP`, `TMP`, and `TMPDIR` scoped to `E:/unity/project/XLuaHotfix/Temp`; .NET telemetry is disabled for the invoked processes. No package restore/network, publisher, real remote service, or Unity GUI is invoked.

### S3

```text
dotnet build tests/scenario/s3_resource_boundary/S3ResourceBoundaryTests.csproj --no-restore --no-incremental -v:q
Build succeeded. 0 warnings, 0 errors. Elapsed 00:00:03.98. EXIT=0

dotnet tests/scenario/s3_resource_boundary/bin/Debug/net8.0/S3ResourceBoundaryTests.dll
PASS UpperPackageBoundary
PASS BackendLabelPanels
PASS LabelParityRetirement
PASS StaleSerializedDependency
export boundary: AA types=36, AB types=102
export boundary: Compat types=42
PASS ExportBoundary
xlua framework boundary: FYAsset types=282
PASS XLuaFrameworkBoundary
S3 scenarios: 6/6 passed
EXIT=0
```

### Runtime Resource

```text
dotnet build tests/scenario/runtime_resource/RuntimeResourceScenarioTests.csproj --no-restore --no-incremental -v:q
Build succeeded. 0 warnings, 0 errors. Elapsed 00:00:00.82. EXIT=0

dotnet tests/scenario/runtime_resource/bin/Debug/net8.0/RuntimeResourceScenarioTests.dll
PASS - AsyncLeaderSyncFollowerUsesOnePhysicalRequest
PASS - DifferentRootsShareOneDependencyPhysicalOpen
PASS - FailedInflightFansOutAndCanRetry
PASS - DiamondSucceedsAndTrueCycleFails
PASS - SequentialLoadsRetainTwoTickets
PASS - ConcurrentLoadsDoNotOverwriteTickets
PASS - FailedTicketsAreReleasedAndNotRetained
PASS - LoadsCachesAndUnloadsThroughFacade
PASS - ParseFailureReleasesFacadeAsset
PASS - SourceBoundaryUsesOnlyFacade
PASS - runtime resource scenarios.
EXIT=0
```

### S2

```text
dotnet build tests/scenario/S2RuntimeBoundaryTests.csproj --no-restore --no-incremental -v:q
Build succeeded. 0 warnings, 0 errors. Elapsed 00:00:00.63. EXIT=0

dotnet tests/scenario/bin/Debug/net8.0/S2RuntimeBoundaryTests.dll
PASS ABDependencyActivePathTests
PASS ABTypedAddressResolutionTests
PASS ConcretePackageOwnershipTests
PASS AssetFacadeBindingTests
PASS StartupLoadErrorTests
PASS - S2 runtime boundary checks.
EXIT=0
```

### Hotfix Decisions

```text
dotnet build tests/scenario/HotfixRuntimeStateMachineTests.csproj --no-restore --no-incremental -v:q
Build succeeded. 0 warnings, 0 errors. Elapsed 00:00:00.62. EXIT=0

dotnet tests/scenario/bin/Debug/net8.0/HotfixRuntimeStateMachineTests.dll
PASS - Windows hotfix state decisions verified.
EXIT=0
```

### Solution

```text
dotnet build XLuaHotfix.sln --no-restore --no-incremental -v:q "-flp:LogFile=Logs/review-20260905-solution.log;Verbosity=normal"
Build succeeded.
4 warnings.
0 errors.
Elapsed 00:00:04.74.
EXIT=0
```

Full local compiler log: `Logs/review-20260905-solution.log`. The four MSB3277 warnings are System.Net.Http and System.IO.Compression version conflicts in Assembly-CSharp-firstpass and Assembly-CSharp. They are not a warning-free build. Build-summary phrases above are English translations of the localized CLI output; scenario output is verbatim.

## Reproduction Evidence

The failures here are intentional RED probes. They do not contradict the earlier scenario PASS output: the existing suites do not cover these contracts. No production file was edited for a reproduction.

### Export Gate Invocation

```text
CWD: E:/unity/project/XLuaHotfix/tests/scenario/s3_resource_boundary
dotnet bin/Debug/net8.0/S3ResourceBoundaryTests.dll
PASS UpperPackageBoundary
PASS BackendLabelPanels
PASS LabelParityRetirement
PASS StaleSerializedDependency
export boundary: AA types=0, AB types=0
export boundary: Compat types=0
PASS ExportBoundary
xlua framework boundary: FYAsset types=0
PASS XLuaFrameworkBoundary
S3 scenarios: 6/6 passed
EXIT=0
```

The command exits successfully, but zero scanned types are the reproduced defect. The same DLL run from repository root earlier reported nonzero counts.

### Lua Event Dispatch

Runtime check: `C:/Coding/c/MinGW/MSYS2/ucrt64/bin/lua.exe -v` printed `Lua 5.4.8`, exit 0. This uses the installed local interpreter, not a new dependency and not the Unity embedded VM.

```text
CWD: E:/unity/project/XLuaHotfix
C:/Coding/c/MinGW/MSYS2/ucrt64/bin/lua.exe -e "local e = dofile('Assets/XLuaFramework/LuaScripts/Framework/EventCentre/EventCentre.lua'); e.init(); local called = 0; e.on(e.Port.LuaToLua, 'audit', function() called = called + 1 end); e.trigger(e.Port.LuaToLua, 'audit'); assert(called == 1, 'listener must run exactly once')"
EventCentre.lua initialized
C:/Coding/c/MinGW/MSYS2/ucrt64/bin/lua.exe: ...amework/LuaScripts/Framework/EventCentre/EventCentre.lua:84: attempt to call a table value (local 'handler')
stack traceback:
    ...amework/LuaScripts/Framework/EventCentre/EventCentre.lua:84: in field 'trigger'
    (command line):1: in main chunk
    [C]: in ?
EXIT=1
```

### Lua Coroutine Identity

The real Lua module is executed; C# ID/notification and logging boundaries are in-memory substitutes. No Unity scheduler/bootstrap is claimed tested.

```text
CWD: E:/unity/project/XLuaHotfix
C:/Coding/c/MinGW/MSYS2/ucrt64/bin/lua.exe -e "local n=0; M={LogUtility={Info=function() end, Error=function(...) end, LogLayer={Core=1}}}; CS={LuaCoroutineScheduler={GenerateLuaCoID=function() n=n+1; return n end, NotifyLuaComplete=function() end}, CoroutineBridge={CleanupWaitRelations=function() end}}; local m=dofile('Assets/XLuaFramework/LuaScripts/Core/CoroutineScheduler/coroutineBridge.lua'); local observed; local a=m.create(function() coroutine.yield(); observed=m.get_current_id() end); local b=m.create(function() coroutine.yield() end); m.resume(a); m.resume(b); m.resume(a); print('expected A='..a..', observed='..tostring(observed)); assert(observed==a, 'resumed coroutine must retain its own identity')"
expected A=1, observed=2
C:/Coding/c/MinGW/MSYS2/ucrt64/bin/lua.exe: (command line):1: resumed coroutine must retain its own identity
stack traceback:
    [C]: in function 'assert'
    (command line):1: in main chunk
    [C]: in ?
EXIT=1
```

### Handle Identity

PowerShell Add-Type compiles the actual HandleRegistry/AssetHandle source bodies in memory. Only imports are consolidated; Unity Object/Debug and RuntimeMessage are minimal substitutes. No asset loading, Unity process, or project data mutation occurs. Compiler temp paths are project-scoped.

```powershell
$ErrorActionPreference = 'Stop'
$env:TEMP = Join-Path (Get-Location) 'Temp'
$env:TMP = $env:TEMP
$registry = Get-Content -Raw -LiteralPath 'Assets/FYAsset/Scripts/AB/Runtime/Backends/Models/HandleRegistry.cs'
$handle = Get-Content -Raw -LiteralPath 'Assets/FYAsset/Scripts/AB/Runtime/Backends/Models/AssetHandle.cs'
$body = ($registry + "`n" + $handle) -replace '(?m)^using [^;]+;\r?\n', ''
$prefix = 'using System; using System.Collections.Generic; using UnityEngine; namespace UnityEngine { public class Object {} public static class Debug { public static void LogWarning(object x) {} } } public class RuntimeMessage {}'
$probe = @'
public static class AuditHandleProbe {
 public static bool Run() {
  HandleRegistry.Reset();
  var slot = HandleRegistry.Alloc("first", "bundle", null, _ => {});
  AssetHandle<UnityEngine.Object> empty = default;
  bool defaultValid = empty.IsValid;
  empty.Release();
  Console.WriteLine("default handle IsValid=" + defaultValid + "; after default.Release active=" + HandleRegistry.ActiveCount);
  HandleRegistry.Reset();
  var oldSlot = HandleRegistry.Alloc("old", "old-bundle", null, _ => {});
  var stale = new AssetHandle<UnityEngine.Object>(oldSlot.handleId, oldSlot.generation, new UnityEngine.Object());
  HandleRegistry.Reset();
  int released = 0;
  var next = HandleRegistry.Alloc("new", "new-bundle", null, _ => released++);
  bool staleValid = stale.IsValid;
  stale.Release();
  Console.WriteLine("after Reset stale.IsValid=" + staleValid + "; stale.Release new callback count=" + released);
  return !defaultValid && !staleValid && released == 0;
 }
}
'@
Add-Type -TypeDefinition ($prefix + "`n" + $body + "`n" + $probe)
if (-not [AuditHandleProbe]::Run()) { exit 1 }
```

```text
default handle IsValid=True; after default.Release active=0
after Reset stale.IsValid=True; stale.Release new callback count=1
EXIT=1
```

### Tracked Test Projects

```text
git ls-files tests/scenario/*.csproj tests/scenario/*/*.csproj tests/scenario/*/NuGet.Config
tests/scenario/HotfixRuntimeStateMachineTests.csproj
tests/scenario/runtime_resource/NuGet.Config
tests/scenario/s3_resource_boundary/NuGet.Config
EXIT=0

git check-ignore -v tests/scenario/S2RuntimeBoundaryTests.csproj tests/scenario/HotfixRuntimeStateMachineTests.csproj tests/scenario/s3_resource_boundary/S3ResourceBoundaryTests.csproj tests/scenario/runtime_resource/RuntimeResourceScenarioTests.csproj
.gitignore:37:*.csproj tests/scenario/S2RuntimeBoundaryTests.csproj
.gitignore:37:*.csproj tests/scenario/s3_resource_boundary/S3ResourceBoundaryTests.csproj
.gitignore:37:*.csproj tests/scenario/runtime_resource/RuntimeResourceScenarioTests.csproj
EXIT=0
```

## Housekeeping And History

- Executed plans `2026-09-04-pipeline-custom-tasks.md` and `2026-09-04-backend-selection-decoupling.md` are now under `requirements/plan/archive/`.
- ACCEPT-01 retains Unity AA/AB build-isomorphism acceptance; ACCEPT-02 retains Prefab/backend AB startup acceptance in `requirements/plan.md`.
- Archive headers explicitly separate historical verification from current acceptance. The custom-task original slot spec is marked superseded by T1, without erasing the historical decision text.
- FYNet Git scope/approval/backup/local-editor-state rules were imported into AGENTS.md: four exact source lines match; whitespace check exits 0.
- History cleanup is a draft: `requirements/plan/drafts/2026-09-05-history-regrouping.md`. The 66-commit / 737-path inventory has no missing, extra, duplicate or unassigned endpoint paths. H17 contains explicit content-preservation decisions still awaiting approval.
- No commit, reset, stash, history rewrite, push, branch deletion or worktree deletion has been performed in this review round.

## Open Work

- All numbered findings remain open; no production fix or retrospective native-acceptance claim is authorized by this report.
- Keep candidate findings separate until their remaining checks are complete.
- Obtain exact history-group approval and H17 disposition before rewriting.
- ACCEPT-01/02 retain native build/runtime acceptance; ACCEPT-03 retains the newly identified missing executable custom-task gate.
