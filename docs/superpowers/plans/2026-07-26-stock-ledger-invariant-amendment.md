# Stock Ledger Invariant Amendment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every ledger `QtyAfter` equal the persisted tuple's
`StockBalance.QtyAvailable`, represent QC rejection transfers as balanced
source/destination ledger pairs, and lock the historical-row migration
precondition against concurrent writers.

**Architecture:** Keep all existing transactional service boundaries. Ledger
producers read `QtyAfter` only from the mutated tuple's `QtyAvailable`; QC emits
two immutable transfer rows per source under one transfer reference. The SQL
Server migration acquires a transaction-duration exclusive table lock before
checking history and altering the ledger schema.

**Tech Stack:** ASP.NET Core 8, Entity Framework Core 8, C#, SQL Server, SQLite
and EF InMemory test providers, xUnit.

## Global Constraints

- Preserve receipt/issue completion and cancellation concurrency hardening.
- Preserve the no-negative-stock invariant and all existing transactions.
- Do not fabricate historical `QtyAfter` or valuation data.
- Do not modify `.superpowers/sdd/task-1-report.md`.

**Execution status:** Completed. Every checklist step below was executed with
recorded RED/GREEN evidence. Final review also expanded the producer audit to
`Data/DbSeeder.cs` and required serializable QC balance reads; those follow-ups
are covered in the report.

---

### Task 1: Global `QtyAfter` invariant and QC reconciliation

**Files:**
- Modify: `WmsMes.Tests/MesCoreServiceTests.cs`
- Modify: `WmsMes.Tests/QcAndReportingTests.cs`
- Modify: `Services/WorkOrderService.cs`
- Modify: `Services/QcService.cs`

**Interfaces:**
- Consumes: `WorkOrderService.CompleteWorkOrderAsync`,
  `QcService.SubmitQCInspectionAsync`, `StockBalance.QtyAvailable`.
- Produces: one invariant for all work-order and QC ledger rows; paired transfer
  rows sharing `ReferenceNo` for every rejected QC source.

- [x] **Step 1: Write failing work-order invariant assertions**

Set a material balance to non-zero `QtyAvailable`, `QtyReserved`, and
`QtyOnHold`. Assert the finished receipt `QtyAfter` is the finished tuple's
persisted `QtyAvailable` (`0`), and the backflush `QtyAfter` is the material
tuple's unchanged persisted `QtyAvailable`, not total on-hand.

- [x] **Step 2: Run the work-order test RED**

Run:

```text
dotnet test WmsMes.Tests/WmsMes.Tests.csproj --no-restore --filter
"FullyQualifiedName~CompleteWorkOrderAsync_BackflushesReservationsAndCreatesFinishedLotGenealogy"
```

Expected: failure because both current values use available + reserved + held.

- [x] **Step 3: Implement the minimal work-order fix**

Assign both work-order ledger rows directly from `balance.QtyAvailable` and
remove the total-on-hand helper:

```csharp
QtyAfter = finishedBalance.QtyAvailable
QtyAfter = balance.QtyAvailable
```

- [x] **Step 4: Run the work-order test GREEN**

Run the Step 2 command and expect 1 passed, 0 failed.

- [x] **Step 5: Write failing QC pair/reconciliation assertions**

Use two non-quarantine sources and an existing quarantine tuple, all with
non-zero available/reserved/on-hold buckets. For each generated transfer
reference assert exactly:

```csharp
Assert.Equal(new[] { -movedQty, movedQty }, pair.Select(entry => entry.Qty));
Assert.Equal(source.QtyAvailable, sourceEntry.QtyAfter);
Assert.Equal(quarantine.QtyAvailable, destinationEntry.QtyAfter);
Assert.Equal(sourceEntry.ValuationRate, destinationEntry.ValuationRate);
```

Also assert all negative quantities sum to `-totalMoved`, all positive
quantities sum to `totalMoved`, and each ledger `QtyAfter` equals its persisted
tuple's `QtyAvailable`.

- [x] **Step 6: Run the QC tests RED**

Run:

```text
dotnet test WmsMes.Tests/WmsMes.Tests.csproj --no-restore --filter
"FullyQualifiedName~SubmitQCInspectionAsync_WritesPairedTransferLedgerEntriesForEverySourceAndExactAvailableBalances"
```

Expected: failures because the service currently writes one zero-quantity
destination row and uses total on-hand for `QtyAfter`.

- [x] **Step 7: Implement the minimal QC pair**

For each source, preserve the existing `StockTransfer.TransferNo`, add a source
row with negative moved quantity and source `QtyAvailable`, and add a
destination row with positive moved quantity and target `QtyAvailable`.
Both rows use the current lot valuation and the same transfer reference.

- [x] **Step 8: Run the QC tests GREEN**

Run the Step 6 command and expect all selected tests to pass.

---

### Task 2: SQL Server writer-blocking migration guard

**Files:**
- Modify: `WmsMes.Tests/StockLedgerMigrationTests.cs`
- Modify: `Data/Migrations/20260726065752_AddStockLedgerFields.cs`

**Interfaces:**
- Consumes: EF Core transactional migration execution on SQL Server.
- Produces: a first `SqlOperation` using `TABLOCKX` and `HOLDLOCK`, with
  `SuppressTransaction == false`, before all `AddColumnOperation` instances.

- [x] **Step 1: Write the failing lock-boundary test**

Assert the first SQL operation contains:

```text
FROM [StockTransactions] WITH (TABLOCKX, HOLDLOCK)
```

and assert `guard.SuppressTransaction` is false.

- [x] **Step 2: Run the migration test RED**

Run:

```text
dotnet test WmsMes.Tests/WmsMes.Tests.csproj --no-restore --filter
"FullyQualifiedName~AddStockLedgerFieldsMigration_FailsBeforeSchemaMutationWhenHistoricalRowsExist"
```

Expected: failure because the current guard has no table lock hints.

- [x] **Step 3: Implement the minimal locking guard**

Use the SQL Server table hints in the historical-row query while leaving the
operation transactional:

```sql
IF EXISTS (
    SELECT 1
    FROM [StockTransactions] WITH (TABLOCKX, HOLDLOCK)
)
    THROW 51000, N'...', 1;
```

- [x] **Step 4: Run the migration test GREEN**

Run the Step 2 command and expect 1 passed, 0 failed.

---

### Task 3: Audit, report, verification, and commit

**Files:**
- Modify: `WmsMes.Tests/InventoryServiceTests.cs` only if invariant assertions
  are missing from an existing inventory producer test.
- Modify: `Data/DbSeeder.cs`
- Modify: `WmsMes.Tests/DbSeederTests.cs`
- Modify: `.superpowers/sdd/final-fix-report.md`

**Interfaces:**
- Consumes: all nine service ledger producer sites.
- Produces: explicit RED/GREEN evidence, producer audit evidence, generated SQL
  transaction/lock evidence, and one scoped commit.

- [x] **Step 1: Audit all producer assignments**

Run:

```text
rg -n "StockTransactions|QtyAfter\\s*=" Services
```

Verify receipt, issue, both cancellations, stocktake, adjustment, manufactured
receipt, backflush, both QC transfer sides, and comprehensive sample-data
producers use exact tuple `QtyAvailable`.

- [x] **Step 2: Run focused suites**

Run the full `InventoryServiceTests`, `MesCoreServiceTests`,
`QcAndReportingTests`, and `StockLedgerMigrationTests` classes.

- [x] **Step 3: Verify build, full solution, EF, and generated SQL**

Run:

```text
dotnet build WmsMes.sln --no-restore
dotnet test WmsMes.sln --no-build --no-restore
dotnet ef migrations has-pending-model-changes --no-build
dotnet ef migrations script 20260726065735_AddCycleCountSchema 20260726065752_AddStockLedgerFields --no-build --no-transactions
git diff --check
```

Inspect the generated migration script separately with transactions enabled and
confirm the lock query and all three `ALTER TABLE` statements occur inside one
`BEGIN TRANSACTION` / `COMMIT`.

- [x] **Step 4: Append the report and self-review**

Record exact RED/GREEN counts, invariant coverage, generated SQL boundaries,
full verification results, and remaining concerns in
`.superpowers/sdd/final-fix-report.md`.

- [x] **Step 5: Commit only scoped files**

Stage the services, tests, migration, plan, and report. Confirm
`.superpowers/sdd/task-1-report.md` is unstaged, then commit:

```text
git commit -m "fix: enforce stock ledger balance invariants"
```
