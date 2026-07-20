# Kế hoạch thực hiện: Tích hợp Giao diện & Tương tác người dùng (UI/Frontend Integration Phase)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Xây dựng hệ thống giao diện Web hoàn chỉnh cho các nghiệp vụ WMS & MES đã được viết dưới lớp Service, chia thành 3 giai đoạn để dễ kiểm soát và thử nghiệm.

**Architecture:** Sử dụng kiến trúc MVC. Các Controller tiếp nhận dữ liệu từ các Business Services hiện có. Dữ liệu hiển thị ra Razor Views bằng Bootstrap 5. Tích hợp SignalR Client để giao diện Dashboard trang chủ tự động cập nhật số liệu.

**Tech Stack:** ASP.NET Core MVC (.NET 8), EF Core, SQL Server, SignalR, Bootstrap 5, Chart.js, xUnit, Moq.

## Global Constraints
- Hệ điều hành: Windows
- Tên dự án: WmsMes.Web
- Kiểm thử: Viết unit/integration tests bằng xUnit trong dự án `WmsMes.Tests`
- Công nghệ UI: Bootstrap 5 (không dùng Tailwind CSS)
- Kiểm tra phân quyền (Role-based Authorization) trên từng Controller/Action tương ứng.

---

## GIAI ĐOẠN 1: Quản lý Kho hàng (WMS UI)

### Task 1: Giao diện Nhập kho (Goods Receipt)

**Files:**
- Modify: [InventoryController.cs](file:///d:/Quản lý sản xuất/Controllers/InventoryController.cs)
- Create: `Views/Inventory/Receipts.cshtml`
- Create: `Views/Inventory/CreateReceipt.cshtml`
- Create: [InventoryControllerTests.cs](file:///d:/Quản lý sản xuất/WmsMes.Tests/InventoryControllerTests.cs)

**Interfaces:**
- Consumes: `IInventoryService.CompleteGoodsReceiptAsync` để nhập kho.
- Produces: Danh sách phiếu nhập kho và form tạo phiếu nhập mới.

- [ ] **Step 1: Viết test cho endpoint Receipts của InventoryController**
  Tạo `WmsMes.Tests/InventoryControllerTests.cs`:
  ```csharp
  using Microsoft.AspNetCore.Mvc;
  using Microsoft.EntityFrameworkCore;
  using System.Threading.Tasks;
  using WmsMes.Web.Controllers;
  using WmsMes.Web.Data;
  using WmsMes.Web.Domain.Entities;
  using Xunit;

  namespace WmsMes.Tests
  {
      public class InventoryControllerTests
      {
          [Fact]
          public async Task Receipts_ReturnsViewWithReceipts()
          {
              var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                  .UseInMemoryDatabase(databaseName: "Inv_Receipts_Db")
                  .Options;

              using (var context = new ApplicationDbContext(options))
              {
                  context.GoodsReceipts.Add(new GoodsReceipt { ReceiptNo = "GR-1", ProductId = 1, Qty = 50, SupplierId = 1, LocationId = 1 });
                  await context.SaveChangesAsync();
              }

              using (var context = new ApplicationDbContext(options))
              {
                  var controller = new InventoryController(context);
                  var result = await controller.Receipts();

                  var viewResult = Assert.IsType<ViewResult>(result);
                  var model = Assert.IsAssignableFrom<System.Collections.Generic.IEnumerable<GoodsReceipt>>(viewResult.Model);
                  Assert.Single(model);
              }
          }
      }
  }
  ```

- [ ] **Step 2: Chạy test và xác nhận thất bại**
  Chạy: `dotnet test --filter "FullyQualifiedName=WmsMes.Tests.InventoryControllerTests.Receipts_ReturnsViewWithReceipts"`
  Kết quả mong đợi: Thất bại do chưa khai báo action `Receipts` trong `InventoryController`.

- [ ] **Step 3: Cập nhật InventoryController**
  Thêm các action sau vào [InventoryController.cs](file:///d:/Quản lý sản xuất/Controllers/InventoryController.cs):
  ```csharp
  public async Task<IActionResult> Receipts()
  {
      var receipts = await _context.GoodsReceipts
          .Include(r => r.Product)
          .Include(r => r.Supplier)
          .Include(r => r.Location)
          .OrderByDescending(r => r.ReceiptDate)
          .ToListAsync();
      return View(receipts);
  }

  [HttpGet]
  public async Task<IActionResult> CreateReceipt()
  {
      ViewBag.Products = await _context.Products.ToListAsync();
      ViewBag.Suppliers = await _context.Suppliers.ToListAsync();
      ViewBag.Locations = await _context.Locations.ToListAsync();
      return View();
  }

  [HttpPost]
  [ValidateAntiForgeryToken]
  public async Task<IActionResult> CreateReceipt(GoodsReceipt receipt)
  {
      receipt.ReceiptNo = $"GR-{DateTime.UtcNow:yyyyMMddHHmmss}";
      receipt.ReceiptDate = DateTime.UtcNow;
      receipt.Status = DocumentStatus.Draft;

      _context.GoodsReceipts.Add(receipt);
      await _context.SaveChangesAsync();

      var service = HttpContext.RequestServices.GetService(typeof(IInventoryService)) as IInventoryService;
      if (service != null)
      {
          await service.CompleteGoodsReceiptAsync(receipt.Id, "system");
      }

      TempData["StatusMessage"] = $"Đã nhập kho lô hàng {receipt.LotNo} thành công.";
      return RedirectToAction(nameof(Receipts));
  }
  ```

- [ ] **Step 4: Thiết kế Views cho Nhập kho**
  Tạo `Views/Inventory/Receipts.cshtml`:
  ```html
  @model IEnumerable<WmsMes.Web.Domain.Entities.GoodsReceipt>
  @{
      ViewData["Title"] = "Nhập kho mua hàng";
  }

  <section class="toolbar-row">
      <div>
          <p class="eyebrow">WMS</p>
          <h2>Lịch sử Nhập kho</h2>
      </div>
      <a class="btn btn-primary" asp-action="CreateReceipt">Nhập lô mới</a>
  </section>

  @if (TempData["StatusMessage"] != null)
  {
      <div class="alert alert-info mt-3">@TempData["StatusMessage"]</div>
  }

  <section class="data-panel mt-3">
      <div class="table-responsive">
          <table class="table data-table align-middle">
              <thead>
                  <tr>
                      <th>Mã Phiếu</th>
                      <th>Sản phẩm</th>
                      <th>Nhà cung cấp</th>
                      <th>Mã Lô</th>
                      <th class="text-end">Số lượng</th>
                      <th>Ngày nhập</th>
                  </tr>
              </thead>
              <tbody>
              @foreach (var r in Model)
              {
                  <tr>
                      <td><code>@r.ReceiptNo</code></td>
                      <td>@r.Product?.Name</td>
                      <td>@r.Supplier?.Name</td>
                      <td><code>@r.LotNo</code></td>
                      <td class="text-end fw-semibold text-success">+@r.Qty.ToString("N2")</td>
                      <td>@r.ReceiptDate.ToString("yyyy-MM-dd HH:mm")</td>
                  </tr>
              }
              </tbody>
          </table>
      </div>
  </section>
  ```

  Tạo `Views/Inventory/CreateReceipt.cshtml`:
  ```html
  @model WmsMes.Web.Domain.Entities.GoodsReceipt
  @{
      ViewData["Title"] = "Tạo phiếu nhập kho";
  }

  <section class="toolbar-row">
      <div>
          <p class="eyebrow">Goods Receipt</p>
          <h2>Tạo Phiếu Nhập kho mới</h2>
      </div>
  </section>

  <div class="ops-panel mt-3 col-md-8">
      <form asp-action="CreateReceipt" method="post">
          <div class="mb-3">
              <label for="SupplierId" class="form-label">Nhà cung cấp</label>
              <select id="SupplierId" name="SupplierId" class="form-select" required>
                  @foreach (var s in ViewBag.Suppliers)
                  {
                      <option value="@s.Id">@s.Name</option>
                  }
              </select>
          </div>
          <div class="mb-3">
              <label for="ProductId" class="form-label">Nguyên vật liệu/Sản phẩm</label>
              <select id="ProductId" name="ProductId" class="form-select" required>
                  @foreach (var p in ViewBag.Products)
                  {
                      <option value="@p.Id">@p.Code - @p.Name</option>
                  }
              </select>
          </div>
          <div class="mb-3">
              <label for="LotNo" class="form-label">Số lô (Lot No)</label>
              <input id="LotNo" name="LotNo" class="form-control" required />
          </div>
          <div class="mb-3">
              <label for="Qty" class="form-label">Số lượng</label>
              <input id="Qty" name="Qty" type="number" class="form-control" min="0.01" step="0.01" required />
          </div>
          <div class="mb-3">
              <label for="UnitPrice" class="form-label">Đơn giá</label>
              <input id="UnitPrice" name="UnitPrice" type="number" class="form-control" min="0" required />
          </div>
          <div class="mb-3">
              <label for="LocationId" class="form-label">Vị trí lưu trữ</label>
              <select id="LocationId" name="LocationId" class="form-select" required>
                  @foreach (var l in ViewBag.Locations)
                  {
                      <option value="@l.Id">@l.Code</option>
                  }
              </select>
          </div>
          <div class="d-flex gap-2">
              <button type="submit" class="btn btn-primary">Xác nhận nhập kho</button>
              <a class="btn btn-outline-secondary" asp-action="Receipts">Hủy</a>
          </div>
      </form>
  </div>
  ```

- [ ] **Step 5: Chạy test và commit**
  Chạy: `dotnet test`
  Kết quả mong đợi: PASS.
  Commit: `git add Controllers/InventoryController.cs Views/Inventory/ WmsMes.Tests/InventoryControllerTests.cs` và commit với thông điệp `"feat: implement Goods Receipt UI"`

---

### Task 2: Giao diện Xuất kho (Goods Issue)

**Files:**
- Modify: [InventoryController.cs](file:///d:/Quản lý sản xuất/Controllers/InventoryController.cs)
- Create: `Views/Inventory/Issues.cshtml`
- Create: `Views/Inventory/CreateIssue.cshtml`

**Interfaces:**
- Consumes: `IInventoryService.CompleteGoodsIssueAsync` để thực hiện xuất kho.
- Produces: Màn hình danh sách phiếu xuất và tạo mới phiếu xuất kho.

- [ ] **Step 1: Thêm action Xuất kho trong InventoryController**
  Sửa [InventoryController.cs](file:///d:/Quản lý sản xuất/Controllers/InventoryController.cs):
  ```csharp
  public async Task<IActionResult> Issues()
  {
      var issues = await _context.GoodsIssues
          .Include(i => i.Product)
          .Include(i => i.Customer)
          .Include(i => i.Location)
          .Include(i => i.Lot)
          .OrderByDescending(i => i.IssueDate)
          .ToListAsync();
      return View(issues);
  }

  [HttpGet]
  public async Task<IActionResult> CreateIssue()
  {
      ViewBag.Products = await _context.Products.ToListAsync();
      ViewBag.Customers = await _context.Customers.ToListAsync();
      ViewBag.Locations = await _context.Locations.ToListAsync();
      ViewBag.Lots = await _context.Lots.ToListAsync();
      return View();
  }

  [HttpPost]
  [ValidateAntiForgeryToken]
  public async Task<IActionResult> CreateIssue(GoodsIssue issue)
  {
      issue.IssueNo = $"GI-{DateTime.UtcNow:yyyyMMddHHmmss}";
      issue.IssueDate = DateTime.UtcNow;
      issue.Status = DocumentStatus.Draft;

      _context.GoodsIssues.Add(issue);
      await _context.SaveChangesAsync();

      var service = HttpContext.RequestServices.GetService(typeof(IInventoryService)) as IInventoryService;
      if (service != null)
      {
          try
          {
              await service.CompleteGoodsIssueAsync(issue.Id, "system");
              TempData["StatusMessage"] = $"Đã xuất kho {issue.Qty} thành công.";
          }
          catch (System.Exception ex)
          {
              TempData["StatusMessage"] = $"Lỗi xuất kho: {ex.Message}";
          }
      }

      return RedirectToAction(nameof(Issues));
  }
  ```

- [ ] **Step 2: Thiết kế Views cho Xuất kho**
  Tạo `Views/Inventory/Issues.cshtml`:
  ```html
  @model IEnumerable<WmsMes.Web.Domain.Entities.GoodsIssue>
  @{
      ViewData["Title"] = "Xuất kho";
  }

  <section class="toolbar-row">
      <div>
          <p class="eyebrow">WMS</p>
          <h2>Lịch sử Xuất kho</h2>
      </div>
      <a class="btn btn-primary" asp-action="CreateIssue">Tạo phiếu xuất</a>
  </section>

  @if (TempData["StatusMessage"] != null)
  {
      <div class="alert alert-info mt-3">@TempData["StatusMessage"]</div>
  }

  <section class="data-panel mt-3">
      <div class="table-responsive">
          <table class="table data-table align-middle">
              <thead>
                  <tr>
                      <th>Mã Phiếu</th>
                      <th>Sản phẩm</th>
                      <th>Khách hàng</th>
                      <th>Lô xuất</th>
                      <th class="text-end">Số lượng</th>
                      <th>Ngày xuất</th>
                  </tr>
              </thead>
              <tbody>
              @foreach (var i in Model)
              {
                  <tr>
                      <td><code>@i.IssueNo</code></td>
                      <td>@i.Product?.Name</td>
                      <td>@i.Customer?.Name</td>
                      <td><code>@i.Lot?.LotNo</code></td>
                      <td class="text-end fw-semibold text-danger">-@i.Qty.ToString("N2")</td>
                      <td>@i.IssueDate.ToString("yyyy-MM-dd HH:mm")</td>
                  </tr>
              }
              </tbody>
          </table>
      </div>
  </section>
  ```

  Tạo `Views/Inventory/CreateIssue.cshtml`:
  ```html
  @model WmsMes.Web.Domain.Entities.GoodsIssue
  @{
      ViewData["Title"] = "Tạo phiếu xuất kho";
  }

  <section class="toolbar-row">
      <div>
          <p class="eyebrow">Goods Issue</p>
          <h2>Tạo Phiếu Xuất kho mới</h2>
      </div>
  </section>

  <div class="ops-panel mt-3 col-md-8">
      <form asp-action="CreateIssue" method="post">
          <div class="mb-3">
              <label for="CustomerId" class="form-label">Khách hàng / Đối tượng</label>
              <select id="CustomerId" name="CustomerId" class="form-select" required>
                  @foreach (var c in ViewBag.Customers)
                  {
                      <option value="@c.Id">@c.Name</option>
                  }
              </select>
          </div>
          <div class="mb-3">
              <label for="ProductId" class="form-label">Sản phẩm cần xuất</label>
              <select id="ProductId" name="ProductId" class="form-select" required>
                  @foreach (var p in ViewBag.Products)
                  {
                      <option value="@p.Id">@p.Code - @p.Name</option>
                  }
              </select>
          </div>
          <div class="mb-3">
              <label for="LotId" class="form-label">Lô xuất</label>
              <select id="LotId" name="LotId" class="form-select" required>
                  @foreach (var l in ViewBag.Lots)
                  {
                      <option value="@l.Id">@l.LotNo</option>
                  }
              </select>
          </div>
          <div class="mb-3">
              <label for="Qty" class="form-label">Số lượng xuất</label>
              <input id="Qty" name="Qty" type="number" class="form-control" min="0.01" step="0.01" required />
          </div>
          <div class="mb-3">
              <label for="LocationId" class="form-label">Vị trí lấy hàng</label>
              <select id="LocationId" name="LocationId" class="form-select" required>
                  @foreach (var l in ViewBag.Locations)
                  {
                      <option value="@l.Id">@l.Code</option>
                  }
              </select>
          </div>
          <div class="d-flex gap-2">
              <button type="submit" class="btn btn-primary">Xác nhận xuất kho</button>
              <a class="btn btn-outline-secondary" asp-action="Issues">Hủy</a>
          </div>
      </form>
  </div>
  ```

- [ ] **Step 3: Chạy test và commit**
  Chạy: `dotnet test`
  Expected: PASS
  Commit: `git add Controllers/InventoryController.cs Views/Inventory/` và commit `"feat: implement Goods Issue UI"`

---

### Task 3: Tái cấu trúc Menu Điều hướng (Sidebar Redesign)

**Files:**
- Modify: [Shared/_Layout.cshtml](file:///d:/Quản lý sản xuất/Views/Shared/_Layout.cshtml)

- [ ] **Step 1: Cập nhật Sidebar trong Layout**
  Thay thế phân đoạn `<nav class="side-nav">` trong [Shared/_Layout.cshtml](file:///d:/Quản lý sản xuất/Views/Shared/_Layout.cshtml) từ dòng 23-31:
  ```html
  <nav class="side-nav">
      <div class="nav-section-title">Tổng quan</div>
      <a asp-controller="Home" asp-action="Index">Dashboard</a>
      
      <div class="nav-section-title">Quản lý Kho (WMS)</div>
      <a asp-controller="Inventory" asp-action="Index">Số dư tồn kho</a>
      <a asp-controller="Inventory" asp-action="Receipts">Nhập kho</a>
      <a asp-controller="Inventory" asp-action="Issues">Xuất kho</a>
      <a asp-controller="Warehouse" asp-action="Index">Kho & Vị trí</a>
      
      <div class="nav-section-title">Quản lý Sản xuất (MES)</div>
      <a asp-controller="WorkOrder" asp-action="Index">Lệnh sản xuất</a>
      <a asp-controller="Worker" asp-action="Index">Trạm vận hành</a>
      <a asp-controller="Mrp" asp-action="Index">Lập kế hoạch MRP</a>
      <a asp-controller="Product" asp-action="Index">Sản phẩm (SKU)</a>
      
      <div class="nav-section-title">Kiểm soát Chất lượng & Truy vết</div>
      <a asp-controller="Qc" asp-action="Index">Kiểm định chất lượng</a>
      <a asp-controller="Traceability" asp-action="Index">Truy vết lô hàng</a>
  </nav>
  ```

- [ ] **Step 2: Commit**
  ```powershell
  git add Views/Shared/_Layout.cshtml
  git commit -m "style: restructure sidebar menu by modules"
  ```

*--- KIỂM TRA ĐIỂM DỪNG GIAI ĐOẠN 1: Chạy ứng dụng, đăng nhập và thử tạo 1 phiếu nhập kho nguyên liệu mới xem tồn kho có tăng lên hay không.*

---

## GIAI ĐOẠN 2: Quản lý Sản xuất (MES UI)

### Task 4: Quản lý Lệnh sản xuất (Work Orders Management)

**Files:**
- Create: [WorkOrderController.cs](file:///d:/Quản lý sản xuất/Controllers/WorkOrderController.cs)
- Create: `Views/WorkOrder/Index.cshtml`
- Create: `Views/WorkOrder/Create.cshtml`
- Create: `Views/WorkOrder/Details.cshtml`
- Create: [WorkOrderControllerTests.cs](file:///d:/Quản lý sản xuất/WmsMes.Tests/WorkOrderControllerTests.cs)

**Interfaces:**
- Consumes: `IWorkOrderService` để lập kế hoạch, phê duyệt và hoàn tất lệnh.
- Produces: Các endpoint CRUD và phê duyệt Lệnh sản xuất.

- [ ] **Step 1: Viết test cho WorkOrderController**
  Tạo `WmsMes.Tests/WorkOrderControllerTests.cs`:
  ```csharp
  using Microsoft.AspNetCore.Mvc;
  using Microsoft.EntityFrameworkCore;
  using Moq;
  using System.Collections.Generic;
  using System.Threading.Tasks;
  using WmsMes.Web.Controllers;
  using WmsMes.Web.Data;
  using WmsMes.Web.Domain.Entities;
  using WmsMes.Web.Services;
  using Xunit;

  namespace WmsMes.Tests
  {
      public class WorkOrderControllerTests
      {
          [Fact]
          public async Task Index_ReturnsViewWithWorkOrders()
          {
              var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                  .UseInMemoryDatabase(databaseName: "WO_Index_Db")
                  .Options;

              using (var context = new ApplicationDbContext(options))
              {
                  context.WorkOrders.Add(new WorkOrder { Code = "WO-001", ProductId = 1, Qty = 10, DueDate = System.DateTime.UtcNow });
                  await context.SaveChangesAsync();
              }

              var serviceMock = new Mock<IWorkOrderService>();
              using (var context = new ApplicationDbContext(options))
              {
                  var controller = new WorkOrderController(context, serviceMock.Object);
                  var result = await controller.Index();

                  var viewResult = Assert.IsType<ViewResult>(result);
                  var model = Assert.IsAssignableFrom<IEnumerable<WorkOrder>>(viewResult.Model);
                  Assert.Single(model);
              }
          }
      }
  }
  ```

- [ ] **Step 2: Chạy test và xác nhận thất bại**
  Run: `dotnet test --filter "FullyQualifiedName=WmsMes.Tests.WorkOrderControllerTests.Index_ReturnsViewWithWorkOrders"`

- [ ] **Step 3: Tạo WorkOrderController**
  Tạo [WorkOrderController.cs](file:///d:/Quản lý sản xuất/Controllers/WorkOrderController.cs):
  ```csharp
  using Microsoft.AspNetCore.Authorization;
  using Microsoft.AspNetCore.Mvc;
  using Microsoft.EntityFrameworkCore;
  using System;
  using System.Linq;
  using System.Security.Claims;
  using System.Threading.Tasks;
  using WmsMes.Web.Data;
  using WmsMes.Web.Domain.Entities;
  using WmsMes.Web.Domain.Enums;
  using WmsMes.Web.Services;

  namespace WmsMes.Web.Controllers
  {
      [Authorize(Roles = "Admin,Manager,Planner")]
      public class WorkOrderController : Controller
      {
          private readonly ApplicationDbContext _context;
          private readonly IWorkOrderService _workOrderService;

          public WorkOrderController(ApplicationDbContext context, IWorkOrderService workOrderService)
          {
              _context = context;
              _workOrderService = workOrderService;
          }

          public async Task<IActionResult> Index()
          {
              var workOrders = await _context.WorkOrders
                  .Include(w => w.Product)
                  .OrderByDescending(w => w.DueDate)
                  .ToListAsync();
              return View(workOrders);
          }

          public async Task<IActionResult> Details(int id)
          {
              var workOrder = await _context.WorkOrders
                  .Include(w => w.Product)
                  .Include(w => w.Steps)
                      .ThenInclude(s => s.WorkCenter)
                  .FirstOrDefaultAsync(w => w.Id == id);

              if (workOrder == null) return NotFound();

              var reservations = await _context.MaterialReservations
                  .Include(r => r.Product)
                  .Include(r => r.Lot)
                  .Include(r => r.Location)
                  .Where(r => r.WorkOrderId == id)
                  .ToListAsync();

              ViewBag.Reservations = reservations;
              return View(workOrder);
          }

          [HttpGet]
          public async Task<IActionResult> Create()
          {
              ViewBag.Products = await _context.Products
                  .Where(p => p.IsManufactured)
                  .ToListAsync();
              return View();
          }

          [HttpPost]
          [ValidateAntiForgeryToken]
          public async Task<IActionResult> Create(WorkOrder workOrder)
          {
              if (string.IsNullOrWhiteSpace(workOrder.Code))
              {
                  workOrder.Code = $"WO-{DateTime.UtcNow:yyyyMMddHHmmss}";
              }
              workOrder.Status = WorkOrderStatus.Draft;

              _context.WorkOrders.Add(workOrder);
              await _context.SaveChangesAsync();

              TempData["StatusMessage"] = $"Đã tạo lệnh sản xuất nháp {workOrder.Code}.";
              return RedirectToAction(nameof(Index));
          }

          [HttpPost]
          [ValidateAntiForgeryToken]
          public async Task<IActionResult> Approve(int id)
          {
              var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
              try
              {
                  var success = await _workOrderService.ApproveWorkOrderAsync(id, userId);
                  if (success)
                  {
                      TempData["StatusMessage"] = "Đã phê duyệt lệnh sản xuất và giữ chỗ vật tư thành công.";
                  }
                  else
                  {
                      TempData["StatusMessage"] = "Không thể phê duyệt lệnh sản xuất này.";
                  }
              }
              catch (Exception ex)
              {
                  TempData["StatusMessage"] = $"Lỗi khi duyệt lệnh: {ex.Message}";
              }
              return RedirectToAction(nameof(Details), new { id = id });
          }

          [HttpPost]
          [ValidateAntiForgeryToken]
          public async Task<IActionResult> Complete(int id)
          {
              var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
              try
              {
                  var success = await _workOrderService.CompleteWorkOrderAsync(id, userId);
                  if (success)
                  {
                      TempData["StatusMessage"] = "Đã hoàn thành lệnh sản xuất, trừ tồn vật tư và sinh lô thành phẩm chờ QC.";
                  }
                  else
                  {
                      TempData["StatusMessage"] = "Không thể hoàn thành lệnh sản xuất này. Đảm bảo tất cả các công đoạn đã hoàn thành.";
                  }
              }
              catch (Exception ex)
              {
                  TempData["StatusMessage"] = $"Lỗi khi hoàn thành lệnh: {ex.Message}";
              }
              return RedirectToAction(nameof(Details), new { id = id });
          }
      }
  }
  ```

- [ ] **Step 4: Tạo Views tương ứng cho WorkOrder**
  Tạo `Views/WorkOrder/Index.cshtml`: (Xem mã nguồn chi tiết trong [implementation_plan.md](file:///C:/Users/LUONG/.gemini/antigravity-ide/brain/954be6d3-f2a9-4659-9b5a-f4a074aaf86a/implementation_plan.md))
  Tạo `Views/WorkOrder/Create.cshtml`: (Xem mã nguồn chi tiết trong [implementation_plan.md](file:///C:/Users/LUONG/.gemini/antigravity-ide/brain/954be6d3-f2a9-4659-9b5a-f4a074aaf86a/implementation_plan.md))
  Tạo `Views/WorkOrder/Details.cshtml`: (Xem mã nguồn chi tiết trong [implementation_plan.md](file:///C:/Users/LUONG/.gemini/antigravity-ide/brain/954be6d3-f2a9-4659-9b5a-f4a074aaf86a/implementation_plan.md))

- [ ] **Step 5: Chạy test và commit**
  Chạy: `dotnet test`
  Expected: PASS
  Commit: `git add Controllers/WorkOrderController.cs Views/WorkOrder/ WmsMes.Tests/WorkOrderControllerTests.cs`

---

### Task 5: Cải tiến Màn hình Lập kế hoạch MRP

**Files:**
- Modify: [MrpController.cs](file:///d:/Quản lý sản xuất/Controllers/MrpController.cs)
- Modify: [Mrp/Index.cshtml](file:///d:/Quản lý sản xuất/Views/Mrp/Index.cshtml)
- Modify: [wwwroot/css/site.css](file:///d:/Quản lý sản xuất/wwwroot/css/site.css)

- [ ] **Step 1: Cập nhật MrpController**
  Sửa [MrpController.cs](file:///d:/Quản lý sản xuất/Controllers/MrpController.cs) nạp sản phẩm có `IsManufactured = true`:
  ```csharp
  using Microsoft.AspNetCore.Authorization;
  using Microsoft.AspNetCore.Mvc;
  using Microsoft.EntityFrameworkCore;
  using System.Linq;
  using System.Threading.Tasks;
  using WmsMes.Web.Data;
  using WmsMes.Web.Services;

  namespace WmsMes.Web.Controllers
  {
      [Authorize(Roles = "Admin,Manager,Planner")]
      public class MrpController : Controller
      {
          private readonly ApplicationDbContext _context;
          private readonly IMrpService _mrpService;

          public MrpController(ApplicationDbContext context, IMrpService mrpService)
          {
              _context = context;
              _mrpService = mrpService;
          }

          [HttpGet]
          public async Task<IActionResult> Index()
          {
              ViewBag.Products = await _context.Products.Where(p => p.IsManufactured).ToListAsync();
              return View();
          }

          [HttpPost]
          [ValidateAntiForgeryToken]
          public async Task<IActionResult> Calculate(int productId, decimal qty)
          {
              var results = await _mrpService.CalculateRequirementsAsync(productId, qty);
              ViewData["ProductId"] = productId;
              ViewData["Qty"] = qty;
              ViewBag.Products = await _context.Products.Where(p => p.IsManufactured).ToListAsync();
              return View("Index", results);
          }
      }
  }
  ```

- [ ] **Step 2: Cập nhật Mrp/Index.cshtml thành Dropdown và thêm style CSS**
  Sửa [Mrp/Index.cshtml](file:///d:/Quản lý sản xuất/Views/Mrp/Index.cshtml) và thêm class `.nav-section-title` vào [wwwroot/css/site.css](file:///d:/Quản lý sản xuất/wwwroot/css/site.css) (xem chi tiết mã nguồn trong [implementation_plan.md](file:///C:/Users/LUONG/.gemini/antigravity-ide/brain/954be6d3-f2a9-4659-9b5a-f4a074aaf86a/implementation_plan.md)).

- [ ] **Step 3: Chạy test và commit**
  Chạy: `dotnet test`
  Expected: PASS
  Commit: `git add Controllers/MrpController.cs Views/Mrp/Index.cshtml wwwroot/css/site.css` và commit với tin nhắn `"feat: improve MRP input and layout css"`

*--- KIỂM TRA ĐIỂM DỪNG GIAI ĐOẠN 2: Tạo một lệnh sản xuất mới dạng nháp, nhấn "Phê duyệt" để xem các dòng nguyên liệu đã được tự động giữ chỗ thành công hay chưa.*

---

## GIAI ĐOẠN 3: Kiểm soát Chất lượng (QC) & Real-time Dashboard

### Task 6: Giao diện Kiểm tra chất lượng (QC)

**Files:**
- Create: [QcController.cs](file:///d:/Quản lý sản xuất/Controllers/QcController.cs)
- Create: `Views/Qc/Index.cshtml`
- Create: `Views/Qc/Inspect.cshtml`
- Create: [QcControllerTests.cs](file:///d:/Quản lý sản xuất/WmsMes.Tests/QcControllerTests.cs)

**Interfaces:**
- Consumes: `IQcService.SubmitQCInspectionAsync` để đánh giá lô.
- Produces: Giao diện đánh giá chất lượng lô hàng tạm giữ.

- [ ] **Step 1: Tạo kiểm thử cho QC**
  (Xem mã kiểm thử mẫu `QcControllerTests.cs` chi tiết trong [implementation_plan.md](file:///C:/Users/LUONG/.gemini/antigravity-ide/brain/954be6d3-f2a9-4659-9b5a-f4a074aaf86a/implementation_plan.md)).

- [ ] **Step 2: Tạo QcController và thiết kế các Views QC**
  Tạo các tệp tin controller và views tương tự như đặc tả trong [implementation_plan.md](file:///C:/Users/LUONG/.gemini/antigravity-ide/brain/954be6d3-f2a9-4659-9b5a-f4a074aaf86a/implementation_plan.md).

- [ ] **Step 3: Chạy test và commit**
  Chạy: `dotnet test`
  Expected: PASS
  Commit: `git add Controllers/QcController.cs Views/Qc/ WmsMes.Tests/QcControllerTests.cs`

---

### Task 7: Bảng điều khiển thời gian thực (SignalR Dashboard)

**Files:**
- Modify: [HomeController.cs](file:///d:/Quản lý sản xuất/Controllers/HomeController.cs)
- Modify: [Home/Index.cshtml](file:///d:/Quản lý sản xuất/Views/Home/Index.cshtml)
- Create: [HomeControllerTests.cs](file:///d:/Quản lý sản xuất/WmsMes.Tests/HomeControllerTests.cs)

- [ ] **Step 1: Viết test cho HomeController và triển khai code**
  (Xem mã nguồn HomeController và Home/Index.cshtml chi tiết trong [implementation_plan.md](file:///C:/Users/LUONG/.gemini/antigravity-ide/brain/954be6d3-f2a9-4659-9b5a-f4a074aaf86a/implementation_plan.md)).

- [ ] **Step 2: Chạy toàn bộ kiểm thử tích hợp của hệ thống**
  Chạy: `dotnet test`
  Expected: PASS

- [ ] **Step 3: Commit**
  ```powershell
  git add Controllers/HomeController.cs Views/Home/Index.cshtml WmsMes.Tests/HomeControllerTests.cs
  git commit -m "feat: implement real-time dashboard on home view"
  ```

*--- KIỂM TRA ĐIỂM DỪNG GIAI ĐOẠN 3: Tạo lệnh sản xuất -> Trạm vận hành hoàn thành sản xuất -> Thành phẩm đưa vào trạng thái tạm giữ và xuất hiện trên màn hình QC -> QC bấm PASS -> Thành phẩm chuyển sang trạng thái khả dụng và số liệu Dashboard trang chủ nhảy lập tức.*
