# Draft: Address Generation Conflict Policy

> **Status**: Draft
> **Date**: 2026-06-04
> **Purpose**: discuss the replacement for automatic Address conflict upgrade in `AssetAddressGenerator`.
> **Executable**: No. This draft records direction and code surfaces; it must be promoted to `requirements/plan/` before implementation.

## Current Verified State

- `AssetAddressGenerator.GenerateAddresses()` still contains the historical rule: auto entries first use the filename short name, then same short name with different `PrimaryType` is upgraded to `Filename_Type`.
- The active collector path currently bypasses that batch method and calls `GenerateShortAddress(assetPath, primaryType, true)` from `CollectionScanner`, so newly scanned assets default to `Filename_Type`.
- The Assets Collection details panel also uses `GenerateShortAddress(..., true)` when creating a missing `AssetEntry` or running `Reset Auto`.
- `AssetConflictRules` does not require Address uniqueness. It blocks only unresolvable cases such as duplicate `EntryId` or identical `Address + PrimaryType + LabelSet`; same `Address + PrimaryType` with different Labels is currently a warning.
- Historical progress entries from 2026-03-30 approved automatic `Filename_Type` upgrade. This draft supersedes that direction if promoted.

## Problem

Automatic per-conflict upgrade makes Address style inconsistent inside the same project:

- One asset named `Player` may keep `Player`, while another same-name asset becomes `Player_Prefab` only because a conflict happened in the current scan set.
- The resulting Address depends on nearby assets and scan timing rather than an explicit project decision.
- It does not solve the root issue that Address is intentionally not globally unique; the real resolver identity remains `Address + PrimaryType + Labels`, with `EntryId` as the internal unique id.

## Target Direction

- Default automatic Address should be the filename short name without extension.
- Same short-name Address should be allowed as a normal state; it is not automatically rewritten.
- `Filename_Type` remains a supported explicit naming style, but only through user-selected operations.
- The explicit operations should mirror Addressables user mental model at the asset level and group/batch level:
  - asset-level operation: apply short Address or apply `Filename_Type`;
  - group-level operation: batch-apply short Address or batch-apply `Filename_Type` to assets in that Group.
- Manual overrides remain locked until the user explicitly switches an asset back to Auto or applies a batch operation that is documented to overwrite selected asset Address values.

## Conflict Policy

Address duplication alone should not become a hard error.

- Pass: same Address with different `PrimaryType` or distinguishable Labels.
- Warn: same `Address + PrimaryType` where Labels are the only distinguishing condition, matching the current warning behavior.
- Block: duplicate `EntryId`, identical `Address + PrimaryType + LabelSet`, or any other state where runtime resolve cannot distinguish entries.

This keeps the existing resolver model intact instead of pretending Address can be a unique key.

## Candidate Code Surfaces

- `AssetAddressGenerator`
  - update comments to remove automatic conflict-upgrade wording;
  - keep `GenerateShortAddress(assetPath, primaryType, useTypeSuffix)` as the shared primitive;
  - change `GenerateAddresses()` so auto entries generate short names only and do not rewrite conflict groups.
- `CollectionScanner`
  - default generated Address should call `GenerateShortAddress(assetPath, primaryType, false)`.
- `AssetsCollectionPanel`
  - `EnsureAssetEntry()` and Address `Reset Auto` should use the selected auto-address policy, with short name as the default.
  - add asset-level and group-level explicit operations for applying short names or `Filename_Type`.
- Conflict reporting
  - keep the current `AssetConflictRules` resolution model;
  - optionally improve messages to explain that duplicated Address is allowed until Type/Labels cannot disambiguate.

## Open Questions Before Promotion

- Exact UI placement for asset-level and group-level Address operations in the current Assets Collection panel.
- Whether group-level operations should overwrite manual `AutoAddress=false` entries by default, require a confirmation, or only affect auto entries.
- Whether the selected Address style should be stored as project state or remain a one-shot batch operation.

## Promotion Criteria

- UI behavior for manual overrides is specified.
- Default scan, `Reset Auto`, and batch operation behavior are unambiguous.
- Acceptance cases cover same filename/different type, same filename/same type/different Labels, and identical unresolvable entries.
