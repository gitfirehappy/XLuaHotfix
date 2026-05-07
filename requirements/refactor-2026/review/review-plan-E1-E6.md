# Review Report: E1–E6 Execution Plans

> **Review date**: 2026-04-26
> **Scope**: All E-series sub-plans (E1-1/E1-2/E1-3/E1-4/E2/E4/E5/E5-1/E5-2/E6)
> **Review type**: Pre-execution readiness audit
> **Archival status**: 📦 ARCHIVED — All E1~E6 plans have since been executed/landed. This was a pre-execution gate review; findings were addressed in implementation commits.

---

## Plan Status Overview

| Plan | Status | Approval | Risk | Dependencies Satisfied? |
|------|--------|----------|------|------------------------|
| **E1-1** | ⚠️ Needs back-change | Approved | Low | — |
| **E1-2** | DONE | Approved | Low | E1-1 |
| **E1-3** | ⚠️ Needs plan update | Approved (pre-audit) | Low-Medium | E1-1 + E1-2 + E2 |
| **E1-4** | ⚠️ Needs plan update | Approved (pre-audit) | Low | E1-1 + E1-2 + E1-3 |
| **E2** | Approved | Approved | Low-Medium | E1-1 + E1-2 |
| **E4** | Draft | ❌ Not approved | Medium | E1-1 + E1-3 + E5 |
| **E5** | Draft (parent) | — (container) | High | — |
| **E5-1** | Draft | ❌ Not approved | High | E1-1 |
| **E5-2** | Draft | ❌ Not approved | Medium | E5-1 + E1-3 + E4 |
| **E6** | Draft | ❌ Not approved | Low | E5-1 + E5-2 + E4 + B6 |

---

## Critical Issues (P0 — Must Resolve Before Execution)

### P0-1: E1-1 IGroupRule Back-Change Not Executed

**Severity**: Critical
**Affected plans**: E1-1, E1-3, E1-4, E2

E1-1 was executed on 2026-04-25 with only IAddressRule/IPackRule/IFilterRule. The 2026-04-26 direction audit found that IGroupRule was silently dropped from the approved draft (which specified a three-rule model: FilterRule → GroupRule → PackRule). Five audit tasks (TA1–TA5) are defined but **not yet executed**:

| Task | Content |
|------|---------|
| TA1 | Create `IGroupRule.cs` interface + `GroupRuleContext` struct |
| TA2 | Add `GroupRuleName` field to `Collector` data class |
| TA3 | Add `GetGroupRule()` method to `RuleResolver` |
| TA4 | Add `GROUP_RULE_*` constants to `Constants.cs` |
| TA5 | Compilation verification |

**Impact**: E1-3's scan pipeline explicitly depends on IGroupRule for the GroupRule step (between Classify and Address). Until TA1-TA5 are executed, E1-3 cannot proceed.

**Recommendation**: Execute E1-1 audit tasks before any other E1 sub-plan work.

---

### P0-2: E4, E5-1, E5-2, E6 — All in Draft, Zero Approvals

**Severity**: Critical
**Affected plans**: E4, E5-1, E5-2, E6

Four of six remaining sub-plans are in Draft status with **no approval checklist items checked**. This blocks all Phase 5/6 downstream execution.

```
E5-1 (core engine) ──NOT APPROVED──→ E5-2 (backbone tasks) ──NOT APPROVED──→ blocked
E4 (dependency analysis) ──NOT APPROVED──→ E5-2 + E6 blocked
E6 (manifest generation) ──NOT APPROVED──→ blocked
```

**Impact**: The entire build pipeline (Phase 5–6) is gated on these approvals. Without E5-1, no IBuildTask contract exists for any task to implement against. Without E4, TaskBuildBundles (E5-2) can't get dependency graph data.

**Recommendation**: Prioritize approval in dependency order: E5-1 first (defines the contract all others implement), then E4 (provides context input for E5-2/E6), then E5-2, then E6.

---

## High-Severity Issues (P1 — Should Resolve Before Execution)

### P1-1: E1-3 and E1-4 Plan Documents Stale After GroupRule Audit

**Severity**: High
**Affected plans**: E1-3, E1-4

Both plans acknowledge the 2026-04-26 audit with `[审计修正]` markers, but their status lines still say "needs plan update." Specific gaps:

**E1-3 plan gaps**:
- The scan pipeline flow diagram and execution order now include GroupRule (step g), which is consistent — but the Task Breakdown (T6) description likely references the pre-audit flow
- Dependency list says "E1-1 (data model, enums, interfaces, **including IGroupRule**)" — correctly updated

**E1-4 plan gaps**:
- RuleDropdownHelper code snippet only shows `AddressRulePopup`/`PackRulePopup`/`FilterRulePopup` — missing `GroupRulePopup`
- Property Panel table correctly includes `GroupRuleName` row marked `[审计新增]`

**Recommendation**: Full line-by-line sync of E1-3 and E1-4 against the audit findings before execution. Clear the "needs plan update" status banners once synced.

---

### P1-2: ECollectorType.Implicit — Cross-Plan Enum Collision

**Severity**: High
**Affected plans**: E1-1, E4

E1-1 defines `ECollectorType` with 3 values: `Main = 0, Static = 1, Depend = 2`. E4 (D5) adds `Implicit = 3` — a new enum value that was NOT in E1-1's original scope.

- E1-1's plan states: "ECollectorType (Main/Static/Depend)" — no mention of Implicit
- E4's plan correctly notes this as an additive change to CollectorEnums.cs
- **Risk**: If E1-1 is updated (IGroupRule back-change) without awareness of E4's Implicit addition, enum numbering could conflict if someone independently adds enum values in between

**Recommendation**: Add a forward-reference note in E1-1's ECollectorType section mentioning that E4 will add `Implicit = 3`. Or, add the value now in E1-1's back-change batch to avoid future rework. The value `3` is reserved — adding it now costs nothing and prevents drift.

---

### P1-3: E4 SharePolicyConfig Depends on Commented-Out Placeholder in E1-1

**Severity**: High
**Affected plans**: E1-1, E4

E1-1's `CollectorPackage` class contains a commented-out placeholder:
```csharp
// public SharePolicyConfig SharePolicy;  // uncomment when E4 is implemented
```

E4 (Modified Files table) says: "Add `public SharePolicyConfig SharePolicy = new();` to `CollectorPackage` (uncomment placeholder)."

This is a clean handoff — but E4's SharePolicyConfig.cs lands in Runtime assembly (`Build/Collector/`), and E1-1's CollectorSetting.cs is also Runtime. The comment is clear but if the actual code deviates from the plan (e.g., different field name, different default), this becomes a coordination issue.

**Recommendation**: Confirm during E4 implementation that the field name and type signature match the placeholder comment exactly.

---

## Medium-Severity Issues (P2 — Should Address, Not Blocking)

### P2-1: E1-3 Task T6 Dependency Chain is Fragile

E1-3-T6 (per-Collector scan with full pipeline) depends on: T1, T2, T3, T4, T5, E1-2 done, **E2 done**. This is the most heavily-depended-upon single task across all plans. If any upstream is delayed, T6 is blocked.

No mitigation in the plan — T6 is a monolithic task combining 10 sub-steps (FilterRule → IgnorePatterns → Classify → GroupRule → Address → Tags → PackKey → BundleNameBuilder → Type → Assemble).

**Recommendation**: Consider splitting T6 into T6a (asset discovery + dedup + filter) and T6b (classification + rule execution + assembly). The granularity would match the complexity.

---

### P2-2: E5-1 DAGScheduler Same-Key Read-Write Pattern — Consistent but Undocumented Edge Cases

**Severity**: Medium

E5 parent plan declares "Same-key read-write: `CollectedAssets` by TaskAnalyzeDependencies — intentional augmentation pattern." E5-1's DAGScheduler confirms this is **not** treated as a Write-Write conflict.

However, the edge cases are not fully specified:
- What if two extension nodes both declare WriteKeys containing `CollectedAssets`?
- What if an extension node writes `CollectedAssets` BEFORE TaskAnalyzeDependencies runs?

E5-1 says "Write-Write conflict: two Tasks declare same WriteKey → error." But the same-key read-write exception for backbone nodes could silently mask extension node conflicts.

**Recommendation**: Document the rule explicitly: same-key read-write is **only** allowed for the specific backbone case (TaskCollectAssets write → TaskAnalyzeDependencies read+write). Extension node conflicts with this key should still be rejected.

---

### P2-3: E6 Assumes E4 BundleDependencyGraph Read is Optional

**Severity**: Medium

E6 TaskGenerateManifest spec step 4 says: `(optional) ctx.Get<BundleDependencyGraph>("BundleDependencyGraph")`. But E6 Invariant #4 states: "DependBundleIndices must be populated." If the BundleDependencyGraph is optional, how does Invariant #4 hold?

The two statements conflict:
- If DependBundleIndices depends on BundleDependencyGraph, the graph cannot be optional
- If the graph is optional, DependBundleIndices may be empty (contradicting the invariant)

**Recommendation**: Make BundleDependencyGraph a required ReadKey (`Require<T>` not `Get<T>`), or relax Invariant #4 to allow empty DependBundleIndices when no graph is provided.

---

### P2-4: E1-4 RuleDropdownHelper Missing IGroupRule Popup Method

**Severity**: Medium

E1-4's RuleDropdownHelper code snippet defines only `AddressRulePopup`, `PackRulePopup`, `FilterRulePopup`. The text mentions IGroupRule scanning in the description but the code block shows only 3 methods. If implemented as-written, the GroupRule dropdown won't appear in the Collector Property Panel.

**Recommendation**: Update the RuleDropdownHelper code block in E1-4 to include `GroupRulePopup(Rect rect, string currentValue)`.

---

## Low-Severity Observations (P3 — Nice to Have)

### P3-1: E1-1 Audit Task Tracking

The 5 audit tasks (TA1-TA5) are only listed inside plan-E1-1.md. There is no standalone execution tracker. As E1-1 was already "executed" once, the back-change is a delta — tracking it in-progress separately would reduce risk of forgotten items.

### P3-2: E5 Parent Plan "Status: Draft" is Ambiguous

E5 plan says "Status: Draft — 拆分为 E5-1 + E5-2." But E5 is a container/parent plan, not an executable plan. Its status doesn't matter — what matters is the approval status of E5-1 and E5-2. Consider marking E5 as "Container/Split" to avoid confusion.

### P3-3: E3 Cancellation is Clean

The E3 cancellation notes are thorough — all 11/12 items absorbed by E1-3, sole uncovered item (Dev/CI severity policy) deferred to E5. All references in E1-3, E4, E5 correctly use "E1-3" not "E3." No issues found.

### P3-4: E2 Proactive Contract Adoption is Well-Handled

E2's change log notes that E1-1/E1-2 proactively adopted the E2 contract (GetPackKey method name, Labels field, collectDirName-only return). This reduced E2's scope from 9 tasks to 6 and modified files from 5 to 1. This is good forward-planning and should be noted as a pattern for other cross-plan dependencies.

---

## Dependency Graph (Current State)

```
                    ┌──────────────────────┐
                    │     E1-1 ⚠️           │
                    │  (needs IGroupRule    │
                    │   back-change)        │
                    └──────┬───────────────┘
                           │
          ┌────────────────┼────────────────┐
          ▼                ▼                 ▼
   ┌──────────────┐ ┌──────────────┐ ┌──────────────┐
   │  E1-2 ✅     │ │  E5-1 ❌     │ │  E5 ❌       │
   │  (DONE)      │ │  (Draft,     │ │  (parent)    │
   │              │ │  not approved)│ │              │
   └──────┬───────┘ └──────┬───────┘ └──────────────┘
          │                │
          ▼                │
   ┌──────────────┐        │
   │  E2 ✅       │        │
   │  (Approved)  │        │
   └──────┬───────┘        │
          │                │
          ▼                ▼
   ┌──────────────┐ ┌──────────────┐
   │  E1-3 ⚠️     │ │  E5-2 ❌     │
   │  (Approved,  │ │  (Draft,     │
   │  needs update)│ │  not approved)│
   └──────┬───────┘ └──────▲───────┘
          │                │
          ▼                │
   ┌──────────────┐        │
   │  E4 ❌       │────────┘
   │  (Draft,     │
   │  not approved)│────────┐
   └──────────────┘        │
          │                │
          ▼                ▼
   ┌──────────────┐ ┌──────────────┐
   │  E1-4 ⚠️     │ │  E6 ❌       │
   │  (Approved,  │ │  (Draft,     │
   │  needs update)│ │  not approved)│
   └──────────────┘ └──────────────┘

   ✅ = Ready to execute     ⚠️ = Plan needs update before execution
   ❌ = Not approved (blocked)     — = Container/not applicable
```

---

## Recommended Execution Order

After P0 and P1 issues are resolved:

1. **E1-1 audit back-change** (TA1-TA5) — unblock E1-3, E1-4
2. **E5-1 approval + execution** — define the IBuildTask contract all tasks implement against (can parallel with step 1, only depends on E1-1 for Constants)
3. **E1-3 plan sync + execution** — scan engine (depends on step 1 + E1-2 + E2)
4. **E4 approval** (requires developer sign-off on all 12 checklist items)
5. **E5-2 approval + execution** — backbone tasks (depends on step 2 + 3 + 4)
6. **E1-4 plan sync + execution** — Editor UI (depends on steps 1 + 3)
7. **E6 approval + execution** — manifest generation (depends on steps 2 + 4 + 5)

---

## Summary

| Severity | Count | Key Items |
|----------|-------|-----------|
| P0 Critical | 2 | IGroupRule back-change not executed; E4/E5-1/E5-2/E6 all Draft |
| P1 High | 3 | E1-3/E1-4 stale after audit; Implicit enum collision; SharePolicy placeholder |
| P2 Medium | 4 | E1-3-T6 monolithic; DAGScheduler edge case; E6 optional graph conflict; RuleDropdownHelper missing popup |
| P3 Low | 4 | Audit task tracking; E5 status ambiguity; E3 cancellation clean; E2 proactive adoption good |
