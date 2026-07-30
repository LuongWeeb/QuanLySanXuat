# Task 1 Report: DTOs and OeeService

## Scope delivered

- Added `OeeMetricsDto` and `InventoryAgingDto`.
- Added `IOeeService` and EF Core-backed `OeeService`.
- Registered `IOeeService` as scoped in `Program.cs`.
- Added focused OEE and inventory-aging tests.

## TDD evidence

### RED

Command:

```powershell
dotnet test WmsMes.Tests/WmsMes.Tests.csproj --filter FullyQualifiedName~OeeServiceTests --no-restore
```

Output summary:

```text
error CS0246: The type or namespace name 'OeeService' could not be found
```

The tests referenced the required service before that production type existed.

### GREEN

Command:

```powershell
dotnet test WmsMes.Tests/WmsMes.Tests.csproj --filter FullyQualifiedName~OeeServiceTests --no-restore
```

Output summary:

```text
Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3
```

Focused tests cover: completed-step period filtering and rounded OEE formula, active work-center filtering with success/warning thresholds, and the inclusive 30/60/90-day inventory-value buckets.

### Full verification

Command:

```powershell
dotnet test WmsMes.sln --no-restore
```

Output summary:

```text
Passed!  - Failed:     0, Passed:   521, Skipped:     0, Total:   521
```

The test-host output included pre-existing expected JWT options-validation log entries from negative host-start tests; the suite exit code was 0.

## Files

- `DTOs/OeeMetricsDto.cs`
- `Services/IOeeService.cs`
- `Services/OeeService.cs`
- `Program.cs`
- `WmsMes.Tests/OeeServiceTests.cs`

## Self-review

- Uses `WorkOrderStep.Status == Completed`, `EndTime` inside the inclusive requested period, and valid start/end timestamps.
- Planned operating time is 480 minutes per inclusive calendar day, with a one-day minimum.
- Uses the matching work order's captured routing version and its standard minutes per item for performance, caps all percentages at 100, uses 100 quality when nothing was produced, rounds returned percentages to one decimal, and maps rounded OEE to success/warning/danger.
- Inventory aging values use the full on-hand stock balance (`available + reserved + on-hold`) times the lot unit price, consistent with the existing dashboard inventory-volume convention.
- `git diff --check` produced no whitespace errors.

## Concerns

- Lots with no `ManufactureDate` are excluded from aging buckets because the schema permits a null date and an age cannot be calculated. Existing produced lots set the date, but historical data should be backfilled if it needs aging analytics.
