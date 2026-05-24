# FYAssetSettings Build Settings Split

## Summary
Split build-time configuration out of `FYAssetSettings` into dedicated ScriptableObject assets:
`SharedBuildSettings`, `AABuildSettings`, and `ABBuildSettings`.

## Background
`FYAssetSettings` previously mixed runtime/global hotfix settings with build paths, manifest output format,
pipeline config paths, hotfix package limits, and repository push targets. This made the Settings panel too
dense and created unclear ownership between runtime configuration and build backend configuration.

## Decisions
- Keep `FYAssetSettings` as the runtime/global settings asset.
- Keep `VersionDataBase` shared as the product-version source; do not split versions by AA/AB.
- Keep `BuildPackagesFolderName` in `FYAssetSettings` because runtime URL assembly still needs it.
- Move shared build paths and push targets into `SharedBuildSettings`.
- Move backend-specific pipeline config path, manifest output format, and hotfix size limit into AA/AB settings.
