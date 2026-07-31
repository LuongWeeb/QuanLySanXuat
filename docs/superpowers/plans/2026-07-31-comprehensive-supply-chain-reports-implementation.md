# Phase 9: Supply Chain, Financial Reports & Smart Notifications Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Triển khai quy trình Lấy hàng & Đóng gói tối ưu (`PickList`, `PackingSlip` PDF), Báo cáo Tài chính Kho & Giá vốn COGS xuất Excel bằng `ClosedXML`, và Trung tâm Cảnh báo Realtime (`AppNotification`) hiển thị Icon Chuông thông báo trên Navbar Header.

**Architecture:** Tạo các thực thể `PickList`, `PickListItem`, `PackingSlip`, `AppNotification`. Phát triển `PickListService` tự động sắp xếp vị trí lấy hàng theo thứ tự ưu tiên tối ưu đường đi trong kho, `NotificationService` bắn thông báo realtime qua SignalR. Sử dụng `ClosedXML` xuất file báo cáo kho `.xlsx`.

**Tech Stack:** ASP.NET Core 8 MVC, Entity Framework Core 8, QuestPDF, ClosedXML, SignalR, Bootstrap 5.

## Global Constraints
- Target Framework: `.NET 8` net8.0.
- Mã danh sách lấy hàng có định dạng `PK-YYYYMMDD-XXX`.
- Mã phiếu đóng gói có định dạng `PS-YYYYMMDD-XXX`.
- Tệp báo cáo kho xuất ra có định dạng `.xlsx` với phông chữ chuẩn, định dạng phân cách hàng nghìn cho tiền tệ và số lượng.

---

### Task 1: Cấu trúc Dữ liệu & Migration Phase 9

**Files:**
- Create: [PickList.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Entities/PickList.cs)
- Create: [PickListItem.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Entities/PickListItem.cs)
- Create: [PackingSlip.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Entities/PackingSlip.cs)
- Create: [AppNotification.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Entities/AppNotification.cs)
- Modify: [ApplicationDbContext.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Data/ApplicationDbContext.cs)

**Interfaces:**
- Consumes: Database schema hiện có.
- Produces: Các bảng `PickLists`, `PickListItems`, `PackingSlips`, `AppNotifications` trong DB.

- [ ] **Step 1: Tạo PickList.cs & PickListItem.cs**
  Tạo `Domain/Entities/PickList.cs`:
  ```csharp
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;
  using WmsMes.Web.Domain.Enums;

  namespace WmsMes.Web.Domain.Entities;

  public class PickList
  {
      public int Id { get; set; }

      [Required]
      [MaxLength(50)]
      public string PickListNo { get; set; } = string.Empty;

      [Required]
      public int SalesOrderId { get; set; }

      [ForeignKey(nameof(SalesOrderId))]
      public virtual SalesOrder? SalesOrder { get; set; }

      [Required]
      public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

      [Required]
      public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

      public virtual ICollection<PickListItem> Items { get; set; } = new List<PickListItem>();
  }
  ```

  Tạo `Domain/Entities/PickListItem.cs`:
  ```csharp
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  namespace WmsMes.Web.Domain.Entities;

  public class PickListItem
  {
      public int Id { get; set; }

      [Required]
      public int PickListId { get; set; }

      [ForeignKey(nameof(PickListId))]
      public virtual PickList? PickList { get; set; }

      [Required]
      public int ProductId { get; set; }

      [ForeignKey(nameof(ProductId))]
      public virtual Product? Product { get; set; }

      [Required]
      public int LocationId { get; set; }

      [ForeignKey(nameof(LocationId))]
      public virtual Location? Location { get; set; }

      [Required]
      public int LotId { get; set; }

      [ForeignKey(nameof(LotId))]
      public virtual Lot? Lot { get; set; }

      [Column(TypeName = "decimal(18,2)")]
      public decimal QtyToPick { get; set; }

      [Column(TypeName = "decimal(18,2)")]
      public decimal PickedQty { get; set; } = 0m;

      public int SequenceOrder { get; set; } = 1;
  }
  ```

- [ ] **Step 2: Tạo PackingSlip.cs & AppNotification.cs**
  Tạo `Domain/Entities/PackingSlip.cs`:
  ```csharp
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;
  using WmsMes.Web.Domain.Enums;

  namespace WmsMes.Web.Domain.Entities;

  public class PackingSlip
  {
      public int Id { get; set; }

      [Required]
      [MaxLength(50)]
      public string PackingNo { get; set; } = string.Empty;

      [Required]
      public int SalesOrderId { get; set; }

      [ForeignKey(nameof(SalesOrderId))]
      public virtual SalesOrder? SalesOrder { get; set; }

      public int PackageNo { get; set; } = 1;

      [Column(TypeName = "decimal(18,2)")]
      public decimal GrossWeight { get; set; } = 0m;

      [Required]
      public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
  }
  ```

  Tạo `Domain/Entities/AppNotification.cs`:
  ```csharp
  using System.ComponentModel.DataAnnotations;

  namespace WmsMes.Web.Domain.Entities;

  public class AppNotification
  {
      public int Id { get; set; }

      [Required]
      [MaxLength(150)]
      public string Title { get; set; } = string.Empty;

      [Required]
      [MaxLength(500)]
      public string Message { get; set; } = string.Empty;

      [Required]
      [MaxLength(20)]
      public string Severity { get; set; } = "Info"; // Info, Warning, Danger

      public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

      public bool IsRead { get; set; } = false;

      [MaxLength(450)]
      public string? UserId { get; set; }

      [MaxLength(500)]
      public string? ReferenceUrl { get; set; }
  }
  ```

- [ ] **Step 3: Đăng ký DbSets & Chạy EF Migration**
  Trong `Data/ApplicationDbContext.cs`:
  ```csharp
  public DbSet<PickList> PickLists { get; set; } = null!;
  public DbSet<PickListItem> PickListItems { get; set; } = null!;
  public DbSet<PackingSlip> PackingSlips { get; set; } = null!;
  public DbSet<AppNotification> AppNotifications { get; set; } = null!;
  ```

  Run: `dotnet ef migrations add AddPhase9SupplyChainAndNotificationTables`
  Run: `dotnet ef database update`
  Expected: Cập nhật DB thành công.

- [ ] **Step 4: Commit**
  Run: `git add Domain/Entities/ Data/ApplicationDbContext.cs Data/Migrations/`
  Run: `git commit -m "feat: add phase 9 supply chain and notification entities and migrations"`

---

### Task 2: Dịch vụ Tối ưu PickList & Trung tâm Thông báo

**Files:**
- Create: [IPickListService.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Services/IPickListService.cs) & [PickListService.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Services/PickListService.cs)
- Create: [INotificationService.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Services/INotificationService.cs) & [NotificationService.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Services/NotificationService.cs)
- Modify: [Program.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Program.cs)

**Interfaces:**
- Consumes: DB Context & SignalR Hub.
- Produces: Dịch vụ tối ưu thứ tự vị trí lấy hàng và bắn thông báo realtime.

- [ ] **Step 1: Triển khai PickListService.cs**
  Viết thuật toán sắp xếp thứ tự vị trí lấy hàng theo chuỗi `Zone.Code` -> `Location.Code`:
  ```csharp
  public class PickListService : IPickListService
  {
      private readonly ApplicationDbContext _context;

      public PickListService(ApplicationDbContext context)
      {
          _context = context;
      }

      public async Task<PickList?> CreatePickListForSalesOrderAsync(int salesOrderId)
      {
          var order = await _context.SalesOrders
              .Include(s => s.Items)
              .FirstOrDefaultAsync(s => s.Id == salesOrderId);

          if (order == null) return null;

          var pickList = new PickList
          {
              PickListNo = $"PK-{DateTime.UtcNow:yyyyMMddHHmmss}",
              SalesOrderId = salesOrderId,
              CreatedAt = DateTime.UtcNow,
              Status = DocumentStatus.Draft
          };

          // Tìm tồn kho khả dụng cho các sản phẩm trong đơn bán hàng
          int seq = 1;
          foreach (var item in order.Items)
          {
              var availableBalances = await _context.StockBalances
                  .Include(sb => sb.Location).ThenInclude(l => l!.Zone)
                  .Include(sb => sb.Lot)
                  .Where(sb => sb.ProductId == item.ProductId && sb.QtyAvailable > 0)
                  .OrderBy(sb => sb.Location!.Zone!.Code)
                  .ThenBy(sb => sb.Location!.Code)
                  .ToListAsync();

              decimal remainingNeed = item.Qty - item.DeliveredQty;
              foreach (var balance in availableBalances)
              {
                  if (remainingNeed <= 0) break;
                  decimal take = Math.Min(remainingNeed, balance.QtyAvailable);

                  pickList.Items.Add(new PickListItem
                  {
                      ProductId = item.ProductId,
                      LocationId = balance.LocationId,
                      LotId = balance.LotId,
                      QtyToPick = take,
                      SequenceOrder = seq++
                  });

                  remainingNeed -= take;
              }
          }

          _context.PickLists.Add(pickList);
          await _context.SaveChangesAsync();
          return pickList;
      }
  }
  ```

- [ ] **Step 2: Triển khai NotificationService.cs**
  Viết dịch vụ gửi thông báo và đếm số thông báo chưa đọc:
  ```csharp
  public class NotificationService : INotificationService
  {
      private readonly ApplicationDbContext _context;

      public NotificationService(ApplicationDbContext context)
      {
          _context = context;
      }

      public async Task SendNotificationAsync(string title, string message, string severity, string? refUrl = null)
      {
          var notif = new AppNotification
          {
              Title = title,
              Message = message,
              Severity = severity,
              CreatedAt = DateTime.UtcNow,
              ReferenceUrl = refUrl
          };
          _context.AppNotifications.Add(notif);
          await _context.SaveChangesAsync();
      }

      public async Task<int> GetUnreadCountAsync()
      {
          return await _context.AppNotifications.CountAsync(n => !n.IsRead);
      }

      public async Task<IEnumerable<AppNotification>> GetRecentNotificationsAsync(int take = 5)
      {
          return await _context.AppNotifications
              .OrderByDescending(n => n.CreatedAt)
              .Take(take)
              .ToListAsync();
      }
  }
  ```

- [ ] **Step 3: Đăng ký dịch vụ trong Program.cs**
  ```csharp
  builder.Services.AddScoped<IPickListService, PickListService>();
  builder.Services.AddScoped<INotificationService, NotificationService>();
  ```

- [ ] **Step 4: Commit**
  Run: `git add Services/ Program.cs`
  Run: `git commit -m "feat: implement PickListService and NotificationService"`

---

### Task 3: In Tem Đóng Gói PDF & Xuất Báo Cáo Tài Chính Kho Excel (ClosedXML)

**Files:**
- Modify: [PrintController.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Controllers/PrintController.cs)
- Create: [ReportController.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Controllers/ReportController.cs)

**Interfaces:**
- Consumes: API HTTP GET `/api/print/packingslip/{id}` và `/Report/ExportStockValuationExcel`.
- Produces: File PDF nhãn tem đóng gói 100x100mm và file Excel `.xlsx` báo cáo tài chính kho qua ClosedXML.

- [ ] **Step 1: Bổ sung endpoint In tem đóng gói PDF trong PrintController.cs**
  Thêm endpoint `/api/print/packingslip/{id}` trả về tem đóng gói PDF có mã QR code.

- [ ] **Step 2: Tạo ReportController.cs xuất Excel qua ClosedXML**
  ```csharp
  using ClosedXML.Excel;
  using Microsoft.AspNetCore.Authorization;
  using Microsoft.AspNetCore.Mvc;
  using Microsoft.EntityFrameworkCore;
  using WmsMes.Web.Data;

  namespace WmsMes.Web.Controllers;

  [Authorize]
  public class ReportController : Controller
  {
      private readonly ApplicationDbContext _context;

      public ReportController(ApplicationDbContext context)
      {
          _context = context;
      }

      public async Task<IActionResult> StockValuation()
      {
          var balances = await _context.StockBalances
              .Include(sb => sb.Product)
              .Include(sb => sb.Lot)
              .Include(sb => sb.Location).ThenInclude(l => l!.Zone).ThenInclude(z => z!.Warehouse)
              .Where(sb => sb.QtyAvailable > 0)
              .ToListAsync();

          return View(balances);
      }

      [HttpGet]
      public async Task<IActionResult> ExportStockValuationExcel()
      {
          var balances = await _context.StockBalances
              .Include(sb => sb.Product)
              .Include(sb => sb.Lot)
              .Include(sb => sb.Location).ThenInclude(l => l!.Zone).ThenInclude(z => z!.Warehouse)
              .Where(sb => sb.QtyAvailable > 0)
              .ToListAsync();

          using var workbook = new XLWorkbook();
          var worksheet = workbook.Worksheets.Add("Báo cáo Tài chính Kho");

          // Header
          worksheet.Cell(1, 1).Value = "BÁO CÁO GIÁ TRỊ TỒN KHO & TÀI CHÍNH";
          worksheet.Cell(1, 1).Style.Font.Bold = true;
          worksheet.Cell(1, 1).Style.Font.FontSize = 16;

          worksheet.Cell(3, 1).Value = "Mã SKU";
          worksheet.Cell(3, 2).Value = "Tên sản phẩm";
          worksheet.Cell(3, 3).Value = "Tên Kho";
          worksheet.Cell(3, 4).Value = "Vị trí";
          worksheet.Cell(3, 5).Value = "Số Lô";
          worksheet.Cell(3, 6).Value = "Số lượng tồn";
          worksheet.Cell(3, 7).Value = "Đơn giá vốn (VNĐ)";
          worksheet.Cell(3, 8).Value = "Tổng giá trị (VNĐ)";

          var headerRange = worksheet.Range("A3:H3");
          headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#0d6efd");
          headerRange.Style.Font.FontColor = XLColor.White;
          headerRange.Style.Font.Bold = true;

          int row = 4;
          foreach (var b in balances)
          {
              var totalVal = b.QtyAvailable * (b.Lot?.UnitPrice ?? 0m);
              worksheet.Cell(row, 1).Value = b.Product?.Code;
              worksheet.Cell(row, 2).Value = b.Product?.Name;
              worksheet.Cell(row, 3).Value = b.Location?.Zone?.Warehouse?.Name;
              worksheet.Cell(row, 4).Value = b.Location?.Code;
              worksheet.Cell(row, 5).Value = b.Lot?.LotNo;
              worksheet.Cell(row, 6).Value = b.QtyAvailable;
              worksheet.Cell(row, 7).Value = b.Lot?.UnitPrice ?? 0m;
              worksheet.Cell(row, 8).Value = totalVal;
              row++;
          }

          worksheet.Columns().AdjustToContents();

          using var stream = new MemoryStream();
          workbook.SaveAs(stream);
          var content = stream.ToArray();

          return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"BaoCao_TaiChinh_Kho_{DateTime.Now:yyyyMMdd}.xlsx");
      }
  }
  ```

- [ ] **Step 3: Commit**
  Run: `git add Controllers/`
  Run: `git commit -m "feat: implement packing slip PDF print and StockValuation Excel export using ClosedXML"`

---

### Task 4: Viết Unit Tests Kiểm chứng Phase 9

**Files:**
- Create: [SupplyChainReportsTests.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/WmsMes.Tests/SupplyChainReportsTests.cs)

**Interfaces:**
- Consumes: `PickListService`, `NotificationService`, `ReportController`.
- Produces: Kết quả test PASS.

- [ ] **Step 1: Viết test PickListSequenceOrdering**
  Kiểm tra danh sách PickList tự động sắp xếp vị trí kho đúng thứ tự.

- [ ] **Step 2: Viết test ExportStockValuationExcel_ReturnsValidSpreadsheet**
  Kiểm tra hàm xuất Excel trả về file stream `.xlsx` chuẩn.

- [ ] **Step 3: Chạy Unit Tests**
  Run: `dotnet test WmsMes.Tests/WmsMes.Tests.csproj`
  Expected: PASS tất cả bài test.

- [ ] **Step 4: Commit**
  Run: `git add WmsMes.Tests/`
  Run: `git commit -m "test: add unit tests for supply chain picklist ordering and excel export"`

---

### Task 5: Thiết kế Views & Tích hợp Bell Notification Badge trên Header Layout

**Files:**
- Create Views: `PickList/Index.cshtml`, `Create.cshtml`, `Details.cshtml`
- Create Views: `Report/StockValuation.cshtml`
- Modify: [Views/Shared/_Layout.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/Shared/_Layout.cshtml)

**Interfaces:**
- Consumes: Layout HTML/CSS và Javascript SignalR notification listener.
- Produces: Icon Chuông thông báo trên Header Navbar và các màn hình View mới.

- [ ] **Step 1: Tạo các View PickList & Report**
  Tạo các View giao diện HTML hiển thị danh sách lấy hàng và báo cáo tài chính kho có nút **Xuất Excel**.

- [ ] **Step 2: Thêm Icon Chuông Thông báo trên Header Navbar**
  Mở `Views/Shared/_Layout.cshtml` chèn thẻ Icon Chuông thông báo kèm badge số dư chưa đọc trên thanh Header Navbar:
  ```html
  <div class="dropdown me-3">
      <button class="btn btn-link position-relative text-dark p-0" id="notificationDropdown" data-bs-toggle="dropdown" aria-expanded="false">
          <i class="bi bi-bell fs-4"></i>
          <span class="position-absolute top-0 start-100 translate-middle badge rounded-pill bg-danger" id="unread-count">3</span>
      </button>
      <ul class="dropdown-menu dropdown-menu-end p-2" style="width: 300px;" aria-labelledby="notificationDropdown">
          <li class="dropdown-header bold">Thông báo hệ thống</li>
          <li><hr class="dropdown-divider"></li>
          <div id="notification-list">
              <!-- Render danh sách thông báo mới nhất -->
          </div>
      </ul>
  </div>
  ```

- [ ] **Step 3: Thêm menu Pick List & Báo cáo Tài chính Kho vào Sidebar**
  Chèn các liên kết menu tương ứng vào Sidebar Layout.

- [ ] **Step 4: Kiểm tra biên dịch**
  Run: `dotnet build`
  Expected: Build thành công không có lỗi.

- [ ] **Step 5: Commit**
  Run: `git add Views/`
  Run: `git commit -m "feat: complete UI views with bell notification navbar badge and supply chain reports"`
