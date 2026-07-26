# Task 2 — Posting Engine ledger fields

## Scope

Updated the receipt and issue posting paths in `InventoryService` so their `StockTransaction` records persist the post-transaction quantity, lot valuation rate, and active cancellation state. The relational issue path retains its conditional `ExecuteUpdateAsync` decrement; it reads the persisted balance immediately afterward for the ledger's running balance.

## TDD evidence

### RED

Added two focused behavioral tests in `WmsMes.Tests/InventoryServiceTests.cs`:

- `CompleteGoodsReceiptAsync_WritesRunningBalanceAndLotValuationToLedger`
- `CompleteGoodsIssueAsync_WritesRemainingBalanceAndLotValuationToLedger`

Command:

```powershell
dotnet test WmsMes.Tests/WmsMes.Tests.csproj --filter "FullyQualifiedName~CompleteGoodsReceiptAsync_WritesRunningBalanceAndLotValuationToLedger|FullyQualifiedName~CompleteGoodsIssueAsync_WritesRemainingBalanceAndLotValuationToLedger"
```

Result before production changes: 0 passed, 2 failed. Both failures were the expected unset `QtyAfter` values (`Expected: 12, Actual: 0` for receipt and `Expected: 7, Actual: 0` for issue).

### GREEN

Implemented the minimal receipt and issue ledger field assignments in `Services/InventoryService.cs`.

Re-ran the focused command: 2 passed, 0 failed.

Additional focused service coverage:

```powershell
dotnet test WmsMes.Tests/WmsMes.Tests.csproj --filter "FullyQualifiedName~InventoryServiceTests"
```

Result: 14 passed, 0 failed.

## Full verification

`dotnet test` alone is ambiguous in this directory because it contains both `WmsMes.sln` and `WmsMes.Web.csproj` (`MSB1011`). The explicit full-suite equivalent succeeded:

```powershell
dotnet test WmsMes.sln
```

Result: 319 passed, 0 failed, 0 skipped.

The test output contains expected host-startup validation log entries for a missing development JWT signing key; these do not fail the tests.

`git diff --check` completed with no whitespace errors.

## Changed files

- `Services/InventoryService.cs`
- `WmsMes.Tests/InventoryServiceTests.cs`

## Concerns

- The relational issue path makes one additional balance read after its atomic conditional decrement. This preserves the existing negative-stock guard while obtaining the persisted running balance for the ledger.
- An unrelated pre-existing modification to `.superpowers/sdd/task-1-report.md` was left out of this task's commit.

---

## Review-fix follow-up

### Changes

- Receipt and issue completion now throw `InvalidOperationException("Quantity must be greater than zero.")` before any lot, stock-balance, or ledger work for a non-positive line quantity.
- Added parameterized coverage for zero and negative receipt/issue quantities. Receipt assertions prove no lot, balance, transaction, or completed document is created; issue assertions prove stock remains unchanged with no transaction or completed document.
- Added SQLite rollback coverage where a valid line is followed by an invalid line, proving the surrounding relational transaction rolls back all prior stock and ledger work.
- Added SQLite relational issue coverage for the `ExecuteUpdateAsync` decrement and subsequent persisted-balance read. It verifies persisted `QtyAvailable=7`, `Qty=-3`, `QtyAfter=7`, `ValuationRate=7.5`, and `IsCancelled=false`.

### RED

Command:

```powershell
dotnet test WmsMes.Tests/WmsMes.Tests.csproj --filter "FullyQualifiedName~CompleteGoodsReceiptAsync_RejectsNonPositiveQuantityBeforeWritingStockOrLedger|FullyQualifiedName~CompleteGoodsIssueAsync_RejectsNonPositiveQuantityBeforeChangingStockOrLedger|FullyQualifiedName~CompleteGoodsIssueAsync_OnRelationalStore_WritesPersistedLedgerBalanceAndLotValuation"
```

Output before production validation:

```text
Failed!  - Failed:     4, Passed:     1, Skipped:     0, Total:     5
```

The four expected failures were the zero/negative receipt and issue cases: `Assert.Throws() Failure: No exception was thrown`. The SQLite ledger characterization was already green because it verifies the previously implemented relational `ExecuteUpdateAsync` plus read-back behavior.

### GREEN

Same focused command after validation:

```text
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5
```

SQLite rollback command:

```powershell
dotnet test WmsMes.Tests/WmsMes.Tests.csproj --filter "FullyQualifiedName~CompleteGoodsReceiptAsync_WhenLaterLineHasNonPositiveQuantity_RollsBackEarlierPosting|FullyQualifiedName~CompleteGoodsIssueAsync_WhenLaterLineHasNonPositiveQuantity_RollsBackEarlierPosting"
```

Output:

```text
Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2
```

Covering service command:

```powershell
dotnet test WmsMes.Tests/WmsMes.Tests.csproj --filter "FullyQualifiedName~InventoryServiceTests"
```

Output:

```text
Passed!  - Failed:     0, Passed:    21, Skipped:     0, Total:    21
```

Full-suite command:

```powershell
dotnet test WmsMes.sln
```

Output:

```text
Passed!  - Failed:     0, Passed:   326, Skipped:     0, Total:   326
```

### Valuation fallback review

The issue path retains `lot?.UnitPrice ?? 0m`. `LotId` is a required foreign key and normal posting therefore guarantees a lot, so no behavior change was made solely for the theoretically missing lot. The fallback remains a review concern: if referential integrity is bypassed, it silently records a zero valuation rather than failing fast.
