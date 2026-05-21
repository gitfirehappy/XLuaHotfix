# Implementation Pitfalls

> Silent failure, dual truth sources, infrastructure bypass, and data structure contract violations.

## IP-01: Missing File Degrades to Default Value

**Symptom:** Missing build output → CRC written as `0` in manifest. Error detected only at runtime.

**Root cause:** `if (File.Exists) ...` with no `else` branch — value stays at default.

**Prevention:** Missing files during generation must be fatal errors. Integrity gaps must propagate at the point of detection.

---

## IP-02: Critical Bootstrap With No Explicit Failure Contract

**Symptom:** Manifest loading fails → system enters uninitialized limbo. Logs but provides no error signal. AB mode is all-or-nothing but failure is silent.

**Root cause:** "Soft" failure mode for critical path — neither fail-fast nor supported fallback.

**Prevention:** Every critical bootstrap path must choose: fail-fast with explicit fatal state, or supported fallback with structured warning. Silent uninitialized is never acceptable.

---

## IP-03: Invalid Config Silently Skipped

**Symptom:** Null/empty PackageName → `continue;` with zero diagnostic output.

**Root cause:** Skip without generating any message.

**Prevention:** Every skipped/ignored pipeline input must produce diagnostic output. Never silently swallow invalid config.

---

## IP-04: Empty catch Block

**Symptom:** `catch { ... continue; }` with no log, no warning. Exception becomes undebuggable.

**Root cause:** Prioritized crash-avoidance over diagnosability.

**Prevention:** Every `catch` block must log, rethrow, or handle explicitly. Empty `catch {}` is forbidden. The only exception: fully expected + harmless scenarios WITH an explanatory comment.

---

## IP-05: Guard That Silently Skips Cleanup

**Symptom:** `if (!string.IsNullOrEmpty(eid))` controls BOTH count decrement AND resource cleanup. Precondition violation is silently no-op'd.

**Root cause:** Precondition not enforced at API level; code silently skips essential logic.

**Prevention:** API preconditions essential to correctness must be enforced (assertion, exception, or validation). Silent no-ops hide future bugs.

---

## IP-06: Dead Code Appearing to Provide Safety

**Symptom:** `SCHEDULER_DEADLOCK` branch that can never execute because validation already catches cycles. Appears to handle runtime deadlocks but doesn't.

**Root cause:** Validation guarantees the precondition making execution-time detection unreachable.

**Prevention:** Dead code appearing to provide safety is worse than no code. Remove it or add explicit comment: "Provably unreachable because X guarantees Y. Retained as defense-in-depth."

---

## IP-07: Fire-and-Forget Async Without Await

**Symptom:** Async method called without `await` in error-recovery path. Exception unobserved.

**Root cause:** Missing `await` on async call.

**Prevention:** Every async call must be awaited, have exception handled, or be explicitly fire-and-forget with a comment. Especially dangerous in error-recovery code.

---

## IP-08: Re-Init Not Clearing Previous State

**Symptom:** `Initialize()` does not clear caches before repopulating. AB init can fall back to AA, mixing stale data.

**Root cause:** Idempotency/cleanup not considered.

**Prevention:** Every public init method must clear all caches first. Do not assume single-initialization.

---

## IP-09: Two Independent Refcount Systems

**Symptom:** `AssetCache` (global refcount) and `HandleRegistry` (per-slot refcount) independently track the same concept. They desynchronize → use-after-free.

**Root cause:** Two sources of truth for the same concept with no synchronization protocol.

**Prevention:** Exactly ONE source of truth for lifetime tracking. If two layers must both count, define an explicit synchronization protocol.

---

## IP-10: Dependencies Declared in Two Independent Sources

**Symptom:** `IBuildTask.DependsOn` (code) and `TaskEntry.DependsOn` (SO config) both declare dependencies. Scheduler merges without cross-validation. Updating one silently produces wrong graph.

**Root cause:** Dual-source design without single-authority rule.

**Prevention:** Config with two declaration sites must have ONE authority. Secondary source must be additive-only, or a validation gate must enforce consistency.

---

## IP-11: Shared Key Written But Not Consumed — Half-Unified Truth

**Symptom:** `TaskPrepareContext` writes `BuildVersion`; `TaskGenerateManifest` ignores it, reloads SO directly. DAG can't validate dependency; two version sources exist.

**Root cause:** New shared key introduced but existing consumers not migrated.

**Prevention:** When introducing a shared context key, migrate ALL consumers simultaneously. Half-unified is worse than no unification.

---

## IP-12: Dead Config Field — Exposed But Never Read

**Symptom:** `MinAssetSizeBytes` exposed in Inspector, tunable by developer. No code reads it. Tuning has zero effect.

**Root cause:** Field added but consumer never wired.

**Prevention:** Every exposed config field must be actively consumed. Dead config is the hardest bug to diagnose — the system silently ignores user input.

---

## IP-13: Lazy Cache Not Invalidated on Mutation

**Symptom:** `GetDependencyMap()` lazily builds `_dependencyMap`. `AddEdge()` mutates edges without clearing cache. Callers read stale data.

**Root cause:** Lazy cache with no invalidation on mutation paths.

**Prevention:** Every lazy cache must invalidate on ALL mutation paths.

---

## IP-14: Cached State on Type With Public Mutable Input

**Symptom:** `RuntimeAssetEntry` caches normalized labels, documents manual `InvalidateLabelCache()`, but `Labels` is public `List<string>` — type can't enforce invalidation.

**Root cause:** Cache added to type whose inputs were already public and mutable.

**Prevention:** Once a type adds cached derived state, encapsulate all inputs. Public mutable collections and cached state are incompatible. Choose: pure DTO or guarded model.

---

## IP-15: Static Readonly Captures Config at Type-Init

**Symptom:** `static readonly _hotfixUrl = FYAssetSettings.Instance.HotfixUrl` — value frozen at type init. Modifying SO has no effect.

**Root cause:** Static readonly caches what was meant to be dynamic configuration.

**Prevention:** When migrating from constants to SO config, use on-demand property access, not `static readonly`. Static caching defeats ScriptableObject — unless startup-only snapshot is an explicit, documented decision.

---

## IP-16: Raw File/Directory I/O Bypassing Shared Helper

**Symptom:** `File.Exists`, `Directory.CreateDirectory`, `File.Copy`, `File.WriteAllText` used raw instead of `FileHelper`. Atomic-write used in AB path but not AA. Error handling fragmented.

**Root cause:** `FileHelper` not extended with needed operations; developers fell back to raw I/O.

**Prevention:** All file I/O must go through `FileHelper`. When a new pattern is needed, extend `FileHelper` first. Non-append persistent writes MUST use atomic write (temp + rename).

**2026-05-20 recurrence:** Review after BOU-1/HPU-1 found raw `Directory.CreateDirectory`/`File.Delete` in recently touched bootstrap, download retry, and AA manifest-temp paths. The fix routed those paths through `FileHelper.EnsureDirectory` / `FileHelper.TryDelete`. Prevention addendum: after touching a file that already has a `FileHelper` boundary, run a targeted raw I/O grep on that file before verification.

---

## IP-17: Build Artifact Logic Triplicated

**Symptom:** Three classes independently implement path creation, file copy, manifest writing. Already caused layout drift requiring follow-up fix.

**Root cause:** No shared artifact infrastructure.

**Prevention:** Before the third copy of similar build-output logic, extract a shared abstraction.

---

## IP-18: Ad-Hoc Error Transport Instead of Structured Result

**Symptom:** `Task<bool>` + `string BuildSummary` as error channel. No structured diagnostics (code, severity, source).

**Root cause:** No structured orchestration result type existed.

**Prevention:** Every public async orchestration API must return a structured result type carrying diagnostics. A `string Summary` is not an error channel.

---

## IP-19: Hardcoded Error Codes Instead of Centralized Constants

**Symptom:** `"INVALID_ARG"`, `"BUILD_FAILED"` appear as raw strings in multiple files.

**Root cause:** Developer habit of inline strings instead of extending centralized enums.

**Prevention:** Every new error code must go into the centralized constants file. Raw string error codes forbidden.

---

## IP-20: Hardcoded Asset Paths

**Symptom:** `"Assets/Build/BuildPipelineConfig.asset"` as raw string in multiple files. Path change → manual multi-file edits.

**Root cause:** Inline string literals instead of central constant.

**Prevention:** Every asset path must be defined as a constant in a single place. No raw path strings in non-constant code.

---

## IP-21: Error Code Reused for Different Condition

**Symptom:** Containment reuses overlap error code. Aggregate reporting becomes ambiguous.

**Root cause:** Copy-pasted error code instead of creating distinct one.

**Prevention:** Each semantically distinct condition must have a unique error code.

---

## IP-22: Inconsistent Result/Message Patterns

**Symptom:** Some result types use factories; others are mutable field bags; some embed messages; others use bool+string.

**Root cause:** No shared convention defined.

**Prevention:** Define and enforce a subsystem convention for result/message types.

---

## IP-23: Log Prefix Hardcoded Instead of nameof

**Symptom:** `"[ABPackageBackend]"` repeated as magic string. Class rename → must hunt every log call.

**Prevention:** Log prefixes should derive from the class name programmatically.

---

## IP-24: Duplicated Utility Logic Across Files

**Symptom:** ~150 lines of path utilities duplicated verbatim across 3 files. Bug fix in one must be manually propagated.

**Root cause:** No shared utility extracted early.

**Prevention:** Non-trivial logic appearing in >1 file must be extracted to shared class. Third occurrence = mandatory extraction.

---

## IP-25: Validation and Execution Have Independent Implementations

**Symptom:** `CollectionScanner` and `CollectorSettingValidator` each built their own rule-resolution helper. If semantics change, one lags.

**Root cause:** Both built resolution logic independently.

**Prevention:** Validation and execution operating on the same concept must use the same code.

---

## IP-26: HashSet First-Element for Identity (Nondeterministic)

**Symptom:** `address → HashSet<EntryId>`. `UnloadAsset(key)` picks "first" from HashSet — undefined iteration order, different result per run.

**Root cause:** Using unordered collection iteration for semantic identity.

**Prevention:** Never rely on `HashSet<T>` iteration order for identity. When duplicate keys exist, unload must be by explicit unique ID.

---

## IP-27: IReadOnlyList Backed by Mutable List — Downcast

**Symptom:** Public API returns `IReadOnlyList<string>` but backing store is `List<string>`. Callers downcast and mutate, corrupting internals.

**Root cause:** Return type changed to read-only interface without changing backing type.

**Prevention:** Never return mutable collection as read-only interface unless backing is genuinely immutable (`string[]`, `ReadOnlyCollection<T>`).

---

## IP-28: Struct Without Explicit Equality Contracts

**Symptom:** `CollectorRef`, `AssetClassification`, `AssetHandle<T>` are structs with business semantics but default comparison only. Fields added → equality silently changes.

**Root cause:** Types started as simple carriers, grew into cross-boundary types, never revisited.

**Prevention:** Any struct crossing API boundaries or used in HashSet/Dictionary must have explicit `IEquatable<T>`, `Equals`, `GetHashCode`, `ToString`.

---

## IP-29: BuildContext Get<T> Returns default(T) for Missing Keys

**Symptom:** Missing key returns `0`; stored `0` also returns `0`. Downstream can't distinguish.

**Root cause:** No `TryGet<T>` overload.

**Prevention:** Key-value store returning `default(T)` for missing must also provide `TryGet<T>`.

---

## IP-30: Boolean Flag With Inconsistent Semantics Across Branches

**Symptom:** `isDuplicated` hardcoded `true` in one branch, computed from count in another. Same name, two meanings.

**Root cause:** Branches written independently, flag never unified.

**Prevention:** Review all branches when adding a flag. Same name = same semantics.

---

## IP-31: Interface Default Methods Throwing NotSupportedException

**Symptom:** `IAssetIndex` has both AA and new methods; old impl throws for new methods. Callers must know which impl they have.

**Root cause:** One interface used as migration bridge for incompatible capability sets.

**Prevention:** Split transitional APIs into separate interfaces. Default-method `NotSupportedException` violates Liskov.

---

## IP-32: Manager Type-Casting Interface to Concrete

**Symptom:** `_backend` cast to `ABPackageBackend` for AB-specific methods. Third backend → another type-cast branch.

**Root cause:** Interface missing methods the manager needs.

**Prevention:** If consumer casts to concrete type, enrich the interface. No type-check branches.

---

## IP-33: Dictionary Lookup Key Mismatch

**Symptom:** Dict keyed by logical `BundleName`; looked up by hashed output filename. Same property name, different meaning — lookup silently fails.

**Root cause:** `BundleName` means different things on different types.

**Prevention:** When same property name has different semantics across types, do NOT assume interchangeability.

---

## IP-34: Mirrored Types With Manual Field Copy, No Compile-Time Alignment

**Symptom:** `ManifestAssetEntry` and `RuntimeAssetEntry` duplicate fields. `ToRuntimeEntry()` copies 8 fields manually. Adding field requires changes in 3 places.

**Root cause:** No shared contract between mirrored types.

**Prevention:** Mirrored types must share an interface or base class. Compiler must enforce alignment.

---

## IP-35: GraphView EdgeControl Mutated Before Layout Is Ready

**Symptom:** Unity Reload produced `NullReferenceException` from `EdgeControl.ComputeLayout()` after setting `edgeControl.edgeWidth`, followed by IMGUI layout-state errors.

**Root cause:** Edge styling touched GraphView internal layout state during edge creation/reload. `edgeWidth` can force `EdgeControl` layout computation before Unity has fully initialized its edge geometry.

**Prevention:** During GraphView rebuilds, avoid layout-affecting `EdgeControl` mutations. Prefer stable styling such as input/output color and element opacity; if width is required, apply it only after the edge is attached and layout is known ready.

---

## IP-36: Visual Dedup Removed a Required Semantic Layer

**Symptom:** Data-flow lines disappeared after reducing graph clutter; only execution-order lines remained, so the DAG no longer showed both dependency semantics.

**Root cause:** The de-duplication rule suppressed data-flow edges whenever the same producer-consumer pair already had an execution edge. This optimized visual noise by deleting information instead of layering it.

**Prevention:** Do not remove a semantic edge layer just because another layer shares endpoints. Render secondary layers with lower opacity, behind primary lines, and de-duplicate only within the same semantic layer.

---

## IP-37: Pointer File Reused Manifest Naming

**Symptom:** The remote package pointer used ABManifest filename constants in build output while runtime downloaded a separate `manifest.json` literal.

**Root cause:** `PackageIndex` and resource manifests were both described as "manifest" files, so build-time and runtime code developed separate filenames.

**Prevention:** Pointer files and content manifests must have distinct constants and names. `PackageIndex` uses `PACKAGE_INDEX_FILE_NAME`; AB/AA content manifests use ABManifest/AAManifest constants only.

---

## IP-38: Bootstrap Baseline Generated Placeholder Data

**Symptom:** Full AB builds exported an empty ABManifest to `StreamingAssets` even though the task graph had already produced the real package output.

**Root cause:** The bootstrap exporter recreated placeholder data instead of consuming the final package output owned by `BuildPackageRequest`.

**Prevention:** Bootstrap/export steps must consume upstream build artifacts, not regenerate substitute state. If a task graph produces the canonical package, downstream bootstrap must copy from that package.

---

## IP-39: Task Migration Left Implementation In Legacy Helper

**Symptom:** A workflow was described as task-managed, but the task only delegated to a legacy helper that still owned the real implementation.

**Root cause:** The migration moved scheduling ownership but not implementation ownership, so the old boundary remained active and contradicted the architectural intent.

**Prevention:** When converting behavior into a pipeline task, move the execution logic or explicitly document the helper as shared infrastructure. Verification must grep for the retired helper/type and confirm source and project-file references are gone when the helper is meant to be replaced.

---

## IP-40: Auto-Repair Creates A Second Config Truth

**Symptom:** Build pipeline assets were expected to define the task backbone, but runtime/editor load paths also auto-added missing backbone tasks.

**Root cause:** Default configuration metadata was implemented as a repair pass that mutated existing assets, mixing template creation, validation, and source-of-truth ownership.

**Prevention:** Default backbone definitions may create new config assets and support validation/UI behavior, but must not silently modify existing config assets during load or build execution. Missing required tasks should fail validation.

