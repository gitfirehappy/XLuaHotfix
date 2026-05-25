# Build Repository Batch Review

> **Date**: 2026-05-24
> **Reviewer**: Codex
> **Scope**: Build Repository batch lifecycle cleanup, archived Plan 1/2/3 files, follow-up draft extraction, README/context alignment, and the shipped repository implementation under `Assets/FYAsset/Scripts/Build/Repository/`
> **Method**: Static review with targeted source inspection, requirement/document cross-check, and archive/index consistency audit

## Findings

### [High] Repository HEAD invalid-state handling collapses malformed and empty states into the same API result

**Files**
- `Assets/FYAsset/Scripts/Build/Repository/Editor/FileBuildRepository.cs:27`
- `Assets/FYAsset/Scripts/Build/Repository/Editor/FileBuildRepository.cs:173`
- `Assets/FYAsset/Scripts/Build/Repository/Editor/FileBuildRepository.cs:199`
- `Assets/FYAsset/Scripts/Build/Repository/Editor/RepositoryStatusPanel.cs:86`

**Problem**

`TryLoadHead()` logs distinct errors for unreadable `HEAD.json`, empty `HEAD.json`, and a `HEAD` that points to a missing object file, but the public `GetStatus()` / `GetHeadCommit()` surface only receives `null`. The UI then renders the same "no HEAD" path for both a genuinely empty repository and a malformed repository state.

**Impact**

A broken repository can look healthy-but-empty instead of explicitly broken. That hides data corruption and makes recovery harder, especially because `RepositoryStatusPanel` uses the same status path for user-facing display.

**Recommendation**

Return an explicit status/result discriminator for malformed HEAD states, or surface an error state in `RepositoryStatus`. Keep empty HEAD and corrupted HEAD separate.

### [High] `Push` still couples target payload to the editor build output path instead of the commit being pushed

**Files**
- `Assets/FYAsset/Scripts/Build/Repository/Editor/FileBuildRepository.cs:132`
- `Assets/FYAsset/Scripts/Build/Repository/Editor/LocalDirectoryPushTarget.cs:62`
- `Assets/FYAsset/Scripts/Build/Repository/Editor/LocalDirectoryPushTarget.cs:63`

**Problem**

`FileBuildRepository.Push()` always passes `BuildPathManager.PackageIndexPath` into the push payload, and `LocalDirectoryPushTarget` copies that path to the target root. That means the push operation depends on the current editor output directory layout, not only on the source commit being published.

**Impact**

If the repository is ever used outside the exact current build-output layout, or if push is invoked after the editor output has been cleaned or relocated, the payload becomes fragile. The repository already owns `toCommit`; the push payload should be derived as much as possible from the commit itself.

**Recommendation**

Keep the push payload self-contained by deriving the package pointer path from the target commit/package root or by validating the editor-side dependency explicitly before push.

### [Medium] Diff Preview temp directory cleanup is isolated correctly, but AB preview still relies on an environment-variable side channel

**Files**
- `Assets/FYAsset/Scripts/Build/Repository/Editor/RepositoryPreviewRunner.cs:45`
- `Assets/FYAsset/Scripts/Build/Repository/Editor/RepositoryPreviewRunner.cs:60`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskPrepareContext.cs:66`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskOrganizeOutput.cs:10`

**Problem**

The AB preview path writes `BUILD_REPOSITORY_PREVIEW_OUTPUT` before running the DAG, and `TaskPrepareContext` later consumes that environment variable as the output-root source. This works, but it creates a hidden coupling between repository preview orchestration and the pipeline's general context initialization.

**Impact**

The preview contract is easy to miss when changing build bootstrapping. A future refactor of `TaskPrepareContext` could silently break preview output routing without touching `RepositoryPreviewRunner`.

**Recommendation**

Prefer an explicit context key or request field for preview output over an environment-variable side channel. The existing cleanup logic is fine; the routing mechanism is the weaker part.

### [Medium] Follow-up extraction is directionally correct, but the new draft should keep repository scope tighter

**Files**
- `requirements/plan/drafts/archive/draft-build-repository-20260518.md`
- `requirements/plan/drafts/draft-build-repository-followup-20260524.md`

**Problem**

The old repository draft bundled the main shipped plans with residual issues. The new follow-up draft is the right split, but it still needs to stay narrow: only unresolved repository concerns belong there.

**Impact**

If the follow-up draft grows back into a second master draft, the archive structure becomes noisy again and the shipped batch loses clarity.

**Recommendation**

Keep the follow-up draft limited to unresolved AA Push / serialization / orphan-object / push-locking / derived published-state questions, and avoid reintroducing Plan 1/2/3 material.

## Non-Blocking Observations

- The archive move itself is consistent: executed Plan 1/2/3 files are no longer active, and the original repository draft now has a clear trace to the follow-up draft.
- README and `context/architecture/resource-build-and-release.md` are aligned with the shipped repository batch at a high level.
- `requirements/review/INDEX.md` still needs a real active-review entry if this review is kept active instead of archived.

## Conclusion

The repository batch is structurally complete and the archive split is sensible. The main residual technical risk is the lack of a first-class malformed-HEAD state, followed by the push payload's dependence on editor output layout and the env-var-based AB preview routing.
