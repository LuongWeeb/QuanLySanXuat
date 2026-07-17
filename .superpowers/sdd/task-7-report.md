# Task 7 Report: Real-time Dashboard

## Outcome

- `HomeController.Index` now renders authoritative dashboard metrics from the database.
- `HomeController.Metrics` provides the same metrics as authenticated JSON for event-triggered refreshes.
- Active work orders are orders with `InProgress` status.
- Pending QC is the distinct count of lots with positive held quantity outside `QC-QUARANTINE`, preventing duplicate counts across locations.
- Inventory volume is physical stock: `QtyAvailable + QtyReserved + QtyOnHold` across every stock balance, including quarantined stock.
- The dashboard connects once to `/productionHub` and `/inventoryHub`, handles `ReceiveProgressUpdate` and `ReceiveStockUpdate`, debounces refreshes, and updates text safely without reloading the page.
- `QcService` emits the existing inventory event after successful PASS or REJECT processing, so QC stock transitions also refresh the dashboard.

## TDD evidence

### RED

`dotnet test WmsMes.Tests/WmsMes.Tests.csproj --no-restore --filter FullyQualifiedName~HomeControllerTests`

Failed as expected because `HomeController` did not accept `ApplicationDbContext` and `DashboardViewModel` did not exist (`CS1729`, `CS0246`).

### GREEN

The same focused command passed 3/3 tests after the minimal controller, view model, endpoint, and view implementation.

## Verification

See the final task handoff for fresh full-test and build results.
