# Editor Pitfalls

Verified editor and user-interface mistakes. Each entry keeps its original IP number and standard fields.

> Historical scope: entries below are records of what happened at the time they were written. The collector data model
> they describe (`AssetEntry` with Address/Labels/Role/Payload fields, `AssetCollectionPackage`, filter/group rules,
> reflection rule dropdowns) was replaced by `AssetCollectionSetting` + `AssetOverrides` + `RawFileRules` +
> `SharePolicy`. Treat the named types as history, not as current architecture.

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

## IP-53: Collector Mutations Split Across Editor Entry Points

**Symptom:** AssetsCollection, Inspector header controls, Project context menus, and picker windows disagreed about collected state. Folder-owned asset deletion had no clear asset-level removal behavior, nested folders under a collected root looked uncollected, Curate did not update immediately after Inspector changes, and deleting a Collector could leave stale Curate/Scan Preview rows that counted assets but could not expand.
**Root cause:** Editor entry points implemented Collector add/remove behavior independently and reused stale scan/reverse-index state. Collector was also treated as if it could own per-asset membership edits, while the accepted model keeps Collector as a rule-based discovery entry and stores asset-level removal intent separately.
**Fix:** Centralize membership changes in `CollectorMutationUtility`, make every mutation invalidate `CollectorReverseIndex` and notify open panels, rebuild Curate scan state after mutations, store folder-owned asset removals as GUID exclusions in `FYAssetABSettings.ExcludedAssetGUIDs`, and keep direct File Collector removal as Collector deletion.
**Prevention:** Established editor workflows need one mutation API and one invalidation path. Any UI that changes collection membership must use the shared API, and scan previews must be rebuilt from the current setting plus current options instead of trusted across mutations. Collector should remain a rule entry; per-asset include/exclude state belongs to an explicit settings-owned structure.

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

## IP-65: Shrinkable Containers Compressed Dynamic Target Content

**Symptom:** Push Target fields first collapsed into horizontal strokes. After individual rows received minimum heights, the second target overflowed its border and overlapped the Local Server controls.
**Root cause:** Each target had expanded from one row to several rows, but the Push card still had a maximum height and its nested editor, target, and local-server containers retained the UI Toolkit default `flexShrink = 1`. Removing only the outer cap was insufficient because nested vertical containers could still surrender layout height while their controls painted outside the reduced boxes. Per-control width and minimum-height changes only moved the overflow.
**Fix:** Remove the Push card's maximum height and set the intrinsic-height editor, target, and local-server containers to `flexShrink = 0`; the existing outer vertical `ScrollView` owns overflow.
**Prevention:** Dynamic lists inside a scroll container must preserve intrinsic height through every nested vertical container. When several sibling rows compress or overlap together, audit the complete ancestor shrink chain before changing individual controls.

## IP-66: Reset Tool Corrupted A Tracked Asset And New Metas Missed A Newline

**Symptom:** After running the local-state reset tool, Unity reported `Unable to parse file Assets/Build/VersionRecord.asset: [Parser Failure at line 22]`, and every newly added asset logged `The GUID inside '...meta' cannot be extracted by the YAML Parser`.
**Root cause:** Two independent format defects. The tool's field-replacement regex used `\s*` after the field name, which also matches newlines, so replacing `Channel:` consumed the following `LastBuildTime:` line and left an orphan `""` line. Separately, newly created `.meta` files were written without a trailing newline, unlike every existing meta, which made Unity's YAML parser fall back to string matching.
**Fix:** Restrict the regex to intra-line whitespace (`[ \t]*`) and repair the damaged asset; append the missing trailing newline to the affected metas and verify that no meta lacks a trailing newline.
**Prevention:** Tools that rewrite YAML assets must validate the result's structure after writing. New `.meta` files must match the existing format byte-for-byte, including the trailing newline.
