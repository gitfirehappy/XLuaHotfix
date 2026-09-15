# FYAsset Resource Pipeline Realignment Execution Review

> **Date**: 2026-09-11
> **Reviewer**: Codex parent review with independent plan-conformance and architecture reviewers; findings below were rechecked by the parent
> **Scope**: Current uncommitted workspace after `c6e27c18`, covering the approved `fyasset-resource-pipeline-realignment` T0–T9 implementation, recorded implementation-time decisions, tests, docs, and commit readiness
> **Method**: Plan/progress mapping, source and call-chain review, diff/status inspection, targeted static checks, fresh solution build and pure .NET scenario execution; existing Unity E2E logs were inspected, but Unity/Player/network tests were not rerun
> **Status**: Open / remediation planned. The source T0–T9 plan was archived as **superseded after acceptance failure**, not as completed; findings remain open under `../plan/plan-fyasset-windows-ab-remediation-20260911.md`.

## Evidence Boundary

- `requirements/plan/archive/plan-fyasset-resource-pipeline-realignment-20260909.md` and `requirements/progress.txt` define the approved scope, recorded deviations, and outstanding acceptance work.
- Source and tests define current behavior. A source-text gate proves only its predicate; a pure .NET/stub scenario does not prove Unity serialization, Player packaging, Scene lifetime, Android StreamingAssets, or real publication behavior.
- The latest retained `ab e2e standalone` run is a real Player failure, but its copied Player package was cleaned during restoration. The log proves the missing-file behavior; it does not by itself prove which build branch produced the stale long name.
- No business code was changed during this review. No Unity batchmode, Player build, process launch, local publication, or network operation was executed.
- The workspace contains 405 status entries, including 164 untracked paths. Untracked implementation files are not intrinsically wrong before staging; ignored scenario project files are a concrete delivery problem because ordinary staging cannot include them.

## Executive Conclusion

The intended architecture is recognizable in the current tree: AA and AB have fixed concrete backbones, Shared owns neutral runner/diff/publication/state-machine mechanisms, Repository/baseline production paths were removed, AB Manifest and Handle contracts were simplified, and package activation reads a single active root. Fresh `pipeline_realignment`, `pipeline_compose`, and `publish_diff` scenarios support those structural claims.

Acceptance is nevertheless blocked. The current tree has a publication path-escape defect, the Standalone Player still fails after the physical-name change, one previously green cache gate is now red, and several ownership/transaction paths contradict the approved failure matrix. The source plan is archived only as superseded acceptance-failure evidence; the findings remain active under the remediation plan.

## Findings

Severity convention: **P0** can escape an owned boundary or destructively affect unrelated state; **P1** blocks an approved runtime/build/publication contract or acceptance; **P2** is a concrete correctness, reproducibility, or documentation defect; **P3** is non-blocking maintenance debt or scope noise.

### F01 — P0 — Publication path segments can escape the server package root [STATIC]

- Evidence: `Assets/FYAsset/Scripts/Shared/Build/Publish/PackageBuildIdentity.cs:104`, `Assets/FYAsset/Scripts/Shared/Build/Publish/PublishRequest.cs:41`, `Assets/FYAsset/Scripts/Shared/Build/Publish/PublishRequest.cs:100`, `Assets/FYAsset/Scripts/Shared/Build/Publish/PackagePublishTransaction.cs:46`, `Assets/FYAsset/Scripts/Shared/Build/Publish/PackagePublishTransaction.cs:51`, `Assets/FYAsset/Scripts/Shared/Build/Publish/PackagePublishTransaction.cs:53`, `Assets/FYAsset/Scripts/Shared/Build/Publish/PackagePublishTransaction.cs:215`.
- `IsSafePackageName()` rejects invalid filename characters and separators but accepts `.` and `..`; it also does not require the established `Build_*` package identity. `PublishRequest.Validate()` does not validate `PackagesFolderName` at all.
- `PackagePublishTransaction` concatenates those values before any canonical containment check. For example, `PackagesFolderName=".."` places `_targetPackageDir` outside the configured server backend root; subsequent move/delete/replace logic can touch an unrelated sibling directory.
- Direction: define one strict path-segment validator for package names and package-folder names; reject dot segments, rooted paths, separators, device names and unexpected package identities. Canonicalize with `Path.GetFullPath` and require every work/target/index path to remain inside its declared owner root before any mutation. Add temporary-directory traversal tests for both explicit identity and summary-derived identity.

### F02 — P1 — Short physical names are not end-to-end accepted; Standalone still reads a long logical name [REPRO]

- Evidence: `requirements/progress.txt:1811`, `requirements/progress.txt:1815`, `Assets/FYAsset/Scripts/AB/Build/Collector/Editor/BundleNameBuilder.cs:20`, `Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/Tasks/BuildABContentTask.cs:647`, `HotfixOutput/TestRuns/AB/e2e/standalone/20260910_165223_27c1fa2d/result.json:2`, `HotfixOutput/TestRuns/AB/e2e/standalone/20260910_165223_27c1fa2d/targets/standalone/player-runtime-standalone.log:65`.
- The approved emergency change maps every logical `ContentName` to a deterministic 16-character physical name, yet the retained Player log still requests `plugins_rawfile_defaultasset_asset_assets-plugins-android-libs-arm64-v8a-libxlua-so~6c76d1cf` and exits with code 1.
- `BundleBuildInputFingerprint.FormatVersion` is already `2` at `Assets/FYAsset/Scripts/AB/Build/Cache/Editor/BundleBuildInputFingerprint.cs:56`; therefore this review does **not** claim that `ABBuildCacheContext.BuildRecipeVersion == 1` alone proves stale-cache reuse. The exact producer of the long name remains open.
- Direction: retain the Standalone package or emit a manifest/file inventory before restoration, then compare logical `ContentName`, `BundleBuildInfo.OutputFileName`, Manifest `FileName`, attempt output, promoted output, Player StreamingAssets, and runtime requested path. Add an executable mapping test covering SerializedObject, Scene and RawFile plus a cache created under the previous naming contract.

### F03 — P1 — The cache scenario regressed after the emergency naming change [REPRO]

- Evidence: `tests/scenario/build_cache/test_content_name_rules.cs:5`, `tests/scenario/build_cache/test_content_name_rules.cs:97`, `Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/Tasks/BuildABContentTask.cs:504`, `Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/Tasks/BuildABContentTask.cs:515`.
- Fresh execution produced `67/68` assertions and `6/7` groups. `BuildTaskUsesContentNameAsBundleName` still requires `assetBundleName = plan.ContentName`, while production now uses `plan.PhysicalName`.
- This is a stale contract, not evidence that short names are wrong. It invalidates the progress claim that all non-Unity gates remain green and leaves the new naming rule without equivalent executable coverage.
- Direction: replace the obsolete predicate with assertions for deterministic short names, logical-to-physical separation, Scene suffix behavior, Manifest mapping, collision handling, and old-cache invalidation. Run the gate red/green around the corrected contract.

### F04 — P1 — A damaged local package does not fall back to the verified built-in package [STATIC]

- Evidence: approved matrix at `requirements/plan/archive/plan-fyasset-resource-pipeline-realignment-20260909.md:541`; implementation at `Assets/FYAsset/Scripts/Shared/Hotfix/HotfixFlowBase.cs:423`, `Assets/FYAsset/Scripts/Shared/Hotfix/HotfixFlowBase.cs:452`, `Assets/FYAsset/Scripts/Shared/Hotfix/HotfixFlowBase.cs:510`; decision helper at `Assets/FYAsset/Scripts/Shared/Hotfix/HotfixStateDecider.cs:179`.
- A trusted local pointer selects `HotfixContentState.Local` before package inspection. If inspection says the local package is incomplete, the code only warns and leaves `CurrentContent`, `CurrentPackageRoot`, and `CurrentPackageInspection` on that damaged package.
- If the remote index is then unavailable, `DecideRemoteFailure()` receives `currentPackageComplete=false` and blocks startup. This contradicts the approved row “本地指针/目录损坏 → 使用 BuiltInPackage 继续远端检查” and the required runtime acceptance “本地包损坏 + 远端失败 → 整包退化 StreamingAssets 并 Warning”.
- Direction: after local inspection fails, atomically restore the current candidate to the already verified built-in identity/root/inspection before remote decisions. Add the exact local-corrupt + remote-unavailable state-machine scenario.

### F05 — P1 — Same-package repair writes into the active package instead of isolated staging [STATIC]

- Evidence: approved flow at `requirements/plan/archive/plan-fyasset-resource-pipeline-realignment-20260909.md:525`, `Assets/FYAsset/Scripts/Shared/Hotfix/HotfixFlowBase.cs:636`, `Assets/FYAsset/Scripts/Shared/Hotfix/HotfixFlowBase.cs:1213`, `Assets/FYAsset/Scripts/Shared/Hotfix/HotfixFlowBase.cs:1472`.
- In the “same version, same package, local content damaged” path, `TargetGUIDRoot` equals `CurrentPackageRoot`. `PrepareTargetCoreAsync()` deliberately skips target deletion, then downloads/replaces bundles and metadata directly under the current root.
- If preparation fails, `DeleteTargetPackage()` also skips deletion when the target is the current complete package. The directory can contain a mixture of old and new files, and there is no previous-directory rollback.
- Direction: repair the same package in a sibling staging directory with a distinct temporary identity; verify the complete package there, close the manager at Apply, atomically swap directories, initialize, and only then commit the pointer. Preserve the old directory until all activation steps settle.

### F06 — P1 — `SceneHandle` ownership is inconsistent with Retain and failure retry [STATIC]

- Evidence: `Assets/FYAsset/Scripts/AB/Runtime/SceneHandle.cs:129`, `Assets/FYAsset/Scripts/AB/Runtime/SceneHandle.cs:151`, `Assets/FYAsset/Scripts/AB/Runtime/ABSceneLoader.cs:177`, `Assets/FYAsset/Scripts/AB/Runtime/ABSceneLoader.cs:205`, `Assets/FYAsset/Scripts/AB/Runtime/ABSceneLoader.cs:340`.
- `UnloadAsync()` releases its token before `SceneManager.UnloadSceneAsync` succeeds. A null operation, exception, or failed wait leaves the scene loaded but the caller has lost the token and cannot retry; Apply can also see fewer active Scene handles than actually loaded scenes.
- `Retain()` creates independent tokens, but `_records` tracks only the original token and one scene/content reference. Any retained copy can unload the shared scene and release its content while the other token remains valid.
- Direction: make the scene record own all tokens or an explicit owner count. A normal `Release` drops one ownership token without unloading; only the last owner may initiate physical unload, and its token/content record settles only after successful scene unload. Failed unload must retain a retryable owner.

### F07 — P1 — Mixed async/sync asset loads can leak one Bundle acquisition [STATIC]

- Evidence: `Assets/FYAsset/Scripts/AB/Runtime/ABPackageBackend.cs:91`, `Assets/FYAsset/Scripts/AB/Runtime/ABPackageBackend.cs:158`, `Assets/FYAsset/Scripts/AB/Runtime/ABBundleLoader.cs:371`, `Assets/FYAsset/Scripts/AB/Runtime/HandleRegistry.cs:160`.
- Backend-level async loading is single-flight by `EntryId`, but the synchronous entry ignores `_inflightLoads`. During an async load, a sync load can independently acquire/extract the same entry.
- `ABBundleLoader` correctly joins the physical inflight request and records two Bundle acquisitions. Both successful calls create Handle tokens, but `HandleRegistry` invokes the backend release callback only once when the final `EntryId` token reaches zero; `ABPackageBackend.ReleaseEntry` therefore releases only one of the two Bundle acquisitions.
- Direction: make the backend cache acquisition single-flight across async and sync callers. The sync API should join safely or return a defined unsupported/busy result; it must not perform a second entry acquisition. Add an EntryId-level async-leader/sync-follower test that asserts both token release and final Bundle refcount zero.

### F08 — P1 — AB built-in/Standalone Bundle loading cannot work from Android StreamingAssets [STATIC]

- Evidence: `Assets/FYAsset/Scripts/AB/Runtime/ABBundleLoader.cs:192`, `Assets/FYAsset/Scripts/AB/Runtime/ABBundleLoader.cs:371`, `Assets/FYAsset/Scripts/AB/Runtime/ABBundleLoader.cs:430`, `Assets/Tools/Scripts/FileHelper.cs:132`.
- `ResolveBundlePath()` requires `FileHelper.Exists(path)`. On Android Player builds, `FileHelper.Exists` intentionally returns false for StreamingAssets `jar:` paths, so built-in bundles become `BundleNotFound` before `AssetBundle.LoadFromFile*` is attempted.
- RawFile has an asynchronous UnityWebRequest path, but Bundle loading has no equivalent extraction or UWR AssetBundle channel. This contradicts the cross-platform built-in package contract.
- Direction: either load Android built-in bundles with an owned UnityWebRequestAssetBundle path, or extract the complete built-in package to a persistent package root before initialization. Define the synchronous API behavior explicitly when extraction has not occurred, and add an Android Player acceptance case.

### F09 — P1 — Cloudflare mirror rollback deletes the previous same-name package [STATIC]

- Evidence: `Assets/FYAsset/Scripts/Compat/Editor/Publish/CloudflarePagesPushTarget.cs:66`, `Assets/FYAsset/Scripts/Compat/Editor/Publish/CloudflarePagesPushTarget.cs:84`, `Assets/FYAsset/Scripts/Compat/Editor/Publish/CloudflarePagesPushTarget.cs:109`.
- `MirrorPackage()` deletes an existing same-name local service package before copying the staged package. Wrangler failure restores PackageIndex and `_headers`, then deletes the new target directory, but it never restores the deleted old package.
- The restored old index can therefore point to a missing directory. This also bypasses the “published package identity is immutable” rule enforced by the directory transaction path.
- Direction: apply the same immutable/idempotent package policy before invoking Wrangler. If replacement is ever allowed, move the old directory to an isolated backup and restore it on every failure path.

### F10 — P1 — New scenario entry projects are ignored and will not survive ordinary staging [REPRO]

- Evidence: `.gitignore:37` and these current files: `tests/scenario/pipeline_realignment/PipelineRealignmentTests.csproj:1`, `tests/scenario/build_cache/BuildCacheTests.csproj:1`, `tests/scenario/pipeline_compose/PipelineComposeTests.csproj:1`, `tests/scenario/publish_diff/PublishDiffTests.csproj:1`.
- `git check-ignore -v` reports all four against `*.csproj`; `git ls-files --error-unmatch` reports all four untracked. The plan requires these executable gates, and README documents direct `dotnet run --project ...` commands.
- A normal `git add -A` will omit the project files, so a clean checkout cannot reproduce the recorded 7 gates, 68 cache checks, 29 Composer checks, or publication scenarios even if all `.cs` files are committed.
- Direction: add narrow ignore exceptions for hand-maintained scenario projects, or commit a deterministic generator and make verification invoke it. Do not weaken the root rule for Unity-generated project files globally.

### F11 — P2 — Case-insensitive public Address is stored in a case-sensitive facade lease table [STATIC]

- Evidence: `Assets/FYAsset/Scripts/Compat/AssetPackageManager.cs:25`, `Assets/FYAsset/Scripts/Compat/AssetPackageManager.cs:104`, `Assets/FYAsset/Scripts/Compat/AssetPackageManager.cs:118`; public contract at `requirements/plan/archive/plan-fyasset-resource-pipeline-realignment-20260909.md:451`.
- AB Address resolution is case-insensitive, but `_abLeases` uses `StringComparer.Ordinal`. Loading `UI/Main` and unloading `ui/main` succeeds at the public identity level but cannot find the retained lease.
- The token remains active and can block `Shutdown`/hotfix Apply.
- Direction: use `OrdinalIgnoreCase` for AB lease identity and add a mixed-case load/unload test. Keep AA behavior separate if its backend contract differs.

### F12 — P2 — Cross-platform path equality ignores case on every platform [STATIC]

- Evidence: `Assets/FYAsset/Scripts/Shared/Helpers/FYAssetPathUtility.cs:8`, `Assets/FYAsset/Scripts/Shared/Helpers/FYAssetPathUtility.cs:160`.
- The utility already defines a Windows-only case-insensitive `FilePathComparison`, but `AreSamePath()` hardcodes `OrdinalIgnoreCase`.
- On a case-sensitive filesystem, two distinct package roots differing only by case can be treated as the same root, affecting deletion guards, active-package protection, repair, and promotion decisions.
- Direction: use `FilePathComparison` consistently and cover Windows/case-sensitive semantics with pure path tests.

### F13 — P2 — The emergency physical-name decision left a live but ineffective configuration surface [STATIC]

- Evidence: `Assets/FYAsset/Scripts/Shared/Build/Pipeline/Editor/BuildPipelineConfig.cs:23`, `Assets/FYAsset/Scripts/Shared/Build/Pipeline/Editor/BuildPipelineConfig.cs:44`, `Assets/FYAsset/Scripts/Shared/Build/Editor/UI/PipelinePanel.cs:184`, `Assets/FYAsset/Scripts/AB/Build/Collector/Editor/BundleNameBuilder.cs:20`.
- `BundleFileNameStyle` remains serialized and editable, but AB production naming now always uses `BuildPhysicalName`; no build consumer reads `FileNameStyle`.
- Users can change three apparent naming modes with no effect. This is both redundant code and a configuration correctness defect.
- Direction: remove the enum/field/YAML/UI and document the fixed physical-name contract, unless AA has a real independent consumer that requires a backend-specific setting. Do not reintroduce long physical names merely to preserve a dead option.

### F14 — P2 — Active docs and authoritative plan status contradict the current implementation [STATIC]

- Evidence: `requirements/plan.md:1`, `requirements/plan.md:7`, `requirements/plan/INDEX.md:8`, `docs/FYAsset/collector-采集与配置.md:97`, `docs/FYAsset/collector-采集与配置.md:120`, `docs/FYAsset/build-pipeline-构建管线.md:143`, `docs/FYAsset/build-pipeline-构建管线.md:150`, `Assets/FYAsset/CollectorData/CollectorSetting.asset:15`.
- The authoritative `requirements/plan.md` still says T0–T9 execution was deferred/awaiting execution, while the active plan body and queue say implemented/awaiting acceptance.
- Collector docs call `ShortName` the default even though the project config is `LongAssetPathWithoutExtension` and progress records the developer-approved change required to avoid 43 uniqueness failures.
- Build docs still say `FileNameStyle` determines final files and that build tasks append hash/extensions; current AB code uses a fixed 16-character physical hash without that contract.
- Direction: after fixes settle, update status to “implemented, acceptance blocked”, distinguish enum defaults from this project’s required Address policy, and document logical `ContentName` versus Manifest physical `FileName` precisely.

### F15 — P3 — Dead mode/config state and stale comments should be removed or assigned a real owner [STATIC]

- Evidence: `Assets/FYAsset/Scripts/Shared/Settings/FYAssetSettings.cs:23`, `Assets/FYAsset/Scripts/Shared/Build/Editor/UI/SettingsPanel.cs:67`, `Assets/FYAsset/Scripts/Shared/Build/Editor/UI/SettingsPanel.cs:83`, `Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/BundleBuildInfo.cs:10`, `Assets/FYAsset/Scripts/Shared/Build/Publish/PublishRequest.cs:117`, `Assets/FYAsset/Scripts/Shared/Build/Publish/PushModels.cs:29`.
- `StandaloneBuild` remains serialized and editable, while production `RuntimeMode` is derived from `BuildType`; current references outside settings/UI are test-time bake/restore. The UI therefore appears to control a runtime mode that production startup no longer reads.
- `BundleBuildInfo.OutputFileName` still says it equals the logical name, which is false after short hashing.
- `_serverBackendRoot`/`UseServerRoot`/`ResolveServerRootOrNull` and `PushPayload.ServerPackagesRoot` have no effective transaction consumer in the reviewed path.
- Direction: remove dead state after confirming no external extension uses it, or make ownership explicit. At minimum, correct the false comments immediately with the naming fix.

### F16 — P3 — Scope noise and formatting residue remain in the workspace [STATIC]

- Evidence: `status_snapshot.txt:1`, `Assets/FYAsset/Scripts/Shared/Build/Pipeline/Editor/IBuildTask.cs:1`, `Assets/Test/ConvertTestAssets/Xml/TestCharacters.xml.meta:1`, `Assets/Test/ConvertTestAssets/Xml/TestGameSettings.xml.meta:1`.
- `status_snapshot.txt` is an untracked root-level dump of an earlier `git status`; it is not a source, fixture, report, or required artifact.
- `Assets/FYAsset/Scripts/Shared/Build/Pipeline/Editor/IBuildTask.cs` currently mixes CRLF and LF. The two XML `.meta` files are modified only by line-ending/EOF normalization and are not covered by the progress entry that fixed newly created `.meta` files.
- Direction: exclude the snapshot from delivery and normalize only files intentionally owned by this plan. Either document why the two existing `.meta` files require a change or leave them outside the eventual commit.

## Recorded Temporary Decisions

The following changes are reasonable responses to observed implementation or Unity failures and are not classified as unauthorized scope expansion. Their remaining verification/doc work is still required.

- `requirements/progress.txt:1771`: removal of dead Rule constants, migration of tests bound to the retired model, and local generated project synchronization.
- `requirements/progress.txt:1773`: single-reference implicit dependencies stay inside the referring Unity content instead of becoming independent public entries.
- `requirements/progress.txt:1780`: dependency-closure cache pruning and post-promotion cache commit protect output equivalence and transactional ownership.
- `requirements/progress.txt:1791`: the cross-batch `ActivePackageRoot` connection was necessary after removing per-file StreamingAssets fallback.
- `requirements/progress.txt:1793`: correcting the self-contradictory default-handle assertion strengthened rather than weakened the test.
- `requirements/progress.txt:1797`: mutable `CustomTaskEntry` is justified by Unity serialization; the Runner seam is justified by pure .NET testing.
- `requirements/progress.txt:1802`: AA attempt delivery and summary copying are required for cumulative-directory transaction consistency.
- `requirements/progress.txt:1808`–`requirements/progress.txt:1810`: Player-visible `PushTargetConfig`, restored default scanner exclusions, directory-asset skipping, reset-regex correction, and damaged fixture restoration were direct fixes for observed failures.
- `requirements/progress.txt:1811`: deterministic short physical names are justified by the reproduced Windows MAX_PATH failure. They require F02/F03/F13/F14 closure before acceptance.
- `requirements/progress.txt:1813`: changing this project’s AddressStyle to `LongAssetPathWithoutExtension` is developer-approved and justified by reproduced case-insensitive uniqueness conflicts. The stale docs in F14 must be corrected.

## Architecture Assessment

### Aligned

- AA and AB define concrete fixed backbones; Shared executes composed tasks without selecting a backend.
- Repository, baseline and snapshot production paths are removed; `FileDigest`/`FileDiff` provide neutral comparison facts.
- AB Manifest separates public asset identity from physical content entries, and generated serializers target Schema 6.
- PackageIndex publication belongs to the publication transaction rather than the build runner.
- Asset Handle tokens reserve zero, use independent generations, reject active-handle Shutdown, and no longer let duplicate release consume another token.
- Runtime loaders are designed around one `ActivePackageRoot`, avoiding intentional per-file cross-package fallback.

### Not Yet Correct

- Runtime package fallback and same-package repair do not yet implement the approved transaction/failure matrix (F04/F05).
- Scene ownership and mixed sync/async asset acquisition do not maintain one verifiable acquisition/release invariant (F06/F07).
- Publication containment and Cloudflare compensation are weaker than the directory transaction’s stated safety boundary (F01/F09).
- Cross-platform built-in loading is incomplete on Android (F08).
- Physical naming currently has four competing truths: production code, stale cache gate, editable `FileNameStyle`, and outdated docs (F02/F03/F13/F14).

## Fresh Verification

Executed in this review:

- `dotnet build XLuaHotfix.sln --no-restore --no-incremental -v:q` — exit `0`; `0` errors; existing `MSB3277` assembly-version warnings remain.
- `dotnet run --project tests/scenario/pipeline_realignment/PipelineRealignmentTests.csproj --no-restore` — `7/7` gates passed.
- `dotnet run --project tests/scenario/pipeline_compose/PipelineComposeTests.csproj --no-restore` — `29/29` assertions, `4/4` groups passed.
- `dotnet run --project tests/scenario/publish_diff/PublishDiffTests.csproj --no-restore` — `4/4` scenarios passed.
- `dotnet run --project tests/scenario/build_cache/BuildCacheTests.csproj --no-restore` — failed: `67/68` assertions, `6/7` groups; obsolete physical-name assertion described in F03.
- `git diff --check && git diff --cached --check` — exit `0`.
- Asset `.meta` scan — `919` GUIDs; `0` orphan `.meta`, `0` missing `.meta`, `0` duplicate GUID.

Retained prior evidence:

- AB Full and two AB Hotfix local builds are recorded as exit `0` in `requirements/progress.txt:1814`.
- Latest retained AB Standalone Player result is exit `1` with the long-name missing file in F02.
- AA Full → Hotfix 1 → Hotfix 2 → Standalone, AB Online E2E, real Scene lifecycle, Android Player, cache-hit/full-build artifact equivalence, and authorized real remote publication remain unverified.

## Acceptance Gate

Do not sign off, archive, or commit as a completed plan until at least:

1. Fix F01 and add traversal/containment tests before any publication acceptance.
2. Close F02 with retained package/manifest evidence and restore all non-Unity gates to green, including F03.
3. Fix and test F04–F09 at their real ownership/transaction boundaries.
4. Make scenario projects reproducible from a clean checkout (F10).
5. Remove or synchronize the dead/stale configuration and documentation contracts in F13–F16.
6. Execute the remaining plan matrix: AA Full → Hotfix 1 → Hotfix 2 → Standalone; AB Standalone and Online E2E; cache reuse/invalidation comparison; AB RawFile/Scene/runtime Apply behavior; Android built-in package path where supported.
