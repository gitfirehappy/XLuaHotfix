# Dependencies Knowledge Index

## YooAsset Reference (Source: E:\unity\project\YooAsset)

Distilled from YooAsset source code analysis. These documents serve as architectural reference for XLuaHotfix's resource management system overhaul (Phase 5-10).

### Build-Time (Phase 5-6 Reference)
- `yooasset-collector-packing.md` — Collector framework: IAddressRule/IPackRule/IFilterRule interfaces, built-in implementations, collection flow, collector hierarchy. **Primary reference for E1-E3.**
- `yooasset-build-pipeline.md` — Build pipeline: IBuildTask interface, 4 pipeline types with task sequences, BuildContext data flow, dependency analysis algorithm (8-phase). **Primary reference for E4-E7.**

### Data Model (Phase 3-4 Reference)
- `yooasset-manifest-model.md` — Manifest data model: PackageManifest/PackageBundle/PackageAsset structures, binary+JSON serialization, runtime lookups, comparison with our ABManifest design.

### Runtime (Comparison Reference)
- `yooasset-runtime-loading.md` — Runtime loading: ResourceManager/Provider/Handle architecture, loading flow, reference counting, OperationSystem scheduler. Includes mapping table to our B5-2 AssetHandle/AssetResolver design. **Note: our runtime may be refactored.**
- `yooasset-filesystem.md` — FileSystem abstraction: IFileSystem interface, 6 built-in implementations, cache system internals, download management, verification levels.

### How to Use These Documents
- Each document ends with a **Relevance to XLuaHotfix** section noting what to adopt, adapt, or skip
- Cross-reference with `requirements/refactor-2026/plan.md` for phase alignment
- YooAsset source is available at `E:\unity\project\YooAsset` for deeper investigation
