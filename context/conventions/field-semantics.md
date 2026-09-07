# Field Semantics

Stable naming rules for fields that cross the resource-management layers. This file explains semantic boundaries; it
is not an API reference or a current implementation inventory.

## Asset And Bundle Fields

- `Labels` are asset-level query labels. Do not use `Labels` for bundle delivery metadata.
- `Tags` are bundle-level delivery or strategy markers. Do not use `Tags` for asset query metadata. Labels and Tags do
  not aggregate automatically.
- `Address` is a runtime logical name and may have the duplicate policy defined by the owning index.
- `AssetPath` and `SourcePath` are project-relative source paths for editor/build diagnostics, not runtime lookup keys.
- `EntryId` is the canonical unique asset identity. A build-time GUID field that represents the same identity must keep a
  one-to-one mapping instead of inventing a second identity.
- `PrimaryType` describes one asset. A bundle type describes a bundle-level classification and must not be substituted
  for an asset type.
- `BundleName` identifies a physical or logical bundle. Do not derive asset query semantics from it.

## Path And Locator Fields

- `URL` fields are remote locators and must be joined as URLs.
- `FilePath`, `Directory`, and `Root` fields are local filesystem paths and must use filesystem path operations.
- Unity project asset paths use `/` separators and must remain distinguishable from local filesystem paths.

## Naming Rule

Use one field name for one semantic meaning across related data structures. When two values can both be described as an
"address", "name", "path", or "tag" but have different owners or lifetimes, keep distinct names and document the
boundary instead of relying on context.
