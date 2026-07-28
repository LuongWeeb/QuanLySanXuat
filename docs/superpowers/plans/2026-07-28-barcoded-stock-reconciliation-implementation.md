# Phân hệ Kiểm kê Kho bằng Mã vạch & Điều chỉnh Sổ cái Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Triển khai Phân hệ Kiểm kê Kho bằng Mã vạch (`CycleCountOrder`), hỗ trợ chụp ảnh tồn kho hệ thống (`SystemQty`), quét mã vạch/QR để kiểm đếm thực tế (`CountedQty`), hiển thị báo cáo chênh lệch tài chính và tự động tạo các bút toán điều chỉnh Sổ cái (`StockTransaction` type = `Adjust`) khi được duyệt.

**Architecture:** Xây dựng dịch vụ `CycleCountService` đảm bảo tính toàn vẹn của sổ cái. Phát triển màn hình đếm hàng quét mã lai (Hybrid Barcode/QR Input) và màn hình duyệt chênh lệch cho Quản lý.

**Tech Stack:** ASP.NET Core 8 MVC, Entity Framework Core 8, C#, Bootstrap 5, Javascript.

## Global Constraints
- Target Framework: `.NET 8` net8.0.
- Đợt kiểm kê có mã định dạng `CC-YYYYMMDD-XXX`.
- Khi Quản lý duyệt đợt kiểm kê, các dòng chênh lệch `VarianceQty != 0` phải tự động sinh ra bản ghi `StockTransaction` với `Type = TransactionType.Adjust` và cập nhật `QtyAvailable` trên `StockBalance`.

---

### Task 1: Phát triển Dịch vụ Nghiệp vụ Kiểm kê Kho & DI

**Files:**
- Create: [ICycleCountService.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Services/ICycleCountService.cs)
- Create: [CycleCountService.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Services/CycleCountService.cs)
- Modify: [Program.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Program.cs)

**Interfaces:**
- Consumes: DB Context.
- Produces: Dịch vụ `CycleCountService` xử lý đợt kiểm kê và bút toán điều chỉnh sổ cái.

- [ ] **Step 1: Tạo ICycleCountService.cs**
  Khai báo interface:
  ```csharp
  using WmsMes.Web.Domain.Entities;

  namespace WmsMes.Web.Services;

  public interface ICycleCountService
  {
      Task<CycleCountOrder?> GetByIdAsync(int id);
      Task<CycleCountOrder> CreateOrderAsync(int warehouseId, string createdBy);
      Task<bool> UpdateCountedQtysAsync(int orderId, Dictionary<int, decimal> itemCounts);
      Task<bool> ApproveAndAdjustLedgerAsync(int orderId, string managerUserId);
  }
  ```

- [ ] **Step 2: Triển khai CycleCountService.cs**
  Viết logic tạo snapshot tồn kho và duyệt bút toán điều chỉnh:
  ```csharp
  using Microsoft.EntityFrameworkCore;
  using WmsMes.Web.Data;
  using WmsMes.Web.Domain.Entities;
  using WmsMes.Web.Domain.Enums;

  namespace WmsMes.Web.Services;

  public class CycleCountService : ICycleCountService
  {
      private readonly ApplicationDbContext _context;

      public CycleCountService(ApplicationDbContext context)
      {
          _context = context;
      }

      public async Task<CycleCountOrder?> GetByIdAsync(int id)
      {
          return await _context.CycleCountOrders
              .Include(c => c.Warehouse)
              .Include(c => c.Items).ThenInclude(i => i.Product)
              .Include(c => c.Items).ThenInclude(i => i.Location)
              .Include(c => c.Items).ThenInclude(i => i.Lot)
              .FirstOrDefaultAsync(c => c.Id == id);
      }

      public async Task<CycleCountOrder> CreateOrderAsync(int warehouseId, string createdBy)
      {
          var countNo = $"CC-{DateTime.UtcNow:yyyyMMddHHmmss}";
          var order = new CycleCountOrder
          {
              CountNumber = countNo,
              WarehouseId = warehouseId,
              Status = "Draft",
              CreatedAt = DateTime.UtcNow,
              CreatedBy = createdBy
          };

          // Tải số dư tồn kho khả dụng hiện tại trong Kho này
          var activeBalances = await _context.StockBalances
              .Include(sb => sb.Location)
              .Where(sb => sb.Location!.Zone!.WarehouseId == warehouseId && sb.QtyAvailable > 0)
              .ToListAsync();

          foreach (var balance in activeBalances)
          {
              order.Items.Add(new CycleCountItem
              {
                  ProductId = balance.ProductId,
                  LocationId = balance.LocationId,
                  LotId = balance.LotId,
                  SystemQty = balance.QtyAvailable,
                  CountedQty = null
              });
          }

          _context.CycleCountOrders.Add(order);
          await _context.SaveChangesAsync();
          return order;
      }

      public async Task<bool> UpdateCountedQtysAsync(int orderId, Dictionary<int, decimal> itemCounts)
      {
          var order = await _context.CycleCountOrders
              .Include(c => c.Items)
              .FirstOrDefaultAsync(c => c.Id == orderId);

          if (order == null || order.Status == "Approved" || order.Status == "Cancelled") return false;

          foreach (var item in order.Items)
          {
              if (itemCounts.TryGetValue(item.Id, out var count))
              {
                  item.CountedQty = count;
              }
          }

          order.Status = "Completed";
          order.CompletedAt = DateTime.UtcNow;
          await _context.SaveChangesAsync();
          return true;
      }

      public async Task<bool> ApproveAndAdjustLedgerAsync(int orderId, string managerUserId)
      {
          var order = await GetByIdAsync(orderId);
          if (order == null || order.Status == "Approved") return false;

          await using var transaction = await _context.Database.BeginTransactionAsync();
          try
          {
              foreach (var item in order.Items)
              {
                  var variance = item.VarianceQty;
                  if (variance == 0m) continue;

                  var balance = await _context.StockBalances
                      .FirstOrDefaultAsync(sb => sb.ProductId == item.ProductId && sb.LocationId == item.LocationId && sb.LotId == item.LotId);

                  if (balance == null)
                  {
                      balance = new StockBalance
                      {
                          ProductId = item.ProductId,
                          LocationId = item.LocationId,
                          LotId = item.LotId,
                          QtyAvailable = 0m
                      };
                      _context.StockBalances.Add(balance);
                  }

                  balance.QtyAvailable += variance;
                  if (balance.QtyAvailable < 0)
                  {
                      throw new InvalidOperationException($"Số lượng chênh lệch dẫn tới tồn kho âm cho sản phẩm {item.Product?.Code}.");
                  }

                  _context.StockTransactions.Add(new StockTransaction
                  {
                      Type = TransactionType.Adjust,
                      ProductId = item.ProductId,
                      LotId = item.LotId,
                      LocationId = item.LocationId,
                      Qty = variance,
                      QtyAfter = balance.QtyAvailable,
                      ValuationRate = item.Lot?.UnitPrice ?? 0m,
                      TransactionDate = DateTime.UtcNow,
                      UserId = managerUserId,
                      ReferenceNo = order.CountNumber
                  });
              }

              order.Status = "Approved";
              order.ApprovedBy = managerUserId;
              await _context.SaveChangesAsync();
              await transaction.CommitAsync();
              return true;
          }
          catch
          {
              await transaction.RollbackAsync();
              throw;
          }
      }
  }
  ```

- [ ] **Step 3: Đăng ký dịch vụ trong Program.cs**
  ```csharp
  builder.Services.AddScoped<ICycleCountService, CycleCountService>();
  ```

- [ ] **Step 4: Commit**
  Run: `git add Services/ Program.cs`
  Run: `git commit -m "feat: implement ICycleCountService and register in DI"`

---

### Task 2: Viết Unit Tests Cho Kiểm Kê Kho & Bút Toán Điều Chỉnh

**Files:**
- Create: [CycleCountTests.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/WmsMes.Tests/CycleCountTests.cs)

**Interfaces:**
- Consumes: `CycleCountService`.
- Produces: Bài kiểm thử tự động PASS.

- [ ] **Step 1: Viết test ApproveAndAdjustLedger_UpdatesBalanceAndInsertsStockTransaction**
  Tạo bài test kiểm tra việc duyệt đợt đếm có chênh lệch sẽ tự động tạo bút toán `StockTransaction` kiểu `Adjust` và cập nhật `StockBalance.QtyAvailable`.

- [ ] **Step 2: Chạy Unit Tests**
  Run: `dotnet test WmsMes.Tests/WmsMes.Tests.csproj`
  Expected: PASS tất cả bài test.

- [ ] **Step 3: Commit**
  Run: `git add WmsMes.Tests/CycleCountTests.cs`
  Run: `git commit -m "test: add unit tests for stock count reconciliation and ledger adjustments"`

---

### Task 3: Xây dựng Bộ điều khiển CycleCountController

**Files:**
- Create: [CycleCountController.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Controllers/CycleCountController.cs)

**Interfaces:**
- Consumes: Yêu cầu quản lý đợt đếm kho.
- Produces: Các endpoint MVC Index, Create, ExecuteScan, SaveScan, Details, Approve.

- [ ] **Step 1: Tạo CycleCountController.cs**
  Xây dựng các action điều khiển luồng kiểm kê kho:
  ```csharp
  using Microsoft.AspNetCore.Authorization;
  using Microsoft.AspNetCore.Mvc;
  using Microsoft.EntityFrameworkCore;
  using System.Security.Claims;
  using WmsMes.Web.Data;
  using WmsMes.Web.Services;

  namespace WmsMes.Web.Controllers;

  [Authorize(Roles = "Admin,WarehouseManager,Manager")]
  public class CycleCountController : Controller
  {
      private readonly ApplicationDbContext _context;
      private readonly ICycleCountService _countService;

      public CycleCountController(ApplicationDbContext context, ICycleCountService countService)
      {
          _context = context;
          _countService = countService;
      }

      public async Task<IActionResult> Index()
      {
          var orders = await _context.CycleCountOrders
              .Include(c => c.Warehouse)
              .OrderByDescending(c => c.CreatedAt)
              .ToListAsync();
          return View(orders);
      }

      [HttpGet]
      public async Task<IActionResult> Create()
      {
          ViewBag.Warehouses = await _context.Warehouses.Where(w => w.IsActive).ToListAsync();
          return View();
      }

      [HttpPost]
      [ValidateAntiForgeryToken]
      public async Task<IActionResult> Create(int warehouseId)
      {
          var username = User.Identity?.Name ?? "warehouse";
          var order = await _countService.CreateOrderAsync(warehouseId, username);
          return RedirectToAction(nameof(ExecuteScan), new { id = order.Id });
      }

      public async Task<IActionResult> ExecuteScan(int id)
      {
          var order = await _countService.GetByIdAsync(id);
          if (order == null) return NotFound();
          return View(order);
      }

      [HttpPost]
      [ValidateAntiForgeryToken]
      public async Task<IActionResult> SaveScan(int id, Dictionary<int, decimal> itemCounts)
      {
          await _countService.UpdateCountedQtysAsync(id, itemCounts);
          return RedirectToAction(nameof(Details), new { id });
      }

      public async Task<IActionResult> Details(int id)
      {
          var order = await _countService.GetByIdAsync(id);
          if (order == null) return NotFound();
          return View(order);
      }

      [HttpPost]
      [ValidateAntiForgeryToken]
      public async Task<IActionResult> Approve(int id)
      {
          var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
          try
          {
              var success = await _countService.ApproveAndAdjustLedgerAsync(id, userId ?? "manager");
              if (success) TempData["StatusMessage"] = "Đã duyệt đợt kiểm kê và cập nhật Sổ cái kho thành công.";
          }
          catch (Exception ex)
          {
              TempData["ErrorMessage"] = ex.Message;
          }
          return RedirectToAction(nameof(Details), new { id });
      }
  }
  ```

- [ ] **Step 2: Commit**
  Run: `git add Controllers/CycleCountController.cs`
  Run: `git commit -m "feat: implement CycleCountController endpoints"`

---

### Task 4: Thiết kế Giao diện Views & Tích hợp Menu Layout

**Files:**
- Create Views: `CycleCount/Index.cshtml`, `Create.cshtml`, `ExecuteScan.cshtml`, `Details.cshtml`
- Modify: [Views/Shared/_Layout.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/Shared/_Layout.cshtml)

**Interfaces:**
- Consumes: Razor view engine & Javascript quét mã vạch.
- Produces: Màn hình kiểm kê kho quét mã vạch tiện lợi.

- [ ] **Step 1: Tạo các Views Kiểm kê kho**
  Tạo các View giao diện HTML đẹp mắt:
  *   `Index.cshtml`: Bảng danh sách đợt kiểm đếm.
  *   `Create.cshtml`: Form chọn Kho để khởi tạo đợt kiểm đếm.
  *   `ExecuteScan.cshtml`: Màn hình đếm hàng tích hợp thanh quét vạch lai (Barcode scanner autofocus) tự động highlight vị trí/lô hàng tương ứng.
  *   `Details.cshtml`: Báo cáo chênh lệch tồn kho (System vs Counted vs Variance) và nút "Duyệt & Điều chỉnh Sổ cái".

- [ ] **Step 2: Thêm Menu vào Sidebar Layout**
  Mở `Views/Shared/_Layout.cshtml` và thêm mục menu "Kiểm kê kho (Stocktake)".

- [ ] **Step 3: Kiểm tra biên dịch**
  Run: `dotnet build`
  Expected: Build thành công không có lỗi.

- [ ] **Step 4: Commit**
  Run: `git add Views/`
  Run: `git commit -m "feat: complete UI views for barcoded stock reconciliation module"`
