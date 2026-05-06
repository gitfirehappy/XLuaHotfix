# Sub-Plan: 代码审查修复 (2026-05-06 Review Follow-up)

> **Source**: review-e4-editor-code-quality-20260506.md  
> **Status**: Completed  
> **Risk**: Low (纯重构+优化，不改行为)  
> **Completed**: 2026-05-06 — dotnet build 0 errors, 7/7 tasks, net -30 lines

---

## Objective

修复审查报告中 7 项 High + Medium 问题，涉及 E4 DependencyAnalyzer、TaskAnalyzeDependencies、CollectionScanner、DAGScheduler、CollectorTreeView 五个文件。

---

## Tasks

| # | Level | File | Task | Depends |
|---|-------|------|------|---------|
| F1 | 🔴 H1 | DependencyAnalyzer.cs | 循环检测 O(1) HashSet 优化 | — |
| F2 | 🔴 H2 | DependencyAnalyzer.cs | 空 catch 加 Debug.LogWarning | — |
| F3 | 🟡 M1 | DependencyAnalyzer.cs | AnalyzePackage 拆为 3 个方法 | F1,F2 (同文件一起改) |
| F4 | 🟡 M2 | TaskAnalyzeDependencies.cs | 简化警告收集重复逻辑 | — |
| F5 | 🟡 M3 | CollectorTreeView.cs | 删除空 OnGUI 覆盖 | — |
| F6 | 🟡 M4 | CollectionScanner.cs + RuleResolver.cs | if/typeof 链 → RuleResolver.GetRule<T>() | — |
| F7 | 🟡 M5 | DAGScheduler.cs | 提取 BuildAdjacencyGraph 去重 | — |

---

## Modified Files

| File | Change Summary | Risk |
|------|---------------|------|
| DependencyAnalyzer.cs | H1: 加 bfsGuidSet 并行 HashSet + O(1) 检测; H2: catch 块加日志; M1: 拆 BfsTraverse + ReportCycles + ApplySharePolicy | Low |
| TaskAnalyzeDependencies.cs | M2: 合并 warnings 收集逻辑 | Very Low |
| CollectorTreeView.cs | M5: 删除空 OnGUI 覆盖 | None |
| CollectionScanner.cs | M4: ResolveRuleSafe 改用泛型字典 | Low |
| RuleResolver.cs | M4: 新增 GetRule<T>() 泛型方法 | Low |
| DAGScheduler.cs | M7: 提取 BuildAdjacencyGraph() | Low |

---

## Invariants

1. `dotnet build XLuaHotfix.sln` passes with 0 errors
2. All existing behavior preserved — pure refactor/optimization
3. BFS cycle detection produces identical results with O(1) lookup
4. No new allocations in hot paths beyond the minimal HashSet

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-06 | Initial plan, 7 tasks derived from review report |
| 2026-05-06 | All 7 tasks completed — F1(bfsGuidSet), F2(catch log), F3(method split), F4(warning simplify), F5(TODO), F6(GetRule<T>), F7(BuildAdjacency) |
