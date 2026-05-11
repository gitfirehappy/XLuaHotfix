# Post-Review Fix 20260510 Multi-Dimensional Review

> **Date**: 2026-05-11
> **Reviewer**: Codex GPT-5
> **Scope**: `requirements/refactor-2026/plan/plan-post-review-fix-20260510.md` executed changes
> **Method**: Static source review, plan-vs-implementation audit, API boundary review, serialization compatibility review, runtime/cache safety review

## Findings

### High - CLI `--version` does not update the manifest version

Evidence:
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskPrepareContext.cs:34-36` reads `--version` into `buildVersionString`.
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskPrepareContext.cs:56-61` still reads `BuildConfig.Version` from `VersionDataBase` SO, with no CLI parse and no SO write-back.
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskGenerateManifest.cs:150-154` writes `ABManifest.PackageVersion = cfg.Version`.

Impact:
The plan's H-1 decision says CLI `--version` should write SO first, then read SO, preserving SO as the single source. The current implementation does not do that. A batch build invoked with `--version 1.2.3-rc+4` will name the output folder with that string, but the generated `ABManifest.PackageVersion` will remain whatever is already stored in `Assets/Build/VersionDataBase.asset`.

Why this matters:
This breaks release traceability and can publish an artifact whose directory/build id and manifest package version disagree. Downstream version comparison and hotfix eligibility checks may use the stale manifest version.

Suggested fix direction:
Parse CLI `--version` with `VersionNumber.TryParse`, write `VersionDataBase.CurrentVersion`, mark/save the asset in editor/batch mode, then read it back into `BuildConfig.Version`. If `--version` is intended only as build id, rename the CLI argument or update the plan/invariants; right now the plan explicitly requires version override semantics.

### High - `IReadOnlyList<string>` return type still exposes mutable internal lists

Evidence:
- `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs:24-26` stores caches as `Dictionary<string, List<string>>`.
- `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs:144-153` returns the cached `List<string>` directly as `IReadOnlyList<string>`.

Impact:
The API signature changed, but the object identity is still a `List<string>`. Existing or future callers can still do:

```csharp
var keys = AssetPackageManager.Instance.GetKeysByLabel(label);
if (keys is List<string> mutable) mutable.Clear();
```

That mutates `_labelToKeys` / `_typeToKeys` inside `AssetPackageManager`.

Why this matters:
This misses the plan invariant: "调用者无法 cast 回 `List<string>` 篡改". It also means the review-fix gives a false sense of immutability while leaving cache corruption possible.

Suggested fix direction:
Return arrays or read-only wrappers that do not expose the backing `List`. For hot paths, build immutable array caches during initialization (`Dictionary<string, string[]>`) and return those arrays as `IReadOnlyList<string>`. If arrays are considered mutable by index, use `ReadOnlyCollection<string>` or a small internal immutable list wrapper.

### Medium - `VersionNumber.Channel` whitelist is not enforceable globally

Evidence:
- `Assets/FYAsset/Scripts/Build/BuildManage/VersionDataBase.cs:75-79` exposes `VersionNumber.Channel` as a public field.
- `Assets/FYAsset/Scripts/Build/BuildManage/VersionDataBase.cs:223-225` validates channel only inside `TryParse`.
- `Assets/FYAsset/Scripts/Build/BuildManage/VersionDataBase.cs:51-58` validates only the `IncrementVersion` path.
- `Assets/FYAsset/Scripts/Build/BuildManage/VersionDataBase.cs:103-112` throws if `CompareTo` sees an unknown channel.

Impact:
Any code, inspector serialization, JSON load, or direct object initializer can still set `Channel = "dev"` or another unknown value. The invalid state is accepted until a comparison runs, at which point `CompareTo` throws.

Why this matters:
The plan's H-2 says "`TryParse` 与 setter 拒绝未知 channel"; no setter exists in the current implementation. The invariant is only partially implemented, so version objects can still enter invalid states through non-parse paths.

Suggested fix direction:
Convert `Channel` into a serialized backing field plus property setter, or provide a `SetChannel`/constructor factory and make direct mutation impossible where feasible. If Unity serialization requires a field, add an explicit `Validate()` or `OnAfterDeserialize` guard and call it before comparisons/build use.

### Medium - `VersionState.version -> Version` rename lacks serialization migration

Evidence:
- `Assets/FYAsset/Scripts/Build/BuildManage/HelperBuildData_Remote/VersionState.cs:7-12` now defines `public VersionNumber Version;`.
- No `[FormerlySerializedAs("version")]` is present on the renamed field.
- Runtime hotfix load reads existing JSON through `SerializationUtility.DeserializeJson<VersionState>()`, which uses Unity `JsonUtility` via `JsonCodec`.

Impact:
Existing `version_state.json` files generated before the rename likely contain `"version": ...`. Unity `JsonUtility` maps fields by name, so those files will deserialize with `Version == null`.

Why this matters:
`LegacyHotfixBackend` logs and converts `versionState.Version` directly. At minimum this causes missing local/remote version data; depending on call path, it can also produce null-version comparisons later in the hotfix flow. The plan explicitly asked whether `FormerlySerializedAs("version")` is needed, but the executed code does not include a compatibility bridge.

Suggested fix direction:
Add `[FormerlySerializedAs("version")]` if Unity's JSON path honors it for this use case, or add a temporary legacy field/upgrade path that can read both `version` and `Version`. Verify with a real pre-rename `version_state.json`.

### Medium - BuildConfig placement and dependency shape drift from the plan

Evidence:
- Plan T1 says `BuildConfig.cs` should be created with `BuildContextKeys` in the Runtime assembly.
- Actual file is `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/BuildConfig.cs:1-8` and imports `UnityEditor`.
- `Assets/FYAsset/Scripts/Build/BuildContextKeys.cs:1-4` still states the keys are shared by Editor Task and Runtime consumers.

Impact:
This is not an immediate compile defect if all consumers remain editor-only. It is still an architectural drift: the key is globally visible, but the value type lives in an editor task area and depends on `UnityEditor.BuildTarget`.

Why this matters:
Future runtime or non-editor tooling that sees `BuildContextKeys.BuildConfig` cannot legally consume the associated type. It also contradicts the documented boundary in the plan and `BuildContextKeys` comment.

Suggested fix direction:
Either move `BuildConfig` to the same non-editor boundary and remove the `UnityEditor` dependency, or explicitly document that `BuildConfig` is editor-pipeline-only and `BuildContextKeys.BuildConfig` is not for runtime consumers.

### Low - `GetKeysByType/GetKeysByLabel` case-insensitive caches may change duplicate behavior

Evidence:
- `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs:24-26` now uses `StringComparer.OrdinalIgnoreCase`.
- Legacy initialization assigns whole lists with `_typeToKeys[item.Type] = ...` and `_labelToKeys[item.Label] = ...` at lines 98-101.

Impact:
If legacy SO data contains labels or types that differ only by case, the later entry overwrites the earlier one. The plan wanted case-insensitive lookup, so the direction is reasonable, but duplicate-case data should be detected or merged rather than silently overwritten.

Suggested fix direction:
During initialization, merge duplicate-case key lists or emit a warning/error when duplicate-case labels/types are found.

## Positive Checks

- The old scattered `BuildContextKeys.BackendMode/BuildVersion/Version/OutputRoot/TargetPlatform` references are no longer present in the reviewed task code.
- `TaskGenerateManifest` now declares and reads `BuildContextKeys.BuildConfig`, which covers the previous `OutputRoot` read declaration gap.
- `ABAssetIndex.GetEntriesByAddressAndType()` now returns prebuilt arrays from `_addressTypeResults`, removing the previous per-call list allocation.
- `AssetPackageManager.Initialize()` now clears query caches before initialization and defensively copies legacy SO lists during cache build.
- `VersionNumber.TryParse()` rejects negative major/minor/patch/build values and unknown channel strings on the parse path.

## Verification Notes

- `dotnet build XLuaHotfix.sln` was started during review, but the shell session did not return output in time. This report therefore does not claim a fresh build result.
- The review is static and source-based; no Unity Editor playmode or batch build was executed.

## Overall Assessment

The plan is partially landed, but two core invariants are not actually satisfied:

1. CLI version override does not feed `ABManifest.PackageVersion`.
2. `GetKeysByType/GetKeysByLabel` still expose mutable internal `List<string>` instances.

I would not sign off this plan as fully closed until those two High items are fixed or the plan is explicitly revised to narrow the intended behavior.

---

## Execution Notes (2026-05-11)

### Fixes Applied (commit a1aff30)

| Item | Fix | Files |
|------|-----|-------|
| H-1 | TaskPrepareContext: CLI `--version` parsed with `VersionNumber.TryParse` → writes `VersionDataBase.CurrentVersion` + `EditorUtility.SetDirty` + `AssetDatabase.SaveAssets` → reads back for `BuildConfig.Version`. `BuildVersionString` always gets raw CLI string (or timestamp). | TaskPrepareContext.cs |
| H-2 | `_labelToKeys` / `_typeToKeys` changed from `Dictionary<K, List<string>>` to `Dictionary<K, string[]>`. `BuildQueryCaches` builds temp `Dictionary<K, List<string>>` then converts via `.ToArray()`. Legacy init also uses `.ToArray()`. Returned `string[]` (as `IReadOnlyList<string>`) cannot be cast to `List<string>` for `Clear()`/`Add()`/`Remove()`. | AssetPackageManager.cs |
| M-2 | `[FormerlySerializedAs("version")]` on `Version.Version` for Unity binary serialization. `[SerializeField] private VersionNumber version` bridge field for JsonUtility JSON backward compat. `MigrateLegacyVersionField()` copies `version`→`Version` if needed, called after both deserialization sites in `LegacyHotfixBackend`. | VersionState.cs, LegacyHotfixBackend.cs |

### Acknowledged (not fixed)

| Item | Reason |
|------|--------|
| M-1 (Channel public field) | Converting to property requires Unity serialization regression testing across all VersionNumber usage — deferred to next cycle |
| M-3 (BuildConfig placement) | `BuildTarget` dependency requires Editor assembly; `BuildContextKeys` comment updated. Not a runtime defect |
| Low (duplicate case) | Accepted behavior: later entry overwrites earlier one on case-collision |

### Verification

- `dotnet build XLuaHotfix.sln` — 0 errors
- `GetKeysByType/Label` returns `string[]` as `IReadOnlyList<string>` — structural mutation prevented
