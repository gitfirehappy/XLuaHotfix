# Draft: Hotfix 双管线统一与健壮性改进

Status: Promoted
Date: 2026-05-19
Promoted to: `requirements/plan/plan-hotfix-pipeline-unification-20260519.md`

## Promotion Note

This draft was promoted into an executable plan.
The executable plan keeps the confirmed scope, records conservative defaults for unresolved items, and leaves AA build UI / AA DAG alignment out of scope.

## Original Direction

Hotfix 运行时通过 `IHotfixPipeline` 接口抽象 AA/AB 两条管线，`HotfixManager` 执行 11 步热更流程。原 draft 覆盖：

- 代码重复分析
- 错误处理修复
- AA 管线完整支持方向记录
- AA/AB Manifest 输出格式对齐
- 日志边界收敛

## Promoted Decisions

- 不抽取 manifest helper。
- `CRC == 0` 不是 bug，但必须加 Warning。
- 下载失败与 CRC 失败统一进入重试流程。
- 下载使用 `.tmp`，校验通过后替换正式文件。
- AA/AB 默认都产出 JSON + Binary manifest。
- 日志最终摘要由外层 orchestrator 负责。
- AA build UI 和 AA DAG 对齐不进入本计划。

