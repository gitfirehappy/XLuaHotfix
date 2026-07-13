# Step 4 - Implement to Make Tests Pass

## Implementations Completed

- FR-1: Deterministic Hotfix Runtime State Machine - `docs/scenario/hotfix-runtime-state-machine.md` - Implemented in `HotfixStateDecider`, `HotfixFlowBase`, the AA/AB hotfix backends, shared download policy, checked package-manager finalization, and ABManifest schema v4.

`dotnet run --project tests/scenario/HotfixRuntimeStateMachineTests.csproj` passed after the production decision API was implemented.
