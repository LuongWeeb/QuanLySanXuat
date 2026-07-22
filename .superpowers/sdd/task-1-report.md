# Task 1 Report: Filter Worker Station Steps by Work-Order Status

## Implementation

Updated `WorkerController.Index` so it returns only unfinished `WorkOrderStep` records whose associated `WorkOrder` is `Approved` or `InProgress`. The existing includes and due-date/code/step-number ordering are unchanged. `StartStepAsync` was not modified.

## Files changed

- `Controllers/WorkerController.cs`
- `WmsMes.Tests/WorkerControllerTests.cs`
- `.superpowers/sdd/task-1-report.md`

## TDD evidence

### RED

Command:

```powershell
dotnet test WmsMes.Tests\WmsMes.Tests.csproj --filter FullyQualifiedName~WorkerControllerTests.Index_ReturnsOnlyUnfinishedStepsForApprovedOrInProgressWorkOrders --no-restore
```

Result: failed (0 passed, 1 failed).

The new test expected only `WO-APPROVED` and `WO-IN-PROGRESS`. Before the change, the controller returned `WO-COMPLETED`, `WO-DRAFT`, and `WO-PENDING` as well, proving that the missing work-order-status predicate was the cause.

### GREEN

After adding the required predicate, the same focused command passed: 1 passed, 0 failed.

## Full-suite evidence

Command:

```powershell
dotnet test WmsMes.sln --no-restore
```

Result: 186 passed, 0 failed, 0 skipped.

The suite emitted existing JWT options-validation log entries from host-startup test scenarios, but the command exited successfully and every test passed.

## Self-review

- The predicate uses the exact required statuses: `Approved` and `InProgress`.
- Completed steps remain excluded.
- The null guard for `WorkOrder` is included exactly as required.
- Existing `Include`, `ThenInclude`, ordering, `AsNoTracking`, and start-step behavior are unchanged.
- The regression test covers eligible statuses, ineligible work-order statuses, and a completed step.
- `git diff --check` completed with no whitespace errors.

## Concerns

None.
