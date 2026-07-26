# Task 5 Report — Cancellation API and Inventory UI

## Outcome

Implemented the warehouse receipt/issue cancellation endpoints and their MVC list
affordances. Added an accessible stock-ledger page that displays running quantity,
valuation rate, and cancellation status.

## TDD Evidence

### RED — cancellation actions and ledger page

Command:

```text
dotnet test WmsMes.Tests\WmsMes.Tests.csproj --no-restore --filter "FullyQualifiedName~InventoryControllerTests|FullyQualifiedName~InventoryViewTests"
```

Expected failure:

```text
InventoryControllerTests.cs: error CS0117:
'InventoryController' does not contain a definition for 'CancelReceipt'
'InventoryController' does not contain a definition for 'CancelIssue'
```

This proved the focused controller tests were exercising the missing production
entry points before implementation.

### GREEN — focused controller and view behavior

The same focused command passed after the minimal implementation:

```text
Passed: 51, Failed: 0, Skipped: 0, Total: 51
```

### RED/GREEN — single antiforgery token

Self-review identified that combining the POST form tag helper with
`@Html.AntiForgeryToken()` could emit duplicate antiforgery fields. A focused
source test was changed first to require `asp-antiforgery="true"` and reject the
manual helper.

RED:

```text
Failed: 2, Passed: 0, Total: 2
Not found: asp-antiforgery="true"
```

GREEN after updating both forms:

```text
Passed: 2, Failed: 0, Skipped: 0, Total: 2
```

## Implementation

- Added role-restricted, antiforgery-protected POST actions:
  - `CancelReceipt(int id)`
  - `CancelIssue(int id)`
- Both actions:
  - fail fast when `IInventoryService` is unavailable;
  - pass the authenticated name identifier or the specified `system` fallback;
  - set clear Vietnamese success/failure `TempData`;
  - redirect to the originating document list;
  - log unexpected exception details server-side and display a stable safe
    message rather than exposing exception contents.
- Added `Transactions()` because the brief referenced `Transactions.cshtml`, but
  neither the view nor a reachable controller action existed in the worktree.
  The query eagerly loads product, lot, and location and orders newest first.
- Receipt and issue lists now:
  - display success and error alerts;
  - render a cancellation POST form only for `Completed` documents;
  - show a red `Đã hủy` badge for `Cancelled` documents;
  - show statuses/actions once per multi-line document using `rowspan`;
  - provide a link to the stock ledger.
- The ledger displays transaction type/reference, item dimensions, quantity
  change, `QtyAfter`, `ValuationRate`, date, and valid/cancelled status.

## UI and Accessibility Decisions

- Kept the established Bootstrap/MVC visual language and existing table layout.
- Used semantic POST forms with `asp-antiforgery="true"` and confirmation prompts.
- Added document-specific `aria-label` text to destructive buttons.
- Added `role="status"` for success feedback and `role="alert"` for failures.
- Added a visually hidden ledger table caption describing the financial/quantity
  columns.
- Kept cancelled ledger rows muted and struck through as required, with a red
  textual badge so status is not communicated by color alone.

## Tests Added

- Security attributes on both cancellation actions.
- Receipt success, authenticated user ID, TempData, and redirect.
- Issue false result, `system` fallback, TempData, and redirect.
- Safe exception behavior for both endpoints.
- Missing-service fail-fast behavior for both endpoints.
- Transaction query ordering and eager-loaded display relationships.
- Receipt/issue cancellation form, condition, confirmation, status, error alert,
  and antiforgery source checks.
- Ledger column, formatting, and cancellation-treatment source checks.

## Verification

Focused:

```text
Passed: 51, Failed: 0, Skipped: 0, Total: 51
```

Fresh solution build:

```text
dotnet build WmsMes.sln --no-restore
Build succeeded. 0 Warning(s), 0 Error(s).
```

Fresh full solution test:

```text
dotnet test WmsMes.sln --no-build --no-restore
Passed: 382, Failed: 0, Skipped: 0, Total: 382
```

The full test output includes two expected host-start failure logs from existing
tests that deliberately verify rejection of a missing/short JWT signing key; the
test runner reports zero failures.

## Changed Files

- `Controllers/InventoryController.cs`
- `Views/Inventory/Receipts.cshtml`
- `Views/Inventory/Issues.cshtml`
- `Views/Inventory/Transactions.cshtml` (new)
- `WmsMes.Tests/InventoryControllerTests.cs`
- `WmsMes.Tests/InventoryViewTests.cs`
- `.superpowers/sdd/task-5-report.md`

## Concerns and Scope Notes

- The supplied brief described `Transactions.cshtml` as an existing file, but it
  was absent. Creating the view without a GET action would leave it unreachable,
  so the minimal `Transactions()` action was added within the requested scope.
- The brief's illustrative catch block displayed `ex.Message`. The implementation
  intentionally follows the repository's safer controller convention: log the
  exception and do not disclose internal details to the browser.
- `.superpowers/sdd/task-1-report.md` was already modified before Task 5. It was
  not edited or staged as part of this task.

---

## Quality Follow-up — Pagination, Index, and Runtime MVC Coverage

### Findings Addressed

- Replaced the unbounded stock-ledger entity-graph load with 50-row keyset
  pagination over the stable `(TransactionDate DESC, Id DESC)` order.
- Added a server-side projection containing only the fields rendered by the
  ledger view.
- Preserved `AsNoTracking()` and navigation display values through the
  projection.
- Added `StockTransactionPageViewModel` and
  `StockTransactionListItemViewModel`.
- Added accessible `Mới nhất` and `Cũ hơn` ledger navigation. The cursor is the
  last visible row's ISO-8601 timestamp plus ID.
- Added explicit `[HttpGet]` and retained
  `[Authorize(Roles = "Admin,Warehouse,Manager")]` on `Transactions`.
- Added the descending composite index
  `IX_StockTransactions_TransactionDate_Id` and the pure
  `AddStockLedgerPagingIndex` migration.
- Added runtime `WebApplicationFactory` coverage through the real MVC/Razor,
  authorization, and antiforgery pipeline.

### RED/GREEN Evidence

Pagination RED was observed before adding production paging types:

```text
InventoryControllerTests.cs: error CS0246:
The type or namespace name 'StockTransactionPageViewModel' could not be found
```

After the minimal paging implementation, the new controller test proves that 53
rows sharing one timestamp are returned as pages of 50 and 3, in descending ID
order, with no overlap.

The migration boundary was independently regression-checked by temporarily
removing only the new migration source from discovery and restoring it in a
`finally` block:

```text
RED: Assert.Single() Failure: The collection was empty
GREEN: Passed 1, Failed 0
```

The existing cancellation UI already had the intended runtime behavior from the
first Task 5 commit. The new integration tests therefore passed once the test
project compiled; their purpose is to replace source inspection as the sole
evidence, not to force an unnecessary production change.

An existing supplementary source test also correctly failed after the ledger
model changed:

```text
Not found: @model IEnumerable<...StockTransaction>
```

It was updated to assert the paged view model and keyset controls, then passed
`6/6`.

### Runtime MVC/Razor Coverage

`InventoryCancellationRuntimeTests` verifies rendered responses rather than
only reading `.cshtml` source:

- Completed two-line receipts and issues render exactly one cancellation form
  per document.
- Cancelled two-line documents render no cancellation form and show `Đã hủy`.
- Every rendered cancellation form contains exactly one generated
  `__RequestVerificationToken`.
- Each two-line table renders four rows with the expected 9/7 cell pattern and
  four `rowspan="2"` cells, preserving valid multi-line table structure.
- Anonymous access receives `401`; authenticated users without the required
  role receive `403`.
- Both cancellation POST endpoints reject an authorized request without an
  antiforgery token with `400`.
- A `Warehouse` user can open receipt, issue, and ledger pages and follow the
  generated ledger links.

### Migration Verification

The migration contains only:

```text
CreateIndex IX_StockTransactions_TransactionDate_Id
DropIndex   IX_StockTransactions_TransactionDate_Id
```

EF represents "all indexed columns descending" as an empty descending metadata
array. SQL Server application output confirmed the resulting DDL:

```sql
CREATE INDEX [IX_StockTransactions_TransactionDate_Id]
ON [StockTransactions] ([TransactionDate] DESC, [Id] DESC);
```

Commands and results:

```text
dotnet ef migrations has-pending-model-changes --no-build
No changes have been made to the model since the last migration.

dotnet ef database update --no-build
Applied AddStockLedgerFields and AddStockLedgerPagingIndex successfully.
```

### Final Verification

```text
Focused inventory/controller/view/migration/runtime tests:
Passed 66, Failed 0, Skipped 0

dotnet build WmsMes.sln --no-restore
Build succeeded. 0 Warning(s), 0 Error(s).

dotnet test WmsMes.sln --no-build --no-restore
Passed 395, Failed 0, Skipped 0
```

The full-suite output still includes the two expected host-start error logs from
tests that intentionally reject missing/short JWT keys; the runner reports zero
test failures.

### Additional Changed Files

- `Data/ApplicationDbContext.cs`
- `Data/Migrations/20260726094346_AddStockLedgerPagingIndex.cs`
- `Data/Migrations/20260726094346_AddStockLedgerPagingIndex.Designer.cs`
- `Data/Migrations/ApplicationDbContextModelSnapshot.cs`
- `ViewModels/InventoryViewModels.cs`
- `WmsMes.Tests/InventoryCancellationRuntimeTests.cs`
- `WmsMes.Tests/StockLedgerMigrationTests.cs`

### Follow-up Concerns

- An initial `dotnet ef migrations add --no-build` used a stale assembly and
  generated an empty migration. A subsequent stale `migrations remove` targeted
  the prior ledger migration and rolled it back in the development database.
  Work stopped immediately; the exact committed Task 1 migration files and
  snapshot were restored from `HEAD`, the empty artifacts were removed, the web
  project was rebuilt, and EF regenerated the correct migration. The final
  `database update` restored the Task 1 columns and applied the new descending
  index. No Task 1 source/report change is included.
- EF continues to print pre-existing precision warnings for
  `CycleCountItem.SystemQty` and `CountedQty` during design-time commands. They
  are outside Task 5 and unchanged by this work.
