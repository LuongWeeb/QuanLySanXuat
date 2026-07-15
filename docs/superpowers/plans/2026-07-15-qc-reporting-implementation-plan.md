# Kế hoạch thực hiện: Giai đoạn 4 - Kiểm soát Chất lượng & Báo cáo (QC & Reporting)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Triển khai các tính năng quản lý tiêu chuẩn QC, trạm kiểm tra chất lượng, cơ chế tạm giữ khóa kho chờ QC, thuật toán tính giá thành thực tế theo Lô, sơ đồ truy vết SVG đệ quy, SignalR cảnh báo chất lượng và Dashboard phân tích KPI.

**Architecture:** Sử dụng kiến trúc Single-Project Monolith. Tách các nghiệp vụ QC và tính giá thành tại Service Layer bọc trong DB Transaction. Vẽ cây phả hệ lô bằng cấu trúc đệ quy DTO và render SVG phía Client.

**Tech Stack:** ASP.NET Core MVC (.NET 8), EF Core, SQL Server, SignalR, Bootstrap 5, Chart.js, EPPlus (hoặc ClosedXML).

## Global Constraints
- Hệ điều hành: Windows
- Tên dự án: WmsMes.Web
- Công nghệ: ASP.NET Core MVC (.NET 8), EF Core, SQL Server, SignalR, Bootstrap 5.
- Khi phê duyệt QC PASS, tự động chuyển `QtyOnHold -> QtyAvailable` trong `StockBalance` và tính đơn giá vốn thành phẩm lưu vào `Lot.UnitPrice`.
- Khi phê duyệt QC REJECT, tự động tạm giữ hàng và chuyển đến Vị trí cách ly (Quarantine Location).
- Tính giá thành phải dựa trên đơn giá mua thực tế của các lô NVL tiêu hao (trong bảng `LotGenealogy`).
- Truy vết lô hàng phải hỗ trợ cả hai chiều (xuôi/ngược) và vẽ dạng sơ đồ cây trực quan Node bằng SVG.

---

### Task 1: Thiết lập các Thực thể Tiêu chuẩn & Kết quả QC (QC Checklist & Inspection)

**Files:**
- Create: `Domain/Entities/QCChecklist.cs`
- Create: `Domain/Entities/QCChecklistItem.cs`
- Create: `Domain/Entities/QCInspection.cs`
- Create: `Domain/Entities/QCInspectionLine.cs`
- Create: `Domain/Enums/QCResult.cs`
- Modify: `Data/ApplicationDbContext.cs`

**Interfaces:**
- Consumes: CSDL từ Giai đoạn 3.
- Produces: Các bảng cấu hình và kết quả kiểm tra chất lượng trên SQL Server.

- [ ] **Step 1: Tạo QCResult Enum**

Tạo `Domain/Enums/QCResult.cs`:
```csharp
namespace WmsMes.Web.Domain.Enums
{
    public enum QCResult
    {
        PASS = 0,
        REJECT = 1,
        REWORK = 2
    }
}
```

- [ ] **Step 2: Tạo QCChecklist và QCChecklistItem Entities**

Tạo `Domain/Entities/QCChecklist.cs`:
```csharp
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities
{
    public class QCChecklist
    {
        public int Id { get; set; }

        [Required]
        public int ProductId { get; set; }

        [ForeignKey("ProductId")]
        public virtual Product? Product { get; set; }

        public int? StepNumber { get; set; } // Trống nếu là QC thành phẩm cuối cùng

        [Required]
        [MaxLength(250)]
        public string Name { get; set; } = string.Empty;

        [Required]
        public bool IsActive { get; set; } = true;

        public virtual ICollection<QCChecklistItem> Items { get; set; } = new List<QCChecklistItem>();
    }
}
```

Tạo `Domain/Entities/QCChecklistItem.cs`:
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities
{
    public class QCChecklistItem
    {
        public int Id { get; set; }

        [Required]
        public int QCChecklistId { get; set; }

        [ForeignKey("QCChecklistId")]
        public virtual QCChecklist? QCChecklist { get; set; }

        [Required]
        [MaxLength(150)]
        public string ParameterName { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,4)")]
        public decimal? MinVal { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal? MaxVal { get; set; }

        [MaxLength(50)]
        public string Unit { get; set; } = string.Empty;

        [Required]
        public bool IsRequired { get; set; } = true;
    }
}
```

- [ ] **Step 3: Tạo QCInspection và QCInspectionLine Entities**

Tạo `Domain/Entities/QCInspection.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.Domain.Entities
{
    public class QCInspection
    {
        public int Id { get; set; }

        [Required]
        public int WorkOrderId { get; set; }

        [ForeignKey("WorkOrderId")]
        public virtual WorkOrder? WorkOrder { get; set; }

        [Required]
        public int LotId { get; set; }

        [ForeignKey("LotId")]
        public virtual Lot? Lot { get; set; }

        [Required]
        public DateTime InspectionTime { get; set; } = DateTime.UtcNow;

        [Required]
        [MaxLength(450)]
        public string InspectorId { get; set; } = string.Empty;

        [Required]
        public QCResult Result { get; set; } = QCResult.PASS;

        [MaxLength(500)]
        public string Note { get; set; } = string.Empty;

        [MaxLength(500)]
        public string EvidencePath { get; set; } = string.Empty;

        public virtual ICollection<QCInspectionLine> Lines { get; set; } = new List<QCInspectionLine>();
    }
}
```

Tạo `Domain/Entities/QCInspectionLine.cs`:
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities
{
    public class QCInspectionLine
    {
        public int Id { get; set; }

        [Required]
        public int QCInspectionId { get; set; }

        [ForeignKey("QCInspectionId")]
        public virtual QCInspection? QCInspection { get; set; }

        [Required]
        [MaxLength(150)]
        public string ParameterName { get; set; } = string.Empty;

        [Required]
        [MaxLength(250)]
        public string ValueInspected { get; set; } = string.Empty;

        [Required]
        public bool IsOK { get; set; }
    }
}
```

- [ ] **Step 4: Đăng ký trong DbContext và cấu hình Cascade Delete**

Sửa `Data/ApplicationDbContext.cs`:
```csharp
public DbSet<QCChecklist> QCChecklists { get; set; }
public DbSet<QCChecklistItem> QCChecklistItems { get; set; }
public DbSet<QCInspection> QCInspections { get; set; }
public DbSet<QCInspectionLine> QCInspectionLines { get; set; }

// Trong OnModelCreating:
builder.Entity<QCChecklist>()
    .HasMany(c => c.Items)
    .WithOne(i => i.QCChecklist)
    .HasForeignKey(i => i.QCChecklistId)
    .OnDelete(DeleteBehavior.Cascade);

builder.Entity<QCInspection>()
    .HasMany(i => i.Lines)
    .WithOne(l => l.QCInspection)
    .HasForeignKey(l => l.QCInspectionId)
    .OnDelete(DeleteBehavior.Cascade);
```

- [ ] **Step 5: Chạy Migration và cập nhật database**

Run: `dotnet ef migrations add AddQcTables -o Data/Migrations`
Expected: Tạo migration thành công.
Run: `dotnet ef database update`
Expected: Tạo các bảng trên SQL Server.

- [ ] **Step 6: Commit code**

Run:
```bash
git add Domain/Enums/ Domain/Entities/ Data/ApplicationDbContext.cs Data/Migrations/
git commit -m "feat: implement quality control configurations and inspection tables"
```

---

### Task 2: Cập nhật các bảng danh mục CSDL để hỗ trợ Đơn giá Lô và Phiếu nhập (Costing fields)

**Files:**
- Modify: `Domain/Entities/GoodsReceiptLine.cs`
- Modify: `Domain/Entities/Lot.cs`
- Modify: `Data/ApplicationDbContext.cs`

**Interfaces:**
- Consumes: CSDL từ Task 1.
- Produces: Cột `UnitPrice` trên các bảng `GoodsReceiptLines` và `Lots` để lưu đơn giá phục vụ tính giá vốn sản xuất.

- [ ] **Step 1: Cập nhật GoodsReceiptLine Entity**

Sửa `Domain/Entities/GoodsReceiptLine.cs` để thêm trường đơn giá mua:
```csharp
// Thêm trường sau vào class GoodsReceiptLine
[Column(TypeName = "decimal(18,2)")]
public decimal UnitPrice { get; set; }
```

- [ ] **Step 2: Cập nhật Lot Entity**

Sửa `Domain/Entities/Lot.cs` để thêm trường giá trị vốn:
```csharp
// Thêm trường sau vào class Lot
[Column(TypeName = "decimal(18,2)")]
public decimal UnitPrice { get; set; }
```

- [ ] **Step 3: Tạo migration mới và cập nhật database**

Run: `dotnet ef migrations add AddCostingFields -o Data/Migrations`
Expected: Tạo migration thành công.
Run: `dotnet ef database update`
Expected: CSDL được cập nhật thêm các cột đơn giá.

- [ ] **Step 4: Commit code**

Run:
```bash
git add Domain/Entities/ Data/Migrations/
git commit -m "feat: add unitprice columns for goodsreceiptline and lot costing"
```

---

### Task 3: Triển khai Dịch vụ Kiểm soát Chất lượng & Mở khóa Kho (QcService)

**Files:**
- Create: `Services/IQcService.cs`
- Create: `Services/QcService.cs`
- Modify: `Program.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext`, `ICostingService` (Task 4).
- Produces: `IQcService` xử lý lưu phiếu QC, tự đánh giá đạt/lỗi và thực hiện mở khóa kho hoặc điều chuyển kho lỗi.

- [ ] **Step 1: Tạo QC Service Interface**

Tạo `Services/IQcService.cs`:
```csharp
using System.Threading.Tasks;
using WmsMes.Web.Domain.Entities;

namespace WmsMes.Web.Services
{
    public interface IQcService
    {
        Task<bool> SubmitQCInspectionAsync(QCInspection inspection, string userId);
    }
}
```

- [ ] **Step 2: Tạo QC Service Implementation (xử lý logic Gating tồn kho)**

Tạo `Services/QcService.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.Services
{
    public class QcService : IQcService
    {
        private readonly ApplicationDbContext _context;
        private readonly ICostingService _costingService;

        public QcService(ApplicationDbContext context, ICostingService costingService)
        {
            _context = context;
            _costingService = costingService;
        }

        public async Task<bool> SubmitQCInspectionAsync(QCInspection inspection, string userId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. Tự động chấm điểm các chỉ tiêu QC
                foreach (var line in inspection.Lines)
                {
                    // Lấy chỉ tiêu cấu hình để so khớp
                    var configItem = await _context.QCChecklistItems
                        .FirstOrDefaultAsync(i => i.ParameterName == line.ParameterName);

                    if (configItem != null && configItem.MinVal.HasValue && configItem.MaxVal.HasValue)
                    {
                        if (decimal.TryParse(line.ValueInspected, out decimal measuredVal))
                        {
                            line.IsOK = measuredVal >= configItem.MinVal.Value && measuredVal <= configItem.MaxVal.Value;
                        }
                        else
                        {
                            line.IsOK = false; // Lỗi định dạng số
                        }
                    }
                    else
                    {
                        // Tiêu chí định tính Đúng/Sai
                        line.IsOK = line.ValueInspected.Equals("PASS", StringComparison.OrdinalIgnoreCase) || 
                                    line.ValueInspected.Equals("TRUE", StringComparison.OrdinalIgnoreCase);
                    }
                }

                // Nếu có bất kỳ tiêu chí bắt buộc nào không đạt -> Thiết lập kết quả đánh giá cuối cùng là REJECT
                bool hasFailedRequired = inspection.Lines
                    .Any(l => !l.IsOK); // Simplified: If any fails, inspection fails
                
                if (hasFailedRequired)
                {
                    inspection.Result = QCResult.REJECT;
                }

                inspection.InspectionTime = DateTime.UtcNow;
                inspection.InspectorId = userId;

                await _context.QCInspections.AddAsync(inspection);
                await _context.SaveChangesAsync();

                // 2. Gating kho dựa trên kết quả QC
                var balance = await _context.StockBalances
                    .FirstOrDefaultAsync(sb => sb.LotId == inspection.LotId);

                if (balance != null)
                {
                    if (inspection.Result == QCResult.PASS)
                    {
                        // Mở khóa kho: QtyOnHold -> QtyAvailable
                        balance.QtyAvailable += balance.QtyOnHold;
                        balance.QtyOnHold = 0;

                        // Tính giá thành thực tế WO và gán vào Lô thành phẩm
                        decimal unitCost = await _costingService.CalculateProductionCostAsync(inspection.WorkOrderId);
                        var lot = await _context.Lots.FindAsync(inspection.LotId);
                        if (lot != null)
                        {
                            lot.UnitPrice = unitCost;
                            balance.Product = await _context.Products.FindAsync(balance.ProductId);
                        }
                    }
                    else if (inspection.Result == QCResult.REJECT)
                    {
                        // Giữ nguyên trạng thái On Hold, tự động chuyển Lô hàng này sang Vị trí cách ly (Ví dụ: LocationId = 999)
                        balance.LocationId = 999; // Quarantine Location
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                return true;
            }
            catch
            {
                await transaction.RollbackAsync();
                return false;
            }
        }
    }
}
```

- [ ] **Step 3: Đăng ký dịch vụ QC**

Sửa `Program.cs` để thêm dịch vụ DI:
```csharp
using WmsMes.Web.Services;

// Thêm trước builder.Build()
builder.Services.AddScoped<IQcService, QcService>();
```

- [ ] **Step 4: Commit code**

Run:
```bash
git add Services/IQcService.cs Services/QcService.cs Program.cs
git commit -m "feat: implement QcService with database transaction gating logic"
```

---

### Task 4: Triển khai dịch vụ Tính toán Giá thành Thực tế (CostingService)

**Files:**
- Create: `Services/ICostingService.cs`
- Create: `Services/CostingService.cs`
- Modify: `Program.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext`.
- Produces: `ICostingService` dùng phả hệ lô tính toán chính xác đơn giá giá vốn.

- [ ] **Step 1: Tạo Costing Service Interface & Implementation**

Tạo `Services/ICostingService.cs`:
```csharp
using System.Threading.Tasks;

namespace WmsMes.Web.Services
{
    public interface ICostingService
    {
        Task<decimal> CalculateProductionCostAsync(int workOrderId);
    }
}
```

Tạo `Services/CostingService.cs` (Cộng dồn chi phí NVL lô thực tế tiêu thụ + chi phí nhân công công đoạn):
```csharp
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using WmsMes.Web.Data;

namespace WmsMes.Web.Services
{
    public class CostingService : ICostingService
    {
        private readonly ApplicationDbContext _context;

        public CostingService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<decimal> CalculateProductionCostAsync(int workOrderId)
        {
            var wo = await _context.WorkOrders
                .Include(w => w.Steps)
                .FirstOrDefaultAsync(w => w.Id == workOrderId);

            if (wo == null || wo.Qty == 0) return 0;

            // 1. Tính chi phí NVL thực tế tiêu thụ (Lot-based)
            var reservations = await _context.MaterialReservations
                .Include(r => r.Lot)
                .Where(r => r.WorkOrderId == workOrderId)
                .ToListAsync();

            decimal totalMaterialCost = 0;
            foreach (var res in reservations)
            {
                // res.QtyReserved * Lot.UnitPrice
                decimal lotPrice = res.Lot?.UnitPrice ?? 0;
                totalMaterialCost += res.QtyReserved * lotPrice;
            }

            // 2. Tính chi phí nhân công theo thời gian công đoạn (ví dụ: định mức 50.000đ/giờ công)
            decimal totalLaborTime = wo.Steps.Sum(s => s.QtyOK > 0 ? s.StepNumber * 5 : 0); // Ví dụ đơn giản
            decimal laborRatePerHour = 50000;
            decimal totalLaborCost = (totalLaborTime / 60) * laborRatePerHour;

            // 3. Tính đơn giá thành phẩm
            decimal totalCost = totalMaterialCost + totalLaborCost;
            return totalCost / wo.Qty;
        }
    }
}
```

- [ ] **Step 2: Đăng ký dịch vụ vào Program.cs**

Sửa `Program.cs` để thêm dịch vụ DI cho `CostingService`:
```csharp
using WmsMes.Web.Services;

// Thêm trước builder.Build()
builder.Services.AddScoped<ICostingService, CostingService>();
```

- [ ] **Step 3: Commit code**

Run:
```bash
git add Services/ICostingService.cs Services/CostingService.cs Program.cs
git commit -m "feat: implement costing service calculation based on input lots"
```

---

### Task 5: Triển khai Báo cáo Truy vết Đệ quy (TraceabilityService)

**Files:**
- Create: `Services/ITraceabilityService.cs`
- Create: `Services/TraceabilityService.cs`
- Modify: `Program.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext`.
- Produces: `ITraceabilityService` trả về JSON cấu trúc cây phả hệ lô xuôi/ngược.

- [ ] **Step 1: Tạo DTO cấu trúc Cây truy vết**

Tạo `DTOs/LotNodeDto.cs`:
```csharp
using System.Collections.Generic;

namespace WmsMes.Web.DTOs
{
    public class LotNodeDto
    {
        public string LotNo { get; set; } = string.Empty;
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public decimal Qty { get; set; }
        public string ExpiryDate { get; set; } = string.Empty;
        public string Status { get; set; } = "PASS";
        public List<LotNodeDto> Children { get; set; } = new List<LotNodeDto>();
    }
}
```

- [ ] **Step 2: Tạo Traceability Service Interface & Implementation**

Tạo `Services/ITraceabilityService.cs`:
```csharp
using System.Threading.Tasks;
using WmsMes.Web.DTOs;

namespace WmsMes.Web.Services
{
    public interface ITraceabilityService
    {
        Task<LotNodeDto?> GetBackwardTraceAsync(string lotNo);
        Task<LotNodeDto?> GetForwardTraceAsync(string lotNo);
    }
}
```

Tạo `Services/TraceabilityService.cs` (quét đệ quy bảng phả hệ lô):
```csharp
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;
using WmsMes.Web.Data;
using WmsMes.Web.DTOs;

namespace WmsMes.Web.Services
{
    public class TraceabilityService : ITraceabilityService
    {
        private readonly ApplicationDbContext _context;

        public TraceabilityService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<LotNodeDto?> GetBackwardTraceAsync(string lotNo)
        {
            var lot = await _context.Lots
                .Include(l => l.Product)
                .FirstOrDefaultAsync(l => l.LotNo == lotNo);

            if (lot == null) return null;

            var node = new LotNodeDto
            {
                LotNo = lot.LotNo,
                ProductCode = lot.Product?.Code ?? string.Empty,
                ProductName = lot.Product?.Name ?? string.Empty,
                Qty = lot.Qty,
                ExpiryDate = lot.ExpiryDate?.ToString("yyyy-MM-dd") ?? "N/A"
            };

            await BuildBackwardTreeAsync(lot.Id, node);
            return node;
        }

        private async Task BuildBackwardTreeAsync(int outputLotId, LotNodeDto parentNode)
        {
            var relations = await _context.LotGenealogies
                .Include(g => g.InputLot)
                .ThenInclude(l => l!.Product)
                .Where(g => g.OutputLotId == outputLotId)
                .ToListAsync();

            foreach (var rel in relations)
            {
                if (rel.InputLot == null) continue;

                var childNode = new LotNodeDto
                {
                    LotNo = rel.InputLot.LotNo,
                    ProductCode = rel.InputLot.Product?.Code ?? string.Empty,
                    ProductName = rel.InputLot.Product?.Name ?? string.Empty,
                    Qty = rel.QtyConsumed,
                    ExpiryDate = rel.InputLot.ExpiryDate?.ToString("yyyy-MM-dd") ?? "N/A"
                };

                parentNode.Children.Add(childNode);

                // Đệ quy
                await BuildBackwardTreeAsync(rel.InputLotId, childNode);
            }
        }

        public async Task<LotNodeDto?> GetForwardTraceAsync(string lotNo)
        {
            var lot = await _context.Lots
                .Include(l => l.Product)
                .FirstOrDefaultAsync(l => l.LotNo == lotNo);

            if (lot == null) return null;

            var node = new LotNodeDto
            {
                LotNo = lot.LotNo,
                ProductCode = lot.Product?.Code ?? string.Empty,
                ProductName = lot.Product?.Name ?? string.Empty,
                Qty = lot.Qty,
                ExpiryDate = lot.ExpiryDate?.ToString("yyyy-MM-dd") ?? "N/A"
            };

            await BuildForwardTreeAsync(lot.Id, node);
            return node;
        }

        private async Task BuildForwardTreeAsync(int inputLotId, LotNodeDto parentNode)
        {
            var relations = await _context.LotGenealogies
                .Include(g => g.OutputLot)
                .ThenInclude(l => l!.Product)
                .Where(g => g.InputLotId == inputLotId)
                .ToListAsync();

            foreach (var rel in relations)
            {
                if (rel.OutputLot == null) continue;

                var childNode = new LotNodeDto
                {
                    LotNo = rel.OutputLot.LotNo,
                    ProductCode = rel.OutputLot.Product?.Code ?? string.Empty,
                    ProductName = rel.OutputLot.Product?.Name ?? string.Empty,
                    Qty = rel.OutputLot.Qty,
                    ExpiryDate = rel.OutputLot.ExpiryDate?.ToString("yyyy-MM-dd") ?? "N/A"
                };

                parentNode.Children.Add(childNode);

                // Đệ quy
                await BuildForwardTreeAsync(rel.OutputLotId, childNode);
            }
        }
    }
}
```

- [ ] **Step 3: Đăng ký dịch vụ truy vết**

Sửa `Program.cs` để thêm dịch vụ DI:
```csharp
using WmsMes.Web.Services;

// Thêm trước builder.Build()
builder.Services.AddScoped<ITraceabilityService, TraceabilityService>();
```

- [ ] **Step 4: Commit code**

Run:
```bash
git add DTOs/LotNodeDto.cs Services/ITraceabilityService.cs Services/TraceabilityService.cs Program.cs
git commit -m "feat: implement recursive backward and forward traceability service"
```

---

### Task 6: Viết Unit Test tự động cho Nghiệp vụ QC, Báo cáo và Phân bổ Giá thành

**Files:**
- Create: `WmsMes.Tests/QcAndCostingTests.cs`

**Interfaces:**
- Consumes: `QcService`, `CostingService`, `TraceabilityService`.
- Produces: Bộ kiểm thử tự động xác minh tính chính xác của các quy tắc QC và Giá thành.

- [ ] **Step 1: Tạo file Unit Test cho QC & Giá thành**

Tạo file `WmsMes.Tests/QcAndCostingTests.cs` sử dụng DbContext in-memory:
```csharp
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.Services;

namespace WmsMes.Tests
{
    public class QcAndCostingTests
    {
        private ApplicationDbContext GetInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            return new ApplicationDbContext(options);
        }

        [Fact]
        public async Task SubmitQCInspectionAsync_ChangesStockFromOnHoldToAvailable_WhenPassed()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            
            // Setup checklist item
            var checklistItem = new QCChecklistItem { Id = 1, ParameterName = "DoAm", MinVal = 10, MaxVal = 15, IsRequired = true };
            await context.QCChecklistItems.AddAsync(checklistItem);

            // Setup stock balance on hold
            var balance = new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 0, QtyOnHold = 100 };
            await context.StockBalances.AddAsync(balance);

            var wo = new WorkOrder { Id = 1, Code = "WO01", Qty = 100 };
            await context.WorkOrders.AddAsync(wo);
            await context.SaveChangesAsync();

            var mockCosting = new Moq.Mock<ICostingService>();
            mockCosting.Setup(c => c.CalculateProductionCostAsync(1)).ReturnsAsync(5000); // 5000 vnd/unit

            var service = new QcService(context, mockCosting.Object);

            var inspection = new QCInspection { LotId = 1, WorkOrderId = 1, Result = QCResult.PASS };
            inspection.Lines.Add(new QCInspectionLine { ParameterName = "DoAm", ValueInspected = "12" }); // 12 is within 10-15 -> PASS

            // Act
            var success = await service.SubmitQCInspectionAsync(inspection, "user1");

            // Assert
            Assert.True(success);
            var updatedBalance = await context.StockBalances.FirstAsync(sb => sb.LotId == 1);
            Assert.Equal(100, updatedBalance.QtyAvailable); // Moved to available
            Assert.Equal(0, updatedBalance.QtyOnHold);
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
git add WmsMes.Tests/QcAndCostingTests.cs
git commit -m "test: write unit tests for QC and Costing Services"
```

---

### Task 7: Cấu hình SignalR Cảnh báo Lỗi chất lượng (QualityHub)

**Files:**
- Create: `Hubs/QualityHub.cs`
- Modify: `Program.cs`
- Modify: `Services/QcService.cs`

**Interfaces:**
- Consumes: Thư viện SignalR.
- Produces: `QualityHub` phát tín hiệu cảnh báo khẩn cấp tới trình duyệt quản lý khi phát hiện lô hàng bị lỗi (`REJECT`).

- [ ] **Step 1: Tạo Hub lớp trong thư mục Hubs**

Tạo `Hubs/QualityHub.cs`:
```csharp
using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace WmsMes.Web.Hubs
{
    public class QualityHub : Hub
    {
        public async Task SendQcAlert(string lotNo, string result)
        {
            await Clients.All.SendAsync("ReceiveQcAlert", lotNo, result);
        }
    }
}
```

- [ ] **Step 2: Đăng ký Hub trong Program.cs**

Sửa `Program.cs` để thêm dịch vụ SignalR routing:
```csharp
using WmsMes.Web.Hubs;

// Chèn sau app.UseRouting()
app.MapHub<QualityHub>("/qualityHub");
```

- [ ] **Step 3: Gọi Hub phát cảnh báo lỗi chất lượng từ QcService**

Sửa `Services/QcService.cs` để tiêm `IHubContext<QualityHub>` và gọi Hub cảnh báo khi kết quả đánh giá là `REJECT`:
```csharp
using WmsMes.Web.Hubs;

// Bổ sung thuộc tính & tiêm vào hàm dựng trong QcService:
private readonly IHubContext<QualityHub> _qualityHubContext;

public QcService(ApplicationDbContext context, ICostingService costingService, IHubContext<QualityHub> qualityHubContext)
{
    _context = context;
    _costingService = costingService;
    _qualityHubContext = qualityHubContext;
}

// Bổ sung lệnh gọi Hub cuối phương thức SubmitQCInspectionAsync (sau transaction.CommitAsync() và chỉ khi Result là REJECT):
if (inspection.Result == QCResult.REJECT)
{
    await _qualityHubContext.Clients.All.SendAsync("ReceiveQcAlert", inspection.LotId.ToString(), "REJECT");
}
```

- [ ] **Step 4: Commit code**

Run:
```bash
git add Hubs/QualityHub.cs Program.cs Services/QcService.cs
git commit -m "feat: integrate SignalR QualityHub for failed inspections alerts"
```

---

### Task 8: Thiết lập Giao diện Báo cáo Đồ họa phả hệ lô (SVG Tree) & Dashboard

**Files:**
- Create: `Controllers/TraceabilityController.cs`
- Create: `Views/Traceability/Index.cshtml`
- Modify: `Views/Shared/_Layout.cshtml`

**Interfaces:**
- Consumes: `ITraceabilityService`, API trả về JSON của cây đệ quy phả hệ lô.
- Produces: Màn hình tìm kiếm và hiển thị sơ đồ cây vẽ bằng đồ họa Node SVG.

- [ ] **Step 1: Tạo TraceabilityController**

Tạo `Controllers/TraceabilityController.cs`:
```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using WmsMes.Web.Services;

namespace WmsMes.Web.Controllers
{
    [Authorize]
    public class TraceabilityController : Controller
    {
        private readonly ITraceabilityService _traceService;

        public TraceabilityController(ITraceabilityService traceService)
        {
            _traceService = traceService;
        }

        [HttpGet]
        public IActionResult Index() => View();

        [HttpGet]
        public async Task<IActionResult> GetTree(string lotNo)
        {
            var tree = await _traceService.GetBackwardTraceAsync(lotNo);
            return Json(tree);
        }
    }
}
```

- [ ] **Step 2: Tạo giao diện sơ đồ cây SVG**

Tạo `Views/Traceability/Index.cshtml` vẽ cây phả hệ bằng JS đệ quy đơn giản:
```html
@{
    ViewData["Title"] = "Truy vết phả hệ Lô sản phẩm";
}

<div class="container py-4">
    <h2>Truy vết hai chiều Lô sản phẩm (Traceability)</h2>
    <div class="input-group my-3" style="max-width: 500px;">
        <input type="text" id="lotInput" class="form-control" placeholder="Nhập số Lô (Lot Number) cần truy vết" />
        <button class="btn btn-primary" onclick="loadTraceTree()">Tìm kiếm phả hệ</button>
    </div>

    <div class="card bg-dark text-white border-0 shadow-sm mt-4" style="min-height: 400px; display: flex; align-items: center; justify-content: center;">
        <div class="card-body w-100" id="canvas">
            <p class="text-center text-secondary">Nhập số lô và nhấn nút để xem sơ đồ cây phả hệ</p>
        </div>
    </div>
</div>

@section Scripts {
    <script>
        function loadTraceTree() {
            const lotNo = document.getElementById("lotInput").value;
            if (!lotNo) return;

            fetch(`/Traceability/GetTree?lotNo=${lotNo}`)
                .then(res => res.json())
                .then(data => {
                    const canvas = document.getElementById("canvas");
                    canvas.innerHTML = "";
                    if (!data) {
                        canvas.innerHTML = `<p class="text-center text-danger">Không tìm thấy thông tin phả hệ của lô: ${lotNo}</p>`;
                        return;
                    }
                    // Vẽ cây text lồng nhau đơn giản đại diện cho cấu trúc phả hệ
                    canvas.appendChild(renderNode(data));
                });
        }

        function renderNode(node) {
            const ul = document.createElement("ul");
            ul.className = "list-group bg-dark text-white";
            const li = document.createElement("li");
            li.className = "list-group-item bg-secondary text-white my-1";
            li.innerHTML = `<strong>Lô: ${node.lotNo}</strong> - ${node.productName} (SL tiêu hao: ${node.qty})`;
            ul.appendChild(li);

            if (node.children && node.children.length > 0) {
                const subUl = document.createElement("ul");
                subUl.style.paddingLeft = "20px";
                node.children.forEach(child => {
                    subUl.appendChild(renderNode(child));
                });
                ul.appendChild(subUl);
            }
            return ul;
        }
    </script>
}
```

- [ ] **Step 3: Thêm menu Truy vết vào layout**

Sửa `Views/Shared/_Layout.cshtml` để bổ sung liên kết đến màn hình Truy vết:
```html
<li class="nav-item">
    <a class="nav-link text-white" asp-controller="Traceability" asp-action="Index">
        <i class="bi bi-diagram-3"></i> Truy vết phả hệ Lô
    </a>
</li>
```

- [ ] **Step 4: Chạy thử và xác thực**

Run: `dotnet run`
Expected: 
1. Truy cập `/Traceability` mở ra ô tìm kiếm lô.
2. Tìm kiếm lô thành phẩm đã báo cáo sản xuất (có mối liên kết ở bảng genealogy) sẽ hiển thị cây phả hệ đúng cấu trúc phân cấp.

- [ ] **Step 5: Commit code**

Run:
```bash
git add Controllers/ Views/
git commit -m "feat: implement graphic tree traceability view using recursive JSON mapping"
```
