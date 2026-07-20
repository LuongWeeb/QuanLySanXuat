# Task 1 report — Dashboard OEE & chart metrics

## Status

Complete.

## Implemented

- Step 1: extended `DashboardViewModel` with the specified operational, OEE, production-chart, zone-chart, and QC-count properties.
- Step 2: expanded `HomeController.GetMetricsAsync()` for low-stock alerts, OEE, seven calendar-day planned/actual output, and zone inventory quantities.
- Added the focused controller regression test in `WmsMes.Tests/DashboardMetricsTests.cs`.
- Applied the approved domain mapping: `WorkOrder.Qty` is target; final-step `QtyOK + QtyReject` is produced; final-step `QtyReject` is scrap; planned output uses `DueDate`; actual output uses final-step `EndTime` and `QtyOK`.

## Verification evidence

### Step 1

Command:

```powershell
dotnet test WmsMes.sln
```

Result: passed — 102 passed, 0 failed, 0 skipped (exit code 0).

### Step 2 — RED

Command:

```powershell
dotnet test WmsMes.Tests\WmsMes.Tests.csproj --filter FullyQualifiedName~DashboardMetricsTests
```

Result: failed as expected — the new regression test expected `LowStockAlertCount` of 2 but the unimplemented controller returned 0 (1 failed, exit code 1).

### Step 2 — GREEN

Command:

```powershell
dotnet test WmsMes.Tests\WmsMes.Tests.csproj --filter FullyQualifiedName~DashboardMetricsTests
```

Result: passed — 1 passed, 0 failed (exit code 0).

Required full-suite command:

```powershell
dotnet test WmsMes.sln
```

Result: passed — 103 passed, 0 failed, 0 skipped (exit code 0).

### Final verification after self-review cleanup

The first combined re-run was blocked by sandbox access to `%AppData%\NuGet\NuGet.Config`; no code error was reported. Re-ran each required command with approved profile access:

```powershell
dotnet build WmsMes.sln
dotnet test WmsMes.sln
```

Result: build succeeded with 0 warnings and 0 errors; tests passed — 103 passed, 0 failed, 0 skipped (both exit code 0).

### Step 3

Command:

```powershell
dotnet build WmsMes.sln
```

Result: build succeeded — 0 warnings, 0 errors (exit code 0).

Required full-suite command:

```powershell
dotnet test WmsMes.sln
```

Result: passed — 103 passed, 0 failed, 0 skipped (exit code 0).

## TDD evidence

The focused `DashboardMetricsTests.Metrics_CalculatesOeeAlertsDailyOutputAndZoneInventory` was written before Step 2 production logic. Its first run was RED for the expected missing low-stock calculation; after the minimal controller implementation it was GREEN. The initial planned-output expectation was corrected before the GREEN run to include every work order in the seven-day period, as specified by the approved `DueDate` mapping.

## Confirmed decision and residual concern

The brief's `TargetQty`, `ProducedQty`, `ScrapQty`, and production-date terms do not exist directly in `WorkOrder`; their approved mappings are documented above. Chart labels use the current server-local `DateTime.Today` in `dd/MM` format, which is a presentation choice for the seven-day data contract.

## Changed files

- `ViewModels/DashboardViewModel.cs`
- `Controllers/HomeController.cs`
- `WmsMes.Tests/DashboardMetricsTests.cs`
- `.superpowers/sdd/task-1-report.md`

## Self-review

- DTO property names, types, defaults, and `OverallOeePercent` formula match Step 1 verbatim.
- OEE uses the approved final-step quantities and explicitly returns 0 when no denominator exists.
- The seven-day series is chronological and zero-fills missing days; zone series is ordered by zone name for stable chart pairing.
- Existing SignalR code was not changed; `Metrics()` continues returning the dashboard view model.

## Review fix — QC dashboard counts

Reviewer feedback identified that the three QC-count DTO fields were not populated. The controller now maps them as follows:

- `PassedQcCount`: count of `QCInspections` whose `Result` is `PASS`.
- `HoldQcCount`: distinct lots with `QtyOnHold > 0` outside `QC-QUARANTINE`; `PendingQcLots` uses this same value.
- `QuarantineQcCount`: distinct lots with `QtyOnHold > 0` at `QC-QUARANTINE`.

### TDD RED

The focused dashboard regression test was expanded with one PASS inspection, two held balances for the same lot in normal locations, and one quarantined lot. Before production logic, the command below failed as expected because `PassedQcCount` was 0 instead of 1:

```powershell
dotnet test WmsMes.Tests\WmsMes.Tests.csproj --filter FullyQualifiedName~DashboardMetricsTests
```

Result: 1 failed, 0 passed (exit code 1).

### TDD GREEN and full verification

```powershell
dotnet test WmsMes.Tests\WmsMes.Tests.csproj --filter FullyQualifiedName~DashboardMetricsTests
dotnet test WmsMes.sln
```

Results: focused test passed — 1 passed, 0 failed; full suite passed — 103 passed, 0 failed, 0 skipped (both exit code 0).
