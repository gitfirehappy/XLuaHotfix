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
