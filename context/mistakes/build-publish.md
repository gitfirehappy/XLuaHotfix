# Build And Publish Pitfalls

Verified build, artifact, and publication mistakes (older entries also record the removed build-repository mechanism). Each
entry keeps its original IP number and standard fields.

> Historical scope: entries below are records of what happened at the time they were written. Mechanisms they name as
> the then-current design - build repository, `HEAD`/commit/repair, `PushHistory`, `baseline`/`LatestFull`,
> `ArtifactDelta`/`ArtifactDiffer`, `TaskWritePackageIndex` inside the build graph - were removed by the FYAsset
> pipeline realignment. Do not read them as current architecture; see `docs/FYAsset/publish-本地交付与发布.md` and
> `docs/FYAsset/diff-无状态差异.md` for the current model.

## IP-37: Pointer File Reused Manifest Naming

**Symptom:** A pointer file and a content manifest used the same naming convention.
**Root cause:** Build-time and runtime files were both called "manifest".
**Fix:** Give pointer files distinct constants and names.
**Prevention:** Pointer files and content manifests must not share the same name family.

## IP-38: Bootstrap Export Recreated Placeholder Data

**Symptom:** Bootstrap export wrote empty placeholder data even though real output already existed.
**Root cause:** Downstream step regenerated substitute state.
**Fix:** Consume upstream artifacts directly.
**Prevention:** Bootstrap/export must copy canonical output, not regenerate it.

## IP-39: Migration Left Real Logic in Legacy Helper

**Symptom:** A task wrapper existed, but the legacy helper still owned the implementation.
**Root cause:** Scheduling changed, ownership did not.
**Fix:** Move the execution logic or keep the helper explicitly shared.
**Prevention:** Migrated behavior must move implementation ownership too.

## IP-40: Auto-Repair Created a Second Truth

**Symptom:** Load-time repair mutated config assets that were supposed to be the source of truth.
**Root cause:** Template creation and validation were mixed with repair.
**Fix:** Validate missing required tasks instead of mutating existing assets.
**Prevention:** Default definitions may create new config, but must not silently repair existing config.

## IP-41: Traversal Cache Reused As Reference Accounting

**Symptom:** A visited set caused under-counting of shared dependencies.
**Root cause:** Traversal caching also controlled accounting.
**Fix:** Separate traversal caching from per-root accounting.
**Prevention:** Traversal caches must not become semantic ownership gates.

## IP-42: System-Generated Enum Exposed as User Config

**Symptom:** A generated enum value was shown in manual UI.
**Root cause:** Public and internal states shared one enum without a UI boundary.
**Fix:** Whitelist only user-selectable values in UI and validation.
**Prevention:** Mixed enums need explicit public-value filtering.

## IP-43: Malformed HEAD Collapsed Into Empty State

**Symptom:** A broken repository with invalid `HEAD.json` looked the same as an empty repository in status UI.
**Root cause:** HEAD load failures were reduced to `null` without a separate error state.
**Fix:** Track HEAD error state explicitly on repository status and show it in the UI.
**Prevention:** Empty repository state and corrupted repository state must not share the same status path.

## IP-44: Preview Output Routed Through Environment Variable

**Symptom:** AB Diff Preview depended on `BUILD_REPOSITORY_PREVIEW_OUTPUT` to steer the build output root.
**Root cause:** Preview orchestration and pipeline initialization shared an implicit side channel.
**Fix:** Pass the preview output root through `BuildContext` and read it in `TaskPrepareContext`.
**Prevention:** Preview-only routing should use explicit context keys, not process environment variables.

## IP-51: Staged BuildContext Writes Treated As Exclusive Locks

**Symptom:** AB Pipeline validation failed before any task ran with `CONFLICTING_WRITE_KEYS` for `CollectedAssets`, while AA validation still succeeded.
**Root cause:** `DAGScheduler` kept the original Write-Write fatal validation from the early pipeline design, but later AB tasks changed `CollectedAssets` into a staged data-flow key: collection creates it, builtin collection appends to it, and dependency analysis writes back the augmented list. The scheduler, BuildGraph display, and AB config ordering were not updated together after that semantic shift.
**Fix:** Treat `WriteKeys` as BuildContext write/update declarations instead of exclusive write locks, validate preview runs against the effective task whitelist, and explicitly order AB collection as `TaskCollectAssets -> TaskCollectBuiltins -> TaskAnalyzeDependencies`.
**Prevention:** When a BuildContext key changes from single-producer to staged updates, update the scheduler validation model, default backbone dependencies, existing config assets, graph visualization, context docs, and validation smoke tests in the same plan.

## IP-52: Push Review Fix Confused Package Publication With PackageIndex Ownership

**Symptom:** A repository review fix was initially framed as deriving or validating package-internal `PackageIndex.json` during Push, even though the approved Push model is simple publication of already built package output.
**Root cause:** The review finding about Push depending on the current editor output path was interpreted as a request to make Push smarter, instead of preserving the existing boundary: build tasks own package contents and `PackageIndex.json`; repository Push only publishes package output.
**Fix:** Keep Push as whole-package replacement on the target. Do not regenerate or reinterpret package-internal `PackageIndex.json` in Push. The later repository simplification removed persistent `PushHistory`; successful publication is represented only by the current operation result and target state.
**Prevention:** Before fixing review findings, re-check the approved ownership boundary. If a build task owns an artifact's content, downstream repository/delivery code may copy or publish that artifact but must not become a second authority for its meaning.

## IP-54: Active Settings Ownership Drifted Into Too Many Assets

**Symptom:** Runtime, build, repository, and collector configuration were split across global settings plus Shared/AA/AB/Repository settings, causing panels and docs to disagree about where fields lived. Hotfix URL/retry fields, build paths, Push targets, and AB collection paths could be described or edited through different owners.
**Root cause:** Transitional settings classes became treated as active authorities after migration work instead of remaining compatibility sources. The code and documentation did not keep a small current settings inventory.
**Fix:** Reduce active configuration to three Resources assets: `FYAssetSettings` for global build/project/version/push fields, `FYAssetAASettings` for AA hotfix/build fields, and `FYAssetABSettings` for AB hotfix/build/collection fields. Keep old settings classes only as migration inputs when creating missing active assets.
**Prevention:** After settings migrations, document the active settings inventory and mark old assets as compatibility-only in context and human docs. Provider APIs should expose current owners directly and old-owner reads should be constrained to migration code paths.

## IP-60: Repository Preview Mixed Changes With Delivery Semantics

**Symptom:** AB Repository Changes could be blocked by a missing same-Major Full baseline even though a git-style current-vs-HEAD diff should still be available.
**Root cause:** The preview path used one Hotfix branch for two different questions: current changes against repository HEAD and hotfix delivery against the Full baseline.
**Fix:** Split Repository `Refresh Changes` from AB `Preview Delivery`. Changes uses current-vs-HEAD and treats a missing HEAD as an empty baseline; Delivery uses current-vs-Full-baseline and remains unavailable/failing when the Full baseline is missing.
**Prevention:** Keep status/diff preview questions separate from package delivery questions. Missing HEAD can mean empty baseline for preview; malformed HEAD is corruption; missing Full baseline is an AB delivery constraint, not a generic Changes constraint.

## IP-61: Version Advanced Before Build Success

**Symptom:** Failed builds could consume product versions without producing matching package output or repository commits.
**Root cause:** `VersionRecord` was incremented and saved before the backend build and repository commit had both succeeded.
**Fix:** Stage the next `VersionNumber` in memory, build and commit with that staged request version, then apply and save `VersionRecord` only after the full chain succeeds.
**Prevention:** Product version advancement must be transactional with the artifact/repository state it names. Never persist the next version before the operation that creates that version's package and repository commit has succeeded.

## IP-62: Package Pointer Published Before Repository Commit

**Symptom:** A build that later failed during repository commit still left `PackageIndex.json`, `StreamingAssets/BuildIndex.json`, and copied baseline assets pointing at the failed package.
**Root cause:** `TaskWritePackageIndex` and `TaskExportLocalBuildData` performed publication inside the DAG before the repository commit succeeded, so later repository failure could not prevent already-visible package pointers.
**Fix:** Official backend DAG runs defer package publication through `BuildContextKeys.DeferPackagePublication`; `BuildProjectManager` commits the repository first, then publishes `StreamingAssets` / `PackageIndex`, and deletes the current package directory or writes `FAILED_BUILD.json` when the build fails.
**Prevention:** Generated package content may be staged before commit, but any visible pointer or startup baseline must be published only after the repository state that names it is committed. If post-commit publication fails, roll repository HEAD back to the parent or remove it for the first commit.

## IP-63: Build Metadata Leaked Into Repository Identity

**Symptom:** Repository HEAD, object files, package names, status UI, and the historical push log could use version strings such as `2.0.0+1`, while the product build counter was also stored as a numeric field.
**Root cause:** `Build` metadata was appended to release identity strings, so one concept acted as both product version and build counter. Old `+Build` strings then became invalid repository object names after the version contract was corrected.
**Fix:** Use `GetReleaseVersionString()` (`Major.Minor.Patch[-Channel]`) for package names, repository object names, HEAD, parent versions, logs, and status UI. Store `Build` only as a separate numeric field, reject `+Build` in parsing, and rebuild/delete stale `+Build` repository data. Persistent push history was removed later and is no longer an active identity consumer.
**Prevention:** Artifact identity strings must not include volatile counters unless the format is explicitly part of the release contract. If a persisted identity format is wrong, rebuild or quarantine it instead of silently maintaining compatibility.

## IP-64: DAG Scheduler Over-Engineered Linear Execution

**Symptom:** Build pipeline used `DAGScheduler` with Kahn topological sort, circular dependency detection, merged code/SO dependencies, and complex `BuildGraphView` with layout engine, graph edges, and execution status visualization. Tasks executed in BFS-derived batches even though each batch had exactly one task and execution was deterministic serial.
**Root cause:** Introduced full DAG abstraction when the actual requirement was simpler: tasks run in configured list order with optional dependency validation guardrails. The scheduler paid upfront cost for topological ordering, cycle detection, and batch parallelism that were never used. BuildGraph UI required GraphView, layout computation, and three edge types (Code/SO/Data) when a simple ordered task list sufficed.
**Fix:** Replaced `DAGScheduler` with `BuildPipelineRunner` that executes tasks in `BuildPipelineConfig.Tasks` linear order; `IBuildTask.DependsOn` remains as validation-only guardrails (dependency must exist and appear before current task); removed all DAG validation (cycle detection, scheduler deadlock), graph visualization code (BuildGraphView, BuildGraphLayoutEngine, BuildTaskNode, EdgeStyle), and SO-level `TaskEntry.DependsOn` field. Review found four minor efficiency regressions (TryCreateTask waste, new string[0] allocations, LINQ Count, stopAfterTaskName Skipped reporting) that were fixed immediately.
**Prevention:** Start with the simplest model that satisfies current requirements. Do not introduce graph abstractions, topological ordering, or complex visualization until actual parallelism, dynamic ordering, or graph-editing requirements are proven. When "future flexibility" is the only justification for complexity, defer it. Linear list + validation guardrails covers 90% of pipeline use cases; reserve DAG for proven concurrent or dynamic-order needs.

## IP-65: Player Build Failed On Editor-Only Declared Config Type

**Symptom:** `dotnet build` reported 0 errors and the Unity Editor compiled, but the player build failed with `CS0246: The type or namespace name 'PushTargetConfig' could not be found`.
**Root cause:** A type serialized by a runtime ScriptableObject (`FYAssetSettings.PushTargets`) was declared inside an editor-only file. Generated project files and the Editor compile both define `UNITY_EDITOR`, so only the player compilation saw the missing type.
**Fix:** Keep the serialized data type and its fields runtime-visible; guard only the members that depend on editor-only helpers. Added a static gate asserting that player-visible files never reference types declared only in editor-only files.
**Prevention:** Any type reachable from a runtime ScriptableObject must be runtime-visible. After moving or guarding a type, verify with a player-side compile, not only with the Editor compile.

## IP-66: Overlong Content File Name Exceeded Windows MAX_PATH

**Symptom:** The player failed with `Could not find a part of the path ...\StreamingAssets\Standalone\bundles\<name>` although the content file had been delivered.
**Root cause:** The physical content file name was the logical content name (group + payload + type + mode + address key), 92 characters. Under the deep end-to-end run directory the full path reached 262 characters, beyond the Windows `MAX_PATH` limit of 260, so runtime file probes failed.
**Fix:** Derive the physical file name from a deterministic 16-hex hash of the logical content name, for both Unity-built contents and copied raw files. The logical name and the manifest file-name mapping stay unchanged. Raised the build-cache fingerprint format version so cached long-named artifacts are rejected.
**Prevention:** Keep physical artifact names short and independent of the logical name; check the longest expected deployment path, not only the project-relative one. A naming change is part of the cache identity: bump the cache version in the same change.

## IP-67: Directory Asset Copied As A Single Raw File

**Symptom:** Content build failed with `RAWFILE_COPY_FAILED ... Access to the path 'Assets/Plugins/xlua.bundle' is denied`.
**Root cause:** `Assets/Plugins/xlua.bundle` is a directory that Unity imports as a `DefaultAsset`. Once classification stopped forcing serialized payloads, it was classified as a raw file and the copy step treated the directory as a single file.
**Fix:** Skip directory-typed paths during collection; raw-file handling only accepts real files.
**Prevention:** Raw file handling must validate "is a file" before copying. Directory-shaped Unity assets are not packable content units.
