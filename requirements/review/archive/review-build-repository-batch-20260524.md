# Build Repository Batch Review

> **Date**: 2026-05-24
> **Reviewer**: Codex
> **Scope**: Build Repository batch lifecycle cleanup, archived Plan 1/2/3 files, follow-up draft extraction, README/context alignment, and the shipped repository implementation under `Assets/FYAsset/Scripts/Build/Repository/`
> **Method**: Static review with targeted source inspection, requirement/document cross-check, and archive/index consistency audit

## Remediation Status

Updated: 2026-06-02

Archival status: all direct findings are fixed or explicitly deferred to `requirements/plan/drafts/draft-build-repository-followup-20260524.md`; this review can be archived.

| Finding | Status | Resolution |
|---|---|---|
| Repository HEAD invalid-state handling collapses malformed and empty states | Fixed by `plan-review-hardening-20260602` | HEAD-dependent calls now throw `RepositoryHeadException` for missing or malformed HEAD instead of treating corrupted state as empty; status still exposes `HasHeadError` for UI display. |
| `Push` couples payload to editor build output path | Fixed by `plan-review-hardening-20260602` | Push remains simple publication: the local target replaces the built package directory as a whole and does not reinterpret package-internal `PackageIndex.json`; repository push history records the changed artifact count after success. |
| Diff Preview temp directory cleanup / env-var side channel | Previously fixed | Preview output is passed through `BuildContextKeys.RepositoryPreviewOutput`; no action in this plan. |
| Follow-up draft scope should stay tight | Deferred to follow-up draft | Residual repository capabilities stay limited to AA Push, serialization, orphan-object cleanup, concurrent push coordination, and optional published-state view. Completed Plan 1/2/3 scope is not reintroduced. |

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

**Status**: Previously fixed.

`RepositoryPreviewRunner` now passes preview output through `BuildContextKeys.RepositoryPreviewOutput`, and `TaskPrepareContext` reads that explicit context key. The original environment-variable side channel is no longer active.

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

The repository batch is structurally complete and the archive split is sensible. Direct review findings have been fixed, while broader repository follow-up capabilities remain intentionally deferred in the follow-up draft.
