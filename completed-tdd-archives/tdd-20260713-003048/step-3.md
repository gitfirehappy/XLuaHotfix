# Step 3 - Write Failing Test

## Failing Tests Created

- FR-1: Deterministic Hotfix Runtime State Machine - `docs/scenario/hotfix-runtime-state-machine.md` - `tests/scenario/test_hotfix_runtime_state_machine.cs`

The standalone test project references the intended pure production decision API and must fail until that API exists.

RED confirmed with `dotnet run --project tests/scenario/HotfixRuntimeStateMachineTests.csproj`: compiler error `CS2001` reported the missing production `HotfixStateDecider.cs`.
