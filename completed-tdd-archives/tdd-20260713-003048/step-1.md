# Step 1 - Understand Intent

## Functional Requirements

### FR-1: Deterministic Hotfix Runtime State Machine

The hotfix startup must select between local activation, repair, update, recoverable fallback, and fatal startup failure from the remote/local package pointers, exact local package completeness, configured failure policies, and app Major compatibility. Successful activation must finalize and emit completion once; fatal failure must fault startup.

## Assumptions

- The approved runtime state-machine plan is the authoritative behavior contract.
- One scenario and one focused self-check file cover the single cohesive state machine requirement with multiple cases.
- Unity integration acceptance supplements the deterministic self-check rather than replacing it.
- The developer requested autonomous execution, so the TDD gates proceed without an additional confirmation between steps.
