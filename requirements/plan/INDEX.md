# Plan Queue

Shared plan files live here while they are active or awaiting execution. `requirements/plan.md` is the authoritative
current status table. Plan bodies contain their own specifications, decisions, tasks, and verification evidence.

## Current Queue

- `plan-fyasset-windows-ab-remediation-20260911.md` - Implemented / awaiting sign-off（执行已批准 2026-09-11）：T0–T11 全部落地；纯 .NET 全矩阵绿、Windows Unity/Player 四项 exit 0、docs 对齐、F01–F16 逐条处置（见 disposition）。遗留：F08 Android、AA 完整矩阵、生成物 HTML 重跑、T8 流程探针入库。
- `plan-fyasset-ab-publish-target-cleanup-20260912.md` - Draft / implementation not approved：汇总 2026-09-12 grill 的 34 条收敛决策；AB-only Target/URL ownership、Summary-driven Publish、采集配置预转换、测试与文档整理。执行前需单独批准。

## Drafts

- `plan-playmode-draft.md` - PlayMode editor verification draft.
- `draft-legacy-plan-review-followups-20260714.md` - legacy review follow-up draft.
- `draft-fyasset-architecture-review-20260707.md` - architecture review draft.
- `draft-debug-panel-20260512.md` - debug panel draft.

## Archive

- `archive/plan-fyasset-resource-pipeline-realignment-20260909.md` - Superseded / acceptance failed 2026-09-11；T0–T9 未通过正式 review 与 Unity 验收，由 Windows AB remediation 计划接管。
- `archive/plan-fyasset-editor-tools-modeling-20260907.md` - Executed / verified / developer signed off 2026-09-09；后续资源管线纠偏由当前计划接续。
- `archive/plan-ab-delivery-ownership-20260906.md` - Executed / Verified；历史验收边界已转入后续计划与审查记录。
- `archive/plan-fyasset-ab-structure-comment-cleanup-20260906.md` - Executed / archived 2026-09-07；后续统一工作由 FYAsset editor-tools-modeling 计划接续。

Executed, signed-off, superseded, cancelled, and deprecated plans remain in `archive/`. Do not duplicate their content
here. Use the file name and the plan body when historical detail is needed.
