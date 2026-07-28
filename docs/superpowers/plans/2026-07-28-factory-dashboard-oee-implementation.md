# Factory KPI & Realtime OEE Dashboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Triển khai Màn hình Dashboard Giám sát Nhà máy & Báo cáo OEE theo Thời gian thực (`OeeService`, `DashboardController`, Chart.js), hiển thị chỉ số Hiệu suất Thiết bị Tổng thể (OEE = Availability x Performance x Quality) theo từng trạm sản xuất, biểu đồ sản lượng thực tế vs định mức, biểu đồ phế phẩm và tuổi tồn kho.

**Architecture:** Tạo DTO `OeeMetricsDto` và dịch vụ `OeeService` thực hiện các phép tính OEE và phân tích tồn kho. Xây dựng `DashboardController` cung cấp API dữ liệu cho Chart.js và giao diện Razor View tích hợp SignalR cập nhật số liệu realtime.

**Tech Stack:** ASP.NET Core 8 MVC, Entity Framework Core 8, Chart.js, SignalR, Bootstrap 5.

## Global Constraints
- Target Framework: `.NET 8` net8.0.
- Công thức OEE: $\text{OEE} = \text{Availability} \times \text{Performance} \times \text{Quality}$.
- Mức OEE $\ge 85\%$ hiển thị màu Xanh lá, $65\% - 85\%$ hiển thị màu Vàng, $< 65\%$ hiển thị màu Đỏ.

---

### Task 1: Xây dựng DTOs & Dịch vụ OeeService

**Files:**
- Create: [OeeMetricsDto.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/DTOs/OeeMetricsDto.cs)
- Create: [IOeeService.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Services/IOeeService.cs)
- Create: [OeeService.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Services/OeeService.cs)
- Modify: [Program.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Program.cs)

**Interfaces:**
- Consumes: Database schema hiện có.
- Produces: Dịch vụ `OeeService` cung cấp dữ liệu tính toán OEE và các chỉ số sản xuất.

- [ ] **Step 1: Tạo OeeMetricsDto.cs**
  Tạo `DTOs/OeeMetricsDto.cs`:
  ```csharp
  namespace WmsMes.Web.DTOs;

  public class OeeMetricsDto
  {
      public int WorkCenterId { get; set; }
      public string WorkCenterCode { get; set; } = string.Empty;
      public string WorkCenterName { get; set; } = string.Empty;
      public decimal Availability { get; set; } // %
      public decimal Performance { get; set; }  // %
      public decimal Quality { get; set; }      // %
      public decimal Oee { get; set; }          // %
      public string StatusColor => Oee >= 85m ? "success" : (Oee >= 65m ? "warning" : "danger");
  }

  public class InventoryAgingDto
  {
      public decimal LessThan30Days { get; set; }
      public decimal Days30To60 { get; set; }
      public decimal Days60To90 { get; set; }
      public decimal MoreThan90Days { get; set; }
  }
  ```

- [ ] **Step 2: Tạo IOeeService.cs**
  Khai báo interface:
  ```csharp
  using WmsMes.Web.DTOs;

  namespace WmsMes.Web.Services;

  public interface IOeeService
  {
      Task<OeeMetricsDto> GetWorkCenterOeeAsync(int workCenterId, DateTime startDate, DateTime endDate);
      Task<IEnumerable<OeeMetricsDto>> GetAllWorkCentersOeeAsync(DateTime startDate, DateTime endDate);
      Task<InventoryAgingDto> GetInventoryAgingAnalyticsAsync();
  }
  ```

- [ ] **Step 3: Triển khai OeeService.cs**
  Viết các thuật toán tính toán OEE theo đúng công thức:
  ```csharp
  using Microsoft.EntityFrameworkCore;
  using WmsMes.Web.Data;
  using WmsMes.Web.DTOs;

  namespace WmsMes.Web.Services;

  public class OeeService : IOeeService
  {
      private readonly ApplicationDbContext _context;

      public OeeService(ApplicationDbContext context)
      {
          _context = context;
      }

      public async Task<OeeMetricsDto> GetWorkCenterOeeAsync(int workCenterId, DateTime startDate, DateTime endDate)
      {
          var wc = await _context.WorkCenters.FindAsync(workCenterId);
          if (wc == null) return new OeeMetricsDto();

          var steps = await _context.WorkOrderSteps
              .Where(s => s.WorkCenterId == workCenterId && s.StartTime >= startDate && s.EndTime <= endDate)
              .ToListAsync();

          decimal actualOperatingMins = 0m;
          decimal totalProduced = 0m;
          decimal totalOk = 0m;

          foreach (var step in steps)
          {
              if (step.StartTime.HasValue && step.EndTime.HasValue)
              {
                  actualOperatingMins += (decimal)(step.EndTime.Value - step.StartTime.Value).TotalMinutes;
              }
              totalProduced += (step.QtyOK + step.QtyReject + step.QtyRework);
              totalOk += step.QtyOK;
          }

          // Planned Available Minutes: Mặc định 480 phút/ngày
          var totalDays = Math.Max(1, (endDate.Date - startDate.Date).Days + 1);
          decimal plannedMins = totalDays * 480m;

          decimal availability = plannedMins > 0 ? Math.Min(100m, (actualOperatingMins / plannedMins) * 100m) : 0m;
          
          // Target Capacity: Giả định 1 sản phẩm/phút hoặc tính theo Routing
          decimal targetQty = actualOperatingMins > 0 ? actualOperatingMins : 1m;
          decimal performance = targetQty > 0 ? Math.Min(100m, (totalProduced / targetQty) * 100m) : 0m;

          decimal quality = totalProduced > 0 ? (totalOk / totalProduced) * 100m : 100m;

          decimal oee = (availability / 100m) * (performance / 100m) * (quality / 100m) * 100m;

          return new OeeMetricsDto
          {
              WorkCenterId = wc.Id,
              WorkCenterCode = wc.Code,
              WorkCenterName = wc.Name,
              Availability = Math.Round(availability, 1),
              Performance = Math.Round(performance, 1),
              Quality = Math.Round(quality, 1),
              Oee = Math.Round(oee, 1)
          };
      }

      public async Task<IEnumerable<OeeMetricsDto>> GetAllWorkCentersOeeAsync(DateTime startDate, DateTime endDate)
      {
          var wcs = await _context.WorkCenters.Where(w => w.IsActive).ToListAsync();
          var results = new List<OeeMetricsDto>();

          foreach (var wc in wcs)
          {
              results.Add(await GetWorkCenterOeeAsync(wc.Id, startDate, endDate));
          }

          return results;
      }

      public async Task<InventoryAgingDto> GetInventoryAgingAnalyticsAsync()
      {
          var now = DateTime.UtcNow;
          var lots = await _context.Lots.Include(l => l.Product).ToListAsync();

          decimal less30 = 0m, d30to60 = 0m, d60to90 = 0m, more90 = 0m;

          foreach (var lot in lots)
          {
              var ageDays = (now - lot.ManufactureDate).TotalDays;
              var val = lot.Qty * lot.UnitPrice;

              if (ageDays <= 30) less30 += val;
              else if (ageDays <= 60) d30to60 += val;
              else if (ageDays <= 90) d60to90 += val;
              else more90 += val;
          }

          return new InventoryAgingDto
          {
              LessThan30Days = Math.Round(less30, 2),
              Days30To60 = Math.Round(d30to60, 2),
              Days60To90 = Math.Round(d60to90, 2),
              MoreThan90Days = Math.Round(more90, 2)
          };
      }
  }
  ```

- [ ] **Step 4: Đăng ký dịch vụ trong Program.cs**
  ```csharp
  builder.Services.AddScoped<IOeeService, OeeService>();
  ```

- [ ] **Step 5: Commit**
  Run: `git add DTOs/ Services/ Program.cs`
  Run: `git commit -m "feat: implement OeeService with OEE formulas and inventory aging analytics"`

---

### Task 2: Viết Unit Tests Cho Công Thức OEE

**Files:**
- Create: [OeeServiceTests.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/WmsMes.Tests/OeeServiceTests.cs)

**Interfaces:**
- Consumes: `OeeService`.
- Produces: Kết quả kiểm thử tự động PASS.

- [ ] **Step 1: Viết test CalculateOee_ReturnsCorrectPercentages**
  Tạo bài test kiểm tra công thức tính Availability, Performance, Quality và OEE tổng thể.

- [ ] **Step 2: Chạy Unit Tests**
  Run: `dotnet test WmsMes.Tests/WmsMes.Tests.csproj`
  Expected: PASS tất cả bài test.

- [ ] **Step 3: Commit**
  Run: `git add WmsMes.Tests/OeeServiceTests.cs`
  Run: `git commit -m "test: add unit tests for OEE calculations and metrics"`

---

### Task 3: Xây dựng Bộ điều khiển DashboardController & API

**Files:**
- Create: [DashboardController.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Controllers/DashboardController.cs)

**Interfaces:**
- Consumes: `IOeeService`.
- Produces: Các API JSON trả về dữ liệu OEE, Tuổi tồn kho và View Dashboard.

- [ ] **Step 1: Tạo DashboardController.cs**
  Xây dựng các action trả về View và JSON Data:
  ```csharp
  using Microsoft.AspNetCore.Authorization;
  using Microsoft.AspNetCore.Mvc;
  using WmsMes.Web.Services;

  namespace WmsMes.Web.Controllers;

  [Authorize]
  public class DashboardController : Controller
  {
      private readonly IOeeService _oeeService;

      public DashboardController(IOeeService oeeService)
      {
          _oeeService = oeeService;
      }

      public IActionResult Index()
      {
          return View();
      }

      [HttpGet]
      public async Task<IActionResult> GetOeeData()
      {
          var endDate = DateTime.UtcNow;
          var startDate = endDate.AddDays(-7);
          var data = await _oeeService.GetAllWorkCentersOeeAsync(startDate, endDate);
          return Json(data);
      }

      [HttpGet]
      public async Task<IActionResult> GetAgingData()
      {
          var data = await _oeeService.GetInventoryAgingAnalyticsAsync();
          return Json(data);
      }
  }
  ```

- [ ] **Step 2: Commit**
  Run: `git add Controllers/DashboardController.cs`
  Run: `git commit -m "feat: implement DashboardController API endpoints for OEE and aging"`

---

### Task 4: Thiết kế Giao diện View Dashboard & Chart.js

**Files:**
- Create View: [Dashboard/Index.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/Dashboard/Index.cshtml)
- Modify: [Views/Shared/_Layout.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/Shared/_Layout.cshtml)

**Interfaces:**
- Consumes: Chart.js từ CDN và SignalR.
- Produces: Màn hình Dashboard quản trị nhà máy trực quan.

- [ ] **Step 1: Tạo View Dashboard/Index.cshtml**
  Thiết kế giao diện Dashboard với các thẻ card chỉ số OEE của từng WorkCenter và các biểu đồ Chart.js (Bar chart sản lượng, Donut chart tuổi tồn kho):
  ```html
  <script src="https://cdn.jsdelivr.net/npm/chart.js"></script>

  <div class="row mb-4">
      <div class="col-12">
          <h2>Dashboard Giám sát Nhà máy & Báo cáo OEE</h2>
      </div>
  </div>

  <!-- Thẻ danh sách OEE WorkCenter -->
  <div id="oee-cards-container" class="row mb-4"></div>

  <!-- Hàng chứa các biểu đồ Chart.js -->
  <div class="row">
      <div class="col-md-6 mb-4">
          <div class="card h-100">
              <div class="card-header bg-primary text-white"><h5>Phân tích Tuổi Tồn kho (Inventory Aging)</h5></div>
              <div class="card-body">
                  <canvas id="agingChart"></canvas>
              </div>
          </div>
      </div>
  </div>

  <script>
      // Viết script fetch dữ liệu từ /Dashboard/GetOeeData và /Dashboard/GetAgingData để render Chart.js
  </script>
  ```

- [ ] **Step 2: Thêm Menu vào Sidebar Layout**
  Mở `Views/Shared/_Layout.cshtml` và chèn mục menu "Dashboard Nhà máy & OEE" dưới phần Tổng quan.

- [ ] **Step 3: Kiểm tra biên dịch dự án**
  Run: `dotnet build`
  Expected: Build thành công không có lỗi.

- [ ] **Step 4: Commit**
  Run: `git add Views/`
  Run: `git commit -m "feat: complete UI view with Chart.js graphs for factory dashboard and OEE"`
