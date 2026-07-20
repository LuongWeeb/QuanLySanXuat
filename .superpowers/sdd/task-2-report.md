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

---

## Review-fix follow-up

### Changes

- `CreateCycleCountOrderAsync` now excludes balances at `QcService.QuarantineLocationCode`, even when they belong to the requested warehouse.
- `RecordCountResultsAsync` now rejects any result whose item ID is not part of the specified order before mutating counts. It marks the order `InProgress` while any item is uncounted and `Completed` only after every item has a `CountedQty`.
- `ApproveAndAdjustStockAsync` now returns `false` unless the order status is `Completed`, preventing empty and partial approvals from changing stock.
- Added CycleCountTests coverage for quarantine exclusion, foreign item-ID rejection, partial approval rejection with unchanged stock/no transactions, and the Completed-to-approved lifecycle.

### TDD evidence

#### RED

Command:

```powershell
dotnet test WmsMes.Tests\WmsMes.Tests.csproj --filter FullyQualifiedName~CycleCountTests --no-restore
```

Result before the fix: `Failed: 4, Passed: 0, Total: 4`.

- The same-warehouse quarantine balance was incorrectly snapshotted.
- An out-of-order result ID was incorrectly accepted.
- A fully counted order remained `InProgress` instead of becoming `Completed`.
- A partial order was incorrectly approved.

#### GREEN (focused)

Command:

```powershell
dotnet test WmsMes.Tests\WmsMes.Tests.csproj --filter FullyQualifiedName~CycleCountTests --no-restore
```

Result: `Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4`.

### Full suite verification

Command:

```powershell
dotnet test WmsMes.sln --no-restore
```

Result: `Passed! - Failed: 0, Passed: 124, Skipped: 0, Total: 124`.

### Files changed

- `Services/CycleCountService.cs`
- `WmsMes.Tests/CycleCountTests.cs`
- `.superpowers/sdd/task-2-report.md`

### Self-review

- Approval now requires the single lifecycle state produced only by a complete set of count results.
- Foreign item IDs return `false` without changing the persisted order, matching the existing boolean service contract.
- `git diff --check` completed without whitespace errors.

---

## Final branch-review fix wave

### Commit

- `1e27333 fix: harden cycle count transactions`

### Changes and rationale

- `InventoryService.AdjustStockAsync` now uses one conditional relational `ExecuteUpdateAsync`: the tuple must match and `QtyAvailable + adjustmentQty >= 0`. Exactly one affected row is required before the audit transaction is inserted. This removes the stale tracked read/check/write window while preserving the tracked fallback for EF InMemory tests.
- Relational cycle-count approval atomically claims `Completed -> Approved` inside the approval transaction. Only the request affecting one row may adjust stock; rollback restores `Completed` if any adjustment fails.
- `RecordCountResultsAsync` validates the complete result batch before mutation. Negative quantities, duplicate item IDs, and IDs outside the order return `false` with no count/status changes.
- Notification ownership now follows the established workflow: caller-owned ambient relational transactions do not broadcast; service-owned operations notify only after commit; notification exceptions are logged and do not turn a durable approval into a reported failure.
- Added deterministic SQLite stale-context tests for atomic inventory adjustment and double approval, plus SQLite rollback and ambient-transaction notification tests.

### RED evidence

Command:

```powershell
dotnet test WmsMes.Tests\WmsMes.Tests.csproj --filter "FullyQualifiedName~CycleCountTests|FullyQualifiedName~InventoryServiceTests.AdjustStockAsync" --no-restore
```

Result before production changes: `Failed! - Failed: 6, Passed: 5, Skipped: 0, Total: 11`.

Failures reproduced:

- negative and duplicate batches returned `true`;
- stale relational stock adjustment returned `true` twice;
- stale relational cycle approval returned `true` twice;
- ambient transaction notified;
- post-commit notification exception escaped to the caller.

The SQLite rollback characterization already passed and remained green through the status-claim implementation.

### GREEN evidence

Focused command:

```powershell
dotnet test WmsMes.Tests\WmsMes.Tests.csproj --filter "FullyQualifiedName~CycleCountTests|FullyQualifiedName~InventoryServiceTests.AdjustStockAsync" --no-restore
```

Result: `Passed! - Failed: 0, Passed: 11, Skipped: 0, Total: 11`.

Full-suite command:

```powershell
dotnet test WmsMes.sln --no-restore
```

Result: `Passed! - Failed: 0, Passed: 131, Skipped: 0, Total: 131`.

`git diff --check` completed with no whitespace errors.

### Files changed

- `Services/InventoryService.cs`
- `Services/CycleCountService.cs`
- `WmsMes.Tests/InventoryServiceTests.cs`
- `WmsMes.Tests/CycleCountTests.cs`
- `.superpowers/sdd/task-2-report.md`

### Remaining concerns

- The existing migration concern remains unchanged; no migration was requested in this fix wave.
- SQLite tests deterministically use stale contexts rather than timing-dependent parallel tasks. They directly reproduce the lost-update/double-approval defects while avoiding SQLite lock flakiness; the production relational implementation is a single conditional SQL update suitable for SQL Server concurrency.
