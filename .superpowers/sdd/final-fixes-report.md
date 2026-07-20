# Final review fixes report

## Status

Completed all two Important findings and the Minor finding from the final review.

## Implemented

- Dashboard time is now based on injectable .NET 8 `TimeProvider` and an injectable `TimeZoneInfo` resolved from the configurable `BusinessTimeZone` setting. The default is `Asia/Ho_Chi_Minh`.
- Timezone resolution accepts IANA or Windows identifiers and falls back through .NET's ID conversion APIs, so the Vietnam default works across supported Windows/Linux hosts.
- UTC final-step `EndTime` values are explicitly treated as UTC and converted to the business timezone before seven-day window grouping. Planned output continues to use the `DueDate` calendar date.
- Dashboard work-order data uses a no-tracking projection containing only status, target quantity, due date, and the final step's end time / accepted / rejected quantities. It no longer loads complete work orders and step histories.
- Inventory volume, low-stock count, passed QC count, distinct hold/quarantine lot counts, and zone inventory are aggregated by relational queries. No stock/location/zone entity graphs are materialized.
- Chart.js is pinned to `4.4.9` at `https://cdn.jsdelivr.net/npm/chart.js@4.4.9/dist/chart.umd.min.js`.
- Existing OEE, production, QC, inventory-zone mappings and SignalR/UI behavior were preserved.

## TDD evidence

### Injectable clock and timezone

Command:

```powershell
dotnet test WmsMes.Tests\WmsMes.Tests.csproj --filter FullyQualifiedName~HomeController_AcceptsInjectableClockAndBusinessTimeZone --no-restore
```

- RED: 1 failed because the existing constructor contained only logger/context and did not contain `TimeProvider`.
- GREEN: 1 passed after adding the two injectable time dependencies and application configuration.

The cross-platform resolver also followed RED/GREEN: its focused test initially threw `NotImplementedException`, then passed after adding direct lookup plus IANA/Windows alternate-ID fallback.

### UTC-to-business-date midnight boundary

Command:

```powershell
dotnet test WmsMes.Tests\WmsMes.Tests.csproj --filter FullyQualifiedName~Metrics_GroupsUtcFinalStepEndTimesByBusinessDateAcrossMidnight --no-restore
```

- RED: 1 failed — with fixed UTC now at `2026-07-20T18:00:00Z`, expected final label `21/07`, actual `20/07`.
- GREEN: 1 passed after deriving today from the injected clock/timezone and converting final-step UTC timestamps before grouping. The regression also verifies `16:30Z -> 23:30` on `20/07` and `17:30Z -> 00:30` on `21/07` in UTC+7.

### Relational projection and aggregation

Command:

```powershell
dotnet test WmsMes.Tests\WmsMes.Tests.csproj --filter FullyQualifiedName~Metrics_SqliteQueriesTranslateAndProjectOnlyDashboardFields --no-restore
```

- RED: 1 failed because the original `Include` query selected unrelated full-entity columns including `BomVersion`, `RoutingVersion`, and `StepName`.
- During GREEN, the test first exposed an expression-tree compile error from the former in-memory null-propagation and then SQLite's unsupported decimal `Sum`; both causes were corrected in the relational query design.
- GREEN: 1 passed against an open SQLite in-memory database with seeded relational data. It verifies the resulting metrics, SQL `SUM`/`GROUP BY`, and absence of unrelated work-order/history columns.
- `Microsoft.EntityFrameworkCore.Sqlite` 8.0.0 was already a direct test-project dependency, so no package change was required.

### Immutable Chart.js CDN

Command:

```powershell
dotnet test WmsMes.Tests\WmsMes.Tests.csproj --filter FullyQualifiedName~DashboardView_InitializesAndRefreshesAllChartsWithSafelySerializedMetrics --no-restore
```

- RED: 1 failed because the view still referenced mutable `npm/chart.js`.
- GREEN: 1 passed after pinning the exact 4.x distribution URL and rejecting the mutable tag in the source contract test.

## Verification

Focused dashboard/controller regression:

```powershell
dotnet test WmsMes.Tests\WmsMes.Tests.csproj --filter "FullyQualifiedName~DashboardMetricsTests|FullyQualifiedName~HomeControllerTests" --no-restore
```

Result: passed — 15 passed, 0 failed, 0 skipped (exit code 0).

Required build:

```powershell
dotnet build WmsMes.sln
```

Result: build succeeded — 0 warnings, 0 errors (exit code 0).

Required full suite:

```powershell
dotnet test WmsMes.sln
```

Result: passed — 111 passed, 0 failed, 0 skipped (exit code 0).

## Changed files

- `Controllers/HomeController.cs`
- `Program.cs`
- `Services/BusinessTimeZoneResolver.cs`
- `appsettings.json`
- `Views/Home/Index.cshtml`
- `WmsMes.Tests/DashboardMetricsTests.cs`
- `WmsMes.Tests/HomeControllerTests.cs`
- `.superpowers/sdd/final-fixes-report.md`

## Concerns

- SQLite cannot translate `Sum(decimal)`, so only the explicitly detected SQLite compatibility branch casts aggregate inputs to `double`. SQL Server and all non-SQLite providers retain `Sum(decimal)`; the exact-decimal regression exercises that normal path.
- An invalid configured timezone identifier fails during dependency resolution rather than silently using server-local time. This keeps the business-time contract explicit.

## Final re-review fixes

### SQL-only OEE and bounded chart rows

Command:

```powershell
dotnet test WmsMes.Tests\WmsMes.Tests.csproj --filter FullyQualifiedName~Metrics_SqliteQueriesTranslateAndProjectOnlyDashboardFields --no-restore
```

- RED: 1 failed because the command projecting `DueDate` and final `EndTime` had no `>=` lower bound; the assertion reported that `>=` was absent.
- GREEN: 1 passed after replacing the all-work-order materialization with an OEE `GROUP BY` aggregate, a planned projection bounded to the seven business dates, and an actual final-step projection bounded to the corresponding UTC start/end instants.
- The relational fixture includes a 2020 historical work order plus the 2026 chart-window order. It verifies the current row appears in planned/actual output and that both time-series SQL commands contain lower and exclusive upper bounds. OEE still covers all orders, preserving the approved formula.

### Provider-specific decimal precision

Command:

```powershell
dotnet test WmsMes.Tests\WmsMes.Tests.csproj --filter FullyQualifiedName~Metrics_PreservesExactDecimalInventoryAggregatesOutsideSqlite --no-restore
```

- RED: 1 failed — expected exact `123456789012345.88m`, actual value after the unconditional floating aggregate was `123456789012346m`.
- GREEN: 1 passed after limiting floating aggregates to `Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite"`; the normal/InMemory and production SQL Server paths now use decimal `Sum` for OEE, total inventory, and zone totals.
- No package change was made; provider detection does not require adding SQLite to the web project.

### Fixed clock in the primary dashboard regression

Command:

```powershell
dotnet test WmsMes.Tests\WmsMes.Tests.csproj --filter FullyQualifiedName~Metrics_CalculatesOeeAlertsDailyOutputAndZoneInventory --no-restore
```

- RED: 1 failed after changing expectations to the fixed `15/01/2026` business date while the test still used the system clock; actual labels were for July.
- GREEN: 1 passed after injecting `FixedTimeProvider` and the explicit UTC+7 test timezone. The test no longer reads `DateTime.Today`.

### Focused regression after all re-review fixes

```powershell
dotnet test WmsMes.Tests\WmsMes.Tests.csproj --filter FullyQualifiedName~DashboardMetricsTests --no-restore
```

Result: passed — 6 passed, 0 failed, 0 skipped (exit code 0).

### Fresh full verification after re-review fixes

```powershell
dotnet build WmsMes.sln
dotnet test WmsMes.sln
```

Results: build succeeded with 0 warnings and 0 errors; full suite passed — 112 passed, 0 failed, 0 skipped (both exit code 0).
