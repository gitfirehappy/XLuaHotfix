# Step 1 - Understand Intent

## Functional Requirements

### FR-1: Major baseline and durable active pointer

Replace the legacy BuildGUID marker with directional Major rules, verify same-package Bundle CRC, persist a changed local PackageIndex only after runtime initialization, and make file replacement genuinely atomic.

## Assumptions

- BuildGUID remains the unique Full-build baseline identity but never determines compatibility.
- Offline Full-package delivery remains out of scope.
- Remote Major mismatch always falls back to current-Major local content or the built-in baseline.
