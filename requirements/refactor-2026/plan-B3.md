# Sub-Plan B3: DialogueDataManager Independent Dual-Mode

> **Risk**: Low (independent module, does not affect core hotfix pipeline)
> **Dependencies**: Can execute after B1 completion (B2 not strictly required)
> **Status**: Completed (2026-03-18)

---

## Background (Developer Must-Read)

DialogueDataManager is an independent module of the dialogue system, designed to be 'copy-and-use':
other projects can copy just the dialogue system without depending on the full AAPackageManager.

Therefore, it is not recommended to force DialogueDataManager to load through AAPackageManager.
Instead, preserve the ability to call Addressables directly while providing an optional 'integrate with AAPackageManager' mode.

---

## Design Approach

Through compile switches (#if) or runtime configuration (DialogueLoaderMode), let DialogueDataManager
work in two modes:

**Mode A (default, preserves current behavior)**: Directly calls Addressables.LoadAssetAsync
- Suitable for: standalone dialogue system usage, no AAPackageManager needed

**Mode B (optional, integrated mode)**: Loads through AAPackageManager
- Suitable for: project has full AB management system, wants unified resource entry point

---

## Scope of Changes

| File | Change |
|------|--------|
| DialogueDataManager.cs | Add LoaderMode enum + switchable loading logic |

---

## Implementation Approach

```csharp
public static class DialogueDataManager
{
    /// <summary>
    /// Asset loading mode
    /// Standalone: uses Addressables directly (module independently usable)
    /// Integrated: through AAPackageManager (project unified entry point)
    /// </summary>
    public enum LoaderMode { Standalone, Integrated }

    /// <summary> Current loading mode, defaults to Standalone, can be switched at project init </summary>
    public static LoaderMode Mode = LoaderMode.Standalone;

    public static List<DialogueData> LoadDialogueData(string csvFileName)
    {
        ...
        // Choose loading path based on Mode
        if (Mode == LoaderMode.Standalone)
        {
            // Original logic: direct Addressables.LoadAssetAsync
        }
        else
        {
            // Integrated logic: AAPackageManager.Instance.LoadAssetSync<TextAsset>
        }
    }
}
```

---

## Preservation Requirements (Must Pass)

- [ ] Without setting Mode, behavior is identical to current (Standalone is default)
- [ ] DialogueDataManager can still be independently copied from the project
- [ ] LoadDialogueData(TextAsset) overload unchanged (directly passes resource, no loader needed)

---

## Acceptance Criteria

- [ ] Standalone mode: behavior identical to pre-refactoring
- [ ] Integrated mode: loads through AAPackageManager, resources returned correctly
- [ ] Switching Mode does not affect already-cached dialogue data

---

## No Approval Questions Needed

This phase's approach is clear; developer can execute directly after confirming direction:
- Preserve Standalone mode + add optional Integrated mode
- If there are other ideas, feel free to ask