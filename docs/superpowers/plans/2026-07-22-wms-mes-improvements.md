# [Feature] WMS + MES Core Improvements & Localization Implementation Plan

> **For agentic workers (Codex / Antigravity):** REQUIRED SUB-SKILL: Use TDD & step-by-step verification. Follow exact file paths and test execution commands. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Triển khai các tính năng cải tiến hệ thống WMS + MES bao gồm:
1. Sửa lỗi `InvalidOperationException: Work order is not ready for production` ở Trạm vận hành.
2. Thanh tìm kiếm toàn hệ thống (Global Search).
3. Xem chi tiết vị trí lưu trữ của sản phẩm tại các kho và sản phẩm nằm trong vị trí kho.
4. Cho phép Nhập kho/Xuất kho nhiều vật tư/dòng chi tiết trong cùng một phiếu tạo.
5. Kiểm soát chặn xuất kho thủ công vượt quá lượng khả dụng (bảo vệ lượng hàng đã giữ chỗ).
6. Xây dựng chức năng khai báo định mức nguyên vật liệu (BOM) và kích hoạt BOM độc bản.
7. Ghi nhận sản lượng sản xuất hàng ngày của thành phẩm (`DailyProductionLog`) và thanh tiến độ tương ứng.
8. Chuẩn hóa ngôn ngữ tiếng Việt (bao gồm cả trạng thái enums) và định dạng ngày tháng/số kiểu Việt Nam.

**Tech Stack:** ASP.NET Core 8 MVC, Entity Framework Core 8, xUnit.

---

## Global Constraints & Baseline
- Target Framework: `.NET 8` (`net8.0`)
- Không làm gãy bất kỳ kiểm thử hiện có nào trong `WmsMes.Tests`.
- Đảm bảo các giao dịch kho và sản xuất luôn chạy trong Database Transaction.

---

### Task 1: Khắc phục lỗi Start Step ở Trạm vận hành (Worker Controller)

**Files:**
- Modify: [WorkerController.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Controllers/WorkerController.cs)

- [ ] **Step 1: Lọc trạng thái Lệnh sản xuất trong WorkerController.Index**
  Cập nhật truy vấn lấy danh sách `WorkOrderStep` trong `Index` action của `WorkerController.cs` để chỉ hiển thị các công đoạn thuộc Lệnh sản xuất (`WorkOrder`) có trạng thái là `Approved` (Đã duyệt) hoặc `InProgress` (Đang sản xuất):
  ```csharp
  .Where(s => s.Status != WorkOrderStepStatus.Completed && 
              s.WorkOrder != null && 
              (s.WorkOrder.Status == WorkOrderStatus.Approved || s.WorkOrder.Status == WorkOrderStatus.InProgress))
  ```

---

### Task 2: Thanh tìm kiếm hệ thống (Global Search Bar)

**Files:**
- Create: [SearchResultViewModel.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/ViewModels/SearchResultViewModel.cs)
- Modify: [HomeController.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Controllers/HomeController.cs)
- Create: [Search.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/Home/Search.cshtml)
- Modify: [_Layout.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/Shared/_Layout.cshtml)

- [ ] **Step 1: Tạo View Model SearchResultViewModel.cs**
  ```csharp
  using WmsMes.Web.Domain.Entities;

  namespace WmsMes.Web.ViewModels;

  public sealed class SearchResultViewModel
  {
      public string Query { get; set; } = string.Empty;
      public List<Product> Products { get; set; } = new();
      public List<WorkOrder> WorkOrders { get; set; } = new();
      public List<Lot> Lots { get; set; } = new();
      public List<Location> Locations { get; set; } = new();
  }
  ```

- [ ] **Step 2: Triển khai action Search trong HomeController.cs**
  Thêm action `Search` nhận từ khóa `q` và truy vấn dữ liệu từ database:
  ```csharp
  [HttpGet]
  public async Task<IActionResult> Search(string q)
  {
      if (string.IsNullOrWhiteSpace(q))
      {
          return RedirectToAction(nameof(Index));
      }
      q = q.Trim();
      
      var products = await _context.Products.AsNoTracking()
          .Where(p => p.Code.Contains(q) || p.Name.Contains(q))
          .Take(10).ToListAsync();

      var workOrders = await _context.WorkOrders.AsNoTracking()
          .Where(w => w.Code.Contains(q))
          .Take(10).ToListAsync();

      var lots = await _context.Lots.AsNoTracking().Include(l => l.Product)
          .Where(l => l.LotNo.Contains(q))
          .Take(10).ToListAsync();

      var locations = await _context.Locations.AsNoTracking()
          .Where(l => l.Code.Contains(q))
          .Take(10).ToListAsync();

      return View(new SearchResultViewModel
      {
          Query = q,
          Products = products,
          WorkOrders = workOrders,
          Lots = lots,
          Locations = locations
      });
  }
  ```

- [ ] **Step 3: Tạo giao diện hiển thị kết quả Search.cshtml**
  Tạo view `Views/Home/Search.cshtml` phân nhóm kết quả và hiển thị dạng thẻ/bảng kèm các liên kết xem chi tiết.

- [ ] **Step 4: Thêm ô tìm kiếm trên layout _Layout.cshtml**
  Thêm thẻ `<form>` tìm kiếm trong class `app-topbar` bên cạnh thông tin người dùng:
  ```html
  <form class="d-flex gap-2 ms-auto me-3" asp-controller="Home" asp-action="Search" method="get">
      <input type="search" name="q" class="form-control form-control-sm" placeholder="Tìm SKU, lệnh SX, số lô..." required style="max-width: 250px;" />
      <button class="btn btn-outline-secondary btn-sm" type="submit">Tìm</button>
  </form>
  ```

---

### Task 3: Chi tiết vị trí tồn kho của sản phẩm và sản phẩm tại vị trí kho

**Files:**
- Modify: [Product/Index.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/Product/Index.cshtml)
- Modify: [Warehouse/Index.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/Warehouse/Index.cshtml)
- Modify: [ProductController.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Controllers/ProductController.cs)
- Modify: [WarehouseController.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Controllers/WarehouseController.cs)

- [ ] **Step 1: Xem vị trí tồn kho trong Danh mục sản phẩm**
  - Trong `ProductController.cs`, nạp thêm số dư tồn kho của từng sản phẩm thông qua `StockBalances`.
  - Trong `Product/Index.cshtml`, thêm nút "Vị trí tồn". Khi bấm vào, kích hoạt Bootstrap Modal tải danh sách vị trí kho (`Location`), lô hàng (`Lot`) và số lượng khả dụng/giữ chỗ/tạm giữ của sản phẩm đó qua Ajax hoặc nạp sẵn trong HTML.

- [ ] **Step 2: Xem chi tiết sản phẩm tại từng Vị trí trong Cấu trúc kho**
  - Trong `WarehouseController.cs`, nạp danh sách `StockBalance` đi kèm dữ liệu nhà kho để hiển thị thông tin sản phẩm tại từng vị trí.
  - Trong `Warehouse/Index.cshtml`, làm cho các `location-chip` có thể nhấp được (hoặc thêm icon chi tiết). Khi bấm vào, mở một Modal hiển thị: mã SKU, tên sản phẩm, số lô, số lượng khả dụng, giữ chỗ, tạm giữ đang nằm tại vị trí đó.

---

### Task 4: Phiếu Nhập kho & Xuất kho nhiều dòng (Multi-item Receipt & Issue)

**Files:**
- Create: [InventoryViewModels.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/ViewModels/InventoryViewModels.cs)
- Modify: [InventoryController.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Controllers/InventoryController.cs)
- Modify: [CreateReceipt.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/Inventory/CreateReceipt.cshtml)
- Modify: [CreateIssue.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/Inventory/CreateIssue.cshtml)

- [ ] **Step 1: Tạo các View Model phục vụ liên kết nhiều dòng**
  ```csharp
  namespace WmsMes.Web.ViewModels;

  public class CreateReceiptViewModel
  {
      public int SupplierId { get; set; }
      public List<ReceiptLineInput> Lines { get; set; } = new();
  }

  public class ReceiptLineInput
  {
      public int ProductId { get; set; }
      public string LotNo { get; set; } = string.Empty;
      public decimal Qty { get; set; }
      public decimal UnitPrice { get; set; }
      public int LocationId { get; set; }
  }

  public class CreateIssueViewModel
  {
      public int CustomerId { get; set; }
      public List<IssueLineInput> Lines { get; set; } = new();
  }

  public class IssueLineInput
  {
      public int ProductId { get; set; }
      public int LotId { get; set; }
      public decimal Qty { get; set; }
      public int LocationId { get; set; }
  }
  ```

- [ ] **Step 2: Cập nhật giao diện nhập liệu động (JavaScript)**
  - Thay thế form nhập một dòng bằng một bảng chi tiết trong `CreateReceipt.cshtml` và `CreateIssue.cshtml`.
  - Thêm nút "Thêm dòng" và "Xóa dòng" bằng JavaScript. Các thẻ `<input>` hoặc `<select>` bên trong dòng mới sẽ được đặt tên dạng `Lines[0].ProductId`, `Lines[1].ProductId`... để ASP.NET Core Model Binder tự động ánh xạ thành List.

- [ ] **Step 3: Cập nhật controller nhận và xử lý nhiều dòng**
  Cập nhật action POST `CreateReceipt` và `CreateIssue` để nhận các ViewModel tương ứng. Dùng vòng lặp tạo danh sách `GoodsReceiptLine` / `GoodsIssueLine` trong thực thể `GoodsReceipt` / `GoodsIssue` trước khi lưu và gọi dịch vụ hoàn tất phiếu kho.

---

### Task 5: Bảo vệ lượng hàng Giữ chỗ khi Xuất kho

**Files:**
- Modify: [InventoryController.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Controllers/InventoryController.cs)

- [ ] **Step 1: Bổ sung logic chặn xuất kho thủ công vượt quá khả dụng**
  Khi xử lý POST `CreateIssue` cho nhiều dòng, đối với mỗi dòng kiểm tra:
  ```csharp
  var balance = await _context.StockBalances
      .FirstOrDefaultAsync(sb => sb.ProductId == line.ProductId && sb.LotId == line.LotId && sb.LocationId == line.LocationId);
  if (balance == null || balance.QtyAvailable < line.Qty)
  {
      ModelState.AddModelError("", $"Lô hàng tại vị trí đã chọn không đủ số lượng khả dụng để xuất (Chỉ còn {balance?.QtyAvailable ?? 0m}). Số lượng giữ chỗ đang được bảo vệ.");
  }
  ```
  Điều này ngăn chặn việc xuất thủ công lạm dụng vào phần `QtyReserved` đã được khóa cho các lệnh sản xuất.

---

### Task 6: Xây dựng chức năng Khai báo định mức nguyên vật liệu (BOM)

**Files:**
- Create: [BomController.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Controllers/BomController.cs)
- Create Views: `Index.cshtml`, `Create.cshtml`, `Details.cshtml` trong thư mục [Views/Bom](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/Bom)
- Modify: [_Layout.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/Shared/_Layout.cshtml)

- [ ] **Step 1: Tạo BomController.cs**
  Triển khai các action:
  - `Index()`: Liệt kê danh sách định mức BOM (Product, Version, EffectiveDate, IsActive).
  - `Details(int id)`: Xem chi tiết định mức BOM và danh sách vật tư thành phần (`BOMItem` bao gồm ComponentProduct, QtyPer, ScrapPercent).
  - `Create()` (GET/POST): Tạo mới BOM kèm danh sách vật tư thành phần động (sử dụng bảng động JavaScript tương tự như phiếu kho).
  - `ToggleActive(int id)`: Đổi trạng thái kích hoạt BOM. Khi một BOM được chuyển thành `IsActive = true`, tìm các BOM khác của cùng `ProductId` và đặt `IsActive = false` (Quy tắc độc bản `BR-BOM-002`).

- [ ] **Step 2: Thiết kế giao diện quản lý BOM**
  Thiết kế các view tương ứng bằng Vanilla CSS đồng bộ với phong cách thiết kế hiện tại.

- [ ] **Step 3: Thêm menu định mức BOM trên Sidebar**
  Thêm liên kết `<a asp-controller="Bom" asp-action="Index">Định mức vật tư (BOM)</a>` vào phần quản lý sản xuất của `_Layout.cshtml`.

---

### Task 7: Nhật ký sản lượng sản xuất hàng ngày và thanh tiến độ

**Files:**
- Create: [DailyProductionLog.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Entities/DailyProductionLog.cs)
- Modify: [ApplicationDbContext.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Data/ApplicationDbContext.cs)
- Modify: [WorkOrderController.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Controllers/WorkOrderController.cs)
- Modify: [WorkOrder/Index.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/WorkOrder/Index.cshtml)
- Modify: [WorkOrder/Details.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/WorkOrder/Details.cshtml)

- [ ] **Step 1: Định nghĩa thực thể DailyProductionLog.cs**
  ```csharp
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  namespace WmsMes.Web.Domain.Entities;

  public class DailyProductionLog
  {
      public int Id { get; set; }

      [Required]
      public int WorkOrderId { get; set; }

      [ForeignKey(nameof(WorkOrderId))]
      public virtual WorkOrder? WorkOrder { get; set; }

      [Required]
      public DateTime Date { get; set; }

      [Column(TypeName = "decimal(18,2)")]
      public decimal QtyProduced { get; set; }

      [MaxLength(250)]
      public string Notes { get; set; } = string.Empty;
  }
  ```

- [ ] **Step 2: Đăng ký trong DbContext và tạo Migration**
  - Thêm `DbSet<DailyProductionLog> DailyProductionLogs` vào `ApplicationDbContext.cs`.
  - Thiết lập liên kết cascade với `WorkOrder`.
  - Tạo và cập nhật migration thông qua CLI:
    `dotnet ef migrations add AddDailyProductionLog`
    `dotnet ef database update`

- [ ] **Step 3: Triển khai action AddDailyLog trong WorkOrderController.cs**
  Tạo action POST `AddDailyLog` cho phép lưu trữ sản lượng hàng ngày. Chỉ cho phép nhập nhật ký khi Lệnh sản xuất có trạng thái `InProgress` (Đang sản xuất).

- [ ] **Step 4: Cập nhật giao diện xem tiến độ Lệnh sản xuất**
  - Trong `WorkOrder/Index.cshtml` và `Details.cshtml`, nạp kèm danh sách `DailyProductionLogs`.
  - Tính toán `Tổng sản lượng đạt = Sum(QtyProduced)`.
  - Tính toán `% Tiến độ = (Tổng sản lượng đạt / Lệnh.Qty) * 100`.
  - Vẽ thanh tiến độ (`progress-bar`) trực quan. Nếu lệnh chưa hoàn thành mục tiêu và thời gian hiện tại đã vượt quá `DueDate`, chuyển màu thanh tiến độ sang đỏ kèm dòng chữ "Trễ hạn sản xuất!". Nếu còn hạn, hiển thị số ngày còn lại.
  - Trong `Details.cshtml`, thêm form nhập nhanh sản lượng hàng ngày (Date, Qty, Notes) để quản lý kho ghi nhận báo cáo mỗi ngày.

---

### Task 8: Chuẩn hóa ngôn ngữ và Định dạng kiểu Việt Nam

**Files:**
- Create: [CommonExtensions.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Common/CommonExtensions.cs)
- Modify: Toàn bộ các View `.cshtml` trong hệ thống

- [ ] **Step 1: Tạo CommonExtensions.cs hỗ trợ dịch hiển thị trạng thái**
  ```csharp
  using WmsMes.Web.Domain.Enums;

  namespace WmsMes.Web.Domain.Common;

  public static class CommonExtensions
  {
      public static string ToVietnameseString(this WorkOrderStatus status) => status switch
      {
          WorkOrderStatus.Draft => "Nháp",
          WorkOrderStatus.Pending => "Chờ duyệt",
          WorkOrderStatus.Approved => "Đã phê duyệt",
          WorkOrderStatus.InProgress => "Đang sản xuất",
          WorkOrderStatus.Completed => "Đã hoàn thành",
          WorkOrderStatus.Closed => "Đã đóng",
          _ => status.ToString()
      };

      public static string ToVietnameseString(this WorkOrderStepStatus status) => status switch
      {
          WorkOrderStepStatus.Pending => "Chờ bắt đầu",
          WorkOrderStepStatus.InProgress => "Đang sản xuất",
          WorkOrderStepStatus.Completed => "Đã hoàn thành",
          _ => status.ToString()
      };

      public static string ToVietnameseString(this DocumentStatus status) => status switch
      {
          DocumentStatus.Draft => "Nháp",
          DocumentStatus.Pending => "Chờ duyệt",
          DocumentStatus.Completed => "Đã hoàn thành",
          DocumentStatus.Cancelled => "Đã hủy",
          _ => status.ToString()
      };

      public static string ToVietnameseString(this ProductType type) => type switch
      {
          ProductType.RawMaterial => "Nguyên vật liệu",
          ProductType.WIP => "Bán thành phẩm",
          ProductType.FinishedGood => "Thành phẩm",
          _ => type.ToString()
      };
  }
  ```

- [ ] **Step 2: Cập nhật hiển thị ngôn ngữ trên giao diện**
  - Import namespace `@using WmsMes.Web.Domain.Common` trong `_ViewImports.cshtml` hoặc trực tiếp tại các View.
  - Sử dụng `.ToVietnameseString()` để hiển thị trạng thái của Lệnh sản xuất, công đoạn, loại sản phẩm và phiếu kho.
  - Sửa đổi các nhãn tiếng Anh còn sót lại (ví dụ: "Master data" -> "Dữ liệu danh mục", "Storage map" -> "Sơ đồ kho", "Realtime inventory" -> "Tồn kho thực tế", "WIP Zone" -> "Khu bán thành phẩm").
  - Đổi hiển thị ngày tháng từ `.ToString("yyyy-MM-dd")` sang `.ToString("dd/MM/yyyy")`.

---

## Verification Plan

### Automated Tests
- Chạy toàn bộ unit tests hiện có để bảo đảm baseline không bị hỏng:
  `dotnet test`

### Manual Verification
1. Đăng nhập hệ thống dưới quyền Worker, vào Trạm vận hành sản xuất để đảm bảo không hiển thị các lệnh Draft/Pending và nút Bắt đầu hoạt động không lỗi.
2. Thử tạo phiếu Nhập kho/Xuất kho có từ 2-3 vật tư đồng thời và kiểm tra số dư tồn kho realtime tăng giảm tương ứng.
3. Kích hoạt thử một BOM mới cho sản phẩm và kiểm tra xem hệ thống có tự động tắt BOM cũ đang hoạt động hay không.
4. Chọn một Lệnh sản xuất đang thực hiện, ghi nhận sản lượng hàng ngày liên tiếp 3 ngày và theo dõi thanh tiến độ tăng dần. Thử chỉnh DueDate về quá khứ để xem thanh tiến độ đổi sang màu đỏ cảnh báo trễ hạn.
5. Kiểm tra thanh tìm kiếm ở đầu trang bằng cách tìm kiếm mã SKU hoặc mã Lệnh sản xuất.
