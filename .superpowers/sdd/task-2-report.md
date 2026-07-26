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
