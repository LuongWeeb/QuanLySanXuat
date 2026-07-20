# [Feature] Dashboard OEE & Visual Charts (Chart.js) Implementation Plan

> **For agentic workers (Codex / Antigravity):** REQUIRED SUB-SKILL: Use TDD & step-by-step verification. Follow exact file paths and test execution commands. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Nâng cấp Bảng điều khiển (Dashboard) của hệ thống WMS-MES với các chỉ số hiệu suất thiết bị toàn phần OEE (Khả năng sẵn sàng, Hiệu suất, Chất lượng), thẻ cảnh báo tồn kho an toàn và hệ thống biểu đồ tương tác thời gian thực với Chart.js.

**Architecture:** Mở rộng `DashboardViewModel` với các cấu trúc dữ liệu OEE & Chart DTOs. Tính toán động các tỷ lệ OEE và số liệu thống kê trong `HomeController.GetMetricsAsync()`. Tích hợp thư viện Chart.js ở giao diện View (`Views/Home/Index.cshtml`), tự động đồng bộ qua SignalR (`productionHub` và `inventoryHub`).

**Tech Stack:** ASP.NET Core 8 MVC, Chart.js 4.x (CDN), SignalR Realtime, xUnit (.NET 8).

---

## Global Constraints
- Target Framework: `.NET 8` (`net8.0`)
- Giữ nguyên các hàm đồng bộ SignalR real-time sẵn có trong `Index.cshtml`.
- Đảm bảo 100% các unit test cũ và mới vượt qua.

---

### Task 1: Mở rộng DashboardViewModel & Logic Tính toán OEE Metrics

**Files:**
- Modify: `ViewModels/DashboardViewModel.cs`
- Modify: `Controllers/HomeController.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext`
- Produces: `DashboardViewModel` chứa dữ liệu chỉ số OEE, cảnh báo tồn kho & dữ liệu chuỗi thời gian cho Biểu đồ Chart.js.

- [ ] **Step 1: Mở rộng ViewModels/DashboardViewModel.cs**

```csharp
namespace WmsMes.Web.ViewModels;

public sealed class DashboardViewModel
{
    // Chỉ số vận hành chung
    public int ActiveWorkOrders { get; init; }
    public int PendingQcLots { get; init; }
    public decimal InventoryVolume { get; init; }
    public int LowStockAlertCount { get; init; }

    // Chỉ số OEE (Overall Equipment Effectiveness)
    public decimal OeeAvailabilityPercent { get; init; }
    public decimal OeePerformancePercent { get; init; }
    public decimal OeeQualityPercent { get; init; }
    public decimal OverallOeePercent => Math.Round((OeeAvailabilityPercent * OeePerformancePercent * OeeQualityPercent) / 10000m, 1);

    // Dữ liệu Biểu đồ Sản lượng 7 ngày
    public List<string> DailyLabels { get; init; } = new();
    public List<decimal> DailyPlannedOutput { get; init; } = new();
    public List<decimal> DailyActualOutput { get; init; } = new();

    // Dữ liệu Biểu đồ Phân bổ Tồn kho theo Khu vực (Zone)
    public List<string> ZoneLabels { get; init; } = new();
    public List<decimal> ZoneQuantities { get; init; } = new();

    // Dữ liệu Biểu đồ Chất lượng (Pass / Hold / Quarantine)
    public int PassedQcCount { get; init; }
    public int HoldQcCount { get; init; }
    public int QuarantineQcCount { get; init; }
}
```

- [ ] **Step 2: Cập nhật HomeController.cs tính toán số liệu OEE & Chart Data**

Cập nhật phương thức `GetMetricsAsync()` trong `Controllers/HomeController.cs`:
- Tính `LowStockAlertCount`: đếm các dòng `StockBalance` có `QtyAvailable <= 10`.
- Tính chỉ số OEE dựa trên dữ liệu lệnh sản xuất (`WorkOrders`):
  - **Availability**: Tỷ lệ WorkOrders hoàn thành/đang chạy so với tổng số lệnh.
  - **Performance**: Tỷ lệ `ProducedQty` so với `TargetQty` của các lệnh.
  - **Quality**: Tỷ lệ sản phẩm đạt chất lượng `(ProducedQty - ScrapQty) / ProducedQty`.
- Lấy sản lượng 7 ngày gần nhất cho `DailyLabels`, `DailyPlannedOutput`, `DailyActualOutput`.
- Thống kê khối lượng tồn kho theo `Zone.Name` cho `ZoneLabels` & `ZoneQuantities`.

- [ ] **Step 3: Test Build hệ thống**

Run: `dotnet build WmsMes.sln`
Expected: `Build succeeded. 0 Warning(s), 0 Error(s)`

---

### Task 2: Thiết kế Bảng điều khiển & Tích hợp Chart.js trên View

**Files:**
- Modify: `Views/Home/Index.cshtml`

**Interfaces:**
- Consumes: CDN Chart.js `https://cdn.jsdelivr.net/npm/chart.js` & `DashboardViewModel`
- Produces: Màn hình Dashboard tương tác với KPI Cards OEE, Cảnh báo tồn kho & 3 Biểu đồ trực quan (Line/Bar & Doughnut).

- [ ] **Step 1: Thêm Thẻ KPI OEE & Cảnh báo vào Views/Home/Index.cshtml**

Thêm các thẻ Metric hiển thị 4 chỉ số OEE (OEE Tổng thể, Availability %, Performance %, Quality %) cùng thẻ Cảnh báo Tồn kho tối thiểu:
```html
<section class="metric-grid mt-4" aria-label="Chỉ số OEE Sản xuất">
    <article class="metric-card oee-card">
        <span>OEE Tổng thể</span>
        <strong id="overallOee">@Model.OverallOeePercent%</strong>
        <small class="text-muted">Mục tiêu: ≥ 85%</small>
    </article>
    <article class="metric-card">
        <span>Sẵn sàng (Availability)</span>
        <strong id="oeeAvailability">@Model.OeeAvailabilityPercent%</strong>
        <small class="text-muted">Thời gian chạy máy</small>
    </article>
    <article class="metric-card">
        <span>Hiệu suất (Performance)</span>
        <strong id="oeePerformance">@Model.OeePerformancePercent%</strong>
        <small class="text-muted">Tốc độ sản xuất</small>
    </article>
    <article class="metric-card">
        <span>Chất lượng (Quality)</span>
        <strong id="oeeQuality">@Model.OeeQualityPercent%</strong>
        <small class="text-muted">Tỷ lệ thành phẩm đạt</small>
    </article>
</section>
```

- [ ] **Step 2: Thêm Vùng chứa Biểu đồ Chart.js**

Thêm Layout Grid chứa 2 Biểu đồ chính:
1. **Biểu đồ Sản lượng Sản xuất 7 Ngày (Line/Bar Chart)**.
2. **Biểu đồ Phân bổ Tồn kho theo Khu vực (Doughnut Chart)**.

```html
<section class="row mt-4">
    <div class="col-md-8">
        <div class="card p-3 shadow-sm">
            <h4 class="h6 font-weight-bold mb-3">📈 Sản lượng Sản xuất 7 ngày gần nhất</h4>
            <canvas id="productionChart" style="max-height: 300px;"></canvas>
        </div>
    </div>
    <div class="col-md-4">
        <div class="card p-3 shadow-sm">
            <h4 class="h6 font-weight-bold mb-3">🏭 Phân bổ Tồn kho theo Zone</h4>
            <canvas id="inventoryZoneChart" style="max-height: 300px;"></canvas>
        </div>
    </div>
</section>
```

- [ ] **Step 3: Viết Script khởi tạo Chart.js & Đồng bộ SignalR Realtime**

Thêm CDN Chart.js: `<script src="https://cdn.jsdelivr.net/npm/chart.js"></script>`
Khởi tạo instance `productionChart` & `inventoryZoneChart`.
Khi SignalR phát sự kiện `refreshMetrics`, gọi API `GET /Home/Metrics` và gọi `chart.update()` để cập nhật dữ liệu mượt mà không load lại trang.

- [ ] **Step 4: Verify giao diện và build**

Run: `dotnet build WmsMes.sln`
Expected: `Build succeeded`

---

### Task 3: Viết Unit Tests cho Dashboard Metrics Logic

**Files:**
- Create: `WmsMes.Tests/DashboardMetricsTests.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext` (InMemory) & `HomeController`
- Produces: Kiểm thử đơn vị tự động xác nhận tính chính xác của công thức tính OEE và thống kê tồn kho.

- [ ] **Step 1: Viết Unit Test kiểm tra công thức tính OEE**

```csharp
[Fact]
public async Task GetMetricsAsync_ReturnsCorrectOeeMetricsAndCharts()
{
    // Arrange: Tạo DB InMemory với dữ liệu WorkOrder mẫu & StockBalance
    // Act: Gọi HomeController.Metrics()
    // Assert: Xác minh OverallOeePercent, ActiveWorkOrders, và độ dài DailyLabels không null
}
```

- [ ] **Step 2: Chạy kiểm thử toàn bộ suite**

Run: `dotnet test WmsMes.sln`
Expected: `Passed! - All tests pass`

---

## Verification Plan

### Automated Tests
- Chạy `dotnet test WmsMes.sln` đảm bảo tất cả Unit Tests (bao gồm test mới cho Dashboard OEE) vượt qua 100%.

### Manual Verification
- Khởi chạy ứng dụng `dotnet run --project WmsMes.Web.csproj`.
- Mở trang chủ `https://localhost:<port>/` và kiểm tra các thẻ OEE %, biểu đồ sản lượng 7 ngày và biểu đồ phân bổ tồn kho hiển thị trực quan.
