# Final Fix Report

## Implemented findings

1. **Legacy GoodsIssue migration**
   - Replaced the unconditional legacy-row `THROW` with a deterministic backfill.
   - When unmapped rows exist, SQL creates inactive customer `LEGACY-UNASSIGNED` only when absent, updates only null `CustomerId` rows, then makes the column required and adds the FK.
   - The unique stable customer code makes reruns/collisions safe; no synthetic `CustomerId = 0` is used.

2. **Inventory notification ordering**
   - Added completion paths that suppress service-level SignalR emission while a controller-owned transaction is open.
   - Controllers commit first, then call the explicit notification method. Notification errors are logged and do not reverse a committed receipt/issue.

3. **Inventory POST validation and identity**
   - Receipt validates active Supplier, Product, and Location plus lot, quantity, and price.
   - Receipt and issue require an authenticated `NameIdentifier`, pass it into inventory transactions, and do not call completion services for invalid input or missing identity.
   - Errors return the populated form safely.

4. **Work-order approval concurrency**
   - Relational approvals use a serializable transaction and an atomic conditional status claim before reserving stock.
   - FEFO/FIFO ordering remains unchanged.
   - A shared SQLite two-context competition test proves only one competing approval reserves the finite stock and balances/reservations remain consistent.

5. **Configured finished-goods QC location**
   - Completion resolves active location code `LOC-FG-01`, already established by `DbSeeder`, and uses its actual database ID for balance and stock transaction rows.
   - Missing configuration raises a clear error; tests use location ID 77 to prevent regression to an assumed ID of 1.

6. **Hub surface security**
   - Removed client-callable broadcast methods from `InventoryHub` and `ProductionHub`.
   - Server-side `IHubContext` broadcasts remain unchanged.

## TDD evidence

- Migration test failed because generated operations still contained legacy `THROW` and no reserved customer backfill; it passes after the migration rewrite.
- Hub surface test failed on `NotifyStockChange`; it passes after both hub methods were removed.
- Finished-goods location test failed with expected ID 77 / actual ID 1; it passes after stable-code resolution.
- SQLite concurrency test exposed SQLite decimal `Sum` translation; the availability sum is now provider-portable and the competing approval test passes.
- Inventory controller tests cover missing identity, authenticated user propagation, deferred notification, and successful committed outcome when notification throws.

## Database evidence

- `dotnet ef migrations list --no-build` found the complete migration chain through `20260717084611_AddGoodsIssueCustomer`.
- Generated SQL from `20260715095942_AddQcReportingPhase4` to `20260717084611_AddGoodsIssueCustomer` contains the guarded `LEGACY-UNASSIGNED` insert and null-row update, with no legacy-data `THROW`.
- A live database update was not run because the connected database already reports migration history and changing a previously applied migration cannot replay it safely. The corrected SQL is suitable for clean/upgrading environments where this migration is not yet applied.

## Verification

Final focused/full/build results are recorded in the handoff after fresh verification.

## Standalone inventory notification follow-up

- Standalone receipt and issue completion now finish and commit the owned relational transaction before entering best-effort SignalR notification handling.
- A hub exception is caught and logged outside the transactional try/catch, so it cannot trigger rollback-after-commit or change the successful business result.
- When an ambient transaction already exists, the service emits no pre-commit event. The transaction owner performs exactly one explicit notification after commit.
- Relational SQLite tests with a throwing hub prove receipt and issue inventory, document status, and stock transactions remain durable while each method returns success. A separate ambient-transaction test proves notification is deferred and emitted once by the owner.
