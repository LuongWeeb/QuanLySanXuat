# Task 2 report — Dashboard OEE, Chart.js và realtime refresh

## Phạm vi thực hiện

- `Views/Home/Index.cshtml`: KPI OEE, cảnh báo low stock, ba biểu đồ Chart.js và đồng bộ dữ liệu từ `/Home/Metrics`.
- `WmsMes.Tests/HomeControllerTests.cs`: source/render contract tests cho markup, CDN/serialization, và chart refresh.

## Step 1 — KPI OEE và cảnh báo low stock

- RED: thêm `DashboardView_RendersOeeAndLowStockMetricCards`, sau đó chạy
  `dotnet test WmsMes.sln --filter "FullyQualifiedName~DashboardView_RendersOeeAndLowStockMetricCards" --no-restore`.
  Kết quả RED đúng kỳ vọng: thiếu `aria-label="Chỉ số OEE Sản xuất"` trong `Index.cshtml`.
- GREEN: thêm năm metric cards với `overallOee`, ba OEE component, và `lowStockAlertCount`.
- Focused GREEN: 1/1 pass.
- Full verification: `dotnet test WmsMes.sln --no-restore` — 104 passed, 0 failed.

## Step 2 — vùng chứa biểu đồ

- RED: thêm `DashboardView_RendersAccessibleProductionInventoryAndQualityCharts`, sau đó chạy
  `dotnet test WmsMes.sln --filter "FullyQualifiedName~DashboardView_RendersAccessibleProductionInventoryAndQualityCharts" --no-restore`.
  Kết quả RED đúng kỳ vọng: thiếu `id="productionChart"`.
- GREEN: thêm Bootstrap responsive grid với production, inventory-zone và quality cards; canvas có `role="img"`, `aria-label`, fallback text.
- Focused GREEN: 1/1 pass.
- Full verification: `dotnet test WmsMes.sln --no-restore` — 105 passed, 0 failed.

## Step 3 — Chart.js và SignalR refresh

- RED: thêm `DashboardView_InitializesAndRefreshesAllChartsWithSafelySerializedMetrics`, sau đó chạy
  `dotnet test WmsMes.sln --filter "FullyQualifiedName~DashboardView_InitializesAndRefreshesAllChartsWithSafelySerializedMetrics" --no-restore`.
  Kết quả RED đúng kỳ vọng: thiếu CDN Chart.js.
- GREEN: thêm CDN `https://cdn.jsdelivr.net/npm/chart.js`; serialize Razor data bằng `System.Text.Json.JsonSerializer` (default encoder) rồi render JSON an toàn qua `Html.Raw`; khởi tạo một line/bar chart và hai doughnut charts.
- `refreshMetrics` giữ cơ chế fetch/AbortController/generation guard cũ, đồng thời cập nhật KPI hiện có, OEE, low stock, labels/datasets cho ba chart và gọi `update()` trên từng instance. Hai hubs, reconnect/retry/debounce giữ nguyên.
- Focused GREEN: 1/1 pass.
- Full verification: `dotnet test WmsMes.sln --no-restore` — 106 passed, 0 failed.

## Step 4 — build và kiểm chứng cuối

- `dotnet build WmsMes.sln` — Build succeeded, 0 warnings, 0 errors.
- `dotnet test WmsMes.sln --no-restore` — 106 passed, 0 failed.
- `git diff --check` — sạch (không có whitespace errors).

## Concerns

- Không có blocker hoặc known functional issue. CDN Chart.js phụ thuộc kết nối mạng của client như yêu cầu.
- Code review độc lập: không có finding critical, important hoặc minor; reviewer cũng chạy `dotnet test WmsMes.Tests/WmsMes.Tests.csproj --no-restore` với 106/106 pass.
