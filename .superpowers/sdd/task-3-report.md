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

---

## Important review-fix follow-up

This section supersedes the earlier unresolved concurrency and ambient-transaction
concerns.

### Ambient transaction atomicity

- Cancellation-owned relational work now starts a normal provider transaction
  (SQL Server defaults to `ReadCommitted`), avoiding whole-operation conversion
  deadlocks.
- When a cancellation joins an existing relational transaction, it creates a
  method-specific savepoint.
- Success and precondition-false paths release the savepoint.
- Failure rolls back to and releases the savepoint, leaving database work performed
  by the ambient owner before the cancellation untouched.
- A provider that cannot create savepoints is rejected before cancellation
  mutation.

SQLite integration coverage proves:

- a receipt cancellation that fails after its first direct SQL stock mutation can
  be caught while the ambient transaction commits; the prior unrelated product
  update persists, while receipt status, lot, balance, and ledger remain unchanged;
- an issue cancellation whose final ledger save fails behaves the same way;
- issue cancellation success followed by a precondition-false call in the same
  ambient transaction releases both savepoints and commits the valid cancellation.

### Race-safe missing issue balance

The issue reversal path now acquires each exact balance key once, in deterministic
`ProductId`, `LotId`, `LocationId` order:

- SQL Server executes the production query with `UPDLOCK, HOLDLOCK` on the exact
  tuple. `HOLDLOCK` supplies serializable key-range semantics for an absent row and
  `UPDLOCK` retains update ownership until transaction completion.
- The model's unique composite index on
  `(ProductId, LotId, LocationId)` supplies the ordered key range used by that lock.
- Service-owned SQL Server transactions use normal `ReadCommitted`; both owned
  and supported ambient transactions receive the required exact-key range
  behavior from the query hint.
- SQLite uses its serializable transaction/writer serialization, with the document
  status claim occurring before balance access.

The SQL Server evidence is deterministic SQL generation, not a live server stress
test: the test invokes the private production query builder, calls
`ToQueryString()`, verifies `WITH (UPDLOCK, HOLDLOCK)` and all three exact-key
predicates, and verifies the unique model index. This avoids claiming a live
concurrent SQL Server run that was not performed.

Relational behavior tests additionally prove:

- repeated lines for one missing key create one balance with running `QtyAfter`
  values `[2, 5]`;
- repeated lines for one existing key retain the same locked entity and produce
  quantity `3 + 2 + 3 = 8` with running `QtyAfter` values `[5, 8]`;
- a stale tracked balance at `3`, externally updated to `7` before cancellation,
  is refreshed from the locked database row and restored to `9`, not overwritten
  to `5`.

### Dirty tracked targets and lock ordering

- Cancellation detects changes before document claim or inventory mutation.
- It rejects an affected `StockBalance` or `Lot` in any state other than
  `Unchanged`.
- Tests prove dirty balance reservation and dirty lot valuation changes remain
  modified, can be saved by the caller afterward, and do not produce partial
  cancellation. The dirty-lot ambient test also proves prior ambient work survives.
- Receipt lots are resolved before mutation. The later final hardening section
  supersedes this stage's temporary `LotId` ordering with one canonical natural-key
  comparator across every relevant flow.

### Follow-up TDD evidence

First RED command:

```powershell
dotnet test WmsMes.Tests/WmsMes.Tests.csproj --filter "FullyQualifiedName~CancelGoodsReceiptAsync_WhenAmbientCancellationFails_RollsBackOnlyToItsSavepoint|FullyQualifiedName~CancelGoodsReceiptAsync_WhenTargetBalanceIsDirty|FullyQualifiedName~CancelGoodsReceiptAsync_WhenTargetLotIsDirty|FullyQualifiedName~CancelGoodsIssueAsync_UsesSerializableTransaction" --no-restore
```

Result before production changes: 0 passed, 4 failed. The ambient transaction
committed a cancelled receipt, both dirty-target calls completed instead of
throwing, and Serializable/key-range lock SQL was absent.

After the savepoint, preflight, and lock implementation, the same command passed:
4 passed, 0 failed.

Second RED command covered dirty issue valuation lot, stale tracked balance,
cross-flow lock order, the actual generated SQL/model index, and an issue ambient
save failure:

```powershell
dotnet test WmsMes.Tests/WmsMes.Tests.csproj --filter "FullyQualifiedName~WhenValuationLotIsDirty|FullyQualifiedName~WithStaleTrackedBalance|FullyQualifiedName~OrdersResolvedLotsById|FullyQualifiedName~GeneratesExactSqlServerKeyRangeLock|FullyQualifiedName~WhenAmbientSaveFails" --no-restore
```

Result before fixes: 1 passed, 4 failed. The already-implemented issue savepoint
case passed; the four intended regression cases failed with no dirty-lot exception,
stale quantity `5` instead of `9`, receipt ledger lot order `[2,1]` instead of
`[1,2]`, and no callable production SQL query builder.

After fixes, the command passed: 5 passed, 0 failed.

Final reviewer regression:

```powershell
dotnet test WmsMes.Tests/WmsMes.Tests.csproj --filter "FullyQualifiedName~CancelGoodsIssueAsync_WhenRepeatedKeyBalanceExists" --no-restore
```

RED: expected final balance `8`, actual `6`.

GREEN after per-cancellation locked-balance caching: 1 passed, 0 failed.

### Final verification

Focused cancellation suite:

```text
Passed! - Failed: 0, Passed: 20, Skipped: 0, Total: 20
```

Full solution:

```text
Passed! - Failed: 0, Passed: 346, Skipped: 0, Total: 346
```

The full output again contains the expected existing JWT signing-key startup
validation logs; the test process exits successfully.

Independent final review verdict: **Ready**, with no remaining Critical or
Important findings.

### Remaining concern

- No live SQL Server concurrency integration test was executed. Concurrency safety
  is supported by the generated production SQL lock hints, exact predicates,
  unique composite index, deterministic lock order, and relational
  stale/repeated-key behavior tests described above.

---

## Final concurrency hardening

This section supersedes the earlier final counts and completes the second
independent review cycle.

### Cancellation transaction policy

- SQL Server cancellation accepts ambient transactions only at
  `ReadCommitted`; `Snapshot`, `ReadUncommitted`, `RepeatableRead`,
  `Serializable`, `Chaos`, and `Unspecified` are rejected before savepoint,
  document claim, or inventory reads.
- Owned cancellation transactions use the provider's normal isolation.
- Ambient calls use a unique 32-character GUID savepoint name, preventing
  collisions with caller-created or nested cancellation savepoints.
- The relational document compare-and-set claim remains the first document,
  line, lot, or balance access in a cancellation.

The isolation allowlist regression was RED because `ReadUncommitted`, `Chaos`,
and `Unspecified` were accepted. It passed after the explicit allowlist replaced
the earlier partial denylist.

### Cross-flow SQL Server lock order

- Issue completion resolves lot identities with statement-scoped
  `READCOMMITTEDLOCK`, then acquires valuation lot locks in canonical
  `ProductId`, normalized `LotNo` order.
- Receipt completion first resolves lot identities with
  `READCOMMITTEDLOCK`, preventing ambient `Serializable` reads from retaining
  shared locks. It then acquires both existing and missing lots in canonical
  `ProductId`, normalized `LotNo`, `LocationId` order with
  `UPDLOCK, HOLDLOCK`.
- A missing receipt lot is rechecked with `UPDLOCK, HOLDLOCK` on its natural key.
  The model's unique `LotNo` index backs the missing-key range lock.
- Receipt completion acquires each exact balance tuple once with the existing
  SQL Server `UPDLOCK, HOLDLOCK` query, after acquiring its lot.

The first completion-lock RED run had three expected failures:

- existing receipt lots emitted `[2,1]` instead of canonical `[1,2]`;
- issue valuation lots were read `[1,2]` instead of product-first `[2,1]`;
- the SQL Server lot lock query builders did not exist.

All three passed after targeted lock acquisition and canonical ordering.

### Fresh tracked state and repeated keys

Independent review found that SQL locks alone were insufficient when EF already
tracked stale entities. Receipt completion now:

- reads locked lots and balances with `AsNoTracking`;
- explicitly refreshes or attaches the locked database values;
- caches each acquired lot and balance for the operation;
- promotes a lot discovered during the missing-key locked recheck into both
  caches immediately.

Three focused RED/GREEN regressions prove the fixes:

- stale tracked lot/balance: expected lot quantity `12`, actual `7` before the
  refresh fix;
- repeated existing balance tuple: expected `13`, actual `11` before the
  operation-scoped balance cache;
- lot inserted between initial missing resolution and locked recheck: expected
  `15`, actual `13` before the mid-race lot was cached.

The mid-race test uses a SQLite command interceptor to insert the lot after the
initial resolution reader has opened, so the initial query returns missing while
the later recheck sees the inserted row.

### Final review verdict

Independent final review: **Approved**, with no remaining Critical or Important
findings. The reviewer confirmed that the lot and balance caches preserve running
quantities and that Lot-to-Balance lock order remains intact.

### Verification

Focused `InventoryServiceTests`:

```text
Passed! - Failed: 0, Passed: 51, Skipped: 0, Total: 51
```

Full solution:

```text
Passed! - Failed: 0, Passed: 356, Skipped: 0, Total: 356
```

The output includes the expected existing JWT signing-key startup validation
logs; the assertions pass and the test process exits successfully.

### Provider evidence boundary

No live SQL Server stress test was available. SQL Server evidence consists of
generated production SQL (`ToQueryString`) verifying the exact predicates and
lock hints, model verification of the supporting unique indexes, plus SQLite
relational tests for document idempotency, savepoint rollback, stale tracked
state, deterministic order, repeated tuples, and the missing-to-found lot
transition.

---

## Final independent-review follow-up

This section supersedes all earlier lock-order, ambient-isolation, and
verification counts.

### Dirty cancellation documents

Cancellation now inspects the exact tracked target document before starting a
transaction or issuing the relational compare-and-set claim. A tracked
`GoodsReceipt` or `GoodsIssue` in `Added`, `Modified`, or `Deleted` state is
rejected. This prevents the operation's context-wide `SaveChangesAsync` from
overwriting or deleting the status written by the CAS.

Receipt-Modified and Issue-Deleted integration tests prove:

- the rejection happens without stock, lot, ledger, or document mutation;
- the caller's entity state and current values are unchanged;
- the database document remains `Completed`.

### Dirty completion targets

Receipt and issue completion preflight every affected tracked `Lot` and
`StockBalance` before lock acquisition or inventory mutation. Any affected entry
outside `Unchanged` is rejected instead of being silently overwritten by the
fresh locked-value refresh.

Coverage includes:

- dirty receipt `QtyReserved`;
- a receipt lot in `Deleted` state;
- dirty issue `QtyOnHold` and lot valuation together.

Each test verifies no partial database mutation and preservation of the caller's
dirty state.

### One stable natural-key order

All relevant completion and cancellation flows now use:

```text
ProductId -> normalized LotNo -> LocationId -> document line Id
```

Initial existence and database-assigned `LotId` do not participate in lock order.
SQL Server identity resolution uses `READCOMMITTEDLOCK`; retained lot locks use
the exact natural-key query with `UPDLOCK, HOLDLOCK`, backed by the unique
`LotNo` index. Exact balance tuple locks remain
`(ProductId, LotId, LocationId)` after the lot lock.

Behavioral order tests cover receipt completion, issue completion, receipt
cancellation, and issue cancellation with lot IDs deliberately opposite to lot
number order. A deterministic two-key transition test starts with `LOT-Z`
existing and inserts the earlier `LOT-A` after its initial missing-resolution
reader opens; ledger order remains `LOT-A`, then `LOT-Z`.

### TDD evidence

The combined focused RED run produced 10 expected failures:

- two dirty cancellation documents were accepted;
- three dirty completion target cases were accepted;
- `Snapshot` was accepted for SQL Server ambient cancellation;
- receipt completion, issue completion, and receipt cancellation used
  existence/`LotId`-dependent order;
- the two-key missing-to-found transition processed `[LOT-Z, LOT-A]`.

After implementation, the expanded focused filter passed 12/12. The complete
`InventoryServiceTests` suite passed 58/58.

### Final self-review

- The cancellation document guard is in-memory only and therefore preserves
  CAS-before-database-read ordering.
- Dirty completion checks run before any lot or balance mutation.
- Resolution reads under controller-owned `Serializable` transactions use
  `READCOMMITTEDLOCK`; only canonical natural-key acquisition retains locks.
- Receipt cancellation maintains Lot-to-Balance order.
- Issue cancellation, which does not mutate lots, acquires balance locks in the
  same natural-key sequence used by receipt flows.
- Unique savepoints, ambient rollback, exact tuple balance locking, repeated-key
  caches, and stale-value refresh behavior remain covered.

Final full solution:

```text
Passed! - Failed: 0, Passed: 363, Skipped: 0, Total: 363
```

The output includes the expected existing JWT signing-key startup validation
logs; the test process exits successfully.
