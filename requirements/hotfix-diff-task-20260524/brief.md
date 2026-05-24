# Hotfix Diff Task Migration Brief

Goal: move AA hotfix diff scanning and Addressables Hotfix group migration into the build DAG while keeping current manual reset available.

Confirmed decisions:
- Continue hotfix build when the diff is empty.
- Remove `LegacyAddressableHotfixGroups`; migrate the behavior into Task-owned code.
- Keep manual reset for now.
- Add a read-only current-vs-HEAD diff scan method for CLI/batch use.
