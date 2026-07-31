# Task 1 Report — Phase 9 Data Structure & Migration

## Status

Completed on 2026-07-31 in worktree `comprehensive-supply-chain-reports`.

## Changes

- Added `PickList`, `PickListItem`, `PackingSlip`, and `AppNotification` entities.
- Registered the four `DbSet`s and explicit EF Core mappings.
- Added unique indexes for `PickListNo` and `PackingNo`, preserving the PK/PS document-number identifiers for later service generation.
- Configured `PickList -> PickListItem` as cascade delete; business-reference foreign keys use `Restrict` to prevent deleting a SalesOrder, Product, Lot, or Location that is referenced by fulfillment documents.
- Generated migration `AddPhase9SupplyChainAndNotificationTables` and updated the model snapshot.
- Added model-first tests for schema mappings, relationships, unique document numbers, and default entity values.

## Files

- `Domain/Entities/PickList.cs`
- `Domain/Entities/PickListItem.cs`
- `Domain/Entities/PackingSlip.cs`
- `Domain/Entities/AppNotification.cs`
- `Data/ApplicationDbContext.cs`
- `Data/Migrations/20260731061027_AddPhase9SupplyChainAndNotificationTables.cs`
- `Data/Migrations/20260731061027_AddPhase9SupplyChainAndNotificationTables.Designer.cs`
- `Data/Migrations/ApplicationDbContextModelSnapshot.cs`
- `WmsMes.Tests/SupplyChainSchemaTests.cs`

## TDD evidence

### RED

Command:

```powershell
dotnet test WmsMes.Tests\WmsMes.Tests.csproj --filter FullyQualifiedName~SupplyChainSchemaTests --no-restore
```

Result: exit code 1; 0 passed, 2 failed. Both failures were `Assert.NotNull() Failure: Value is null` in `SupplyChainSchemaTests.AssertEntity`, because `PickList` and the other Phase 9 entity types were not registered in the EF model.

### GREEN

Same focused command after the minimal entities and mappings were added.

Result: exit code 0; 2 passed, 0 failed, 0 skipped.

## Migration and database update

Generated with:

```powershell
dotnet ef migrations add AddPhase9SupplyChainAndNotificationTables
```

Result: build succeeded; generated `20260731061027_AddPhase9SupplyChainAndNotificationTables` plus designer and snapshot changes. Manual inspection confirmed all four intended tables, decimal `(18,2)` columns, required/nullability constraints, unique document-number indexes, required foreign keys, correct restrict/cascade delete behavior, and reversible `Down` operations.

Database update command:

```powershell
dotnet ef database update
```

Result: exit code 0. LocalDB applied `20260730113038_EnforceOperationsIntegrityInvariants` (it was already pending) and then applied `20260731061027_AddPhase9SupplyChainAndNotificationTables` successfully.

Model/migration consistency check:

```powershell
dotnet ef migrations has-pending-model-changes --project WmsMes.Web.csproj --startup-project WmsMes.Web.csproj
```

Result: build succeeded; no pending model changes.

## Full verification

```powershell
dotnet build WmsMes.sln --no-restore
dotnet test WmsMes.Tests\WmsMes.Tests.csproj --no-restore
```

- Build: exit code 0, 0 warnings, 0 errors.
- Tests: exit code 0, 619 passed, 0 failed, 0 skipped.

## Self-review

- `git diff --check` is clean.
- New entity fields, defaults, max lengths, decimal precision, table names, relationships, and indexes match the approved Task 1 brief and existing repository conventions.
- Migration contains no unrelated schema operations and its snapshot agrees with the current model.
- Changes are limited to Task 1 schema, migration, and its direct tests/report.

## Concerns

- Current `DocumentStatus` contains `Draft`, `Completed`, and `Cancelled`; it does not contain the design document's narrative `InProgress` value. Task 1 preserves the existing enum and only requires the `Draft` default, so this is left for a separately scoped decision if workflow state progression needs `InProgress`.
- PK/PS format generation belongs to later service/controller work. Task 1 reserves sufficient length and enforces uniqueness for those document numbers.
- The LocalDB update advanced one previously pending migration in addition to Phase 9; no production/external database was targeted.
