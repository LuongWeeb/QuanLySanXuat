# Task 1 Report: FEFO/FIFO Picking Recommendation

## Scope delivered

Implemented only Task 1 from `task-1-brief.md`:

- Added `PickingStrategy` (`FEFO`, `FIFO`) and `PickingRecommendationDto`.
- Added `IInventoryService.GetPickingRecommendationsAsync(int, decimal, PickingStrategy)`.
- Implemented non-mutating recommendations from available stock, excluding `QcService.QuarantineLocationCode`.
- FEFO orders by expiry (missing expiry last) then manufacture date; FIFO orders by manufacture date then `StockBalance.Id`.
- Allocates `RecommendedQty` until `requiredQty` is fulfilled, while retaining actual `AvailableQty`.
- Added `GET api/inventory/picking-recommendations` returning `200 OK` with service results.
- Did not implement or modify any Cycle Counting / Task 2 behavior.

`Lot.ManufactureDate` is nullable in the existing entity while the required DTO property is not; the mapping uses `DateTime.MinValue` when no manufacture date exists.

## Files changed

- `DTOs/PickingRecommendationDto.cs` (new)
- `Services/IInventoryService.cs`
- `Services/InventoryService.cs`
- `Controllers/InventoryController.cs`
- `WmsMes.Tests/FifoFefoPickingTests.cs` (new)

## TDD evidence

### RED: service contract/algorithm tests

Command:

```powershell
dotnet test WmsMes.sln --filter "FullyQualifiedName~FifoFefoPickingTests"
```

Result: exit code `1`; compilation failed at each service test with expected `CS1061`: `InventoryService` did not contain `GetPickingRecommendationsAsync`.

### GREEN: service implementation

Command:

```powershell
dotnet test WmsMes.sln --filter "FullyQualifiedName~FifoFefoPickingTests"
```

Result: exit code `0`; `Passed: 3, Failed: 0`.

### RED: API endpoint test

Command:

```powershell
dotnet test WmsMes.sln --filter "FullyQualifiedName~FifoFefoPickingTests"
```

Result: exit code `1`; compilation failed with expected `CS1061`: `InventoryController` did not contain `GetPickingRecommendations`.

### GREEN: endpoint implementation and review coverage

Commands:

```powershell
dotnet test WmsMes.sln --filter "FullyQualifiedName~FifoFefoPickingTests"
dotnet test WmsMes.sln
```

Results:

- Focused post-endpoint run: exit code `0`; `Passed: 4, Failed: 0`.
- After adding review-requested tie-breaker/null-expiry coverage: focused run exit code `0`; `Passed: 6, Failed: 0`.
- Fresh full-suite run: exit code `0`; `Passed: 120, Failed: 0, Skipped: 0`.

The final full-suite command was run outside the filesystem sandbox after the sandboxed process was denied read access to the user NuGet config; the same command then restored successfully and ran all tests.

## Test coverage

- FEFO chooses earliest expiry and splits a quantity request across balances.
- FIFO chooses earliest manufacture date.
- FEFO uses manufacture date when expiry dates tie and places no-expiry lots last.
- FIFO uses `StockBalance.Id` when manufacture dates tie.
- Quarantine balances are excluded.
- Controller delegates exact inputs to `IInventoryService` and returns `Ok` with its result.

## Self-review

- Confirmed signature, DTO fields, route, FEFO/FIFO sort clauses, allocation, and quarantine exclusion against every Task 1 requirement.
- Confirmed the recommendation query uses `AsNoTracking`, so an API call cannot mutate inventory.
- Ran `git diff --check`: no whitespace errors.
- Independent review reported no Critical or Important issues. Its Minor test-coverage finding (tie-breakers and null expiry) was addressed by the final two tests.

## Concerns

None. The endpoint follows existing controller behavior by throwing a clear exception only if the optional constructor dependency is omitted; normal DI registration supplies `IInventoryService`.
