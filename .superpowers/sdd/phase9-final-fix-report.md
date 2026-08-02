# Phase 9 final review fix report

## Status and scope

- Branch: `feature/comprehensive-supply-chain-reports`
- Fix-wave base: `bf870c25442cea13917016b2edb39020e5250022`
- Implementation commit: `2521fa8d02973ca55f9221c29d6f62c3ff5f3376`
- Result: every Critical and Important final-review finding was fixed and independently re-reviewed.
- The pre-existing modification to `.superpowers/sdd/task-3-report.md` was preserved and excluded from both commits.

## Implemented fixes

### 1. One active Draft PickList per sales order

- Added filtered unique database index
  `UX_PickLists_OneDraftPerSalesOrder` on `SalesOrderId WHERE Status = Draft`.
- `PickListService` returns the existing Draft before allocating stock or document numbers.
- A same-order unique-key race detaches the losing aggregate and returns the winning Draft, including its items.
- Pick-list-number collisions still use the existing bounded retry path; the new order invariant does not misclassify those collisions.
- The Create selector excludes sales orders already holding a Draft PickList.
- The success message is idempotency-safe (`sẵn sàng`) rather than claiming every request created a new row.
- The PickList index table now has a caption and explicit column-header scopes.

### 2. Safe additive data migration

- Added migration
  `20260802081829_AddPhase9FinalIntegrityAndNotificationIndexes` without modifying historical migrations.
- Before creating the filtered unique index, the migration ranks legacy Draft PickLists by
  `CreatedAt, Id`, keeps the earliest Draft, and deterministically marks later duplicates `Cancelled`.
- Added an operation-level upgrade test that creates a legacy SQLite schema with duplicate Drafts,
  applies the actual migration operations, verifies the deterministic status result, and verifies the
  resulting unique constraint rejects another Draft.
- Generated SQL Server SQL was inspected: old index drop -> duplicate reconciliation CTE -> filtered
  unique index -> notification indexes.
- No shared/local application database was mutated during verification.

### 3. Business notification triggers and transaction boundaries

- QC rejection persists a `Danger` notification containing the lot identifier.
- Product aggregate stock crossing from `>= MinStock` to `< MinStock` during the controller goods-issue
  flow persists a `Warning` notification.
- WorkOrder and ProductionPlan completion persist `Info` notifications.
- All user-visible notification links point to `/Dashboard`, which is accessible to every role allowed
  to receive the global notification feed, including Warehouse.
- Inventory captures the pre/post stock crossing and complete payload inside the serializable business
  transaction, commits, safely clears the transaction, and only then publishes. It performs no stock
  re-query after commit.
- QC and WorkOrder now end their rollback exception envelopes immediately after commit. Transaction
  cleanup is guarded separately and every notification/logging path is best-effort after rollback is
  no longer possible.
- ProductionPlan claims `Draft -> Completed` atomically with a conditional relational `ExecuteUpdate`;
  a stale second context returns `false` and emits no duplicate completion notification.
- `NotificationService` persists first and catches/logs a SignalR broadcast failure without reversing or
  reporting failure for the committed notification.

### 4. Notification lifecycle, authorization, and indexes

- Added global shared-read `MarkAllAsReadAsync` behavior.
- Added role-authorized POST-only `NotificationController.MarkAllAsRead` with antiforgery validation.
- The layout emits a normal antiforgery form; it is hidden/disabled at zero unread messages and is
  enabled when a realtime unread notification arrives.
- Notification feed, hub, and mark-read action are limited to `Admin,Warehouse,Manager`.
- Added indexes for unread filtering (`IsRead`) and deterministic recent ordering
  (`CreatedAt DESC, Id DESC`).
- SignalR rendering continues to use text nodes and accepts only safe local reference URLs.

### 5. Report and packing-slip authorization

- `ReportController`, its Excel export, and `PrintPackingSlip` now require exactly
  `Admin,Warehouse,Manager`.
- The financial report navigation item, notification bell, and SignalR client are rendered only for the
  same business-role set.
- Runtime tests cover anonymous `401`, operational-role `403`, Warehouse access, hidden menu/bell, and
  authorization occurring before antiforgery validation.

## TDD evidence

### PickList invariant and collision behavior

- RED: the focused PickList/schema/UI run reported 5 failures out of 40 for the absent filtered index,
  missing service guard/race recovery, and selector behavior.
- GREEN: 41/41 passed after the database and service guards. The set includes:
  - sequential idempotency;
  - deterministic same-order SQLite race;
  - actual relational filtered-unique enforcement;
  - separate PickListNo collision retry;
  - Create-selector exclusion and accessible markup.

### Notification persistence and lifecycle

- RED: a SignalR exception escaped `SendNotificationAsync`; lifecycle tests initially did not compile
  because the service method/controller did not exist.
- GREEN: hub-failure persistence/logging passed 2/2 and lifecycle behavior passed 6/6.

### Business triggers and authorization

- RED: the initial trigger tests failed at construction because QC, WorkOrder, ProductionPlan, and
  Inventory had no notifier dependencies; the authorization run had 8 failures out of 9.
- GREEN: trigger payload/commit tests passed 6/6; role and runtime authorization passed 9/9.
- RED/GREEN: inaccessible detail URLs failed 3/3, then passed 3/3 after using `/Dashboard`.
- RED/GREEN: the MarkAll realtime lifecycle source test failed until the persistent form/button and
  realtime enablement were implemented.

### Concurrency and post-commit failure regressions

- RED: a stale relational ProductionPlan context returned `true` and sent a second notification.
- GREEN: the stale-context test passed, and all ProductionPlan tests passed 5/5.
- RED: the legacy migration test found `CreateIndexOperation` immediately after `DropIndexOperation`.
- GREEN: it found and executed the reconciliation `SqlOperation`, then enforced the unique index.
- RED: a post-commit stock read entered Inventory's business catch and attempted rollback on an already
  completed SQLite transaction.
- GREEN: crossing evaluation moved before commit; notifier failure still returns success with one durable
  GoodsIssue, one stock transaction, and the expected stock quantity.
- RED: relational QC and WorkOrder tests where both notifier and logger throw each attempted rollback on
  an already completed SQLite transaction (2/2 failed).
- GREEN: both passed after cleanup/notification moved outside the rollback envelope; the broader
  QC/WorkOrder/ProductionPlan set passed 30/30.

### Focused regression

```powershell
dotnet test WmsMes.Tests\WmsMes.Tests.csproj --no-restore --filter "FullyQualifiedName~SupplyChainReportServiceTests|FullyQualifiedName~SupplyChainSchemaTests|FullyQualifiedName~SupplyChainMigrationTests|FullyQualifiedName~PickListUiIntegrationTests|FullyQualifiedName~NotificationLifecycleTests|FullyQualifiedName~QcServiceTests|FullyQualifiedName~QcAndReportingTests|FullyQualifiedName~ProductionPlanTests|FullyQualifiedName~InventoryControllerTests|FullyQualifiedName~PackingSlipAndStockValuationTests" --logger "console;verbosity=minimal"
```

Result before the final QC/WorkOrder boundary hardening: 142 passed, 0 failed, 0 skipped.
The final boundary tests and related service group subsequently passed 2/2 and 30/30, and are included
in the full suite below.

## Final verification

### Formatting

```powershell
git diff --check
```

Result: exit code 0; no whitespace errors (only configured LF-to-CRLF working-copy notices).

### Build

```powershell
dotnet build WmsMes.sln --no-restore --verbosity minimal
```

Result: build succeeded, 0 warnings, 0 errors.

### Full solution test suite

```powershell
dotnet test WmsMes.sln --no-build --logger "console;verbosity=minimal"
```

Result: 691 passed, 0 failed, 0 skipped.

### EF model and migration SQL

```powershell
dotnet ef migrations has-pending-model-changes --project WmsMes.Web.csproj --startup-project WmsMes.Web.csproj --no-build
```

Result: `No changes have been made to the model since the last migration.`

```powershell
dotnet ef migrations script 20260731061027_AddPhase9SupplyChainAndNotificationTables 20260802081829_AddPhase9FinalIntegrityAndNotificationIndexes --project WmsMes.Web.csproj --startup-project WmsMes.Web.csproj --no-build
```

Result: generated SQL Server script places the reconciliation CTE before
`CREATE UNIQUE INDEX [UX_PickLists_OneDraftPerSalesOrder]` and includes both notification indexes.

### Independent review

- First final review identified Inventory's post-commit rollback hazard, stale ProductionPlan completion,
  and legacy duplicate migration risk; all were fixed.
- Re-review then identified the analogous QC/WorkOrder transaction boundary; relational RED/GREEN tests
  and the same safe structure were added.
- Final narrow re-review: no remaining Critical or Important findings.
- A separate current-tree audit also returned `clean`.

## Changed files

- Controllers: `InventoryController`, `NotificationController`, `PickListController`, `PrintController`,
  `ReportController`.
- Data: `ApplicationDbContext`, model snapshot, and
  `20260802081829_AddPhase9FinalIntegrityAndNotificationIndexes` migration/designer.
- Hubs/services: `NotificationHub`, `INotificationService`, `NotificationService`, `PickListService`,
  `ProductionPlanService`, `QcService`, `WorkOrderService`.
- Views: `Views/PickList/Index.cshtml`, `Views/Shared/_Layout.cshtml`.
- Tests: Inventory, notification lifecycle, packing/report authorization, PickList UI/service/schema,
  ProductionPlan, QC/reporting, and migration upgrade coverage.

## Explicit remaining limitations

- PackingSlip still has no complete creation/package-line persistence workflow; this wave only protects
  the existing print endpoint and records the existing schema boundary.
- Stock-valuation reporting/export still has no server-side pagination or streaming export path for very
  large datasets.
- The shared document status model still has no `InProgress` value; adding one requires a deliberate
  workflow/schema compatibility change.
- The PickList invariant prevents duplicate Drafts for one sales order but does not reserve aggregate
  stock across different sales orders, so cross-order over-allocation remains possible.
- Low-stock notification coverage is intentionally scoped to controller-created goods issues. Direct
  service completion and other downward stock mutations (reservation, receipt cancellation, negative
  adjustment, cycle-count reconciliation) need a shared after-commit event/outbox design.
- Notifications are persisted/broadcast best-effort without an outbox or exactly-once delivery guarantee.
- Read state is explicitly global/shared; per-user notification receipts remain a separate design change.
