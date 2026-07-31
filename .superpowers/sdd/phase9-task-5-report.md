# Phase 9 — Task 5 Report: Pick List UI and Notification Header

## Delivered

- Added `PickListController` for `Admin`, `Warehouse`, and `Manager` roles:
  - `Index` lists newest pick lists with sales-order/customer context.
  - `Create` renders sales-order choices and accepts only the scalar `salesOrderId`.
  - POST handles missing selection and a sales order that disappears before creation; it delegates creation to `IPickListService`, stores a status message, and redirects to `Details`.
  - `Details` loads the pick route, product, location/zone, lot, and sales-order context; unknown IDs return 404.
- Added responsive Bootstrap 5 views for Pick List index/create/details and stock valuation. Tables have captions, empty states, clear primary actions, and action links. The stock valuation export button targets `ReportController.ExportStockValuationExcel`.
- Added the authenticated notification bell to the shared layout. The initial unread count and five newest entries are fetched dynamically from `INotificationService`; anonymous pages make no notification service/database calls.
- The local SignalR client asset connects to `/notificationHub`, subscribes to `ReceiveNotification`, updates the badge/list, announces new content through an accessible live region, uses automatic reconnect, and retries failed initial connections with capped backoff.
- Added sidebar links for Pick List and Báo cáo Tài chính Kho with active styling.

## Safety and accessibility decisions

- No external CDN was added: SignalR loads from `wwwroot/lib/microsoft-signalr/8.0.0/signalr.min.js`.
- Server-rendered notification title/message values remain Razor-encoded. Client-created notification elements use `textContent`, never HTML injection.
- Notification reference URLs are only rendered/navigated when they are safe local absolute paths (`/…`, not `//…` and without backslashes).
- Bell control has an accessible label; badge count has an accessible label; notification updates use `aria-live`; data tables include hidden captions and scoped table headers. Existing Bootstrap focus styling remains in force.

## TDD and verification

### RED

1. `PickListPages_RejectUnauthenticatedAndUnauthorizedUsers` initially failed with `404 NotFound` for all three Pick List routes because `PickListController` did not exist.
2. `AuthenticatedLayout_RendersDynamicUnreadBadgeAndRecentNotificationLinks` initially failed because `_Layout.cshtml` did not render `id="unread-count"`.

### GREEN

1. `dotnet test WmsMes.Tests\WmsMes.Tests.csproj --filter FullyQualifiedName~PickListUiIntegrationTests --no-restore`
   - Passed: 12
2. `dotnet build WmsMes.sln --no-restore`
   - Passed with 0 warnings and 0 errors.
3. `dotnet test WmsMes.Tests\WmsMes.Tests.csproj --no-restore --no-build`
   - Passed: 654, Failed: 0.
   - The output includes expected JWT options-validation logs from negative startup-validation tests; the suite itself passed.

`PickListUiIntegrationTests` covers route authorization, controller POST validation/success/not-found outcomes, 404 details behavior, authenticated rendered pages, dynamic unread count/recent local link rendering, and the layout SignalR/menu contract (including no hardcoded badge count).

## Files

- `Controllers/PickListController.cs`
- `Views/PickList/Index.cshtml`
- `Views/PickList/Create.cshtml`
- `Views/PickList/Details.cshtml`
- `Views/Report/StockValuation.cshtml`
- `Views/Shared/_Layout.cshtml`
- `wwwroot/css/site.css`
- `WmsMes.Tests/PickListUiIntegrationTests.cs`

## Scope

No PackingSlip workflow, webhook, Telegram integration, or remote dependencies were added.
