# Field Semantics

Stable naming rules for fields that cross the resource-management layers. This file explains semantic boundaries; it
is not an API reference or a current implementation inventory.

## Asset And Content Fields

- `Labels` are asset-level query labels. They are the only asset classification field; there is no bundle-level `Tags`
  field in the current manifest model.
- `Address` is a runtime logical name for public assets only. It is unique and case-insensitive within one package;
  implicit dependency entries carry no public address and must not be reachable through it.
- `BundleKey` is a build-time content bucketing key. It is not a business label and must not be used for runtime queries.
- `AssetPath` and `SourcePath` are project-relative source paths for editor/build diagnostics, not runtime lookup keys.
- `EntryId` is the canonical unique asset identity. A build-time GUID field that represents the same identity must keep a
  one-to-one mapping instead of inventing a second identity.
- `PrimaryType` describes one asset. `AssetContentType` describes how an asset or content file is produced and loaded;
  neither may be substituted for the other.
- Bundle names identify physical or logical content. Do not derive asset query semantics from them.

## Path And Locator Fields

- `URL` fields are remote locators and must be joined as URLs.
- `FilePath`, `Directory`, and `Root` fields are local filesystem paths and must use filesystem path operations.
- Unity project asset paths use `/` separators and must remain distinguishable from local filesystem paths.

## Naming Rule

Use one field name for one semantic meaning across related data structures. When two values can both be described as an
"address", "name", "path", or "tag" but have different owners or lifetimes, keep distinct names and document the
boundary instead of relying on context.
