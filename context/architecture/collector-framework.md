# Collector Framework

Last reviewed: 2026-05-22

## Scope

This document describes the verified collector foundation currently present in `Assets/FYAsset/Scripts/Build/Collector/`.

It is a build-time framework. It is not the same thing as the current runtime loading backend.

## Configuration Hierarchy

The collector model is a four-level hierarchy:

`CollectorSetting -> CollectorPackage -> CollectorGroup -> Collector`

### `CollectorSetting`

- ScriptableObject root
- stores all configured packages
- intended singleton asset for collector configuration

### `CollectorPackage`

- top-level package boundary
- holds `PackageName`
- owns a list of `CollectorGroup`

### `CollectorGroup`

- groups collectors that share packaging semantics
- holds `GroupName`
- contributes group-level `Tags`
- owns a list of `Collector`

### `Collector`

- binds one collect root to a rule set
- stores:
  - `CollectPath`
  - `CollectorType`
  - `ForcePayloadKind`
  - `AddressRuleName`
  - `PackRuleName`
  - `FilterRuleName`
  - collector-level `Tags`
  - `IgnorePatterns`

Manual collector configuration supports only `Main`, `Static`, and `Depend` collector types.
`Implicit` is a system-generated collector type produced by dependency analysis and must not be selected for user-authored collectors.

## Rule Contracts

The editor-side rule system is defined by three interfaces:

- `IAddressRule`
- `IPackRule`
- `IFilterRule`

`RuleResolver` resolves rule class names through reflection and caches instances.

### Pack rule contract

`IPackRule.GetPackKey(PackRuleContext ctx)` returns the group-level pack key, not the final physical bundle name by itself. The framework owns the final naming composition.

`PackRuleContext` includes:

- asset path
- group name
- collect path
- package name
- classification result
- merged labels

## Intermediate Build Model

### `CollectedAssetInfo`

- flattened editor-time representation of one collected asset
- used as an intermediate build result after collector traversal and rule evaluation

### `AssetClassification`

- describes the semantic role and payload kind of an asset
- used by rules and future packaging logic as a shared contract

## Classification Behavior

`AssetClassifier` currently implements a narrow and explicit rule set.

### Asset role mapping

`ECollectorType` maps into `EAssetRole`:

- `Main -> Main`
- `Static -> Static`
- `Depend -> Depend`

### Payload kind resolution

`EForcePayloadKind` controls payload behavior:

- `Serialized` forces serialized payload
- `RawFile` forces raw-file payload
- `Scene` forces scene payload
- `Auto` infers `.unity` as `Scene`, everything else as `Serialized`

Important constraint:

- raw-file behavior is never inferred automatically
- raw-file payload must be selected explicitly through `ForcePayloadKind.RawFile`

## What Is Already True

- the collector configuration model exists
- the rule interfaces and reflection-based resolver exist
- classification contracts exist
- the framework already defines the build-time vocabulary needed for later pipeline work
- `CollectionScanner` produces `ScanResult` containing `BuildMessage` entries via factory methods (`BuildMessage.Error` / `BuildMessage.Warning`)
- `BuildSeverity { Warning, Error }` × `Code` (string, from `BuildErrorCodes`) × `Message` × `Source` (file/collector path)
- `TaskCollectAssets` runs `CollectorSettingValidator` before scanner execution, so invalid package names and invalid manual collector types block the build before asset traversal.
- scanner rule execution failures are converted into structured `RULE_EXECUTION_FAILED` build messages instead of raw exceptions.
- implicit dependency entries preserve the source package name during dependency analysis; dependency query caching is separate from per-bundle reference accounting.

## What This Document Does Not Claim

This document does not claim that the collector framework has already replaced the entire existing build pipeline. The repository still contains the current build/release flow described in `resource-build-and-release.md`.
