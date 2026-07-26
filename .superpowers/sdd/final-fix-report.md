# Final whole-branch review fix report

## Scope and starting point

- Worktree: `D:\Quản lý sản xuất\.worktrees\stock-ledger`
- Branch: `feature/stock-ledger`
- Reviewed/fix-wave base: `23a0d2481ba94b3b98c9ea77bd82122275ca420b`
- Requirements: `.superpowers/sdd/final-fix-brief.md`
- Pre-existing unrelated modification preserved and excluded from this change:
  `.superpowers/sdd/task-1-report.md`
- Baseline command:
  `dotnet test WmsMes.sln --no-restore`
- Baseline result: 395 passed, 0 failed, 0 skipped.

## Requirement mapping

### 1. Receipt and issue completion are concurrency-idempotent

- `InventoryService` now begins an owned relational transaction, or creates a
  unique savepoint when a transaction is already ambient.
- Before loading completion targets or mutating stock, it conditionally updates
  only the requested document from `Draft` to `Completed`.
- An affected-row count other than one returns `false` without stock mutation.
- Completion loads the claimed document with `AsNoTracking`, so a stale tracked
  Draft entity cannot bypass the database claim.
- Successful completion synchronizes a previously tracked document status after
  commit/savepoint release.
- Added stale, two-context SQLite contention tests for the same receipt and the
  same issue. Each asserts exactly one `true`, one `false`, one ledger entry, and
  one stock mutation.

### 2. Completion failures restore exact EF tracker state

- Receipt and issue completion reject dirty tracked document targets before
  beginning.
- Both capture the approved tracker snapshot shape used by hardened cancellation:
  entity reference, state, current/original values, and per-property modified
  and temporary flags, including CLR values for temporary keys.
- Failure rolls back the owned transaction or ambient savepoint, detaches all
  operation-created entities, and restores every pre-existing tracked entry.
- Receipt regression forces a ledger insert failure after operation-created lots
  and pending balances/transactions exist. It verifies unrelated Modified,
  Added-with-temporary-key, and Deleted entries are restored exactly; a later
  `SaveChangesAsync` persists only those caller changes.
- Issue regression fails after an earlier balance CAS/ledger addition. A later
  `SaveChangesAsync` likewise preserves only the unrelated caller edit.

### 3. Every requested stock producer writes complete ledger fields

- Stocktake approval:
  - `QtyAfter` is the counted/post-release balance.
  - `ValuationRate` is the current stocktake lot price.
- Manual adjustment:
  - relational updates read the exact persisted post-CAS quantity;
  - in-memory updates use the exact updated tracked quantity;
  - `ValuationRate` is selected from the current lot, with missing-lot failure.
- Manufactured receipt:
  - `QtyAfter` is the finished tuple's exact `QtyAvailable` (`0` while the
    manufactured quantity remains on hold);
  - `ValuationRate` is the current finished-lot price (no fallback).
- Backflush:
  - material reservations include their lot;
  - insufficient/missing reserved balance fails before it could become negative;
  - `QtyAfter` is the material tuple's exact unchanged `QtyAvailable`, even
    though the movement consumes the reserved bucket;
  - `ValuationRate` is the selected input-lot price.
- QC transfer:
  - quarantine hold is incremented per source;
  - every source movement writes a negative source row and a positive quarantine
    row with one reference, equal magnitude, and the inspected lot's price;
  - each row records its own tuple's exact `QtyAvailable`, not total on-hand.
- Focused tests cover all five requested producer paths.

### 4. Historical ledger migration fails before schema mutation

- `AddStockLedgerFields.Up` now emits an `IF EXISTS` / `THROW` guard as its first
  operation.
- The guard aborts when any historical `StockTransactions` row exists.
- No historical `QtyAfter` or `ValuationRate` is inferred, backfilled, or assigned
  zero.
- A migration-operation boundary test asserts the first operation is the SQL
  guard and every following operation is an `AddColumnOperation`.
- Generated SQL was inspected and orders the guard before all three
  `ALTER TABLE` statements.

### 5. Cursor pairs and empty-page navigation

- `Transactions` returns `400 Bad Request` for:
  - partial cursor pairs;
  - supplied-but-empty or whitespace-only cursor values;
  - model-binding failures/malformed dates;
  - non-positive cursor IDs.
- The Razor view renders its empty state independently from pagination, so a
  cursor page with no rows still exposes `Mới nhất`.
- Runtime fixture now seeds 51 ledger rows.
- HTTP/Razor coverage follows the rendered `Cũ hơn` link, verifies the older row,
  follows the rendered `Mới nhất` link, and verifies the newest page again.
- Separate runtime coverage verifies the empty cursor escape link and all
  malformed/partial cursor cases.

### 6. Issue valuation is fail-fast

- Goods issue completion now throws if the locked valuation lot disappears
  instead of using `?? 0m`.
- Issue cancellation also validates the resolved valuation lot before restoring
  stock and has no valuation fallback.
- Relational race interceptors delete the lot under deferred FK checking; both
  tests prove the operation throws the intended domain error and rolls back
  document, balance, and ledger state.
- A source scan shows no remaining service valuation `?? 0m`.

### 7. Previously approved behavior remains covered

- Existing cancellation CAS claims, savepoints, locks, stable natural-key order,
  dirty-target guards, cancellation ledger semantics, and transaction boundaries
  remain unchanged except for the explicit missing issue valuation rejection.
- Existing controller authorization, antiforgery, serializable create actions,
  pagination index, and accessible ledger markup tests remain green.
- Full solution verification is green.

## TDD evidence

### RED: completion contention, tracker restoration, adjustment/stocktake fields,
and issue valuation

Command:

```text
dotnet test WmsMes.Tests\WmsMes.Tests.csproj --no-restore --filter
"FullyQualifiedName~InventoryServiceTests.CompleteGoodsReceiptAsync_WhenSameDraftIsCompletedConcurrently|
 FullyQualifiedName~InventoryServiceTests.CompleteGoodsIssueAsync_WhenSameDraftIsCompletedConcurrently|
 FullyQualifiedName~InventoryServiceTests.CompleteGoodsReceiptAsync_WhenPostingFails_RestoresExactTrackerStateBeforeLaterSave|
 FullyQualifiedName~InventoryServiceTests.CompleteGoodsIssueAsync_WhenPostingFails_RestoresExactTrackerStateBeforeLaterSave|
 FullyQualifiedName~InventoryServiceTests.CompleteGoodsIssueAsync_WhenLockedValuationLotDisappears_FailsInsteadOfWritingZero|
 FullyQualifiedName~InventoryServiceTests.AdjustStockAsync_WithStaleRelationalContexts|
 FullyQualifiedName~InventoryServiceTests.ApproveStocktakeAsync_ReleasesHold"
```

Observed before implementation: 7 failed, 0 passed.

- Both stale contexts returned `true` for receipt and issue.
- Receipt/issue operation-created tracker entries survived rollback.
- adjustment and stocktake ledger values were zero;
- disappearing issue valuation reached database commit/FK failure instead of the
  intended fail-fast exception.

### GREEN: same completion/tracker/valuation set

Same focused command after implementation: 7 passed, 0 failed.

### RED: producer, migration, cursor, and Razor boundaries

Focused command covered `MesCoreServiceTests`, `QcAndReportingTests`,
`StockLedgerMigrationTests`, `InventoryViewTests`, and
`InventoryCancellationRuntimeTests`.

Observed before implementation: 8 failed, 1 passed.

- manufactured receipt, backflush, and QC transfer ledger fields were zero;
- first migration operation was `AddColumnOperation`;
- malformed/partial cursor requests returned 200;
- an empty cursor page hid `Mới nhất`;
- the already-rendered 51-row non-empty `Cũ hơn` to `Mới nhất` round trip passed.

GREEN after implementation: 10 passed, 0 failed.

### RED/GREEN: remaining cancellation valuation fallback

Command:

```text
dotnet test WmsMes.Tests\WmsMes.Tests.csproj --no-restore --filter
"FullyQualifiedName~InventoryServiceTests.CancelGoodsIssueAsync_WhenValuationLotDisappears_FailsInsteadOfWritingZero"
```

- RED: database commit failed with a deferred FK violation, proving the zero
  fallback let the cancellation continue.
- GREEN: 1 passed, 0 failed after explicit missing-valuation rejection.

### Independent review regressions

The final independent review found two important edge cases and no critical
findings:

- receipt rollback coverage did not yet prove exact restoration of unrelated
  Added/Deleted entries or temporary-key metadata;
- supplied empty cursor values bind to nullable parameters without necessarily
  making `ModelState` invalid.

Before the follow-up fixes, the combined review regression set reported
4 failed and 5 passed: three empty/whitespace cursor forms returned 200 and the
Added entity's accepted temporary key was not restored exactly.

After the fixes, the same set passed 9 of 9. The controller now checks both raw
query-key presence and bound values, while tracker restoration reattaches
accepted Added entries and restores caller CLR temporary-key values and
`IsTemporary` flags after rollback.

### Affected suites

- Inventory service suite: 71 passed, 0 failed.
- Combined inventory/controller/view/runtime, MES, QC, and migration suites:
  169 passed, 0 failed.

## Migration reconciliation and boundaries

- Freshly built migration history command:
  `dotnet ef migrations list --no-build`
- Development database history already contains:
  - `20260726065752_AddStockLedgerFields`
  - `20260726094346_AddStockLedgerPagingIndex`
- No migration-history row was removed or rewritten.
- No development data was deleted or assigned a fabricated valuation.
- Because the development database already records the migration as applied,
  the new guard governs future/fresh deployments; it is intentionally not a
  retroactive backfill mechanism.
- Generated boundary command:

```text
dotnet ef migrations script
20260726065735_AddCycleCountSchema
20260726065752_AddStockLedgerFields
--no-build --no-transactions
```

- Generated order:
  1. acquire `TABLOCKX, HOLDLOCK` on `StockTransactions` and fail when history
     exists
  2. add `IsCancelled`
  3. add `QtyAfter`
  4. add `ValuationRate`
  5. write migration history
- `AddCycleCountSchema` remains cycle-count-only.
- `AddStockLedgerPagingIndex` remains index-only.
- `ApplicationDbContextModelSnapshot` required no edit.
- `dotnet ef migrations has-pending-model-changes --no-build`:
  `No changes have been made to the model since the last migration.`

## Full verification

### Build

```text
dotnet build WmsMes.sln --no-restore
Build succeeded.
0 Warning(s)
0 Error(s)
```

### Full tests

```text
dotnet test WmsMes.sln --no-build --no-restore
Passed: 413
Failed: 0
Skipped: 0
```

The expected negative JWT startup tests still write host validation errors to
the test log; their assertions pass.

### EF/model and source hygiene

- `dotnet ef migrations has-pending-model-changes --no-build`: no changes.
- `git diff --check`: exit 0.
- Repository-wide stock-transaction producer scan: every producer explicitly
  sets `QtyAfter` and `ValuationRate`.
- Service valuation fallback scan: no `ValuationRate` zero-coalescing remains.

## Files changed

- `Controllers/InventoryController.cs`
- `Data/Migrations/20260726065752_AddStockLedgerFields.cs`
- `Data/DbSeeder.cs`
- `Services/InventoryService.cs`
- `Services/QcService.cs`
- `Services/WorkOrderService.cs`
- `Views/Inventory/Transactions.cshtml`
- `WmsMes.Tests/InventoryCancellationRuntimeTests.cs`
- `WmsMes.Tests/InventoryServiceTests.cs`
- `WmsMes.Tests/InventoryViewTests.cs`
- `WmsMes.Tests/MesCoreServiceTests.cs`
- `WmsMes.Tests/QcAndReportingTests.cs`
- `WmsMes.Tests/DbSeederTests.cs`
- `WmsMes.Tests/StockLedgerMigrationTests.cs`
- `docs/superpowers/plans/2026-07-26-stock-ledger-invariant-amendment.md`
- `.superpowers/sdd/final-fix-report.md`

## Self-review and concerns

- The independent review completed with no critical findings. Both important
  findings were converted to failing regressions, fixed, and verified 9 of 9.
- The fail-fast migration uses SQL Server `THROW`, matching the configured
  production provider and generated SQL Server migration assembly.
- The already-applied local development migration is left intact. Retrofitting
  any local historical rows would require authoritative valuation history that
  this repository does not have; fabricating it or silently using zero would
  violate the decision in the brief.
- Manufactured receipt valuation intentionally reads `finishedLot.UnitPrice`.
  That current price can legitimately be zero before QC costing, but it is an
  explicit current-lot value, not a fallback or invented historical rate.
- QC rejection now uses paired signed transfer rows. This doubles the number of
  QC ledger rows by design while keeping the physical transfer document count
  unchanged.
- EF commands continue to report pre-existing precision warnings for
  `CycleCountItem.SystemQty` and `CountedQty`. They are unrelated to this
  stock-ledger fix wave and the required build itself emits zero warnings.

## Final confirmed design amendment

### Global `QtyAfter` invariant

The confirmed amendment establishes one meaning for every stock ledger row:
`QtyAfter` is the exact `StockBalance.QtyAvailable` for that row's
product/lot/location tuple immediately after the movement.

The repository-wide producer audit covers:

1. goods receipt completion;
2. goods issue completion;
3. goods receipt cancellation;
4. goods issue cancellation;
5. stocktake approval;
6. manual adjustment;
7. manufactured receipt;
8. backflush;
9. QC transfer source and destination rows.
10. comprehensive sample-data receipt, issue, manufactured receipt, and
    backflush rows.

The six `InventoryService` producer tests now retain non-zero reserved/on-hold
buckets and compare ledger `QtyAfter` directly to the persisted tuple's
`QtyAvailable`. The work-order test does the same for a manufactured balance
whose quantity is entirely on hold and for a material balance with non-zero
available, reserved, and held quantities. The QC test covers two sources and an
existing quarantine tuple, all with non-zero secondary buckets, and checks every
ledger row against the persisted balance dictionary. Seeder reconciliation
coverage checks every generated sample ledger row against its exact post-event
available balance and explicit current lot valuation.

### Paired QC rejection ledger

For every rejected source hold, `QcService` continues to create one completed
`StockTransfer`, but now writes two immutable `Transfer` ledger rows:

- source location: `Qty = -movedQty`, `QtyAfter = source.QtyAvailable`;
- quarantine location: `Qty = movedQty`,
  `QtyAfter = quarantine.QtyAvailable`.

Both rows share the transfer number, transaction timestamp, lot valuation, user,
product, and lot. Multi-source test coverage verifies two transfer documents
produce four ledger rows, each reference resolves to exactly one negative and
one positive row, and aggregate quantities reconcile to `-10` and `+10`.

`QcService` now starts relational inspections with
`IsolationLevel.Serializable`. This holds the source and quarantine balance
reads through claim, mutation, ledger insertion, and commit, preventing a
concurrent available-balance writer from making `QtyAfter` stale. Work-order
completion already used the same serializable boundary.

### Writer-blocking migration guard

The migration's first operation now checks history with:

```sql
FROM [StockTransactions] WITH (TABLOCKX, HOLDLOCK)
```

`TABLOCKX` blocks concurrent inserts by taking an exclusive table lock, and
`HOLDLOCK` explicitly retains the serializable lock to transaction completion.
The migration test also verifies `SuppressTransaction == false` and that every
later operation is an `AddColumnOperation`.

Fresh generated SQL confirms the precondition and schema changes share one
transaction:

```text
BEGIN TRANSACTION;
IF EXISTS (
    SELECT 1
    FROM [StockTransactions] WITH (TABLOCKX, HOLDLOCK)
)
    THROW ...;
ALTER TABLE ... ADD [IsCancelled] ...;
ALTER TABLE ... ADD [QtyAfter] ...;
ALTER TABLE ... ADD [ValuationRate] ...;
INSERT INTO [__EFMigrationsHistory] ...;
COMMIT;
```

The local development database still records the ledger and paging migrations
as applied; no migration history or data was rewritten.

### Amendment TDD evidence

Baseline before amendment:

```text
dotnet test WmsMes.sln --no-restore
Passed: 413
Failed: 0
Skipped: 0
```

Work order invariant:

- RED: 1 failed, expected manufactured `QtyAfter` `0`, actual total-on-hand `8`.
- GREEN: 1 passed after manufactured receipt and backflush switched to exact
  tuple `QtyAvailable`.

QC pairing and invariant:

- RED: 1 failed, expected four paired rows for two sources, actual two legacy
  zero-quantity destination-only rows.
- GREEN: 1 passed after adding source-negative/destination-positive pairs and
  exact available balances.

Migration writer lock:

- RED: 1 failed because the first SQL operation did not contain
  `WITH (TABLOCKX, HOLDLOCK)`.
- GREEN: 1 passed after adding the transactional writer-blocking hints.

Existing compliant InventoryService producers:

- 6 invariant-focused receipt, issue, reversal, stocktake, and adjustment tests
  passed with non-zero reserved/on-hold buckets.

Final review follow-ups:

- Seeder RED: 12 of 13 generated sample ledger rows retained default
  `QtyAfter = 0`; the one passing row was the legitimate fully-held manufactured
  receipt whose available balance is zero.
- Seeder GREEN: the comprehensive idempotent seeder test passed after every
  seeded ledger producer received explicit post-balance and current lot
  valuation inputs.
- QC isolation RED: expected `Serializable`, observed `Unspecified`.
- QC isolation GREEN: the relational inspection test passed after the
  transaction boundary became explicitly serializable.
- Expanded focused Inventory, MES, QC, migration, and seeder suites:
  101 passed, 0 failed.

### Amendment final verification

```text
dotnet build WmsMes.sln --no-restore
Build succeeded.
0 Warning(s)
0 Error(s)

dotnet test WmsMes.sln --no-build --no-restore
Passed: 414
Failed: 0
Skipped: 0

dotnet ef migrations has-pending-model-changes --no-build
No changes have been made to the model since the last migration.
```

The generated migration script places the locking precondition, all three
`ALTER TABLE` statements, and migration-history insert between one
`BEGIN TRANSACTION` and `COMMIT`. Source scans find no remaining total-on-hand
helper or `QtyAfter` expression that adds reserved/on-hold buckets. The final
review reported no critical findings; both important findings were either
implemented (seeder and QC isolation) or already satisfied by the existing
serializable work-order transaction.
