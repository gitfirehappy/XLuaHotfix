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
**Root cause:** The save button treated the previous preview as an enablement gate instead of running validation when saving; asset-level selection was not part of the left navigation model; Scene discovery depended on Unity type-filter queries such as `t:Scene` / `t:Object`; Project Scan skipped Scene file collectors when a folder collector already owned the path; direct nested-list assignment was followed by a scan-error short circuit that made the save action appear to do nothing; Save reloaded the setting into Curate without rebuilding `_curateResult`, clearing asset rows and details that depend on preview data; the Scan-stage toolbar path back into Curate also cloned saved data without scanning, so returning from Scan could show no asset rows; Save reload also reused the initial Curate expansion initializer, forcing the sidebar tree fully open after every save; Project Scan preview relied on the top toolbar `Curate` action to copy the snapshot, leaving no explicit in-stage confirmation button and making users expect that switching back to Curate would already use the new scan; after adding `Confirm To Curate`, the top toolbar `Curate` action still secretly called the same confirm path, so preview data could overwrite Curate without the explicit button; the sidebar `Foldout` toggle event was intercepted and stopped before Unity could update expansion; manual `AssetEntry.PayloadKind` could override the invariant that `.unity` scenes must be Scene payloads; and `PackSeparately` fed asset Address directly into BundleKey validation even though generated addresses such as `XLua_SceneAsset` can contain `_`, which is reserved in bundle keys.
**Fix:** Keep Save enabled for valid Curate candidates and persist Curate fields regardless of preview scan errors, add Asset nodes to a scrollable Curate sidebar, enumerate project and collector paths without Unity type filters before file filtering, generate explicit file collectors for `.unity` scenes unless another file collector already owns that exact scene, add an explicit Scan Preview `Confirm To Curate` action that replaces the current Curate candidate with the latest Project Scan snapshot, remove the toolbar `Curate` action's hidden preview-confirm behavior, make both Save reload and Scan-to-Curate toolbar entry rebuild `_curateResult` from the saved setting, use simple disclosure labels for the sidebar tree instead of intercepting `Foldout` internals, force reserialization after saving, reload Curate with a fresh `CollectionScanner.Scan` result while preserving existing sidebar expansion state, force `.unity` assets back to Scene payload so `ResolvePackingMode` returns `PackSeparately`, and normalize Address into a bundle-key-safe projection before composing the `PackSeparately` key.
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
