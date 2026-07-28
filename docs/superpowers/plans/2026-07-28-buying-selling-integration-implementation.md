# Phân hệ Mua hàng & Bán hàng Tích hợp (Buying & Selling Integration) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Khép kín chu trình doanh nghiệp sản xuất bằng cách tích hợp Đơn bán hàng (`SalesOrder`), Đơn mua hàng (`PurchaseOrder`), tự động sinh Yêu cầu mua hàng (`PurchaseRequest`) từ MRP và liên kết tự động vào các Phiếu Nhập/Xuất kho.

**Architecture:** Tạo các thực thể `SalesOrder`, `SalesOrderItem`, `PurchaseRequest`, `PurchaseRequestItem`, `PurchaseOrder`, `PurchaseOrderItem`. Phát triển dịch vụ `PurchaseRequestService` chuyển đổi nhu cầu thiếu từ MRP thành PR, `PurchaseOrderService` lập PO gửi nhà cung cấp. Nâng cấp `InventoryService` tự động cập nhật số lượng đã nhập/xuất trên PO/SO và đóng đơn khi hoàn tất.

**Tech Stack:** ASP.NET Core 8 MVC, Entity Framework Core 8, C#, Bootstrap 5, Javascript.

## Global Constraints
- Target Framework: `.NET 8` net8.0.
- Đơn bán hàng có mã định dạng `SO-YYYYMMDD-XXX`.
- Yêu cầu mua hàng tự động tạo từ MRP có mã định dạng `PR-[Mã-kế-hoạch]`.
- Đơn mua hàng có mã định dạng `PO-YYYYMMDD-XXX`.
- Khi nhập kho theo PO, số lượng nhập thực tế sẽ được tích lũy vào `PurchaseOrderItem.ReceivedQty`. Khi tất cả các dòng đã giao đủ, PO chuyển sang trạng thái `Completed`.
- Khi xuất kho theo SO, số lượng xuất thực tế sẽ được tích lũy vào `SalesOrderItem.DeliveredQty`. Khi tất cả các dòng đã xuất đủ, SO chuyển sang trạng thái `Completed`.

---

### Task 1: Định nghĩa Thực thể & Migration Mua hàng - Bán hàng

**Files:**
- Create: [SalesOrder.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Entities/SalesOrder.cs)
- Create: [SalesOrderItem.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Entities/SalesOrderItem.cs)
- Create: [PurchaseRequest.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Entities/PurchaseRequest.cs)
- Create: [PurchaseRequestItem.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Entities/PurchaseRequestItem.cs)
- Create: [PurchaseOrder.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Entities/PurchaseOrder.cs)
- Create: [PurchaseOrderItem.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Entities/PurchaseOrderItem.cs)
- Modify: [GoodsReceipt.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Entities/GoodsReceipt.cs)
- Modify: [GoodsIssue.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Entities/GoodsIssue.cs)
- Modify: [ApplicationDbContext.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Data/ApplicationDbContext.cs)

**Interfaces:**
- Consumes: Cấu trúc cơ sở dữ liệu hiện có.
- Produces: Các bảng mới trong DB và liên kết FK trên `GoodsReceipt` / `GoodsIssue`.

- [ ] **Step 1: Tạo SalesOrder.cs & SalesOrderItem.cs**
  Tạo `Domain/Entities/SalesOrder.cs`:
  ```csharp
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;
  using WmsMes.Web.Domain.Enums;

  namespace WmsMes.Web.Domain.Entities;

  public class SalesOrder
  {
      public int Id { get; set; }

      [Required]
      [MaxLength(50)]
      public string OrderNo { get; set; } = string.Empty;

      [Required]
      public int CustomerId { get; set; }

      [ForeignKey(nameof(CustomerId))]
      public virtual Customer? Customer { get; set; }

      [Required]
      public DateTime OrderDate { get; set; } = DateTime.UtcNow;

      [Required]
      public DateTime DeliveryDate { get; set; }

      [Required]
      public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

      public virtual ICollection<SalesOrderItem> Items { get; set; } = new List<SalesOrderItem>();
  }
  ```

  Tạo `Domain/Entities/SalesOrderItem.cs`:
  ```csharp
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  namespace WmsMes.Web.Domain.Entities;

  public class SalesOrderItem
  {
      public int Id { get; set; }

      [Required]
      public int SalesOrderId { get; set; }

      [ForeignKey(nameof(SalesOrderId))]
      public virtual SalesOrder? SalesOrder { get; set; }

      [Required]
      public int ProductId { get; set; }

      [ForeignKey(nameof(ProductId))]
      public virtual Product? Product { get; set; }

      [Column(TypeName = "decimal(18,2)")]
      public decimal Qty { get; set; }

      [Column(TypeName = "decimal(18,2)")]
      public decimal UnitPrice { get; set; }

      [Column(TypeName = "decimal(18,2)")]
      public decimal DeliveredQty { get; set; } = 0m;
  }
  ```

- [ ] **Step 2: Tạo PurchaseRequest.cs & PurchaseRequestItem.cs**
  Tạo `Domain/Entities/PurchaseRequest.cs`:
  ```csharp
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;
  using WmsMes.Web.Domain.Enums;

  namespace WmsMes.Web.Domain.Entities;

  public class PurchaseRequest
  {
      public int Id { get; set; }

      [Required]
      [MaxLength(50)]
      public string RequestNo { get; set; } = string.Empty;

      [Required]
      public DateTime RequestDate { get; set; } = DateTime.UtcNow;

      [Required]
      public DateTime RequiredDate { get; set; }

      [Required]
      public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

      public int? ProductionPlanId { get; set; }

      [ForeignKey(nameof(ProductionPlanId))]
      public virtual ProductionPlan? ProductionPlan { get; set; }

      public virtual ICollection<PurchaseRequestItem> Items { get; set; } = new List<PurchaseRequestItem>();
  }
  ```

  Tạo `Domain/Entities/PurchaseRequestItem.cs`:
  ```csharp
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  namespace WmsMes.Web.Domain.Entities;

  public class PurchaseRequestItem
  {
      public int Id { get; set; }

      [Required]
      public int PurchaseRequestId { get; set; }

      [ForeignKey(nameof(PurchaseRequestId))]
      public virtual PurchaseRequest? PurchaseRequest { get; set; }

      [Required]
      public int ProductId { get; set; }

      [ForeignKey(nameof(ProductId))]
      public virtual Product? Product { get; set; }

      [Column(TypeName = "decimal(18,2)")]
      public decimal Qty { get; set; }
  }
  ```

- [ ] **Step 3: Tạo PurchaseOrder.cs & PurchaseOrderItem.cs**
  Tạo `Domain/Entities/PurchaseOrder.cs`:
  ```csharp
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;
  using WmsMes.Web.Domain.Enums;

  namespace WmsMes.Web.Domain.Entities;

  public class PurchaseOrder
  {
      public int Id { get; set; }

      [Required]
      [MaxLength(50)]
      public string OrderNo { get; set; } = string.Empty;

      [Required]
      public int SupplierId { get; set; }

      [ForeignKey(nameof(SupplierId))]
      public virtual Supplier? Supplier { get; set; }

      [Required]
      public DateTime OrderDate { get; set; } = DateTime.UtcNow;

      [Required]
      public DateTime ExpectedDeliveryDate { get; set; }

      [Required]
      public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

      public int? PurchaseRequestId { get; set; }

      [ForeignKey(nameof(PurchaseRequestId))]
      public virtual PurchaseRequest? PurchaseRequest { get; set; }

      public virtual ICollection<PurchaseOrderItem> Items { get; set; } = new List<PurchaseOrderItem>();
  }
  ```

  Tạo `Domain/Entities/PurchaseOrderItem.cs`:
  ```csharp
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  namespace WmsMes.Web.Domain.Entities;

  public class PurchaseOrderItem
  {
      public int Id { get; set; }

      [Required]
      public int PurchaseOrderId { get; set; }

      [ForeignKey(nameof(PurchaseOrderId))]
      public virtual PurchaseOrder? PurchaseOrder { get; set; }

      [Required]
      public int ProductId { get; set; }

      [ForeignKey(nameof(ProductId))]
      public virtual Product? Product { get; set; }

      [Column(TypeName = "decimal(18,2)")]
      public decimal Qty { get; set; }

      [Column(TypeName = "decimal(18,2)")]
      public decimal UnitPrice { get; set; }

      [Column(TypeName = "decimal(18,2)")]
      public decimal ReceivedQty { get; set; } = 0m;
  }
  ```

- [ ] **Step 4: Mở rộng GoodsReceipt & GoodsIssue**
  Trong `Domain/Entities/GoodsReceipt.cs`, thêm:
  ```csharp
  public int? PurchaseOrderId { get; set; }

  [ForeignKey(nameof(PurchaseOrderId))]
  public virtual PurchaseOrder? PurchaseOrder { get; set; }
  ```

  Trong `Domain/Entities/GoodsIssue.cs`, thêm:
  ```csharp
  public int? SalesOrderId { get; set; }

  [ForeignKey(nameof(SalesOrderId))]
  public virtual SalesOrder? SalesOrder { get; set; }
  ```

- [ ] **Step 5: Đăng ký DbSets và chạy Migration**
  Trong `Data/ApplicationDbContext.cs`:
  ```csharp
  public DbSet<SalesOrder> SalesOrders { get; set; } = null!;
  public DbSet<SalesOrderItem> SalesOrderItems { get; set; } = null!;
  public DbSet<PurchaseRequest> PurchaseRequests { get; set; } = null!;
  public DbSet<PurchaseRequestItem> PurchaseRequestItems { get; set; } = null!;
  public DbSet<PurchaseOrder> PurchaseOrders { get; set; } = null!;
  public DbSet<PurchaseOrderItem> PurchaseOrderItems { get; set; } = null!;
  ```

  Run: `dotnet ef migrations add AddBuyingAndSellingModule`
  Run: `dotnet ef database update`
  Expected: Cập nhật cơ sở dữ liệu thành công.

- [ ] **Step 6: Commit**
  Run: `git add Domain/Entities/ Data/ApplicationDbContext.cs Data/Migrations/`
  Run: `git commit -m "feat: add buying and selling module entities and migrations"`

---

### Task 2: Triển khai Dịch vụ Nghiệp vụ Mua hàng & Bán hàng

**Files:**
- Create: [IPurchaseRequestService.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Services/IPurchaseRequestService.cs) & [PurchaseRequestService.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Services/PurchaseRequestService.cs)
- Create: [IPurchaseOrderService.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Services/IPurchaseOrderService.cs) & [PurchaseOrderService.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Services/PurchaseOrderService.cs)
- Create: [ISalesOrderService.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Services/ISalesOrderService.cs) & [SalesOrderService.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Services/SalesOrderService.cs)
- Modify: [InventoryService.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Services/InventoryService.cs)
- Modify: [Program.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Program.cs)

**Interfaces:**
- Consumes: DB Context và `IProductionPlanService`.
- Produces: Dịch vụ xử lý PR từ MRP, tạo PO và cập nhật số lượng nhập/xuất lũy kế trên PO/SO.

- [ ] **Step 1: Triển khai PurchaseRequestService (Sinh PR từ MRP)**
  Tạo `Services/PurchaseRequestService.cs`:
  ```csharp
  public class PurchaseRequestService : IPurchaseRequestService
  {
      private readonly ApplicationDbContext _context;
      private readonly IProductionPlanService _planService;

      public PurchaseRequestService(ApplicationDbContext context, IProductionPlanService planService)
      {
          _context = context;
          _planService = planService;
      }

      public async Task<PurchaseRequest?> GenerateFromMrpAsync(int productionPlanId, string userId)
      {
          var mrpResults = await _planService.CalculatePlanRequirementsAsync(productionPlanId);
          var neededItems = mrpResults.Where(r => r.NetDemand > 0).ToList();

          if (!neededItems.Any()) return null;

          var plan = await _context.ProductionPlans.FindAsync(productionPlanId);

          var pr = new PurchaseRequest
          {
              RequestNo = $"PR-{plan?.PlanNo ?? DateTime.UtcNow.Ticks.ToString()}",
              RequestDate = DateTime.UtcNow,
              RequiredDate = DateTime.UtcNow.AddDays(5),
              Status = DocumentStatus.Draft,
              ProductionPlanId = productionPlanId,
              Items = neededItems.Select(item => new PurchaseRequestItem
              {
                  ProductId = item.ComponentProductId,
                  Qty = item.NetDemand
              }).ToList()
          };

          _context.PurchaseRequests.Add(pr);
          await _context.SaveChangesAsync();
          return pr;
      }
  }
  ```

- [ ] **Step 2: Triển khai PurchaseOrderService & SalesOrderService**
  Tạo các dịch vụ quản lý PO và SO.
  Trong `PurchaseOrderService`, viết hàm `CreateOrderFromRequestAsync(int requestId, int supplierId)` để tự động tạo `PurchaseOrder` kèm theo đơn giá tiêu chuẩn `Product.StandardCost`.

- [ ] **Step 3: Cập nhật InventoryService.cs để liên kết PO và SO**
  Trong `CompleteGoodsReceiptCoreAsync` của `InventoryService.cs`:
  ```csharp
  if (receipt.PurchaseOrderId.HasValue)
  {
      var po = await _context.PurchaseOrders
          .Include(p => p.Items)
          .FirstOrDefaultAsync(p => p.Id == receipt.PurchaseOrderId.Value);

      if (po != null)
      {
          foreach (var line in receipt.Lines)
          {
              var poItem = po.Items.FirstOrDefault(i => i.ProductId == line.ProductId);
              if (poItem != null)
              {
                  poItem.ReceivedQty += line.Qty;
              }
          }

          // Đóng PO nếu tất cả các dòng đã giao đủ
          if (po.Items.All(i => i.ReceivedQty >= i.Qty))
          {
              po.Status = DocumentStatus.Completed;
          }
      }
  }
  ```

  Tương tự trong `CompleteGoodsIssueCoreAsync` đối với `SalesOrderId` và `DeliveredQty`.

- [ ] **Step 4: Đăng ký các dịch vụ trong Program.cs**
  ```csharp
  builder.Services.AddScoped<IPurchaseRequestService, PurchaseRequestService>();
  builder.Services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
  builder.Services.AddScoped<ISalesOrderService, SalesOrderService>();
  ```

- [ ] **Step 5: Commit**
  Run: `git add Services/ Program.cs`
  Run: `git commit -m "feat: implement buying and selling core services and integrate PO/SO fulfillment in inventory"`

---

### Task 3: Viết Unit Tests Cho Phân hệ Mua hàng - Bán hàng

**Files:**
- Create: [BuyingSellingIntegrationTests.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/WmsMes.Tests/BuyingSellingIntegrationTests.cs)

**Interfaces:**
- Consumes: Dịch vụ `PurchaseRequestService`, `InventoryService`.
- Produces: Kết quả kiểm thử tự động PASS.

- [ ] **Step 1: Viết test GeneratePRFromMRP_Success**
  Tạo bài test kiểm tra việc tự động tạo Yêu cầu mua hàng từ kết quả MRP:
  ```csharp
  [Fact]
  public async Task GeneratePRFromMRP_CreatesPurchaseRequestWithNetDemand()
  {
      // Thử nghiệm tạo Kế hoạch sản xuất có thiếu vật tư -> Gọi GenerateFromMrpAsync
      // Assert: PurchaseRequest được lưu vào DB với đúng danh sách vật tư thiếu.
  }
  ```

- [ ] **Step 2: Viết test CompleteReceipt_UpdatesPOReceivedQtyAndClosesPO**
  Tạo bài test kiểm tra việc hoàn tất Nhập kho theo PO tự động tích lũy số lượng đã nhận và đóng Đơn mua hàng khi hoàn tất.

- [ ] **Step 3: Chạy Unit Tests**
  Run: `dotnet test`
  Expected: PASS tất cả bài kiểm thử.

- [ ] **Step 4: Commit**
  Run: `git add WmsMes.Tests/BuyingSellingIntegrationTests.cs`
  Run: `git commit -m "test: add integration tests for buying and selling module"`

---

### Task 4: Xây dựng Controllers Mua hàng & Bán hàng

**Files:**
- Create: [SalesOrderController.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Controllers/SalesOrderController.cs)
- Create: [PurchaseOrderController.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Controllers/PurchaseOrderController.cs)
- Modify: [ProductionPlanController.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Controllers/ProductionPlanController.cs)

**Interfaces:**
- Consumes: Yêu cầu HTTP MVC.
- Produces: Các endpoint quản lý Đơn bán hàng, Yêu cầu mua hàng và Đơn mua hàng.

- [ ] **Step 1: Tạo SalesOrderController.cs**
  Triển khai các action xử lý danh sách (`Index`), tạo mới (`Create`), chi tiết (`Details`) cho Đơn bán hàng.

- [ ] **Step 2: Tạo PurchaseOrderController.cs**
  Triển khai các action quản lý Yêu cầu mua hàng (`Requests`), Đơn mua hàng (`Index`, `Create`, `Details`, `CreateFromRequest`).

- [ ] **Step 3: Cập nhật ProductionPlanController.cs**
  Bổ sung POST action `GeneratePurchaseRequest(int id)`:
  ```csharp
  [HttpPost]
  [ValidateAntiForgeryToken]
  public async Task<IActionResult> GeneratePurchaseRequest(int id)
  {
      var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
      var pr = await _prService.GenerateFromMrpAsync(id, userId ?? "system");
      if (pr != null)
      {
          TempData["StatusMessage"] = $"Đã tự động tạo Yêu cầu mua hàng mã {pr.RequestNo} từ kết quả MRP.";
      }
      else
      {
          TempData["StatusMessage"] = "Tất cả các nguyên vật liệu đều đã đủ tồn kho, không cần tạo Yêu cầu mua hàng.";
      }
      return RedirectToAction(nameof(Details), new { id });
  }
  ```

- [ ] **Step 4: Commit**
  Run: `git add Controllers/`
  Run: `git commit -m "feat: add SalesOrderController and PurchaseOrderController endpoints"`

---

### Task 5: Cập nhật Giao diện Views & Tích hợp Tự động hóa

**Files:**
- Create Views: `SalesOrder/Index.cshtml`, `Create.cshtml`, `Details.cshtml`
- Create Views: `PurchaseOrder/Index.cshtml`, `Create.cshtml`, `Details.cshtml`, `Requests.cshtml`
- Modify: [Views/Inventory/CreateReceipt.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/Inventory/CreateReceipt.cshtml)
- Modify: [Views/Inventory/CreateIssue.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/Inventory/CreateIssue.cshtml)
- Modify: [Views/ProductionPlan/Details.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/ProductionPlan/Details.cshtml)
- Modify: [Views/Shared/_Layout.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/Shared/_Layout.cshtml)

**Interfaces:**
- Consumes: Razor view engine & Javascript.
- Produces: Màn hình giao diện bán hàng/mua hàng và tự động nạp dòng chi tiết khi chọn PO/SO.

- [ ] **Step 1: Tạo các View Bán hàng và Mua hàng**
  Tạo các View giao diện HTML đẹp mắt theo chuẩn Bootstrap 5.

- [ ] **Step 2: Nâng cấp CreateReceipt.cshtml (Nạp theo PO)**
  Thêm Dropdown `PurchaseOrderId` ở đầu form `CreateReceipt.cshtml`. Khi thủ kho chọn 1 PO, Javascript sẽ tự động chọn Nhà cung cấp tương ứng và thêm tự động các dòng sản phẩm trong PO vào bảng nhập kho.

- [ ] **Step 3: Nâng cấp CreateIssue.cshtml (Nạp theo SO)**
  Thêm Dropdown `SalesOrderId` ở đầu form `CreateIssue.cshtml`. Khi chọn SO, tự động chọn Khách hàng tương ứng và thêm các dòng sản phẩm trong SO.

- [ ] **Step 4: Nâng cấp ProductionPlan/Details.cshtml**
  Bổ sung nút **"Tạo Yêu cầu mua hàng từ MRP"** bên cạnh nút Tạo Lệnh sản xuất.

- [ ] **Step 5: Cập nhật Sidebar Layout**
  Thêm các mục menu "Đơn bán hàng (SO)" và "Đơn mua hàng (PO)" vào Sidebar của `_Layout.cshtml`.

- [ ] **Step 6: Kiểm tra biên dịch**
  Run: `dotnet build`
  Expected: Build thành công không có lỗi.

- [ ] **Step 7: Commit**
  Run: `git add Views/`
  Run: `git commit -m "feat: complete UI views and automatic PO/SO form population for buying and selling module"`
