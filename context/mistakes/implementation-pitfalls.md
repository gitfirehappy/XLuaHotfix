# Implementation Pitfalls

Verified historical errors and prevention rules.

## IP-01: Missing File Becomes Default Value

**Symptom:** Missing output was written as a default value and only failed later.
**Root cause:** Existence check had no failure branch.
**Fix:** Treat missing generated files as fatal.
**Prevention:** Missing files during generation must fail at detection.

## IP-02: Critical Bootstrap Fails Silently

**Symptom:** Bootstrap failure left the system uninitialized without an explicit error state.
**Root cause:** Critical path used soft failure only.
**Fix:** Use explicit fatal state or supported fallback.
**Prevention:** Critical bootstrap must not end in silent limbo.

## IP-03: Invalid Config Skipped Without Diagnostic

**Symptom:** Invalid pipeline input was skipped with `continue;` and no log.
**Root cause:** Skip path had no diagnostic output.
**Fix:** Emit a diagnostic for every ignored input.
**Prevention:** Never silently swallow invalid pipeline data.

## IP-04: Empty Catch Blocks Hide Failures

**Symptom:** Exceptions were swallowed with no log or rethrow.
**Root cause:** Crash avoidance was prioritized over diagnosability.
**Fix:** Log, rethrow, or handle explicitly.
**Prevention:** Empty `catch {}` is forbidden except for clearly explained harmless cases.

## IP-05: Public API Guard Silently Skips Cleanup

**Symptom:** A guard blocked both refcount decrement and cleanup.
**Root cause:** API precondition was not enforced.
**Fix:** Enforce the precondition with assertion, exception, or validation.
**Prevention:** Correctness-critical preconditions must not become silent no-ops.

## IP-06: Dead Code Pretends to Be Safety

**Symptom:** A dead branch appeared to handle a failure that validation already prevented.
**Root cause:** Execution-time code duplicated an impossible state.
**Fix:** Remove it or mark it as provably unreachable.
**Prevention:** Do not keep safety-looking dead code without explanation.

## IP-07: Async Fire-and-Forget Without Contract

**Symptom:** Async recovery code was invoked without `await`.
**Root cause:** No explicit fire-and-forget contract.
**Fix:** Await it, handle exceptions, or mark it intentionally fire-and-forget.
**Prevention:** Every async call must have an explicit completion strategy.

## IP-08: Re-init Keeps Stale State

**Symptom:** `Initialize()` repopulated caches without clearing them first.
**Root cause:** Idempotency was not handled.
**Fix:** Clear all cached state before repopulating.
**Prevention:** Public init methods must be safe to call again.

## IP-09: Two Refcount Sources Drift Apart

**Symptom:** Two layers tracked the same lifetime independently and desynchronized.
**Root cause:** No single source of truth.
**Fix:** Keep one lifetime owner or define one synchronization protocol.
**Prevention:** Exactly one source of truth for lifetime tracking.

## IP-10: Dependency Declared Twice

**Symptom:** Code and config both declared dependencies and could disagree.
**Root cause:** Dual-authority design.
**Fix:** Choose one authority or enforce consistency.
**Prevention:** A dependency must not have two independent authorities.

## IP-11: Shared Context Key Not Fully Adopted

**Symptom:** One task wrote a shared key while another still read old config directly.
**Root cause:** Consumers were not migrated together.
**Fix:** Migrate all consumers at once.
**Prevention:** Half-unified truth is worse than no unification.

## IP-12: Dead Config Field Never Read

**Symptom:** Inspector config could be edited but had no effect.
**Root cause:** Field was added without a consumer.
**Fix:** Wire every exposed setting into runtime or remove it.
**Prevention:** Do not expose config that nothing reads.

## IP-13: Lazy Cache Not Invalidated

**Symptom:** Cached derived data stayed stale after mutation.
**Root cause:** Mutation paths did not clear the cache.
**Fix:** Invalidate on every mutation path.
**Prevention:** Every lazy cache needs invalidation coverage.

## IP-14: Cached State on Mutable Public Input

**Symptom:** Derived cache relied on a public mutable list.
**Root cause:** The type could not enforce invalidation.
**Fix:** Encapsulate inputs or remove cached derived state.
**Prevention:** Mutable public collections and cached state do not mix.

## IP-15: Static Readonly Freezes Dynamic Config

**Symptom:** A config value was captured once and never updated.
**Root cause:** `static readonly` was used for dynamic data.
**Fix:** Read config on demand.
**Prevention:** Dynamic settings must not be frozen at type init.

## IP-16: Raw File I/O Bypasses Shared Helper

**Symptom:** Raw file and directory APIs were used instead of the shared helper.
**Root cause:** Helper coverage lagged behind new use cases.
**Fix:** Route I/O through the shared helper and extend it first when needed.
**Prevention:** File I/O should go through the shared helper; persistent writes should use atomic write.

**2026-05-20 recurrence:** touched bootstrap, download retry, manifest-temp, and release paths were found using raw I/O. They were routed through `FileHelper` helpers.

## IP-17: Build Artifact Logic Triplicated

**Symptom:** Similar build-output logic existed in multiple classes.
**Root cause:** No shared artifact layer.
**Fix:** Extract shared build-output logic.
**Prevention:** Third copy means extraction is mandatory.

## IP-18: String Summary Used as Error Channel

**Symptom:** Async orchestration used `Task<bool>` plus a freeform string summary for diagnostics.
**Root cause:** No structured result type.
**Fix:** Return structured diagnostics.
**Prevention:** Public orchestration APIs need structured results.

## IP-19: Raw Error Code Strings Scattered

**Symptom:** Error codes were repeated as string literals.
**Root cause:** No centralized constants.
**Fix:** Put codes in the shared constants file.
**Prevention:** Raw string error codes are forbidden.

## IP-20: Raw Asset Paths Scattered

**Symptom:** Asset paths were repeated as string literals.
**Root cause:** No single path constant.
**Fix:** Centralize the path.
**Prevention:** Do not inline asset paths in non-constant code.

## IP-21: Same Error Code Reused for Different Cases

**Symptom:** Different conditions shared one error code.
**Root cause:** Copy-paste reuse.
**Fix:** Assign distinct codes.
**Prevention:** Semantically distinct failures need unique codes.

## IP-22: Inconsistent Result Patterns

**Symptom:** Result types mixed factories, mutable bags, and bool+string shapes.
**Root cause:** No shared convention.
**Fix:** Define one subsystem convention.
**Prevention:** Result/message types must follow one pattern.

## IP-23: Log Prefix Hardcoded

**Symptom:** Class name prefixes were written as magic strings.
**Root cause:** Prefixes were not derived from type names.
**Fix:** Generate prefixes from the class name.
**Prevention:** Log prefixes should not be hardcoded.

## IP-24: Utility Logic Duplicated Across Files

**Symptom:** The same utility code appeared in multiple files.
**Root cause:** Shared helper was not extracted early.
**Fix:** Extract shared utility code.
**Prevention:** Non-trivial repeated logic belongs in one place.

## IP-25: Validation and Execution Diverged

**Symptom:** Validation and runtime each implemented their own rule resolution.
**Root cause:** Independent reimplementation.
**Fix:** Share the same code path.
**Prevention:** Same concept, same implementation.

## IP-26: HashSet Used as Identity Order

**Symptom:** Code relied on `HashSet` iteration order to choose a semantic identity.
**Root cause:** Unordered collection was treated as ordered.
**Fix:** Use explicit identity.
**Prevention:** Never rely on `HashSet<T>` order for meaning.

## IP-27: ReadOnly Interface Backed by Mutable List

**Symptom:** A read-only API could still be downcast and mutated.
**Root cause:** Backing type stayed mutable.
**Fix:** Return a genuinely immutable backing type.
**Prevention:** Read-only interface is not enough if the backing store is mutable.

## IP-28: Struct Without Explicit Equality

**Symptom:** Business-semantic structs depended on default comparison.
**Root cause:** Equality contracts were never defined.
**Fix:** Implement explicit equality contracts.
**Prevention:** Cross-boundary structs need `IEquatable<T>`, `Equals`, `GetHashCode`, and `ToString`.

## IP-29: Missing Key Returns Default(T)

**Symptom:** Missing key and stored default value were indistinguishable.
**Root cause:** Only `Get<T>` existed.
**Fix:** Add `TryGet<T>`.
**Prevention:** Default-returning stores need a presence check API.

## IP-30: Boolean Flag Means Different Things

**Symptom:** One boolean flag had inconsistent semantics across branches.
**Root cause:** Branches were written independently.
**Fix:** Unify the meaning before merging branches.
**Prevention:** Same flag name must mean the same thing everywhere.

## IP-31: Interface Default Method Throws for Migration Gap

**Symptom:** A shared interface mixed old and new capability sets and some implementations threw `NotSupportedException`.
**Root cause:** Transitional surface tried to cover incompatible implementations.
**Fix:** Split the interfaces.
**Prevention:** Migration bridges should not rely on throwing default methods.

## IP-32: Manager Casts Interface to Concrete Type

**Symptom:** Manager code downcasted the backend to access specific methods.
**Root cause:** The interface was too small.
**Fix:** Add the needed methods to the interface.
**Prevention:** Avoid type-check branches in consumers.

## IP-33: Same Property Name, Different Meaning

**Symptom:** A dictionary key was looked up using a value with a different semantic meaning.
**Root cause:** Reused property name hid semantic mismatch.
**Fix:** Separate the concepts.
**Prevention:** Same name does not imply same meaning across types.

## IP-34: Mirrored Types Drift by Manual Copy

**Symptom:** Two mirrored types required manual field copying.
**Root cause:** No shared contract.
**Fix:** Use a shared interface or base type.
**Prevention:** Compiler should enforce mirrored type alignment.

## IP-35: GraphView Edge Layout Mutated Too Early

**Symptom:** Edge styling triggered layout errors during reload.
**Root cause:** Layout-affecting properties were changed before GraphView was ready.
**Fix:** Avoid layout-affecting edge mutations during rebuild.
**Prevention:** Only touch stable styling during GraphView rebuilds unless layout is known ready.

## IP-36: Visual Dedup Removed Meaning

**Symptom:** One edge layer disappeared when another layer already connected the same nodes.
**Root cause:** De-duplication removed semantic information.
**Fix:** Keep layers separate and reduce opacity instead.
**Prevention:** Do not delete a semantic edge layer because another layer shares endpoints.

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

## IP-45: Progress Consolidation Lost Detailed History

**Symptom:** The main `requirements/progress.txt` was replaced by a short summary while standalone progress logs still held detailed history.
**Root cause:** Consolidation was treated as summary replacement instead of detailed entry migration.
**Fix:** Restore the full main progress log, copy standalone progress entries into it with requirement ids, then remove standalone folders.
**Prevention:** Before deleting standalone requirement folders, merge every meaningful `progress.txt` entry into `requirements/progress.txt`; never replace detailed history with summary-only lines.

## IP-46: Backend Refactor Destroyed Approved Editor UX

**Symptom:** The asset collection editor lost the approved Project Scan / Curate workflow, the mutually exclusive Details vs Scan Preview right panel, and the bundle-colored collected tree after the collector backend was refactored.
**Root cause:** The backend model migration was treated as permission to redesign the editor panel instead of preserving the approved UX contract from git history.
**Fix:** Restore the historical workflow implementation first, then adapt only the data model bindings and compile errors to the new backend.
**Prevention:** For editor refactors, identify the approved UX commit and preserve its visible workflow before changing data bindings; do not replace an approved UX with a simpler temporary panel unless explicitly approved.

## IP-47: Scan Enumeration Includes Folders As Assets

**Symptom:** AssetsCollection Project Scan / Curate Preview could show `0 assets / 0 bundles` or abort collection even when the target folders contained valid assets.
**Root cause:** Multiple scan invariants regressed together: `CollectionScanner.CollectAssetPaths` added folder GUIDs into the collected asset path list; full-path ignore patterns such as `Assets/FYAsset/**` were matched only against paths relative to the current collect path; Project Scan generated Group names directly from directory names that could contain bundle-name reserved characters; and a collector-level scan error could prevent already collected package assets from being shown in preview.
**Fix:** Filter folder paths before classification and bundle-key validation, support full `Assets/...` ignore patterns, sanitize Project Scan generated Group names, use explicit `AssetDatabase.FindAssets("t:Object", ...)`, and keep already collected preview assets visible when a later collector reports an error.
**Prevention:** Collector scan enumeration must keep the file-only invariant for folder collectors; ignore tests must cover both full project paths and collector-relative patterns; auto-generated Project Scan names must be valid bundle segments; preview scanners must not discard already collected evidence when a later item fails.

## IP-48: Collector Regained Removed Business Fields

**Symptom:** During AssetsCollection UX recovery, Collector-level `IgnorePatterns` was restored even though the approved design had moved ignore behavior to Project Scan / `AssetCollectionSetting`, and asset rows did not expose `AssetEntry` editing for Address, Labels, Role, and Payload.
**Root cause:** The historical UI was restored mechanically without reapplying the later ownership decisions: Collector is only an editor-time collector with role/payload analysis, while asset-level business metadata belongs to `AssetEntry`.
**Fix:** Remove Collector-level ignore fields and consumers, keep Project Scan ignore at setting level only, and make asset selection open `AssetEntry` editing with save-path support.
**Prevention:** When restoring editor UX from git history, re-check every restored field against the latest ownership decisions before compiling; the accepted asset collection model is Package/Group/Collector for collection structure and `AssetEntry` for Address/Labels/Role/Payload business metadata.

## IP-49: Editor Save and Scene Scan Coupled to Preview Artifacts

**Symptom:** AssetsCollection could make `Save Collectors` unavailable after a stale or erroring preview, asset editing required hunting through the preview tree, and Scene assets could scan as zero assets or fail to use `PackSeparately`.
**Root cause:** The save button treated the previous preview as an enablement gate instead of running validation when saving; asset-level selection was not part of the left navigation model; Scene discovery depended on Unity type-filter queries such as `t:Scene` / `t:Object`; Project Scan skipped Scene file collectors when a folder collector already owned the path; direct nested-list assignment was followed by a scan-error short circuit that made the save action appear to do nothing; Save reloaded the setting into Curate without rebuilding `_curateResult`, clearing asset rows and details that depend on preview data; the Scan-stage toolbar path back into Curate also cloned saved data without scanning, so returning from Scan could show no asset rows; Save reload also reused the initial Curate expansion initializer, forcing the sidebar tree fully open after every save; Project Scan preview relied on the top toolbar `Curate` action to copy the snapshot, leaving no explicit in-stage confirmation button and making users expect that switching back to Curate would already use the new scan; after adding `Confirm To Curate`, the top toolbar `Curate` action still secretly called the same confirm path, so preview data could overwrite Curate without the explicit button; the sidebar `Foldout` toggle event was intercepted and stopped before Unity could update expansion; manual `AssetEntry.PayloadKind` could override the invariant that `.unity` scenes must be Scene payloads; `PackSeparately` fed asset Address directly into BundleKey validation even though generated addresses such as `XLua_SceneAsset` can contain `_`, which is reserved in bundle keys; and Project Scan could append Scene file collectors to an existing folder-derived Group while leaving that Group's configured `BundlePackingMode` at `PackTogetherByLabel`.
**Fix:** Keep Save enabled for valid Curate candidates and persist Curate fields regardless of preview scan errors, add Asset nodes to a scrollable Curate sidebar, enumerate project and collector paths without Unity type filters before file filtering, generate explicit file collectors for `.unity` scenes unless another file collector already owns that exact scene, force Project Scan Scene groups to `PackSeparately` even when reusing an existing folder-derived Group, add an explicit Scan Preview `Confirm To Curate` action that replaces the current Curate candidate with the latest Project Scan snapshot, remove the toolbar `Curate` action's hidden preview-confirm behavior, make both Save reload and Scan-to-Curate toolbar entry rebuild `_curateResult` from the saved setting, use simple disclosure labels for the sidebar tree instead of intercepting `Foldout` internals, force reserialization after saving, reload Curate with a fresh `CollectionScanner.Scan` result while preserving existing sidebar expansion state, force `.unity` assets back to Scene payload so `ResolvePackingMode` returns `PackSeparately`, and normalize Address into a bundle-key-safe projection before composing the `PackSeparately` key.
**Prevention:** Editor save actions must persist user edits and report scan errors separately; Project Scan must remain read-only until an explicit confirmation copies the snapshot into Curate; any path that returns to Curate must restore both editable data and scan result data used by navigation/details without resetting user-owned view state such as sidebar expansion; approved navigation should expose the edited entity directly; Scene collection must be extension-invariant and must keep file-level fallback collectors across backend migrations; do not intercept built-in UI Toolkit control events unless the default behavior is intentionally replaced; scrollable navigation needs an explicit `ScrollView`; Scene payload cannot be downgraded by asset-level manual metadata; never treat asset Address and BundleKey as the same namespace.

## IP-50: Long Editor Repair Without Regression Matrix

**Symptom:** A long AssetsCollection repair repeatedly fixed one visible issue while reintroducing another: approved UX disappeared, the wrong panel was removed, Project Scan returned zero assets, Collector ignore fields came back, Save appeared ineffective, Scene collection broke, sidebar foldouts failed, and bundle-key naming aborted scans.
**Root cause:** The repair was driven by local symptoms and git-history restoration without an explicit end-to-end regression matrix for the approved workflow. Backend model ownership, editor UX, scan output, save persistence, Scene fallback, and bundle naming were validated piecemeal instead of as one workflow contract.
**Fix:** Restore the approved Project Scan / Curate UX from history, reapply current ownership decisions, and verify the full workflow surface: Project Scan returns assets and bundles, Scan Ignore is setting-level only, Curate sidebar exposes Package/Group/Asset rows, AssetEntry edits persist after Save and reload, `.unity` assets force Scene + PackSeparately, and PackSeparately uses a bundle-key-safe Address projection.
**Prevention:** Before changing an established editor workflow, write or maintain a concrete smoke checklist for all approved user-visible behaviors and backend invariants. Every fix in the area must be checked against that matrix, not only against the newest reported symptom.

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

## IP-53: Collector Mutations Split Across Editor Entry Points

**Symptom:** AssetsCollection, Inspector header controls, Project context menus, and picker windows disagreed about collected state. Folder-owned asset deletion had no clear asset-level removal behavior, nested folders under a collected root looked uncollected, Curate did not update immediately after Inspector changes, and deleting a Collector could leave stale Curate/Scan Preview rows that counted assets but could not expand.
**Root cause:** Editor entry points implemented Collector add/remove behavior independently and reused stale scan/reverse-index state. Collector was also treated as if it could own per-asset membership edits, while the accepted model keeps Collector as a rule-based discovery entry and stores asset-level removal intent separately.
**Fix:** Centralize membership changes in `CollectorMutationUtility`, make every mutation invalidate `CollectorReverseIndex` and notify open panels, rebuild Curate scan state after mutations, store folder-owned asset removals as GUID exclusions in `FYAssetABSettings.ExcludedAssetGUIDs`, and keep direct File Collector removal as Collector deletion.
**Prevention:** Established editor workflows need one mutation API and one invalidation path. Any UI that changes collection membership must use the shared API, and scan previews must be rebuilt from the current setting plus current options instead of trusted across mutations. Collector should remain a rule entry; per-asset include/exclude state belongs to an explicit settings-owned structure.

## IP-54: Active Settings Ownership Drifted Into Too Many Assets

**Symptom:** Runtime, build, repository, and collector configuration were split across global settings plus Shared/AA/AB/Repository settings, causing panels and docs to disagree about where fields lived. Hotfix URL/retry fields, build paths, Push targets, and AB collection paths could be described or edited through different owners.
**Root cause:** Transitional settings classes became treated as active authorities after migration work instead of remaining compatibility sources. The code and documentation did not keep a small current settings inventory.
**Fix:** Reduce active configuration to three Resources assets: `FYAssetSettings` for global build/project/version/push fields, `FYAssetAASettings` for AA hotfix/build fields, and `FYAssetABSettings` for AB hotfix/build/collection fields. Keep old settings classes only as migration inputs when creating missing active assets.
**Prevention:** After settings migrations, document the active settings inventory and mark old assets as compatibility-only in context and human docs. Provider APIs should expose current owners directly and old-owner reads should be constrained to migration code paths.

## IP-55: Scene-Only Folders And Opaque Exclusions Broke Collector Acceptance

**Symptom:** Project Scan still generated a folder-level Collector for `Assets/Scenes` when the folder only contained `.unity` scene files; long-path Address removed the file suffix; excluded GUIDs in AB config did not show which asset was excluded and were not managed with scan Ignore rules.
**Root cause:** Project Scan used a generic "has any collectable file" folder-generation check, so scene files satisfied the folder Collector condition before file-level Scene collectors were added. Long-path Address generation kept an old "without extension" implementation after the accepted UI semantics changed. Per-asset collection exclusions were stored as raw GUID strings in `FYAssetABSettings`, placing collection filter state outside `AssetCollectionSetting` and outside the visible Ignore management surface.
**Fix:** Generate Project Scan folder Collectors only for folders containing at least one non-Scene collectable asset after ignore/exclusion filtering, keep `.unity` assets as file Collectors, preserve file extensions in long-path Address generation, move active exclusions to `AssetCollectionSetting.ExcludedAssets` with GUID plus cached path, migrate hidden legacy `FYAssetABSettings.ExcludedAssetGUIDs`, and expose Excluded Assets next to Ignore Patterns with object/path-aware rows.
**Prevention:** Folder-collector generation checks must match the intended ownership shape, not just asset existence. Address-style names kept for serialized compatibility still need current behavior documented and verified. Per-asset collection filters belong beside scan Ignore state in `AssetCollectionSetting`; backend settings may provide migration inputs but must not remain the active UI or build source of truth.

## IP-56: Preview-Only Fix Left Saved Collector Data Invalid

**Symptom:** After a Scene Project Scan fix, existing `CollectorSetting.asset` data still kept `Assets/Scenes` as a Folder Collector, and build/curate scans could still see Scene files through the stale folder entry.
**Root cause:** The fix only changed Project Scan candidate generation. It did not normalize already saved Collector data, mark the Curate candidate as dirty after normalization, or add a scanner guard for stale Folder Collectors.
**Fix:** Normalize Scene-only Folder Collectors into Scene File Collectors when entering Curate and before saving, mark the candidate as unsaved when normalization changes it, and make collection scans skip `.unity` files from every Folder Collector.
**Prevention:** When changing generated configuration shape, cover three paths together: new preview generation, saved-data normalization/persistence, and scanner/build-time defensive behavior for old assets that have not been saved yet.

## IP-57: Disabled ObjectField Looked Like Missing Asset

**Symptom:** Excluded Assets rows showed a grey `None (Object)` field, leaving only Remove usable even when the cached asset path existed.
**Root cause:** The row used a disabled `ObjectField` and resolved the asset with a generic path load that could return null for the displayed asset, so the UI became a read-only dead display instead of a resource reference control.
**Fix:** Resolve excluded assets through GUID/path to `AssetDatabase.LoadMainAssetAtPath` with an all-assets fallback, keep the ObjectField enabled for Unity's built-in reference interaction and replacement, and keep Remove as the explicit collection action.
**Prevention:** Inspector rows that represent project assets should be actionable references. If the user needs to identify or navigate the asset, do not disable the reference field; resolve by GUID first, show the cached path, and keep navigation independent from removal. If a valid path still resolves to `None`, continue the investigation into the asset's serialized references instead of treating the UI as the root cause.

## IP-58: Script Meta GUID Drift Broke Serialized Assets

**Symptom:** An Excluded Assets row displayed the correct cached path for `Assets/SO/SOContainer/Bridge/AnimeBridge.asset`, but the ObjectField still showed `None (Object)`.
**Root cause:** The asset file existed, but its main object referenced `m_Script` GUID `8804d7e5753b2164ba51f8a66736d5f5`; a previous helper-directory refactor recreated `ScriptObjectContainer.cs.meta` with GUID `b31ac084c03fc4a4e83105057cd4ebec`, so Unity could not bind the serialized ScriptableObject script and returned no usable main asset object.
**Fix:** Restore `Assets/FYAsset/Scripts/Shared/Helpers/ScriptObjectContainer.cs.meta` to the historical GUID `8804d7e5753b2164ba51f8a66736d5f5` instead of rewriting the serialized `.asset` files or masking the failure in the inspector UI.
**Prevention:** Unity script moves and directory refactors must preserve `.meta` files. When an asset path exists but `AssetDatabase.LoadMainAssetAtPath` returns null or an inspector ObjectField shows `None`, inspect the YAML `m_Script` GUID, search current `.meta` files, and check git history before changing UI code or asset data.

## IP-59: Payload Auto Classification Drifted From Unity Importers

**Symptom:** Review reasoning treated `.csv`, `.json`, and project `.lua` files as RawFile candidates because the classifier used a serialized-extension whitelist.
**Root cause:** The classifier guessed payload kind from file suffix instead of asking Unity's importer pipeline whether the path had a usable main asset.
**Fix:** Make `AssetClassifier.Auto` importer-first: `.unity` stays `Scene`; a usable non-`DefaultAsset` main asset from `AssetDatabase.GetMainAssetTypeAtPath` / `LoadMainAssetAtPath` is `Serialized`; otherwise fallback to `RawFile`.
**Prevention:** Do not decide serialized-vs-raw from an extension list. If Unity or a ScriptedImporter can produce a usable main asset, Collector Auto must treat it as serialized unless the user explicitly forces another payload kind.

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

## IP-65: Shrinkable Containers Compressed Dynamic Target Content

**Symptom:** Push Target fields first collapsed into horizontal strokes. After individual rows received minimum heights, the second target overflowed its border and overlapped the Local Server controls.
**Root cause:** Each target had expanded from one row to several rows, but the Push card still had a maximum height and its nested editor, target, and local-server containers retained the UI Toolkit default `flexShrink = 1`. Removing only the outer cap was insufficient because nested vertical containers could still surrender layout height while their controls painted outside the reduced boxes. Per-control width and minimum-height changes only moved the overflow.
**Fix:** Remove the Push card's maximum height and set the intrinsic-height editor, target, and local-server containers to `flexShrink = 0`; the existing outer vertical `ScrollView` owns overflow.
**Prevention:** Dynamic lists inside a scroll container must preserve intrinsic height through every nested vertical container. When several sibling rows compress or overlap together, audit the complete ancestor shrink chain before changing individual controls.

## IP-66: Misclassified the Push Stack During Repository Slimming Review

**Symptom:** During the AA/AB repository-slimming review, the push stack (`IPushTarget`, `LocalDirectoryPushTarget`, `CloudflarePagesPushTarget`, `PackagePublishTransaction`, `PushModels`) was proposed for wholesale deletion on the claim that it "mirrors repository objects for team baseline sharing." The developer confirmed deletion based on that claim; the error was only caught by AI self-check during execution, and the developer's own review let the wrong premise through.
**Root cause:** The push stack was judged by directory location (`Shared/Build/Repository/`) instead of actual data flow. In reality `Push()` publishes **built hotfix packages** to mirror roots (local directory, Cloudflare Pages) that runtime hotfix downloads from — it is the release half of the hotfix lifecycle, only borrowing `RepositoryCommit.PackageRootDir` as a version registry. Premature convergence on a tidy "delete the ops stack" narrative skipped the mandatory read of what `Push(PushPayload)` actually moves.
**Fix:** R6 scope corrected before execution: push stack kept and re-homed to `Shared/Build/Publish/` as the publish mechanism with `PushPayload` cut over from `RepositoryCommit` to `BuildBaseline` (+ `PackageRootDir`/`BackendMode` fields); Cloudflare target stays in Compat as the project's CDN channel glue; only the true repository kernel (objects history, facade, health/repair) is deleted.
**Prevention:** Before approving deletion of a subsystem, read the payload/data flow of its primary entry point — never infer function from folder placement. Deletion proposals must state what flows through the code, not just where it lives. Reviewer of a cleanup plan should ask "what does this actually move, and who consumes the moved thing?" before signing off; both author and reviewer share this miss.

## IP-67: `git add -A Assets` Sweeps Build Outputs Into Commits (Twice)

**Symptom:** During the AA/AB decoupling commits (P1 and R6), `git add -A Assets` staged the entire `Assets/StreamingAssets/Standalone/**` build outputs into the commit, despite an explicit project rule to keep them untracked. Caught in pre-commit audit the first time, but repeated weeks later in the same session family.
**Root cause:** Broad-scope staging (`git add -A Assets`) was used for speed instead of explicit path lists; the exclusion of `StreamingAssets/**` lived only in conversation memory, not in `.gitignore`, so nothing mechanical blocked the sweep.
**Fix:** Commits rebuilt with `git restore --staged Assets/StreamingAssets` before landing. Durable rule: never `git add -A` over `Assets/` in this repo — stage explicit paths, and audit `git status` grouped by change type before every commit.
**Prevention:** Add `Assets/StreamingAssets/Standalone/` and `Assets/StreamingAssets/BuildIndex.json*` to `.gitignore` so generated local build state cannot be swept even by blanket staging. (Proposed to developer; not yet applied.) Audit staged file-type counts (`awk '{print $1}' | sort | uniq -c`) as a pre-commit habit.
