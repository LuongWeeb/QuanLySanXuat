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
