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

## Review fixes

- Production and inventory connections now start and retry independently. Initial failures retry with exponential delay capped at 30 seconds; one unavailable hub no longer suppresses the other hub's state.
- Connectivity and metrics-refresh states are rendered separately.
- Each metrics refresh aborts its predecessor and uses a monotonically increasing generation, preventing aborted or older responses from updating the cards.
- `QcService` catches and logs post-commit hub failures independently, preserving the committed PASS/REJECT result and allowing the other notification channel to proceed.
- `InventoryHub` and `ProductionHub` now require authenticated connections; tests verify authorization metadata and mapped route contracts.
- `/Home/Metrics` explicitly disables response caching.
- No local SignalR browser client was found under `wwwroot`. The existing cdnjs 8.0.0 reference remains; no SRI value was added because an integrity hash was not locally verifiable.

### Review TDD evidence

The focused review test run first failed with `CS1729` because the requested logger-aware `QcService` constructor did not exist. After implementation, the focused controller/notification set passed 8/8 tests, including PASS and REJECT notification-failure persistence cases.
