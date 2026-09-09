# Process Pitfalls

Verified workflow, review, and repository-operation mistakes. Each entry keeps its original IP number and standard
fields.

## IP-45: Progress Consolidation Lost Detailed History

**Symptom:** The main `requirements/progress.txt` was replaced by a short summary while standalone progress logs still held detailed history.
**Root cause:** Consolidation was treated as summary replacement instead of detailed entry migration.
**Fix:** Restore the full main progress log, copy standalone progress entries into it with requirement ids, then remove standalone folders.
**Prevention:** Before deleting standalone requirement folders, merge every meaningful `progress.txt` entry into `requirements/progress.txt`; never replace detailed history with summary-only lines.

## IP-66: Misclassified the Push Stack During Repository Slimming Review

**Symptom:** During the AA/AB repository-slimming review, the push stack (`IPushTarget`, `LocalDirectoryPushTarget`, `CloudflarePagesPushTarget`, `PackagePublishTransaction`, `PushModels`) was proposed for wholesale deletion on the claim that it "mirrors repository objects for team baseline sharing." The developer confirmed deletion based on that claim; the error was only caught by AI self-check during execution, and the developer's own review let the wrong premise through.
**Root cause:** The push stack was judged by directory location (`Shared/Build/Repository/`) instead of actual data flow. In reality `Push()` publishes **built hotfix packages** to mirror roots (local directory, Cloudflare Pages) that runtime hotfix downloads from — it is the release half of the hotfix lifecycle, only borrowing `RepositoryCommit.PackageRootDir` as a version registry. Premature convergence on a tidy "delete the ops stack" narrative skipped the mandatory read of what `Push(PushPayload)` actually moves.
**Fix:** R6 scope corrected before execution: push stack kept and re-homed to `Shared/Build/Publish/` as the publish mechanism with `PushPayload` cut over from `RepositoryCommit` to `BuildBaseline` (+ `PackageRootDir`/`BackendMode` fields); Cloudflare target stays in Compat as the project's CDN channel glue; only the true repository kernel (objects history, facade, health/repair) is deleted.
**Prevention:** Before approving deletion of a subsystem, read the payload/data flow of its primary entry point — never infer function from folder placement. Deletion proposals must state what flows through the code, not just where it lives. Reviewer of a cleanup plan should ask "what does this actually move, and who consumes the moved thing?" before signing off; both author and reviewer share this miss.

## IP-67: `git add -A Assets` Sweeps Build Outputs Into Commits (Twice)

**Symptom:** During the AA/AB decoupling commits (P1 and R6), `git add -A Assets` staged the entire `Assets/StreamingAssets/Standalone/**` build outputs into the commit, despite an explicit project rule to keep them untracked. Caught in pre-commit audit the first time, but repeated weeks later in the same session family.
**Root cause:** Broad-scope staging (`git add -A Assets`) was used for speed instead of explicit path lists; the exclusion of `StreamingAssets/**` lived only in conversation memory, not in `.gitignore`, so nothing mechanical blocked the sweep.
**Fix:** Commits rebuilt with `git restore --staged Assets/StreamingAssets` before landing. Durable rule: never `git add -A` over `Assets/` in this repo — stage explicit paths, and audit `git status` grouped by change type before every commit.
**Prevention:** Add `Assets/StreamingAssets/Standalone/` and `Assets/StreamingAssets/BuildIndex.json*` to `.gitignore` so generated local build state cannot be swept even by blanket staging. (Proposed to developer; not yet applied.) Audit staged file-type counts (`awk '{print $1}' | sort | uniq -c`) as a pre-commit habit.

## IP-68: Cleanup Round Drifted From Grilled Decisions

**Symptom:** Approved FYAsset cleanup produced extra review files, all-English comments, HTML/docs still naming deleted types, and Shared editor panels writing AA/AB Settings, while S3 ExportBoundary failed.
**Root cause:** Sub-agents optimized for coverage and English XML instead of the grilled constraints (comment language, no extra reviews, Shared has no backend types, current-docs must match code).
**Fix:** Delete the extra 20260907 reviews; keep JSON missing-Version rejection; inject Apply URL from AA/AB windows; move AB PlayMode to ABConfigPanel; update current docs to BuildDiffEntry/BuildIndexData/TaskScanAAHotfixDiff.
**Prevention:** After a grilled cleanup, re-read the 12 decisions against working tree before claiming progress. Do not add review files unless asked. Shared source must compile against the ExportBoundary test, not just the solution.

## IP-69: Unity Test Command Reported Success Without Running Tests

**Symptom:** MCP auto-failed initialization after 30ms, and Unity batchmode exited 0 without a result XML when `-quit` was supplied. Both could be mistaken for a completed test run.
**Root cause:** MCP `init_timeout` is milliseconds, while `-runTests` owns the batchmode process lifetime and conflicts with an explicit `-quit` during script reload.
**Fix:** Use `init_timeout: 30000`; run Unity with `-batchmode -nographics -runTests` and no `-quit`; require the result XML before interpreting the process exit code.
**Prevention:** Record `testcasecount` and `total` from `<test-run>`. `result=Passed` with `total=0` proves discovery completed but provides no business test coverage.
