# comment-debug-coverage-20260524 Brief

Goal: improve comments and Debug/log messages for the recent build repository, hotfix diff DAG, PackageIndex task, and backend execution changes.

Scope:
- Code comments should use Chinese with English technical terms where useful.
- Debug/log messages should be readable for Unity Console triage.
- Add coverage where execution boundaries, skip paths, preview paths, and failure causes are currently unclear.
- Do not change runtime/build behavior.

Background:
- Recent changes moved AA/AB diff and PackageIndex writing into DAG tasks.
- Repository preview now runs through DAG stop-after flow.
- Some comments/logs still use dry English, unclear failure messages, or no diagnostic message for important skip paths.
