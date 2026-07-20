# Task 2 — Cycle Counting Engine report

## Status

Implemented the cycle-counting domain model, persistence mappings, service workflow, inventory-adjustment API, DI registration, and two integration-style unit tests. No controller or UI was added.

## Changes

- Added `CycleCountOrder` and `CycleCountItem`, including the required `VarianceQty` calculation.
- Added `CycleCountOrders`/`CycleCountItems` `DbSet`s and restrictive FK mappings for warehouse, product, location, and lot; order-to-item deletion cascades.
- Added `ICycleCountService` and `CycleCountService`:
  - `CreateCycleCountOrderAsync` snapshots each `StockBalance` whose location's zone belongs to the target warehouse.
  - `RecordCountResultsAsync` records supplied item quantities and moves the order to `InProgress`.
  - `ApproveAndAdjustStockAsync` adjusts only non-zero counted variances, marks the order `Approved`, records approver/completion time, and emits one post-commit stock-change notification.
- Added `CountResultDto` (`CycleCountItemId`, `CountedQty`) as the input contract required by `RecordCountResultsAsync`.
- Extended inventory with `Task<bool> AdjustStockAsync(int productId, int lotId, int locationId, decimal adjustmentQty, string userId, string referenceNo)`.
  - This is the smallest type-safe tuple needed to locate a balance and preserve the existing `StockTransaction` audit pattern.
  - It rejects a missing balance or an adjustment that would make available stock negative; otherwise it updates `QtyAvailable`, writes an `Adjust` transaction, saves, and notifies when not in an ambient transaction.

## Files

- Added: `Domain/Entities/CycleCountOrder.cs`, `Domain/Entities/CycleCountItem.cs`, `DTOs/CountResultDto.cs`
- Added: `Services/ICycleCountService.cs`, `Services/CycleCountService.cs`
- Modified: `Data/ApplicationDbContext.cs`, `Program.cs`, `Services/IInventoryService.cs`, `Services/InventoryService.cs`
- Added tests: `WmsMes.Tests/CycleCountTests.cs`

## TDD evidence

### RED

Command:

```powershell
dotnet test WmsMes.Tests\WmsMes.Tests.csproj --filter FullyQualifiedName~CycleCountTests --no-restore
```

Expected missing-feature compilation failures occurred before production code: `CycleCountService`, `CountResultDto`, and `ApplicationDbContext.CycleCountOrders` were not found (CS0246/CS1061). Exit code was 1.

### GREEN (focused)

Command:

```powershell
dotnet test WmsMes.Tests\WmsMes.Tests.csproj --filter FullyQualifiedName~CycleCountTests --no-restore
```

Result: `Passed! - Failed: 0, Passed: 2, Skipped: 0, Total: 2`.

The tests verify system-quantity snapshotting (including exclusion of another warehouse) and approval-driven adjustment to `QtyAvailable` plus an audited `StockTransaction` with `Type=Adjust`, variance, user, and cycle-count reference.

## Full suite verification

Command:

```powershell
dotnet test WmsMes.sln --no-restore
```

Result: `Passed! - Failed: 0, Passed: 122, Skipped: 0, Total: 122`.

## Self-review

- Re-read Task 2 brief and the approved contract decision: used the sole approval API name `ApproveAndAdjustStockAsync`; no alias was added.
- Verified `AdjustStockAsync` follows the existing inventory transaction/audit/notification conventions and respects ambient relational transactions so a cycle-count approval commits atomically.
- Verified the feature remains service/domain-only; no unrequested controller or UI changes.
- Ran `git diff --check`: no whitespace errors.

## Concerns

- The brief only requested DbContext changes and did not list a migration, so no EF migration was generated. A deployable SQL schema update will need a separately authorized migration.
