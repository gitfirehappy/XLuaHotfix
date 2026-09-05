# FYAsset Full Audit, Localization, And Slimming Review

> **Date**: 2026-07-14
> **Reviewer**: Codex / ponytail-audit
> **Scope**: All C# source under `Assets/FYAsset/`, including runtime, Hotfix, build pipeline, Repository, Collector,
> settings, compatibility, and Editor UI.
> **Method**: Codegraph call-path inspection, repository-wide reference scans, definition-only type/member scans,
> comment and direct Debug-call scans, source review, and solution compilation.

## Status

Low-risk findings were applied. Candidates that would change runtime loading, Hotfix behavior, build configuration,
Repository publication boundaries, or persisted rule resolution remain review-only pending a separate developer decision.

## Baseline And Result

- Before: 177 C# files, 31,044 lines.
- After: 174 C# files, 30,410 lines.
- Net C# reduction: 634 lines and 3 source files, with no dependency changes.
- The implementation diff currently removes 770 lines and adds 107 lines across FYAsset source and synchronized project
  files, for a net reduction of 663 lines before requirements records.

## Applied Findings

1. `delete:` Unreferenced `AssetConflictRules` and its nested report model; replacement: nothing. The entire 227-line
   type had no caller in project source, tests, serialized assets, or Lua. `[Assets/FYAsset/Scripts/AB/Build/AssetConflictRules.cs]`
2. `delete:` Uninstantiated `BuilderPanel` and `PlaceholderPanel`; replacement: existing concrete AA/AB panels.
   `[Assets/FYAsset/Scripts/AB/Build/Editor/ABPipeline/BuilderPanel.cs]`
   `[Assets/FYAsset/Scripts/Shared/Build/Editor/Shared/PlaceholderPanel.cs]`
3. `shrink:` Removed unused ABManifest Type/Label query indexes and their public query methods; replacement:
   `ABAssetIndex`, which already owns active runtime Type/Label lookup. This also removes per-initialization Dictionary
   and List allocations. `[Assets/FYAsset/Scripts/AB/Runtime/Manifests/AB/ABManifest.cs]`
4. `delete:` Removed definition-only runtime diagnostics and convenience APIs from `ABBundleLoader`, `AssetHandle`,
   `HandleRegistry`, and `ManifestBundleEntry`; replacement: current active load/release paths and direct fields.
5. `delete:` Removed graph-era and placeholder-era APIs from `BuildPipelineBackbone`, `BuildTaskResolver`,
   `BuildPipelineUI`, `BuildContext`, settings access, report storage, Repository preview, Collector helpers, version
   helpers, and path helpers; replacement: current callers already use the remaining direct APIs.
6. `delete:` Removed unimplemented `RULE_GROUP_BY_TYPE`, `RULE_GROUP_BY_LABEL`, and
   `RULE_GROUP_BY_DIRECTORY`; replacement: nothing until matching rule implementations exist.
7. `shrink:` Replaced the manual Editor path-segment loop with one normalized ordinal-ignore-case substring check.
   `[Assets/FYAsset/Scripts/AB/Build/Collector/Editor/Rules/CollectAll.cs]`

## Deferred Findings

1. `yagni:` `RuleResolver` plus `RuleDropdownHelper` use 224 lines of reflection/cache code for one `IFilterRule` and
   one `IGroupRule` implementation. A direct default-rule mapping could remove about 190-210 lines, but persisted
   `FilterRuleName` / `GroupRuleName` values make this a build-configuration behavior change.
2. `yagni:` `IBuildRepository` has one implementation (`FileBuildRepository`). Holding the concrete type would remove
   the 16-line interface and indirection, but this touches the Repository/publication boundary.
3. `shrink:` `HandleRegistry` generation-based ownership remains more complex than current consumers require. Removing
   generation tracking is still a runtime lifetime-contract change and needs focused acceptance.
4. `shrink:` `AssetsCollectionPanel` (~3,019 lines), `RepositoryStatusPanel` (~1,928 lines), and `HotfixFlowBase`
   (~1,199 lines) are large, but splitting them alone would only move code and add files. No size-only refactor was made;
   extract only when a concrete repeated change requires it.

## Kept After Review

- `AssetPackageManager` and `HotfixManager` compatibility facades have active project consumers.
- `BuildProjectManager` is used by `BuildCommandLine`.
- `BuildPipelineWindowMenu`, Collector postprocessors/context menus, custom inspectors, and self-check methods are
  invoked by Unity attributes or naming conventions and are not dead code.
- `RuleResolver` and `IBuildRepository` were not changed for the risk reasons above.

## Language Audit

- 74 meaningful pure-English comment lines were found after excluding XML tags; 53 explanatory lines were localized.
- 21 remaining English lines are command examples, paths, code expressions, identifiers, or file-layout diagrams and
  intentionally remain English.
- 43 direct Debug-call expressions initially contained no Chinese text. Thirty hardcoded descriptions were localized.
  The remaining 13 calls forward dynamic status/error values or external tool output and contain no hardcoded English
  description at the call site.
- Technical terms, API/type/field/variable names, error codes, file names, and ambiguous wording remain English.

net: -634 C# lines, -0 deps possible.
