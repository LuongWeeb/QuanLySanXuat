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

- SQLite cannot translate `Sum(decimal)`. Inventory SQL aggregates cast the decimal expression to the provider-supported floating type, then convert the result back to decimal for the view model. The database columns remain `decimal(18,2)`, and the relational regression covers fractional aggregate results. SQL Server also translates this shape.
- An invalid configured timezone identifier fails during dependency resolution rather than silently using server-local time. This keeps the business-time contract explicit.
