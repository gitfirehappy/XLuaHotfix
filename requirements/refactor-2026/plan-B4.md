# Sub-Plan B4: Catalog Redirect Layer Replacement (High Risk, Independent Evaluation)

> **Risk**: High (hotfix core pipeline)
> **Dependencies**: After B1 + B2 completed and verified stable on device
> **Status**: Concept design stage, not in this round's approval scope

---

## Background

CatalogUpdater uses deep Addressables APIs.
This mechanism is the core of how hotfixes take effect — replacing it means reimplementing the entire hotfix distribution pipeline.

**Developer must understand the current mechanism to evaluate replacement risk**:

### Current Catalog Mechanism Workflow

```
Startup -> Addressables.InitializeAsync()
           |
           Load StreamingAssets built-in Catalog (package's asset index)
           |
Hotfix check -> Download remote Catalog file (HotfixManager -> NetworkDownloader)
           |
CatalogUpdater.LoadExternalCatalog()
           |
           Use Addressables.LoadContentCatalogAsync to load external Catalog
           |
           Use Addressables.RemoveResourceLocator to remove built-in old index
           |
           Addressables.ResourceManager.InternalIdTransformFunc
           redirects remote HTTP paths -> local hotfix directory paths
           |
           All subsequent asset loading automatically uses hotfix directory
```

**Replacing this mechanism is equivalent to building a custom 'asset index management + path redirection' system.**

---

## Custom Equivalent Design (Concept)

| Addressables Capability | Custom Equivalent |
|------------------------|-------------------|
| LoadContentCatalogAsync | Load local ABManifest JSON |
| ResourceLocators | ABResourceRegistry (Key -> BundlePath mapping table) |
| RemoveResourceLocator | ABResourceRegistry.SwitchToHotfixManifest() |
| InternalIdTransformFunc | ABBundleLoader internal path resolution (hotfix directory priority) |
| Addressables.InitializeAsync | ABPackageBackend.InitializeAsync |

---

## Key Design Decisions (Require Review)

1. **ABManifest format**: Reference Addressable Catalog JSON or custom format?
2. **Build-side sync**: How to generate ABManifest when building AB? (Currently HelperBuildDataExporter generates AddressableLabelsConfig; needs to also generate ABManifest)
3. **Incremental download**: NetworkDownloader currently uses VersionState bundle hash comparison; how to maintain this with custom system?
4. **Rollback mechanism**: How to fall back to Addressables if replacement fails?

---

## Recommendations

- B4 requires a dedicated design review before starting
- After B1 + B2 completion, run on device for a period to confirm stability
- B4 can be treated as an independent long-term iteration task, not coupled with B1-B3

---

## No Approval Checklist for This Phase

B4 is in concept design stage; evaluation of whether to execute happens after B1-B3 completion.