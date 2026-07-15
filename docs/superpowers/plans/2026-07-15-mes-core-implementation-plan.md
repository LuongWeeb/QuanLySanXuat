# Kế hoạch thực hiện: Giai đoạn 3 - Điều hành Sản xuất (MES Core)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Xây dựng cơ sở dữ liệu định mức BOM, quy trình công nghệ (Routing), tính toán MRP tương tác, kiểm soát vòng đời Lệnh sản xuất, phân bổ giữ chỗ vật tư (Reservation), trạm ghi nhận của công nhân, và tự động trừ kho (Backflushing) kèm SignalR cập nhật tiến độ realtime.

**Architecture:** Sử dụng kiến trúc Single-Project Monolith. Các nghiệp vụ phê duyệt lệnh và hoàn thành trừ kho được bọc trong DB Transaction ở tầng Service. Sử dụng SignalR để cập nhật tiến độ sản xuất thời gian thực.

**Tech Stack:** ASP.NET Core MVC (.NET 8), EF Core, SQL Server, SignalR, Bootstrap 5.

## Global Constraints
- Hệ điều hành: Windows
- Tên dự án: WmsMes.Web
- Công nghệ: ASP.NET Core MVC (.NET 8), EF Core, SQL Server, SignalR.
- Số Lô thành phẩm tự sinh định dạng: `MãSP-YYYYMMDD-STT` (STT tăng dần trong ngày).
- Không được duyệt lệnh sản xuất (Approve WO) khi kho thiếu nguyên vật liệu.
- Giữ chỗ vật tư (Reservation) khi duyệt lệnh theo FEFO/FIFO, chuyển từ `QtyAvailable` sang `QtyReserved`.
- Trừ kho tự động (Backflush) khi hoàn thành lệnh, giải phóng `QtyReserved` và sinh phả hệ lô trong `LotGenealogy`.
- Ghi nhận tiến độ công đoạn phải theo đúng thứ tự tăng dần của quy trình công nghệ.

---

### Task 1: Thiết lập các Thực thể Định mức vật tư & Công nghệ (BOM & Routing)

**Files:**
- Create: `Domain/Entities/BOM.cs`
- Create: `Domain/Entities/BOMItem.cs`
- Create: `Domain/Entities/WorkCenter.cs`
- Create: `Domain/Entities/Routing.cs`
- Create: `Domain/Entities/RoutingStep.cs`
- Modify: `Data/ApplicationDbContext.cs`

**Interfaces:**
- Consumes: CSDL từ Giai đoạn 2.
- Produces: Các bảng cấu hình BOM, Routing, WorkCenter và ràng buộc khóa trên CSDL.

- [ ] **Step 1: Tạo BOM và BOMItem Entities**

Tạo `Domain/Entities/BOM.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities
{
    public class BOM
    {
        public int Id { get; set; }

        [Required]
        public int ProductId { get; set; }

        [ForeignKey("ProductId")]
        public virtual Product? Product { get; set; }

        [Required]
        [MaxLength(50)]
        public string Version { get; set; } = "V1.0";

        [Required]
        public DateTime EffectiveDate { get; set; } = DateTime.UtcNow;

        [Required]
        public bool IsActive { get; set; } = true;

        public virtual ICollection<BOMItem> Items { get; set; } = new List<BOMItem>();
    }
}
```

Tạo `Domain/Entities/BOMItem.cs`:
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities
{
    public class BOMItem
    {
        public int Id { get; set; }

        [Required]
        public int BomId { get; set; }

        [ForeignKey("BomId")]
        public virtual BOM? Bom { get; set; }

        [Required]
        public int ComponentProductId { get; set; }

        [ForeignKey("ComponentProductId")]
        public virtual Product? ComponentProduct { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal QtyPer { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal ScrapPercent { get; set; }
    }
}
```

- [ ] **Step 2: Tạo WorkCenter, Routing và RoutingStep Entities**

Tạo `Domain/Entities/WorkCenter.cs`:
```csharp
using System.ComponentModel.DataAnnotations;

namespace WmsMes.Web.Domain.Entities
{
    public class WorkCenter
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
    }
}
```

Tạo `Domain/Entities/Routing.cs`:
```csharp
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities
{
    public class Routing
    {
        public int Id { get; set; }

        [Required]
        public int ProductId { get; set; }

        [ForeignKey("ProductId")]
        public virtual Product? Product { get; set; }

        [Required]
        [MaxLength(250)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Version { get; set; } = "V1.0";

        [Required]
        public bool IsActive { get; set; } = true;

        public virtual ICollection<RoutingStep> Steps { get; set; } = new List<RoutingStep>();
    }
}
```

Tạo `Domain/Entities/RoutingStep.cs`:
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities
{
    public class RoutingStep
    {
        public int Id { get; set; }

        [Required]
        public int RoutingId { get; set; }

        [ForeignKey("RoutingId")]
        public virtual Routing? Routing { get; set; }

        [Required]
        public int StepNumber { get; set; } // 10, 20, 30...

        [Required]
        [MaxLength(150)]
        public string StepName { get; set; } = string.Empty;

        [Required]
        public int WorkCenterId { get; set; }

        [ForeignKey("WorkCenterId")]
        public virtual WorkCenter? WorkCenter { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal StandardTimeMinutes { get; set; }

        [Required]
        public bool RequireQC { get; set; }
    }
}
```

- [ ] **Step 3: Cập nhật DbContext và cấu hình khóa ngoại**

Sửa `Data/ApplicationDbContext.cs`:
```csharp
public DbSet<BOM> BOMs { get; set; }
public DbSet<BOMItem> BOMItems { get; set; }
public DbSet<WorkCenter> WorkCenters { get; set; }
public DbSet<Routing> Routings { get; set; }
public DbSet<RoutingStep> RoutingSteps { get; set; }

// Trong OnModelCreating:
builder.Entity<WorkCenter>()
    .HasIndex(w => w.Code)
    .IsUnique();
```

- [ ] **Step 4: Chạy Migration và cập nhật database**

Run: `dotnet ef migrations add AddMesConfigTables -o Data/Migrations`
Expected: Tạo migration thành công.
Run: `dotnet ef database update`
Expected: Tạo các bảng trên SQL Server.

- [ ] **Step 5: Commit code**

Run:
```bash
git add Domain/Entities/ Data/ApplicationDbContext.cs Data/Migrations/
git commit -m "feat: implement BOM and Routing master data entities"
```

---

### Task 2: Định nghĩa các Thực thể Lệnh sản xuất, Tiến độ, Giữ chỗ và Phả hệ lô

**Files:**
- Create: `Domain/Entities/WorkOrder.cs`
- Create: `Domain/Entities/WorkOrderStep.cs`
- Create: `Domain/Entities/MaterialReservation.cs`
- Create: `Domain/Entities/LotGenealogy.cs`
- Create: `Domain/Enums/WorkOrderStatus.cs`
- Create: `Domain/Enums/WorkOrderStepStatus.cs`
- Modify: `Domain/Entities/Lot.cs`
- Modify: `Data/ApplicationDbContext.cs`

**Interfaces:**
- Consumes: CSDL từ Task 1.
- Produces: Các bảng điều hành sản xuất trên SQL Server, bổ sung khóa ngoại của Lot trỏ về WorkOrder.

- [ ] **Step 1: Tạo các Enum trạng thái của WO và Step**

Tạo `Domain/Enums/WorkOrderStatus.cs`:
```csharp
namespace WmsMes.Web.Domain.Enums
{
    public enum WorkOrderStatus
    {
        Draft = 0,
        Pending = 1,
        Approved = 2,
        InProgress = 3,
        Completed = 4,
        Closed = 5
    }
}
```

Tạo `Domain/Enums/WorkOrderStepStatus.cs`:
```csharp
namespace WmsMes.Web.Domain.Enums
{
    public enum WorkOrderStepStatus
    {
        Pending = 0,
        InProgress = 1,
        Completed = 2
    }
}
```

- [ ] **Step 2: Tạo WorkOrder, WorkOrderStep, MaterialReservation và LotGenealogy**

Tạo `Domain/Entities/WorkOrder.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.Domain.Entities
{
    public class WorkOrder
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        [Required]
        public int ProductId { get; set; }

        [ForeignKey("ProductId")]
        public virtual Product? Product { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Qty { get; set; }

        [Required]
        public DateTime DueDate { get; set; }

        [Required]
        public WorkOrderStatus Status { get; set; } = WorkOrderStatus.Draft;

        [Required]
        [MaxLength(50)]
        public string BomVersion { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string RoutingVersion { get; set; } = string.Empty;

        public virtual ICollection<WorkOrderStep> Steps { get; set; } = new List<WorkOrderStep>();
    }
}
```

Tạo `Domain/Entities/WorkOrderStep.cs`:
```csharp
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.Domain.Entities
{
    public class WorkOrderStep
    {
        public int Id { get; set; }

        [Required]
        public int WorkOrderId { get; set; }

        [ForeignKey("WorkOrderId")]
        public virtual WorkOrder? WorkOrder { get; set; }

        [Required]
        public int StepNumber { get; set; }

        [Required]
        [MaxLength(150)]
        public string StepName { get; set; } = string.Empty;

        [Required]
        public int WorkCenterId { get; set; }

        [ForeignKey("WorkCenterId")]
        public virtual WorkCenter? WorkCenter { get; set; }

        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal QtyOK { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal QtyReject { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal QtyRework { get; set; }

        [Required]
        public WorkOrderStepStatus Status { get; set; } = WorkOrderStepStatus.Pending;
    }
}
```

Tạo `Domain/Entities/MaterialReservation.cs`:
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities
{
    public class MaterialReservation
    {
        public int Id { get; set; }

        [Required]
        public int WorkOrderId { get; set; }

        [ForeignKey("WorkOrderId")]
        public virtual WorkOrder? WorkOrder { get; set; }

        [Required]
        public int ProductId { get; set; }

        [ForeignKey("ProductId")]
        public virtual Product? Product { get; set; }

        [Required]
        public int LotId { get; set; }

        [ForeignKey("LotId")]
        public virtual Lot? Lot { get; set; }

        [Required]
        public int LocationId { get; set; }

        [ForeignKey("LocationId")]
        public virtual Location? Location { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal QtyReserved { get; set; }
    }
}
```

Tạo `Domain/Entities/LotGenealogy.cs`:
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities
{
    public class LotGenealogy
    {
        public int Id { get; set; }

        [Required]
        public int OutputLotId { get; set; }

        [ForeignKey("OutputLotId")]
        public virtual Lot? OutputLot { get; set; }

        [Required]
        public int InputLotId { get; set; }

        [ForeignKey("InputLotId")]
        public virtual Lot? InputLot { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal QtyConsumed { get; set; }
    }
}
```

- [ ] **Step 3: Mở rộng thực thể Lot**

Sửa `Domain/Entities/Lot.cs` để thêm khóa ngoại và navigation property trỏ về `WorkOrder`:
```csharp
// Thêm vào trong class Lot
[ForeignKey("WorkOrderId")]
public virtual WorkOrder? WorkOrder { get; set; }
```

- [ ] **Step 4: Đăng ký trong ApplicationDbContext**

Sửa `Data/ApplicationDbContext.cs`:
```csharp
public DbSet<WorkOrder> WorkOrders { get; set; }
public DbSet<WorkOrderStep> WorkOrderSteps { get; set; }
public DbSet<MaterialReservation> MaterialReservations { get; set; }
public DbSet<LotGenealogy> LotGenealogies { get; set; }

// Trong OnModelCreating:
builder.Entity<WorkOrder>()
    .HasIndex(w => w.Code)
    .IsUnique();

builder.Entity<WorkOrder>()
    .HasMany(w => w.Steps)
    .WithOne(s => s.WorkOrder)
    .HasForeignKey(s => s.WorkOrderId)
    .OnDelete(DeleteBehavior.Cascade);
```

- [ ] **Step 5: Chạy Migration và cập nhật database**

Run: `dotnet ef migrations add AddMesExecutionTables -o Data/Migrations`
Expected: Tạo migration thành công.
Run: `dotnet ef database update`
Expected: Tạo các bảng trên SQL Server.

- [ ] **Step 6: Commit code**

Run:
```bash
git add Domain/Enums/ Domain/Entities/ Data/ApplicationDbContext.cs Data/Migrations/
git commit -m "feat: implement workorder, workorderstep, reservation and genealogy tables"
```

---

### Task 3: Triển khai Logic Tính toán Nhu cầu Vật tư (MRP Service)

**Files:**
- Create: `Services/IMrpService.cs`
- Create: `Services/MrpService.cs`
- Modify: `Program.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext`.
- Produces: `IMrpService` thực hiện tính toán Gross và Net Demand của nguyên vật liệu dựa trên BOM.

- [ ] **Step 1: Tạo DTO cho tính toán MRP**

Tạo `DTOs/MrpResultDto.cs`:
```csharp
namespace WmsMes.Web.DTOs
{
    public class MrpResultDto
    {
        public int ComponentProductId { get; set; }
        public string ComponentCode { get; set; } = string.Empty;
        public string ComponentName { get; set; } = string.Empty;
        public decimal GrossDemand { get; set; }
        public decimal StockAvailable { get; set; }
        public decimal NetDemand { get; set; }
    }
}
```

- [ ] **Step 2: Tạo Mrp Service Interface & Implementation**

Tạo `Services/IMrpService.cs`:
```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using WmsMes.Web.DTOs;

namespace WmsMes.Web.Services
{
    public interface IMrpService
    {
        Task<IEnumerable<MrpResultDto>> CalculateRequirementsAsync(int productId, decimal qty);
    }
}
```

Tạo `Services/MrpService.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WmsMes.Web.Data;
using WmsMes.Web.DTOs;

namespace WmsMes.Web.Services
{
    public class MrpService : IMrpService
    {
        private readonly ApplicationDbContext _context;

        public MrpService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<MrpResultDto>> CalculateRequirementsAsync(int productId, decimal qty)
        {
            // Lấy BOM active
            var bom = await _context.BOMs
                .Include(b => b.Items)
                .ThenInclude(i => i.ComponentProduct)
                .FirstOrDefaultAsync(b => b.ProductId == productId && b.IsActive);

            if (bom == null) return Enumerable.Empty<MrpResultDto>();

            var results = new List<MrpResultDto>();

            foreach (var item in bom.Items)
            {
                if (item.ComponentProduct == null) continue;

                // Gross Demand = Qty * QtyPer * (1 + ScrapPercent / 100)
                decimal grossDemand = qty * item.QtyPer * (1 + item.ScrapPercent / 100);

                // Tổng tồn khả dụng khả thi trong kho
                decimal stockAvailable = await _context.StockBalances
                    .Where(sb => sb.ProductId == item.ComponentProductId)
                    .SumAsync(sb => sb.QtyAvailable);

                decimal netDemand = Math.Max(0, grossDemand - stockAvailable);

                results.Add(new MrpResultDto
                {
                    ComponentProductId = item.ComponentProductId,
                    ComponentCode = item.ComponentProduct.Code,
                    ComponentName = item.ComponentProduct.Name,
                    GrossDemand = grossDemand,
                    StockAvailable = stockAvailable,
                    NetDemand = netDemand
                });
            }

            return results;
        }
    }
}
```

- [ ] **Step 3: Đăng ký dịch vụ vào Program.cs**

Sửa `Program.cs` để thêm cấu hình DI cho `MrpService`:
```csharp
using WmsMes.Web.Services;

// Thêm trước builder.Build()
builder.Services.AddScoped<IMrpService, MrpService>();
```

- [ ] **Step 4: Commit code**

Run:
```bash
git add DTOs/ Services/IMrpService.cs Services/MrpService.cs Program.cs
git commit -m "feat: implement mrp calculation service"
```

---

### Task 4: Triển khai Nghiệp vụ Duyệt Lệnh sản xuất & Giữ chỗ vật tư

**Files:**
- Create: `Services/IWorkOrderService.cs`
- Create: `Services/WorkOrderService.cs`
- Modify: `Program.cs`

**Interfaces:**
- Consumes: CSDL và các Repository.
- Produces: `IWorkOrderService` có khả năng duyệt WO, kiểm tra và giữ chỗ vật tư, chặn duyệt nếu thiếu hàng.

- [ ] **Step 1: Tạo WorkOrder Service Interface**

Tạo `Services/IWorkOrderService.cs`:
```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using WmsMes.Web.Domain.Entities;

namespace WmsMes.Web.Services
{
    public interface IWorkOrderService
    {
        Task<WorkOrder?> GetByIdAsync(int id);
        Task<bool> CreateWorkOrderAsync(WorkOrder wo);
        Task<bool> ApproveWorkOrderAsync(int woId, string userId);
        Task<bool> StartStepAsync(int stepId);
        Task<bool> CompleteStepAsync(int stepId, decimal qtyOk, decimal qtyReject, decimal qtyRework);
        Task<bool> CompleteWorkOrderAsync(int woId, string userId);
    }
}
```

- [ ] **Step 2: Tạo WorkOrder Service Implementation (Bắt đầu với Phê duyệt & Giữ chỗ)**

Tạo `Services/WorkOrderService.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.Services
{
    public class WorkOrderService : IWorkOrderService
    {
        private readonly ApplicationDbContext _context;

        public WorkOrderService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<WorkOrder?> GetByIdAsync(int id)
        {
            return await _context.WorkOrders
                .Include(w => w.Product)
                .Include(w => w.Steps)
                .FirstOrDefaultAsync(w => w.Id == id);
        }

        public async Task<bool> CreateWorkOrderAsync(WorkOrder wo)
        {
            wo.Status = WorkOrderStatus.Draft;
            await _context.WorkOrders.AddAsync(wo);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ApproveWorkOrderAsync(int woId, string userId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var wo = await _context.WorkOrders.FindAsync(woId);
                if (wo == null || wo.Status != WorkOrderStatus.Draft) return false;

                // Lấy BOM active để tính nhu cầu giữ chỗ
                var bom = await _context.BOMs
                    .Include(b => b.Items)
                    .FirstOrDefaultAsync(b => b.ProductId == wo.ProductId && b.IsActive);
                if (bom == null) throw new InvalidOperationException("Active BOM not found.");

                // Lấy quy trình Routing active để sinh steps
                var routing = await _context.Routings
                    .Include(r => r.Steps)
                    .FirstOrDefaultAsync(r => r.ProductId == wo.ProductId && r.IsActive);
                if (routing == null) throw new InvalidOperationException("Active Routing not found.");

                // 1. Kiểm tra tồn kho NVL & Giữ chỗ (Reservation)
                foreach (var item in bom.Items)
                {
                    decimal neededQty = wo.Qty * item.QtyPer * (1 + item.ScrapPercent / 100);

                    // Sắp xếp lô theo FEFO/FIFO
                    var product = await _context.Products.FindAsync(item.ComponentProductId);
                    var balancesQuery = _context.StockBalances
                        .Include(sb => sb.Lot)
                        .Where(sb => sb.ProductId == item.ComponentProductId && sb.QtyAvailable > 0);

                    if (product != null && product.ShelfLifeDays.HasValue)
                        balancesQuery = balancesQuery.OrderBy(sb => sb.Lot!.ExpiryDate);
                    else
                        balancesQuery = balancesQuery.OrderBy(sb => sb.Lot!.Id);

                    var balances = await balancesQuery.ToListAsync();
                    decimal totalAvailable = balances.Sum(b => b.QtyAvailable);

                    // Chặn duyệt khi thiếu hàng (BR-MES-002)
                    if (totalAvailable < neededQty)
                    {
                        throw new InvalidOperationException($"Insufficient inventory for component: {product?.Code}. Needed: {neededQty}, Available: {totalAvailable}");
                    }

                    // Tiến hành Reservation
                    decimal remainingToReserve = neededQty;
                    foreach (var balance in balances)
                    {
                        if (remainingToReserve <= 0) break;

                        decimal allocate = Math.Min(balance.QtyAvailable, remainingToReserve);
                        balance.QtyAvailable -= allocate;
                        balance.QtyReserved += allocate;

                        var reservation = new MaterialReservation
                        {
                            WorkOrderId = woId,
                            ProductId = item.ComponentProductId,
                            LotId = balance.LotId,
                            LocationId = balance.LocationId,
                            QtyReserved = allocate
                        };
                        await _context.MaterialReservations.AddAsync(reservation);
                        remainingToReserve -= allocate;
                    }
                }

                // 2. Sinh các công đoạn thực tế cho WO (WorkOrderStep)
                foreach (var step in routing.Steps.OrderBy(s => s.StepNumber))
                {
                    var woStep = new WorkOrderStep
                    {
                        WorkOrderId = woId,
                        StepNumber = step.StepNumber,
                        StepName = step.StepName,
                        WorkCenterId = step.WorkCenterId,
                        Status = WorkOrderStepStatus.Pending,
                        QtyOK = 0,
                        QtyReject = 0,
                        QtyRework = 0
                    };
                    await _context.WorkOrderSteps.AddAsync(woStep);
                }

                wo.BomVersion = bom.Version;
                wo.RoutingVersion = routing.Version;
                wo.Status = WorkOrderStatus.Approved;

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

        // Các hàm StartStep, CompleteStep, CompleteWorkOrder sẽ được cài đặt ở Task 5
        public Task<bool> StartStepAsync(int stepId) => Task.FromResult(false);
        public Task<bool> CompleteStepAsync(int stepId, decimal qtyOk, decimal qtyReject, decimal qtyRework) => Task.FromResult(false);
        public Task<bool> CompleteWorkOrderAsync(int woId, string userId) => Task.FromResult(false);
    }
}
```

- [ ] **Step 3: Đăng ký dịch vụ vào Program.cs**

Sửa `Program.cs` để thêm cấu hình DI cho `WorkOrderService`:
```csharp
using WmsMes.Web.Services;

// Thêm trước builder.Build()
builder.Services.AddScoped<IWorkOrderService, WorkOrderService>();
```

- [ ] **Step 4: Commit code**

Run:
```bash
git add Services/IWorkOrderService.cs Services/WorkOrderService.cs Program.cs
git commit -m "feat: implement WorkOrderService with Approval and Reservation logic"
```

---

### Task 5: Triển khai Ghi nhận tiến độ (Worker Terminal) và Hoàn thành Lệnh (Backflush)

**Files:**
- Modify: `Services/WorkOrderService.cs`

**Interfaces:**
- Consumes: `IWorkOrderService` từ Task 4.
- Produces: Thực thi đầy đủ quy trình sản xuất của công nhân (bắt buộc thứ tự công đoạn) và cơ chế trừ kho (Backflush) tự động sinh phả hệ lô khi xong.

- [ ] **Step 1: Cài đặt code Ghi nhận tiến độ công đoạn (StartStep & CompleteStep)**

Sửa `Services/WorkOrderService.cs` để hoàn thiện hàm `StartStepAsync` và `CompleteStepAsync`:
```csharp
public async Task<bool> StartStepAsync(int stepId)
{
    var currentStep = await _context.WorkOrderSteps.FindAsync(stepId);
    if (currentStep == null || currentStep.Status != WorkOrderStepStatus.Pending) return false;

    // Ràng buộc thứ tự công đoạn: Kiểm tra xem bước trước đã hoàn thành chưa (BR-ROT-002)
    var previousStep = await _context.WorkOrderSteps
        .Where(s => s.WorkOrderId == currentStep.WorkOrderId && s.StepNumber < currentStep.StepNumber)
        .OrderByDescending(s => s.StepNumber)
        .FirstOrDefaultAsync();

    if (previousStep != null && previousStep.Status != WorkOrderStepStatus.Completed)
    {
        throw new InvalidOperationException($"Cannot start step {currentStep.StepNumber}. Previous step {previousStep.StepNumber} is not completed.");
    }

    // Cập nhật trạng thái WO lên InProgress nếu đây là bước đầu tiên
    var wo = await _context.WorkOrders.FindAsync(currentStep.WorkOrderId);
    if (wo != null && wo.Status == WorkOrderStatus.Approved)
    {
        wo.Status = WorkOrderStatus.InProgress;
    }

    currentStep.StartTime = DateTime.UtcNow;
    currentStep.Status = WorkOrderStepStatus.InProgress;
    await _context.SaveChangesAsync();
    return true;
}

public async Task<bool> CompleteStepAsync(int stepId, decimal qtyOk, decimal qtyReject, decimal qtyRework)
{
    var currentStep = await _context.WorkOrderSteps.FindAsync(stepId);
    if (currentStep == null || currentStep.Status != WorkOrderStepStatus.InProgress) return false;

    currentStep.EndTime = DateTime.UtcNow;
    currentStep.QtyOK = qtyOk;
    currentStep.QtyReject = qtyReject;
    currentStep.QtyRework = qtyRework;
    currentStep.Status = WorkOrderStepStatus.Completed;

    await _context.SaveChangesAsync();
    return true;
}
```

- [ ] **Step 2: Cài đặt code Hoàn thành Lệnh sản xuất & Backflush tự động**

Sửa `Services/WorkOrderService.cs` để hoàn thiện hàm `CompleteWorkOrderAsync`:
```csharp
public async Task<bool> CompleteWorkOrderAsync(int woId, string userId)
{
    using var transaction = await _context.Database.BeginTransactionAsync();
    try
    {
        var wo = await _context.WorkOrders
            .Include(w => w.Steps)
            .FirstOrDefaultAsync(w => w.Id == woId);

        if (wo == null || wo.Status != WorkOrderStatus.InProgress) return false;

        // Xác nhận toàn bộ các công đoạn đã hoàn thành
        if (wo.Steps.Any(s => s.Status != WorkOrderStepStatus.Completed))
        {
            throw new InvalidOperationException("Cannot complete Work Order. Not all steps are completed.");
        }

        // Lấy số lượng đạt của công đoạn cuối cùng làm sản lượng đầu ra
        var lastStep = wo.Steps.OrderByDescending(s => s.StepNumber).First();
        decimal finalQty = lastStep.QtyOK;

        // 1. Tự động sinh số lô thành phẩm: MãSP-YYYYMMDD-STT
        var product = await _context.Products.FindAsync(wo.ProductId);
        if (product == null) throw new InvalidOperationException("Product not found.");

        string dateStr = DateTime.Today.ToString("yyyyMMdd");
        string prefix = $"{product.Code}-{dateStr}-";
        
        // Lấy số thứ tự lớn nhất trong ngày
        var existingLots = await _context.Lots
            .Where(l => l.LotNo.StartsWith(prefix))
            .ToListAsync();
        int seq = existingLots.Count + 1;
        string lotNo = $"{prefix}{seq:D4}"; // ví dụ: SP001-20260715-0001

        var finishedLot = new Lot
        {
            LotNo = lotNo,
            ProductId = wo.ProductId,
            ManufactureDate = DateTime.UtcNow,
            ExpiryDate = product.ShelfLifeDays.HasValue ? DateTime.UtcNow.AddDays(product.ShelfLifeDays.Value) : null,
            Qty = finalQty,
            WorkOrderId = woId
        };
        await _context.Lots.AddAsync(finishedLot);
        await _context.SaveChangesAsync(); // Lưu để sinh Id cho Lot

        // Tạo số dư tồn kho tại Vị trí thành phẩm (Mặc định LocationId = 1)
        var finishedBalance = new StockBalance
        {
            ProductId = wo.ProductId,
            LotId = finishedLot.Id,
            LocationId = 1, // Vị trí kho thành phẩm
            QtyAvailable = finalQty,
            QtyReserved = 0,
            QtyOnHold = 0
        };
        await _context.StockBalances.AddAsync(finishedBalance);

        // Sinh StockTransaction nhập kho thành phẩm
        var receiptTx = new StockTransaction
        {
            Type = TransactionType.Receipt,
            ProductId = wo.ProductId,
            LotId = finishedLot.Id,
            LocationId = 1,
            Qty = finalQty,
            TransactionDate = DateTime.UtcNow,
            UserId = userId,
            ReferenceNo = wo.Code
        };
        await _context.StockTransactions.AddAsync(receiptTx);

        // 2. Thực hiện Backflush nguyên vật liệu đã giữ chỗ
        var reservations = await _context.MaterialReservations
            .Where(r => r.WorkOrderId == woId)
            .ToListAsync();

        foreach (var res in reservations)
            {
                var balance = await _context.StockBalances
                    .FirstOrDefaultAsync(sb => sb.ProductId == res.ProductId && sb.LotId == res.LotId && sb.LocationId == res.LocationId);

                if (balance != null)
                {
                    balance.QtyReserved = Math.Max(0, balance.QtyReserved - res.QtyReserved);
                }

                // Ghi nhận giao dịch tiêu hao kho (Backflush)
                var issueTx = new StockTransaction
                {
                    Type = TransactionType.Backflush,
                    ProductId = res.ProductId,
                    LotId = res.LotId,
                    LocationId = res.LocationId,
                    Qty = -res.QtyReserved,
                    TransactionDate = DateTime.UtcNow,
                    UserId = userId,
                    ReferenceNo = wo.Code
                };
                await _context.StockTransactions.AddAsync(issueTx);

                // Ghi nhận phả hệ lô (Lot Genealogy) để phục vụ truy vết
                var genealogy = new LotGenealogy
                {
                    OutputLotId = finishedLot.Id,
                    InputLotId = res.LotId,
                    QtyConsumed = res.QtyReserved
                };
                await _context.LotGenealogies.AddAsync(genealogy);
            }

        wo.Status = WorkOrderStatus.Completed;
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
```

- [ ] **Step 3: Commit code**

Run:
```bash
git add Services/WorkOrderService.cs
git commit -m "feat: implement worker progress steps and backflush logic with lot genealogy"
```

---

### Task 6: Viết Unit Test tự động kiểm thử Nghiệp vụ MES Core

**Files:**
- Create: `WmsMes.Tests/WorkOrderServiceTests.cs`

**Interfaces:**
- Consumes: `WorkOrderService`, `MrpService`.
- Produces: Bộ kiểm thử tự động xác minh tính chính xác của các quy tắc sản xuất (chặn thiếu hàng, kiểm soát thứ tự công đoạn, phả hệ lô).

- [ ] **Step 1: Tạo file Unit Test cho MES**

Tạo file `WmsMes.Tests/WorkOrderServiceTests.cs` sử dụng DbContext in-memory:
```csharp
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.Services;

namespace WmsMes.Tests
{
    public class WorkOrderServiceTests
    {
        private ApplicationDbContext GetInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            return new ApplicationDbContext(options);
        }

        [Fact]
        public async Task ApproveWorkOrderAsync_ThrowsException_WhenMaterialIsInsufficient()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            
            // Tạo sản phẩm và BOM định mức yêu cầu 100 NVL
            var product = new Product { Id = 1, Code = "P01", Name = "Pro 1", BaseUomId = 1 };
            var material = new Product { Id = 2, Code = "M01", Name = "Mat 1", BaseUomId = 1 };
            await context.Products.AddRangeAsync(product, material);

            var bom = new BOM { Id = 1, ProductId = 1, IsActive = true };
            bom.Items.Add(new BOMItem { ComponentProductId = 2, QtyPer = 10, ScrapPercent = 0 });
            await context.BOMs.AddAsync(bom);

            var routing = new Routing { Id = 1, ProductId = 1, IsActive = true };
            routing.Steps.Add(new RoutingStep { StepNumber = 10, StepName = "Step 1", WorkCenterId = 1 });
            await context.Routings.AddAsync(routing);

            var wo = new WorkOrder { Id = 1, Code = "WO01", ProductId = 1, Qty = 10, Status = WorkOrderStatus.Draft, DueDate = DateTime.Today };
            await context.WorkOrders.AddAsync(wo);

            // Chỉ có 50 NVL khả dụng trong kho (thiếu 50)
            var bal = new StockBalance { ProductId = 2, LotId = 1, LocationId = 1, QtyAvailable = 50 };
            await context.StockBalances.AddAsync(bal);
            await context.SaveChangesAsync();

            var service = new WorkOrderService(context);

            // Act & Assert
            await Assert.ThrowsAnyAsync<Exception>(() => service.ApproveWorkOrderAsync(1, "user1"));
        }

        [Fact]
        public async Task StartStepAsync_ThrowsException_WhenPreviousStepIsNotCompleted()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            var wo = new WorkOrder { Id = 1, Code = "WO01", ProductId = 1, Qty = 1, Status = WorkOrderStatus.Approved, DueDate = DateTime.Today };
            await context.WorkOrders.AddAsync(wo);

            var step1 = new WorkOrderStep { Id = 1, WorkOrderId = 1, StepNumber = 10, StepName = "Step 10", Status = WorkOrderStepStatus.Pending, WorkCenterId = 1 };
            var step2 = new WorkOrderStep { Id = 2, WorkOrderId = 1, StepNumber = 20, StepName = "Step 20", Status = WorkOrderStepStatus.Pending, WorkCenterId = 1 };
            await context.WorkOrderSteps.AddRangeAsync(step1, step2);
            await context.SaveChangesAsync();

            var service = new WorkOrderService(context);

            // Act & Assert
            await Assert.ThrowsAnyAsync<Exception>(() => service.StartStepAsync(2)); // Bắt đầu bước 20 khi bước 10 đang Pending -> phải lỗi
        }
    }
}
```

- [ ] **Step 2: Chạy kiểm thử tự động**

Run: `dotnet test`
Expected: Tất cả các Unit Test chạy thành công và vượt qua.

- [ ] **Step 3: Commit code**

Run:
```bash
git add WmsMes.Tests/WorkOrderServiceTests.cs
git commit -m "test: write unit tests for MES Core WorkOrder workflows"
```

---

### Task 7: Thiết lập SignalR Production Hub & Realtime Monitor

**Files:**
- Create: `Hubs/ProductionHub.cs`
- Modify: `Program.cs`
- Modify: `Services/WorkOrderService.cs`

**Interfaces:**
- Consumes: Thư viện SignalR.
- Produces: `ProductionHub` gửi tín hiệu cập nhật phần trăm hoàn thành WO cho quản lý khi công nhân báo tiến độ.

- [ ] **Step 1: Tạo Hub lớp trong thư mục Hubs**

Tạo `Hubs/ProductionHub.cs`:
```csharp
using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace WmsMes.Web.Hubs
{
    public class ProductionHub : Hub
    {
        public async Task NotifyProgressChange()
        {
            await Clients.All.SendAsync("ReceiveProgressUpdate");
        }
    }
}
```

- [ ] **Step 2: Đăng ký Hub trong Program.cs**

Sửa `Program.cs` để thêm dịch vụ SignalR routing:
```csharp
using WmsMes.Web.Hubs;

// Chèn sau app.UseRouting()
app.MapHub<ProductionHub>("/productionHub");
```

- [ ] **Step 3: Tiêm IHubContext để phát thông báo realtime khi cập nhật công đoạn**

Sửa `Services/WorkOrderService.cs` để tiêm `IHubContext<ProductionHub>` và gọi Hub phát tín hiệu trong hàm `StartStepAsync`, `CompleteStepAsync` và `CompleteWorkOrderAsync`:
```csharp
using Microsoft.AspNetCore.SignalR;
using WmsMes.Web.Hubs;

// Bổ sung thuộc tính & hàm dựng trong WorkOrderService:
private readonly IHubContext<ProductionHub> _prodHubContext;

public WorkOrderService(ApplicationDbContext context, IHubContext<ProductionHub> prodHubContext)
{
    _context = context;
    _prodHubContext = prodHubContext;
}

// Gọi Hub sau khi SaveChangesAsync thành công ở cuối các hàm StartStepAsync, CompleteStepAsync, CompleteWorkOrderAsync:
await _prodHubContext.Clients.All.SendAsync("ReceiveProgressUpdate");
```

- [ ] **Step 4: Commit code**

Run:
```bash
git add Hubs/ProductionHub.cs Program.cs Services/WorkOrderService.cs
git commit -m "feat: integrate SignalR Hub for realtime production monitoring"
```

---

### Task 8: Giao diện MRP Tương tác và Màn hình Trạm Công nhân

**Files:**
- Create: `Controllers/MrpController.cs`
- Create: `Views/Mrp/Index.cshtml`
- Create: `Controllers/WorkerController.cs`
- Create: `Views/Worker/Index.cshtml`
- Modify: `Views/Shared/_Layout.cshtml`

**Interfaces:**
- Consumes: `IMrpService`, `IWorkOrderService`.
- Produces: Màn hình chạy thử MRP tương tác, và Giao diện trạm vận hành công nhân với các nút Bắt đầu/Hoàn thành kích thước lớn.

- [ ] **Step 1: Tạo MrpController và View chạy MRP**

Tạo `Controllers/MrpController.cs`:
```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using WmsMes.Web.Services;

namespace WmsMes.Web.Controllers
{
    [Authorize(Roles = "Admin,Manager,Planner")]
    public class MrpController : Controller
    {
        private readonly IMrpService _mrpService;

        public MrpController(IMrpService mrpService)
        {
            _mrpService = mrpService;
        }

        [HttpGet]
        public IActionResult Index() => View();

        [HttpPost]
        public async Task<IActionResult> Calculate(int productId, decimal qty)
        {
            var results = await _mrpService.CalculateRequirementsAsync(productId, qty);
            return View("Index", results);
        }
    }
}
```

Tạo file `Views/Mrp/Index.cshtml` hiển thị bảng chênh lệch vật tư MRP:
```html
@model IEnumerable<WmsMes.Web.DTOs.MrpResultDto>
@{
    ViewData["Title"] = "Chạy kế hoạch vật tư MRP";
}

<div class="container py-4">
    <h2>Lập kế hoạch nhu cầu vật tư (MRP)</h2>
    <form asp-action="Calculate" method="post" class="row g-3 my-3">
        <div class="col-md-6">
            <label class="form-label">Sản phẩm cần sản xuất (ID)</label>
            <input type="number" name="productId" class="form-control" required />
        </div>
        <div class="col-md-4">
            <label class="form-label">Số lượng dự kiến</label>
            <input type="number" name="qty" class="form-control" required />
        </div>
        <div class="col-md-2 d-flex align-items-end">
            <button type="submit" class="btn btn-primary w-100">Tính toán MRP</button>
        </div>
    </form>

    @if (Model != null)
    {
        <div class="card bg-dark text-white border-0 shadow-sm mt-4">
            <div class="card-body">
                <table class="table table-dark table-striped">
                    <thead>
                        <tr>
                            <th>Mã nguyên liệu</th>
                            <th>Tên nguyên liệu</th>
                            <th>Nhu cầu tổng</th>
                            <th>Tồn khả dụng</th>
                            <th>Thiếu hụt</th>
                        </tr>
                    </thead>
                    <tbody>
                        @foreach (var item in Model)
                        {
                            <tr>
                                <td>@item.ComponentCode</td>
                                <td>@item.ComponentName</td>
                                <td>@item.GrossDemand</td>
                                <td>@item.StockAvailable</td>
                                <td class="@(item.NetDemand > 0 ? "text-danger fw-bold" : "text-success")">
                                    @item.NetDemand
                                </td>
                            </tr>
                        }
                    </tbody>
                </table>
            </div>
        </div>
    }
</div>
```

- [ ] **Step 2: Tạo Giao diện trạm vận hành cho Công nhân**

Tạo `Controllers/WorkerController.cs`:
```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using WmsMes.Web.Data;
using WmsMes.Web.Services;

namespace WmsMes.Web.Controllers
{
    [Authorize(Roles = "Admin,Worker")]
    public class WorkerController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWorkOrderService _woService;

        public WorkerController(ApplicationDbContext context, IWorkOrderService woService)
        {
            _context = context;
            _woService = woService;
        }

        public async Task<IActionResult> Index()
        {
            // Lấy danh sách công đoạn WO đang InProgress hoặc Approved
            var steps = await _context.WorkOrderSteps
                .Include(s => s.WorkOrder)
                .ThenInclude(w => w!.Product)
                .Where(s => s.Status != Domain.Enums.WorkOrderStepStatus.Completed)
                .ToListAsync();
            return View(steps);
        }

        [HttpPost]
        public async Task<IActionResult> Start(int id)
        {
            await _woService.StartStepAsync(id);
            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> Complete(int id, decimal qtyOk)
        {
            await _woService.CompleteStepAsync(id, qtyOk, 0, 0);
            return RedirectToAction("Index");
        }
    }
}
```

Tạo file `Views/Worker/Index.cshtml` giao diện trạm vận hành (Tablet layout):
```html
@model IEnumerable<WmsMes.Web.Domain.Entities.WorkOrderStep>
@{
    Layout = "_Layout";
    ViewData["Title"] = "Trạm vận hành sản xuất";
}

<div class="container py-4">
    <h2>Trạm vận hành nhà xưởng</h2>
    <div class="row row-cols-1 row-cols-md-2 g-4 mt-3">
        @foreach (var step in Model)
        {
            <div class="col">
                <div class="card bg-dark text-white border-secondary h-100 shadow-sm">
                    <div class="card-body d-flex flex-column justify-content-between">
                        <div>
                            <h5 class="card-title">@step.WorkOrder?.Code - @step.WorkOrder?.Product?.Name</h5>
                            <p class="card-text mb-1"><strong>Công đoạn:</strong> @step.StepName (Bước @step.StepNumber)</p>
                            <p class="card-text"><strong>Trạng thái:</strong> @step.Status.ToString()</p>
                        </div>
                        <div class="mt-3">
                            @if (step.Status == WmsMes.Web.Domain.Enums.WorkOrderStepStatus.Pending)
                            {
                                <form asp-action="Start" asp-route-id="@step.Id" method="post">
                                    <button type="submit" class="btn btn-warning w-100 py-3 fw-bold">BẮT ĐẦU VẬN HÀNH</button>
                                </form>
                            }
                            else if (step.Status == WmsMes.Web.Domain.Enums.WorkOrderStepStatus.InProgress)
                            {
                                <form asp-action="Complete" asp-route-id="@step.Id" method="post" class="d-flex gap-2">
                                    <input type="number" name="qtyOk" class="form-control" placeholder="SL Đạt" required style="max-width: 120px;" />
                                    <button type="submit" class="btn btn-success flex-grow-1 py-3 fw-bold">BÁO CÁO HOÀN THÀNH</button>
                                </form>
                            }
                        </div>
                    </div>
                </div>
            </div>
        }
    </div>
</div>
```

- [ ] **Step 3: Đăng ký menu vào layout**

Sửa `Views/Shared/_Layout.cshtml` để bổ sung liên kết đến màn hình MRP và Trạm Công nhân:
```html
<li class="nav-item">
    <a class="nav-link text-white" asp-controller="Mrp" asp-action="Index">
        <i class="bi bi-calculator"></i> Lập Kế hoạch MRP
    </a>
</li>
<li class="nav-item">
    <a class="nav-link text-white" asp-controller="Worker" asp-action="Index">
        <i class="bi bi-cpu"></i> Trạm vận hành Công nhân
    </a>
</li>
```

- [ ] **Step 4: Chạy thử và xác thực**

Run: `dotnet run`
Expected: 
1. Planner có thể truy cập `/Mrp` tính thử định mức vật liệu.
2. Công nhân truy cập `/Worker` báo bắt đầu/hoàn thành công đoạn. Hệ thống hoạt động trơn tru không lỗi.

- [ ] **Step 5: Commit code**

Run:
```bash
git add Controllers/ Views/
git commit -m "feat: implement MRP planner screen and Worker terminal UI views"
```
