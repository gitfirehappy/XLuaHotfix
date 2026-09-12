# Review Queue

Review reports live here. Each report is the source for its findings, evidence, and disposition.

## Report Header

Every report starts with:

```markdown
# <Title>

> **Date**: YYYY-MM-DD
> **Reviewer**: <name or tool>
> **Scope**: <what was reviewed>
> **Method**: <static analysis, diff review, perf profiling, etc.>
```

## Active Reviews

- `review-fyasset-ab-publish-target-cleanup-20260912.md` - 本轮 AB Publish Target、Summary Source、短 Address 兼容性、hotfix_flow 场景测试重命名与验证结果；实施完成，等待开发者审查。
- `review-fyasset-windows-ab-remediation-execution-audit-20260911.md` - 签收前执行效果审查：新鲜门禁全绿、无决策偏差；已清理 staged 索引残留与仓库外备份；遗留项见报告（HTML 模型页刷新、SelfCheck 去留、T8 探针入库、EOL 排除、地址对齐确认）。
- `review-fyasset-resource-pipeline-realignment-20260911.md` - findings F01–F16 dispositioned by `disposition-fyasset-windows-ab-remediation-20260911.md` (F01–F07/F09–F16 closed with fresh evidence; F08 Android deferred by plan; F14 markdown closed, generated HTML pending regeneration; residual items listed in the disposition).
- `review-xluaframework-fyasset-20260905.md` - open; current-tree runtime, build, editor, export, test, and decision
  audit.

## Archive

Completed or superseded reviews remain in `archive/`. The index records only file location, status, and short scope;
detailed findings stay in the report.
