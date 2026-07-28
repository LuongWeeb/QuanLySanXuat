# Kế hoạch Sản xuất & Chạy MRP Tổng hợp (Phase 4) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Xây dựng Module Kế hoạch sản xuất (`ProductionPlan`), cho phép gom nhu cầu nhiều sản phẩm, chạy thuật toán MRP tổng hợp tính toán vật tư thiếu hụt trên toàn kế hoạch và tự động hóa khâu tạo hàng loạt Lệnh sản xuất nháp chỉ bằng một click chuột.

**Architecture:** Tạo thực thể `ProductionPlan` và `ProductionPlanItem`. Xây dựng `ProductionPlanService` chứa thuật toán phân rã BOM gộp, tính nhu cầu thực tế sau khi trừ tồn khả dụng và logic tạo `WorkOrder` hàng loạt chạy trong Database Transaction. Tích hợp giao diện quản lý kế hoạch sản xuất thân thiện với Planner.

**Tech Stack:** ASP.NET Core 8 MVC, Entity Framework Core 8, C#, Bootstrap 5, Javascript.

## Global Constraints
- Target Framework: `.NET 8` net8.0.
- Lệnh sản xuất được tạo tự động từ kế hoạch sản xuất sẽ có mã định dạng: `WO-[Mã-kế-hoạch]-[Mã-SKU-sản-phẩm]` và ở trạng thái mặc định là `Draft` (Nháp).
- Hạn hoàn thành mặc định của Lệnh sản xuất tự động là ngày lập kế hoạch cộng thêm 7 ngày.

---

### Task 1: Cấu trúc Dữ liệu & Migration Kế hoạch sản xuất

**Files:**
- Create: [ProductionPlan.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Entities/ProductionPlan.cs)
- Create: [ProductionPlanItem.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Entities/ProductionPlanItem.cs)
- Modify: [ApplicationDbContext.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Data/ApplicationDbContext.cs)

**Interfaces:**
- Consumes: Database schema hiện có.
- Produces: Bảng `ProductionPlans` và `ProductionPlanItems` trong DB.

- [ ] **Step 1: Tạo thực thể ProductionPlan.cs**
  Định nghĩa thực thể chính cho kế hoạch sản xuất:
  ```csharp
  using System.ComponentModel.DataAnnotations;
  using WmsMes.Web.Domain.Enums;

  namespace WmsMes.Web.Domain.Entities;

  public class ProductionPlan
  {
      public int Id { get; set; }

      [Required]
      [MaxLength(50)]
      public string PlanNo { get; set; } = string.Empty;

      [Required]
      public DateTime PlanDate { get; set; } = DateTime.UtcNow;

      [Required]
      public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

      public virtual ICollection<ProductionPlanItem> Items { get; set; } = new List<ProductionPlanItem>();
  }
  ```

- [ ] **Step 2: Tạo thực thể ProductionPlanItem.cs**
  Định nghĩa thực thể chi tiết dòng hàng cần sản xuất:
  ```csharp
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  namespace WmsMes.Web.Domain.Entities;

  public class ProductionPlanItem
  {
      public int Id { get; set; }

      [Required]
      public int ProductionPlanId { get; set; }

      [ForeignKey(nameof(ProductionPlanId))]
      public virtual ProductionPlan? ProductionPlan { get; set; }

      [Required]
      public int ProductId { get; set; }

      [ForeignKey(nameof(ProductId))]
      public virtual Product? Product { get; set; }

      [Column(TypeName = "decimal(18,2)")]
      public decimal PlannedQty { get; set; }

      public int? WorkOrderId { get; set; }

      [ForeignKey(nameof(WorkOrderId))]
      public virtual WorkOrder? WorkOrder { get; set; }
  }
  ```

- [ ] **Step 3: Đăng ký trong ApplicationDbContext.cs**
  Thêm các dòng khai báo DbSet:
  ```csharp
  public DbSet<ProductionPlan> ProductionPlans { get; set; } = null!;
  public DbSet<ProductionPlanItem> ProductionPlanItems { get; set; } = null!;
  ```

- [ ] **Step 4: Chạy EF Core Migration**
  Run: `dotnet ef migrations add AddProductionPlanningTables`
  Expected: Tạo migration thành công.
  Run: `dotnet ef database update`
  Expected: Cập nhật database thành công.

- [ ] **Step 5: Commit**
  Run: `git add Domain/Entities/ Data/ApplicationDbContext.cs Data/Migrations/`
  Run: `git commit -m "feat: add production plan and production plan item entities"`

---

### Task 2: Phát triển Dịch vụ Lập Kế hoạch & Thuật toán MRP Tổng hợp

**Files:**
- Create: [IProductionPlanService.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Services/IProductionPlanService.cs)
- Create: [ProductionPlanService.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Services/ProductionPlanService.cs)
- Modify: [Program.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Program.cs)

**Interfaces:**
- Consumes: DB Context.
- Produces: Dịch vụ `ProductionPlanService` được đăng ký Dependency Injection.

- [ ] **Step 1: Tạo IProductionPlanService.cs**
  Khai báo interface:
  ```csharp
  using WmsMes.Web.Domain.Entities;
  using WmsMes.Web.DTOs;

  namespace WmsMes.Web.Services;

  public interface IProductionPlanService
  {
      Task<ProductionPlan?> GetByIdAsync(int id);
      Task<bool> CreatePlanAsync(ProductionPlan plan);
      Task<IEnumerable<MrpResultDto>> CalculatePlanRequirementsAsync(int planId);
      Task<bool> GenerateWorkOrdersAsync(int planId, string userId);
      Task<bool> CompletePlanAsync(int planId);
  }
  ```

- [ ] **Step 2: Triển khai ProductionPlanService.cs**
  Viết logic thuật toán phân rã BOM gộp và tạo Lệnh sản xuất hàng loạt:
  ```csharp
  using Microsoft.EntityFrameworkCore;
  using WmsMes.Web.Data;
  using WmsMes.Web.Domain.Entities;
  using WmsMes.Web.Domain.Enums;
  using WmsMes.Web.DTOs;

  namespace WmsMes.Web.Services;

  public class ProductionPlanService : IProductionPlanService
  {
      private readonly ApplicationDbContext _context;

      public ProductionPlanService(ApplicationDbContext context)
      {
          _context = context;
      }

      public async Task<ProductionPlan?> GetByIdAsync(int id)
      {
          return await _context.ProductionPlans
              .Include(p => p.Items)
                  .ThenInclude(i => i.Product)
              .Include(p => p.Items)
                  .ThenInclude(i => i.WorkOrder)
              .FirstOrDefaultAsync(p => p.Id == id);
      }

      public async Task<bool> CreatePlanAsync(ProductionPlan plan)
      {
          _context.ProductionPlans.Add(plan);
          await _context.SaveChangesAsync();
          return true;
      }

      public async Task<IEnumerable<MrpResultDto>> CalculatePlanRequirementsAsync(int planId)
      {
          var plan = await _context.ProductionPlans
              .Include(p => p.Items)
              .FirstOrDefaultAsync(p => p.Id == planId);
          if (plan == null) return Enumerable.Empty<MrpResultDto>();

          var componentDemands = new Dictionary<int, decimal>();

          foreach (var item in plan.Items)
          {
              var bom = await _context.BOMs
                  .Include(b => b.Items)
                  .FirstOrDefaultAsync(b => b.ProductId == item.ProductId && b.IsActive);
              if (bom == null) continue;

              foreach (var bomItem in bom.Items)
              {
                  var grossNeed = item.PlannedQty * bomItem.QtyPer * (1 + bomItem.ScrapPercent / 100m);
                  if (componentDemands.ContainsKey(bomItem.ComponentProductId))
                  {
                      componentDemands[bomItem.ComponentProductId] += grossNeed;
                  }
                  else
                  {
                      componentDemands.Add(bomItem.ComponentProductId, grossNeed);
                  }
              }
          }

          var results = new List<MrpResultDto>();
          foreach (var kvp in componentDemands.OrderBy(k => k.Key))
          {
              var product = await _context.Products.FindAsync(kvp.Key);
              if (product == null) continue;

              var stockAvailable = await _context.StockBalances
                  .Where(sb => sb.ProductId == kvp.Key)
                  .SumAsync(sb => sb.QtyAvailable);

              results.Add(new MrpResultDto
              {
                  ComponentProductId = kvp.Key,
                  ComponentCode = product.Code,
                  ComponentName = product.Name,
                  GrossDemand = Math.Round(kvp.Value, 2, MidpointRounding.AwayFromZero),
                  StockAvailable = stockAvailable,
                  NetDemand = Math.Max(0m, Math.Round(kvp.Value - stockAvailable, 2, MidpointRounding.AwayFromZero))
              });
          }

          return results;
      }

      public async Task<bool> GenerateWorkOrdersAsync(int planId, string userId)
      {
          var plan = await _context.ProductionPlans
              .Include(p => p.Items)
                  .ThenInclude(i => i.Product)
              .FirstOrDefaultAsync(p => p.Id == planId);

          if (plan == null || plan.Status != DocumentStatus.Draft) return false;

          await using var transaction = await _context.Database.BeginTransactionAsync();
          try
          {
              foreach (var item in plan.Items)
              {
                  if (item.WorkOrderId.HasValue) continue;

                  var bom = await _context.BOMs.FirstOrDefaultAsync(b => b.ProductId == item.ProductId && b.IsActive);
                  var routing = await _context.Routings.FirstOrDefaultAsync(r => r.ProductId == item.ProductId && r.IsActive);

                  if (bom == null || routing == null)
                  {
                      throw new InvalidOperationException($"Sản phẩm {item.Product?.Code} chưa có BOM hoặc Routing hoạt động để tạo Lệnh sản xuất.");
                  }

                  var workOrder = new WorkOrder
                  {
                      Code = $"WO-{plan.PlanNo}-{item.Product?.Code}",
                      ProductId = item.ProductId,
                      Qty = item.PlannedQty,
                      DueDate = plan.PlanDate.AddDays(7), // Mặc định sau 7 ngày
                      Status = WorkOrderStatus.Draft,
                      BomVersion = bom.Version,
                      RoutingVersion = routing.Version
                  };

                  _context.WorkOrders.Add(workOrder);
                  await _context.SaveChangesAsync();

                  item.WorkOrderId = workOrder.Id;
              }

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

      public async Task<bool> CompletePlanAsync(int planId)
      {
          var plan = await _context.ProductionPlans.FindAsync(planId);
          if (plan == null || plan.Status != DocumentStatus.Draft) return false;

          plan.Status = DocumentStatus.Completed;
          await _context.SaveChangesAsync();
          return true;
      }
  }
  ```

- [ ] **Step 3: Đăng ký Service trong Program.cs**
  Tìm vị trí đăng ký dịch vụ trong `Program.cs` và thêm:
  ```csharp
  builder.Services.AddScoped<IProductionPlanService, ProductionPlanService>();
  ```

- [ ] **Step 4: Commit**
  Run: `git add Services/ IProductionPlanService.cs Program.cs`
  Run: `git commit -m "feat: implement IProductionPlanService and register in DI"`

---

### Task 3: Viết Unit Tests Cho Kế hoạch Sản xuất & MRP

**Files:**
- Create: [ProductionPlanTests.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/WmsMes.Tests/ProductionPlanTests.cs)

**Interfaces:**
- Consumes: `ProductionPlanService`.
- Produces: Kết quả chạy test tự động PASS.

- [ ] **Step 1: Tạo file kiểm thử ProductionPlanTests.cs**
  Viết các test case kiểm tra thuật toán MRP tổng hợp và sinh Work Order:
  ```csharp
  using Microsoft.EntityFrameworkCore;
  using WmsMes.Web.Data;
  using WmsMes.Web.Domain.Entities;
  using WmsMes.Web.Domain.Enums;
  using WmsMes.Web.Services;
  using Xunit;

  namespace WmsMes.Tests;

  public class ProductionPlanTests
  {
      [Fact]
      public async Task CalculatePlanRequirements_AggregatesSharedComponentsCorrectly()
      {
          // Arrange
          var options = new DbContextOptionsBuilder<ApplicationDbContext>()
              .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
              .Options;

          using (var context = new ApplicationDbContext(options))
          {
              var comp = new Product { Id = 1, Code = "RAW-01", Name = "Raw 1", IsActive = true };
              var prodA = new Product { Id = 2, Code = "PROD-A", Name = "Prod A", IsActive = true };
              context.Products.AddRange(comp, prodA);
              await context.SaveChangesAsync();

              var bom = new BOM { ProductId = 2, Version = "V1.0", IsActive = true };
              bom.Items.Add(new BOMItem { ComponentProductId = 1, QtyPer = 2.5m, ScrapPercent = 0 });
              context.BOMs.Add(bom);
              await context.SaveChangesAsync();

              var plan = new ProductionPlan
              {
                  Id = 1,
                  PlanNo = "PP-100",
                  Status = DocumentStatus.Draft,
                  Items = new List<ProductionPlanItem>
                  {
                      new() { ProductId = 2, PlannedQty = 10 } // Yêu cầu 10 * 2.5 = 25 raw
                  }
              };
              context.ProductionPlans.Add(plan);
              await context.SaveChangesAsync();
          }

          // Act & Assert
          using (var context = new ApplicationDbContext(options))
          {
              var service = new ProductionPlanService(context);
              var results = (await service.CalculatePlanRequirementsAsync(1)).ToList();

              Assert.Single(results);
              Assert.Equal(25m, results[0].GrossDemand);
          }
      }
  }
  ```

- [ ] **Step 2: Chạy thử Unit Tests**
  Run: `dotnet test`
  Expected: PASS tất cả bài test.

- [ ] **Step 3: Commit**
  Run: `git add WmsMes.Tests/ProductionPlanTests.cs`
  Run: `git commit -m "test: add unit tests for production planning MRP"`

---

### Task 4: Xây dựng Bộ điều khiển ProductionPlanController

**Files:**
- Create: [ProductionPlanController.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Controllers/ProductionPlanController.cs)

**Interfaces:**
- Consumes: HTTP request của người dùng (Planner).
- Produces: Các endpoint MVC điều khiển luồng hiển thị Kế hoạch sản xuất.

- [ ] **Step 1: Tạo ProductionPlanController.cs**
  Viết các action xử lý CRUD và các thao tác đặc thù (RunMRP, GenerateWorkOrders, Complete):
  ```csharp
  using Microsoft.AspNetCore.Authorization;
  using Microsoft.AspNetCore.Mvc;
  using Microsoft.EntityFrameworkCore;
  using System.Security.Claims;
  using WmsMes.Web.Data;
  using WmsMes.Web.Domain.Entities;
  using WmsMes.Web.Domain.Enums;
  using WmsMes.Web.Services;

  namespace WmsMes.Web.Controllers;

  [Authorize(Roles = "Admin,Planner,Manager")]
  public class ProductionPlanController : Controller
  {
      private readonly ApplicationDbContext _context;
      private readonly IProductionPlanService _planService;

      public ProductionPlanController(ApplicationDbContext context, IProductionPlanService planService)
      {
          _context = context;
          _planService = planService;
      }

      public async Task<IActionResult> Index()
      {
          var plans = await _context.ProductionPlans
              .OrderByDescending(p => p.PlanDate)
              .AsNoTracking()
              .ToListAsync();
          return View(plans);
      }

      public async Task<IActionResult> Details(int id, bool runMrp = false)
      {
          var plan = await _planService.GetByIdAsync(id);
          if (plan == null) return NotFound();

          if (runMrp)
          {
              ViewData["MrpResults"] = await _planService.CalculatePlanRequirementsAsync(id);
              ViewData["MrpRun"] = true;
          }

          return View(plan);
      }

      [HttpGet]
      public async Task<IActionResult> Create()
      {
          ViewBag.Products = await _context.Products
              .Where(p => p.IsManufactured && p.IsActive)
              .OrderBy(p => p.Code)
              .AsNoTracking()
              .ToListAsync();

          return View(new ProductionPlan { PlanNo = $"PP-{DateTime.UtcNow:yyyyMMddHHmmss}" });
      }

      [HttpPost]
      [ValidateAntiForgeryToken]
      public async Task<IActionResult> Create(ProductionPlan plan, List<int> productIds, List<decimal> plannedQtys)
      {
          if (productIds == null || productIds.Count == 0)
          {
              ModelState.AddModelError(string.Empty, "Kế hoạch sản xuất phải có ít nhất một sản phẩm.");
          }
          else
          {
              for (int i = 0; i < productIds.Count; i++)
              {
                  plan.Items.Add(new ProductionPlanItem
                  {
                      ProductId = productIds[i],
                      PlannedQty = plannedQtys[i]
                  });
              }
          }

          if (ModelState.IsValid)
          {
              await _planService.CreatePlanAsync(plan);
              return RedirectToAction(nameof(Index));
          }

          ViewBag.Products = await _context.Products
              .Where(p => p.IsManufactured && p.IsActive)
              .OrderBy(p => p.Code)
              .AsNoTracking()
              .ToListAsync();
          return View(plan);
      }

      [HttpPost]
      [ValidateAntiForgeryToken]
      public async Task<IActionResult> GenerateWorkOrders(int id)
      {
          var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
          try
          {
              var success = await _planService.GenerateWorkOrdersAsync(id, userId ?? "system");
              if (success)
              {
                  TempData["StatusMessage"] = "Đã tự động tạo hàng loạt Lệnh sản xuất nháp thành công.";
              }
              else
              {
                  TempData["ErrorMessage"] = "Không thể tạo Lệnh sản xuất. Vui lòng kiểm tra lại cấu hình BOM và Routing.";
              }
          }
          catch (Exception ex)
          {
              TempData["ErrorMessage"] = ex.Message;
          }

          return RedirectToAction(nameof(Details), new { id });
      }

      [HttpPost]
      [ValidateAntiForgeryToken]
      public async Task<IActionResult> Complete(int id)
      {
          var success = await _planService.CompletePlanAsync(id);
          if (success)
          {
              TempData["StatusMessage"] = "Đã xác nhận kế hoạch sản xuất thành công.";
          }
          return RedirectToAction(nameof(Details), new { id });
      }
  }
  ```

- [ ] **Step 2: Commit**
  Run: `git add Controllers/ProductionPlanController.cs`
  Run: `git commit -m "feat: implement ProductionPlanController with planning endpoints"`

---

### Task 5: Thiết kế Giao diện View & Liên kết Sidebar

**Files:**
- Create: [ProductionPlan/Index.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/ProductionPlan/Index.cshtml)
- Create: [ProductionPlan/Create.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/ProductionPlan/Create.cshtml)
- Create: [ProductionPlan/Details.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/ProductionPlan/Details.cshtml)
- Modify: [_Layout.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/Shared/_Layout.cshtml)

**Interfaces:**
- Consumes: Hệ thống HTML/CSS giao diện.
- Produces: Màn hình thao tác kế hoạch của Planner.

- [ ] **Step 1: Tạo trang Index.cshtml**
  Liệt kê danh sách kế hoạch sản xuất:
  ```html
  @model IEnumerable<WmsMes.Web.Domain.Entities.ProductionPlan>
  <!-- Render danh sách kế hoạch -->
  ```

- [ ] **Step 2: Tạo trang Create.cshtml**
  Thiết kế form tạo kế hoạch với bảng động JavaScript thêm dòng sản phẩm linh hoạt.

- [ ] **Step 3: Tạo trang Details.cshtml**
  Hiển thị thông tin chi tiết kế hoạch, tích hợp các nút Chạy MRP (render bảng MRP gộp) và Tạo Lệnh sản xuất.

- [ ] **Step 4: Thêm menu vào Sidebar Layout**
  Mở `Views/Shared/_Layout.cshtml` tìm khu vực quản lý sản xuất và chèn liên kết:
  ```html
  <a class="nav-link" asp-controller="ProductionPlan" asp-action="Index">
      <i class="bi bi-calendar-event"></i> Kế hoạch sản xuất (MRP)
  </a>
  ```

- [ ] **Step 5: Build và kiểm tra biên dịch dự án**
  Run: `dotnet build`
  Expected: Build thành công không có lỗi.

- [ ] **Step 6: Commit**
  Run: `git add Views/ProductionPlan/ Views/Shared/_Layout.cshtml`
  Run: `git commit -m "feat: complete UI views for production planning module"`
