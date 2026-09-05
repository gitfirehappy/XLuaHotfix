# Lua Resource Boundary Separation Plan 2026-07-19

> **Status**: Completed, signed off, and archived on 2026-07-21
> **Requirement ID**: lua-resource-boundary-separation-20260719
> **Origin**: Promoted from `drafts/archive/draft-lua-index-pipeline-independence-20260520.md` after the verified AB Player P0 and the 2026-07-19 design discussion.
> **Scope**: Make Lua index publication, runtime resource loading, and label editing obey strict AA/AB ownership while preserving one thin application-facing facade.

## Goal

Remove the historical AA/AB shared implementation path without breaking either concrete service. AA and AB must own their
runtime loading, build publication, and label metadata independently. Application code keeps only a small stable loading
facade that binds once after the selected concrete startup path succeeds.

## Current Execution Checkpoint

- S1 is signed off.
- S2 and the approved S2R closure are implemented and verified. Focused real-source tests, all scenario regressions,
  solution compilation, Unity compilation, and independent AA/AB Full, Player build, and clean runtime chains passed.
- The prior AB clean-runtime blocker is closed: the same-Bundle Player path now has zero `DEPENDENCY_FAILED`, zero Unity
  same-file/already-loaded diagnostics, and reaches AB package, facade, Lua, and `GameLauncher` ready markers once.
- AA now retains one real Addressables handle ticket per successful load, and `DialogueDataManager` uses only the thin
  facade while preserving its synchronous API and parsed-data cache.
- S2 and S2R were signed off by the developer on 2026-07-20. The existing Addressables Player processor residue remains
  recorded and unchanged.
- A retained no-caller lifecycle gap needs separate design approval: `ABBundleLoader.UnloadAllBundles()` does not cancel or
  seal pending single-flight operations, so a late completion could republish cache after teardown. Current accepted
  startup/runtime chains do not call this API, and S2R did not expand into teardown cancellation semantics.
- The AA first-label-as-Type rule remains unchanged and explicitly deferred. S3 implementation and verification are complete.
- S3 migration freezes current published AA Addressable entry labels as the verified behavior contract and mirrors those
  labels into AB `AssetEntry.Labels`. Legacy container-only values do not create new AA entries or overwrite current AA
  labels. In particular, `StateMachine` keeps the current AA `Framework` label, while orphan `EventCentre` and
  `UIFormConfigSO` tool metadata does not become newly published content.
- The real `TypeMemberListSO` source assets remain available for the deferred custom-rule discussion, but their obsolete
  AA/AB runtime package publication is removed. Only the legacy `ScriptObjectContainer` wrapper is retired.
- S3 focused tests, all retained scenario regressions, solution compilation, Unity compilation, and independent AA/AB
  Full, Player build, and clean localhost runtime chains passed from separate 1.0.0/Build 0 and Repository resets.
- The final AB manifest has 158 assets and 45 physical Bundles with matching size/hash metadata. Required Lua/UI/Bridge
  addresses resolve, deleted tool GUIDs/types are absent, and the `LuaBehaviourConfigSO` Bundle has no XLua TextAsset
  dependency caused by the removed `luaScript` field.
- UI loading differences between AA and AB remain outside this gate, as agreed; only the startup-ready and clean-error
  criteria are evaluated here.

## Locked Decisions

1. `GameLauncher` selects AA or AB once at startup. The selected path is fatal on failure; AB never falls back to AA.
2. `AssetPackageManager` remains only as an application-facing forwarding facade. It does not own resource behavior.
3. `PackageManagerBase`, `IPackageBackend`, and shared `IAssetIndex` are retiring. AB-only Handle, RawFile, TypeKey, resolve,
   and lifetime capabilities must remain available from AB concrete services.
4. Lua index generation has one shared pure data builder, but AA and AB own address discovery and publication separately.
5. AA Lua container addresses come from Addressables entries. AB Lua container addresses come from final
   `CollectedAssets`. No third address registry is introduced.
6. `Assets/Build/**` remains globally ignored by AB collection. Only the generated Lua index asset is added explicitly.
7. AA and AB label metadata remain separate authorities: Addressables entries for AA and `AssetCollectionSetting` for AB.
8. Label tooling is backend-specific. Shared code may provide presentation-only helpers but no shared write interface.
9. Existing AB runtime capabilities must not be deleted merely because they leave the shared facade.
10. Manifest schemas, Bundle formats, Hotfix protocol, and XLua bridge exposure remain unchanged.
11. AB address loads resolve the requested exact Unity type first. Only requests for the exact `UnityEngine.Object` or
    `ScriptableObject` base type may fall back to a unique address candidate; multiple candidates fail with
    `AMBIGUOUS_MATCH`.
12. The application facade returns `(asset, RuntimeMessage)` for every load. It never hides failure as a bare `null`.
13. A missing `UIResourceConfigSO` is fatal to `GameLauncher`; UI backend behavior beyond that startup gate remains out of
    scope for S2.
14. AB physical loading is single-flight by `BundleName` only at the physical-open stage, after the existing active-path
    dependency traversal. Local sync callers complete the same `AssetBundleCreateRequest`; no Task blocking is allowed.
15. AA relies on Addressables as the only reference-count authority. `AAPackageManager` retains one real Addressables handle
    ticket per successful facade load and releases one ticket per typed unload; it does not maintain a parallel integer count.
16. `DialogueDataManager` keeps its synchronous business API but loads and unloads `TextAsset` through `AssetPackageManager`.
    Its Standalone/Integrated backend switch and direct Addressables handle storage are retired.
17. AA label ordering and the current first-label-as-Type rule are not changed in S2R.
18. S3 label migration uses current AA Addressable entries as the verified label contract. AB mirrors the remaining
    explicit business label query sets; stale legacy container values never overwrite AA or create missing AA entries.
19. `TypeMemberListSO` source assets and editor configuration stay in the project. S3 removes only their dead runtime
    package publication and the legacy `ScriptObjectContainer` wrapper/tooling.

## Execution Slices

### S1: Lua Index Build and Startup P0

- Keep the existing `Assets/Build/LuaScriptsIndex.asset` path and GUID; move its address/path constants to the Lua index module.
- Extract a pure Editor builder that writes container-address and normalized-script mappings and rejects invalid addresses or
  duplicate normalized module keys.
- AA reads addresses from Addressables, registers the index in its AA group, and runs this preparation from the AA backend.
- AB builds the index after `TaskCollectAssets` from final collected container entries; `TaskCollectBuiltins` explicitly adds
  only the generated index asset before dependency analysis.
- Remove Lua index generation from `BuildProjectRunner`.
- Require the bootstrap index and every referenced container address in AA/AB manifests before publication.
- Make `XLuaLoader` index bootstrap fatal; it cannot mark the index ready after a missing or invalid index.
- Stop after static and clean AA/AB build-chain verification and wait for developer sign-off.

### S2: Runtime Ownership Separation

- Fix `ABBundleLoader` sync and async dependency traversal to track only the active recursion path. Legal diamond
  dependencies must load, while a true active-path cycle must still fail.
- Make `AAPackageManager` own the current Addressables cache, query, load, and unload behavior directly.
- Make `ABPackageManager` directly compose AB manifest, index, backend, resolver, RawFile, TypeKey, Handle, and lifetime behavior.
- Delete shared runtime implementation interfaces/bases while preserving concrete AB APIs and behavior.
- Move AB-only resolver/index behavior under AB ownership and remove Shared-to-AB model dependencies from `RuntimeMessage`
  without changing its shared severity/code/message envelope.
- Change `HotfixManager.InitializeAsync` to accept the single selected mode, bind the facade only after successful concrete
  initialization, and fail before binding on any startup error.
- Bind semantics are strict: calls before binding fail, same-mode rebinding succeeds, cross-mode rebinding fails, and there
  is no fallback or later runtime switch.
- Reduce `AssetPackageManager` to typed address load/load-sync/unload forwarding. Disconnect the unused runtime
  `XluaTypeConfigLoader` and Lua label-cache path instead of retaining label APIs solely for dead callers; keep the
  `TypeMemberListSO` source asset and defer package-label cleanup to S3.
- Migrate current facade callers to consume `RuntimeMessage`. `UIResourceConfigSO` failure must stop startup before
  `GameLauncher.IsReady` is set.
- Stop after verification and wait for developer sign-off. If the AB clean-runtime gate exposes a bundle-level
  in-flight/lifetime defect, record it and do not repair it in this slice without a separate approval.

### S2R: Runtime Concurrency Closure

- Add a `BundleName` physical-load operation to `ABBundleLoader` after dependency acquisition. Recheck cache before opening,
  make followers release their duplicate dependency acquisitions, and publish one cache entry with all pending acquisitions.
- Preserve `LoadFromFileAsync` for local async I/O. A local sync follower completes the same request through
  `AssetBundleCreateRequest.assetBundle`; UWR/non-filesystem sync joins return `UNSUPPORTED_OPERATION` without a second open.
- Remove `AAPackageManager`'s custom `ReferenceCount` cache. Retain every successful Addressables handle as one release ticket,
  and release exactly one ticket for each matching typed facade unload.
- Route `DialogueDataManager` loading and unloading through the thin facade while preserving its synchronous public load API
  and parsed-dialogue cache. Remove its backend mode split and direct Addressables dependencies.
- Do not change label/type rules, package formats, public facade signatures, startup ordering, or AB HandleRegistry ownership.
- Stop after focused/static verification and independent clean AA/AB Full, Player build, and runtime chains.

### S3: Upper-Layer and Label Ownership Migration

- Route remaining XLua configuration, UI, and Bridge runtime loads through the thin facade; remove direct upper-layer
  Addressables calls and AA-specific runtime naming.
- Preserve the serialized numeric value while renaming `XLuaLoader.Mode.AddressablesOnly` to `PackageOnly`; remove unused
  AA-label options.
- Add separate Project Selection batch label panels to the AA and AB build windows. AA writes Addressables entries; AB writes
  collected `AssetEntry.Labels`. Neither panel writes the other backend.
- Migrate verified Lua/SO tool-container labels into both backend authorities before deleting the legacy taggers, tool-only
  container assets/types, and AA-only fields/methods from the Lua business model.
- Update stable context only after implementation and runtime verification.

## Slice Approval Gates

- S1 was signed off and S2 execution was explicitly approved by the developer on 2026-07-20.
- S2R execution was explicitly approved by the developer on 2026-07-20. Its authorization is limited to the AB physical
  single-flight fix, AA handle-ticket simplification, Dialogue facade migration, verification, and progress alignment.
- S2/S2R was signed off and the remaining S3 scope was explicitly approved by the developer on 2026-07-20.
- S3 and the full plan were signed off by the developer on 2026-07-21 and are archived.

## Public API Changes

- `HotfixManager.InitializeAsync()` becomes `HotfixManager.InitializeAsync(BackendMode mode)` in S2.
- Shared `AssetPackageManager` retains only:
  ```csharp
  Task<(T asset, RuntimeMessage error)> LoadAssetAsync<T>(string address);
  (T asset, RuntimeMessage error) LoadAssetSync<T>(string address);
  void UnloadAsset<T>(string address);
  ```
- AB-only RawFile, TypeKey, `AssetHandle`, and lifetime APIs remain on `ABPackageManager`.
- `LuaScriptsIndex` owns its bootstrap address and Editor asset path constants from S1.
- `XLuaLoader.Mode.PackageOnly` replaces `AddressablesOnly` with the same serialized enum value in S3.
- S2R does not change the shared facade signatures. It retires the unused `DialogueDataManager.LoaderMode`, `Mode`, and
  `LoadDialogueDataIntegrated` upper-layer APIs after verified caller analysis.

## Verification and Safety

- Before each real backend chain, reset version metadata to `1.0.0`, reset that backend's Repository state, and remove only
  verified project-owned generated packages/reports/bootstrap outputs for the selected backend.
- Before switching AA to AB or AB to AA, repeat the reset and confirm no manifest/catalog residue from the previous backend.
- Never push to Cloudflare or contact remote services in this plan. Player verification uses isolated local/built-in content.
- Run focused static checks, `dotnet build XLuaHotfix.sln --no-restore`, and `git diff --check` for every slice.
- S1 acceptance requires clean AA Full and AB Full builds containing `LuaScriptsIndex`, resolvable referenced containers,
  successful Player startup through `ModuleRegistry`, and fatal behavior for a deliberately missing bootstrap index.
- S2 acceptance requires no shared implementation casts/interfaces, no fallback, deterministic typed address resolution,
  legal AB diamond dependencies, true-cycle rejection, and preserved AA common plus AB-specific loads.
- S2R acceptance requires one physical open for concurrent AB BundleName requests, balanced dependency rollback/release,
  AA load/release parity through real Addressables handle tickets, no upper Dialogue `Addressables.*` calls, and clean AA/AB
  Player error gates from independent `1.0.0 / Build 0` resets.
- S3 acceptance requires identical migrated business label query sets across AA/AB and no upper runtime `Addressables.*` calls.

## Non-Goals

- No unified address or label registry.
- No global collection of `Assets/Build/**`.
- No runtime backend switching after startup.
- No AA implementation of AB-only capabilities for symmetry.
- No unrelated HandleRegistry redesign, package format change, repository format change, or external publication.
- No change to AA label ordering or the first-label-as-Type rule in S2R.
