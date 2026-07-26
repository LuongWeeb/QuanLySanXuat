# Task 3 — Goods receipt and issue cancellation services

## Scope implemented

- Added `CancelGoodsReceiptAsync(int receiptId, string userId)` and
  `CancelGoodsIssueAsync(int issueId, string userId)` to `IInventoryService`.
- Both methods accept only `Completed` documents and set successful documents to
  `Cancelled`.
- Receipt cancellation resolves the receipt lot first, then guards and updates
  `StockBalance` by the full `(ProductId, LotId, LocationId)` tuple.
- Receipt cancellation conditionally decrements both the exact balance and lot
  quantity, so neither can become negative.
- Issue cancellation restores an existing balance or creates one when missing.
- Both methods append immutable reversal ledger entries with the correct sign,
  `QtyAfter`, lot valuation, `IsCancelled = true`, user, and document reference.
- Relational document status and stock changes use conditional bulk updates in a
  database transaction. Deterministic tuple ordering is retained.
- Non-positive line quantities are rejected before document claim or inventory
  mutation.
- Direct relational updates are synchronized back to already tracked balance and
  lot entities.
- On failure, the database transaction is rolled back and the exact pre-call EF
  tracking snapshot is restored, including per-property modification flags. This
  prevents operation-created reversal entries from leaking through a later save
  while preserving unrelated caller changes.

## TDD evidence

### Initial RED

Six focused cancellation tests were written before either public method existed.

Command:

```powershell
dotnet test WmsMes.Tests/WmsMes.Tests.csproj --filter "FullyQualifiedName~InventoryServiceTests.CancelGoods" --no-restore
```

Result: compilation failed with the expected `CS1061` errors because
`InventoryService` had no `CancelGoodsReceiptAsync` or
`CancelGoodsIssueAsync`.

Coverage introduced in this cycle:

- receipt and issue status preconditions;
- receipt exact-lot lookup and reversal ledger fields;
- relational multi-line receipt insufficient-stock rollback;
- issue stock restoration and reversal ledger fields;
- creation of a missing issue balance.

### Initial GREEN

After the minimal interface and service implementation, the same focused command
passed: 6 passed, 0 failed.

### Review RED

Independent review found deterministic edge cases. Tests were strengthened before
their fixes:

- repeated issue lines for one missing relational stock key;
- zero and negative cancellation quantities;
- successful relational state remaining stale in EF tracking;
- operation-created ledger state surviving a database rollback and being written
  by a later `SaveChangesAsync`.

Focused result before fixes: 3 passed, 7 failed. Failures matched the intended
causes: four missing quantity exceptions, one unique balance-key violation, one
stale tracked balance, and one reversal ledger leaked after rollback.

A further regression assertion for an unrelated pre-existing `Modified` entity
failed as expected because rollback restoration marked every scalar property
modified instead of preserving the original per-property flags.

### Review GREEN

After the fixes:

```text
Passed! - Failed: 0, Passed: 10, Skipped: 0, Total: 10
```

The relational insufficient-stock test also asserts the error reports the exact
lot balance (`Cần 5, Hiện có 2`) and that a later save in the same context cannot
reintroduce rolled-back cancellation data.

## Verification

Focused cancellation suite:

```powershell
dotnet test WmsMes.Tests/WmsMes.Tests.csproj --filter "FullyQualifiedName~InventoryServiceTests.CancelGoods" --no-restore
```

Result: 10 passed, 0 failed, 0 skipped.

Full suite:

```powershell
dotnet test WmsMes.sln --no-restore
```

Result: 336 passed, 0 failed, 0 skipped.

The full output includes expected host-startup validation logs for a missing
development JWT signing key; these are assertions exercised by existing tests and
do not fail the suite.

`git diff --check` reports no whitespace errors; Git only reports the repository's
existing LF-to-CRLF conversion warnings.

## Changed files

- `Services/IInventoryService.cs`
- `Services/InventoryService.cs`
- `WmsMes.Tests/InventoryServiceTests.cs`
- `.superpowers/sdd/task-3-report.md`

## Self-review

- No controller or UI files were changed.
- Original posting ledger rows remain immutable; cancellation creates new reversal
  rows.
- Receipt balance lookup cannot consume another lot at the same product/location.
- Every receipt decrement is conditional on sufficient balance and lot quantity.
- All mutation, ledger creation, and document status changes are committed or
  rolled back together for service-owned relational transactions.
- Repeated missing issue-balance keys inside one cancellation reuse the pending
  tracked balance and produce running `QtyAfter` values.
- Existing unrelated modification state in the DbContext survives failed
  cancellation exactly.

## Concerns / design boundaries

- A cross-document race remains possible if two SQL Server transactions cancel
  different issues concurrently and both need to create the same absent
  `(ProductId, LotId, LocationId)` balance. The current project patterns do not
  provide a provider-independent upsert, serializable key-range lock, or
  whole-operation unique-conflict retry. Per the task instruction, no new locking
  strategy was guessed. Normal issue posting requires the balance to exist, so
  this path is a defensive recovery path; a later concurrency design should choose
  one of those explicit strategies.
- When the caller supplies an ambient transaction, the existing transaction helper
  deliberately leaves commit/rollback ownership with that caller. A cancellation
  exception therefore requires the ambient owner to abort its transaction.
  Guaranteeing method-level rollback inside an ambient transaction would require a
  new savepoint contract not used by the existing inventory service patterns.
- Tracking snapshot restoration intentionally covers scalar values, original
  values, entity state, and per-property modification flags. It does not attempt to
  reconstruct navigation loaded-state; no cancellation persistence depends on
  that state.
