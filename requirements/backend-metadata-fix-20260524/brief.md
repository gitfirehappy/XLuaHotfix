# backend-metadata-fix-20260524 Brief

## Goal
Fix Build Repository backend ownership and release metadata boundaries after manual review found that the Repository panel was grouped under both AB and Manage, while package entry metadata did not state whether the target package was AA or AB.

## Decisions
- Build Repository remains separated by backend channel segment: `AA` and `AB`.
- `VersionDataBase` remains a shared product-version source and is not split by backend.
- `PackageIndex` and `BuildIndexData` must declare backend mode using `AA` or `AB`.
- Runtime hotfix must block a remote `PackageIndex` whose backend mode is missing, invalid, or different from the current client backend.
- Repository UI remains a shared Manage panel entry rather than separate AA/AB buttons.

## Acceptance
- Repository sidebar entry appears only under Manage.
- `PackageIndex.json` and `BuildIndex.json` write backend metadata.
- Hotfix flow validates package backend before using `LatestPackage`.
- Documentation and context reflect the verified backend/version boundary.
