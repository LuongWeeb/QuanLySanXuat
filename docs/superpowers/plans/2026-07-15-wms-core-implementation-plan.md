# Kế hoạch thực hiện: Giai đoạn 2 - Quản lý Kho cốt lõi (WMS Core)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Xây dựng cơ sở dữ liệu kho, nghiệp vụ nhập/xuất/điều chuyển/kiểm kê chứng từ (Transaction-based), logic phân bổ lô FEFO/FIFO, hệ thống SignalR cập nhật tồn kho realtime và các giao diện quản trị kho.

**Architecture:** Sử dụng kiến trúc Single-Project Monolith. Các nghiệp vụ kho được bọc trong DB Transaction ở tầng Service để đảm bảo tính ACID. Tích hợp SignalR để broadcast dữ liệu tồn kho.

**Tech Stack:** ASP.NET Core MVC (.NET 8), EF Core, SQL Server, SignalR, Bootstrap 5.

## Global Constraints
- Hệ điều hành: Windows
- Tên dự án: WmsMes.Web
- Công nghệ: ASP.NET Core MVC (.NET 8), EF Core, SQL Server, SignalR.
- Không được phép để tồn kho âm (`QtyAvailable < 0`).
- Kiểm kê phải khóa vị trí bằng cách chuyển khả dụng sang tạm giữ (`QtyAvailable -> QtyOnHold`).
- Giá trị chênh lệch kiểm kê phải lưu vào `StockTransaction` loại `Adjust` khi phê duyệt.

---

### Task 1: Định nghĩa các Thực thể tồn kho & Lịch sử giao dịch kho

**Files:**
- Create: `Domain/Entities/Lot.cs`
- Create: `Domain/Entities/StockBalance.cs`
- Create: `Domain/Entities/StockTransaction.cs`
- Create: `Domain/Enums/TransactionType.cs`
- Modify: `Data/ApplicationDbContext.cs`

**Interfaces:**
- Consumes: CSDL từ Giai đoạn 1.
- Produces: Các bảng `Lots`, `StockBalances`, `StockTransactions` và ràng buộc Unique trên CSDL.

- [ ] **Step 1: Tạo TransactionType Enum**

Tạo `Domain/Enums/TransactionType.cs`:
```csharp
namespace WmsMes.Web.Domain.Enums
{
    public enum TransactionType
    {
        Receipt = 0,
        Issue = 1,
        Transfer = 2,
        Adjust = 3,
        Backflush = 4
    }
}
```

- [ ] **Step 2: Tạo Lot Entity**

Tạo `Domain/Entities/Lot.cs`:
```csharp
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities
{
    public class Lot
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string LotNo { get; set; } = string.Empty;

        [Required]
        public int ProductId { get; set; }

        [ForeignKey("ProductId")]
        public virtual Product? Product { get; set; }

        public DateTime? ManufactureDate { get; set; }
        public DateTime? ExpiryDate { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Qty { get; set; }

        public int? WorkOrderId { get; set; } // Dành cho MES
    }
}
```

- [ ] **Step 3: Tạo StockBalance Entity**

Tạo `Domain/Entities/StockBalance.cs`:
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities
{
    public class StockBalance
    {
        public int Id { get; set; }

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
        public decimal QtyAvailable { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal QtyReserved { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal QtyOnHold { get; set; }
    }
}
```

- [ ] **Step 4: Tạo StockTransaction Entity**

Tạo `Domain/Entities/StockTransaction.cs`:
```csharp
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.Domain.Entities
{
    public class StockTransaction
    {
        public int Id { get; set; }

        [Required]
        public TransactionType Type { get; set; }

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
        public decimal Qty { get; set; }

        [Required]
        public DateTime TransactionDate { get; set; } = DateTime.UtcNow;

        [Required]
        [MaxLength(450)]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string ReferenceNo { get; set; } = string.Empty;
    }
}
```

- [ ] **Step 5: Cập nhật ApplicationDbContext**

Sửa `Data/ApplicationDbContext.cs` để cấu hình DbSet và Unique Index:
```csharp
// Thêm vào DbContext
public DbSet<Lot> Lots { get; set; }
public DbSet<StockBalance> StockBalances { get; set; }
public DbSet<StockTransaction> StockTransactions { get; set; }

// Thêm cấu hình trong OnModelCreating:
builder.Entity<Lot>()
    .HasIndex(l => l.LotNo)
    .IsUnique();

builder.Entity<StockBalance>()
    .HasIndex(sb => new { sb.ProductId, sb.LotId, sb.LocationId })
    .IsUnique();
```

- [ ] **Step 6: Chạy Migration và cập nhật database**

Run: `dotnet ef migrations add AddWmsCoreTables -o Data/Migrations`
Expected: Tạo thành công file migration.
Run: `dotnet ef database update`
Expected: Các bảng được tạo thành công trong SQL Server.

- [ ] **Step 7: Commit code**

Run:
```bash
git add Domain/Enums/ Domain/Entities/ Data/ApplicationDbContext.cs Data/Migrations/
git commit -m "feat: implement lot, stockbalance and stocktransaction entities"
```

---

### Task 2: Định nghĩa các Thực thể Chứng từ Nhập kho & Xuất kho

**Files:**
- Create: `Domain/Entities/GoodsReceipt.cs`
- Create: `Domain/Entities/GoodsReceiptLine.cs`
- Create: `Domain/Entities/GoodsIssue.cs`
- Create: `Domain/Entities/GoodsIssueLine.cs`
- Create: `Domain/Enums/DocumentStatus.cs`
- Modify: `Data/ApplicationDbContext.cs`

**Interfaces:**
- Consumes: CSDL từ Task 1.
- Produces: Các bảng `GoodsReceipts`, `GoodsReceiptLines`, `GoodsIssues`, `GoodsIssueLines` trong SQL Server.

- [ ] **Step 1: Tạo DocumentStatus Enum**

Tạo `Domain/Enums/DocumentStatus.cs`:
```csharp
namespace WmsMes.Web.Domain.Enums
{
    public enum DocumentStatus
    {
        Draft = 0,
        Completed = 1
    }
}
```

- [ ] **Step 2: Tạo GoodsReceipt và GoodsReceiptLine Entities**

Tạo `Domain/Entities/GoodsReceipt.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.Domain.Entities
{
    public class GoodsReceipt
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string ReceiptNo { get; set; } = string.Empty;

        public int? SupplierId { get; set; }

        [ForeignKey("SupplierId")]
        public virtual Supplier? Supplier { get; set; }

        [Required]
        public DateTime ReceiptDate { get; set; } = DateTime.UtcNow;

        [Required]
        public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

        public virtual ICollection<GoodsReceiptLine> Lines { get; set; } = new List<GoodsReceiptLine>();
    }
}
```

Tạo `Domain/Entities/GoodsReceiptLine.cs`:
```csharp
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities
{
    public class GoodsReceiptLine
    {
        public int Id { get; set; }

        [Required]
        public int GoodsReceiptId { get; set; }

        [ForeignKey("GoodsReceiptId")]
        public virtual GoodsReceipt? GoodsReceipt { get; set; }

        [Required]
        public int ProductId { get; set; }

        [ForeignKey("ProductId")]
        public virtual Product? Product { get; set; }

        [Required]
        [MaxLength(100)]
        public string LotNo { get; set; } = string.Empty;

        public DateTime? ExpiryDate { get; set; }
        public DateTime? ManufactureDate { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Qty { get; set; }

        [Required]
        public int LocationId { get; set; }

        [ForeignKey("LocationId")]
        public virtual Location? Location { get; set; }
    }
}
```

- [ ] **Step 3: Tạo GoodsIssue và GoodsIssueLine Entities**

Tạo `Domain/Entities/GoodsIssue.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.Domain.Entities
{
    public class GoodsIssue
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string IssueNo { get; set; } = string.Empty;

        [Required]
        public DateTime IssueDate { get; set; } = DateTime.UtcNow;

        [Required]
        public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

        public virtual ICollection<GoodsIssueLine> Lines { get; set; } = new List<GoodsIssueLine>();
    }
}
```

Tạo `Domain/Entities/GoodsIssueLine.cs`:
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities
{
    public class GoodsIssueLine
    {
        public int Id { get; set; }

        [Required]
        public int GoodsIssueId { get; set; }

        [ForeignKey("GoodsIssueId")]
        public virtual GoodsIssue? GoodsIssue { get; set; }

        [Required]
        public int ProductId { get; set; }

        [ForeignKey("ProductId")]
        public virtual Product? Product { get; set; }

        [Required]
        public int LotId { get; set; }

        [ForeignKey("LotId")]
        public virtual Lot? Lot { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Qty { get; set; }

        [Required]
        public int LocationId { get; set; }

        [ForeignKey("LocationId")]
        public virtual Location? Location { get; set; }
    }
}
```

- [ ] **Step 4: Đăng ký trong DbContext và cấu hình Unique Constraint**

Sửa `Data/ApplicationDbContext.cs`:
```csharp
public DbSet<GoodsReceipt> GoodsReceipts { get; set; }
public DbSet<GoodsReceiptLine> GoodsReceiptLines { get; set; }
public DbSet<GoodsIssue> GoodsIssues { get; set; }
public DbSet<GoodsIssueLine> GoodsIssueLines { get; set; }

// Trong OnModelCreating:
builder.Entity<GoodsReceipt>()
    .HasIndex(r => r.ReceiptNo)
    .IsUnique();

builder.Entity<GoodsIssue>()
    .HasIndex(i => i.IssueNo)
    .IsUnique();
```

- [ ] **Step 5: Chạy Migration và cập nhật database**

Run: `dotnet ef migrations add AddWmsDocumentTables -o Data/Migrations`
Expected: Tạo migration thành công.
Run: `dotnet ef database update`
Expected: Tạo các bảng trên SQL Server.

- [ ] **Step 6: Commit code**

Run:
```bash
git add Domain/Enums/ Domain/Entities/ Data/ApplicationDbContext.cs Data/Migrations/
git commit -m "feat: implement goods receipt and goods issue entities"
```

---

### Task 3: Định nghĩa các Thực thể Chuyển kho và Kiểm kê

**Files:**
- Create: `Domain/Entities/StockTransfer.cs`
- Create: `Domain/Entities/StockTransferLine.cs`
- Create: `Domain/Entities/Stocktake.cs`
- Create: `Domain/Entities/StocktakeLine.cs`
- Create: `Domain/Enums/StocktakeStatus.cs`
- Modify: `Data/ApplicationDbContext.cs`

**Interfaces:**
- Consumes: CSDL từ Task 2.
- Produces: Các bảng `StockTransfers`, `Stocktake` liên quan trên SQL Server.

- [ ] **Step 1: Tạo StocktakeStatus Enum**

Tạo `Domain/Enums/StocktakeStatus.cs`:
```csharp
namespace WmsMes.Web.Domain.Enums
{
    public enum StocktakeStatus
    {
        Draft = 0,
        Counting = 1,
        AwaitingApproval = 2,
        Completed = 3
    }
}
```

- [ ] **Step 2: Tạo StockTransfer và StockTransferLine Entities**

Tạo `Domain/Entities/StockTransfer.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.Domain.Entities
{
    public class StockTransfer
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string TransferNo { get; set; } = string.Empty;

        [Required]
        public DateTime TransferDate { get; set; } = DateTime.UtcNow;

        [Required]
        public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

        public virtual ICollection<StockTransferLine> Lines { get; set; } = new List<StockTransferLine>();
    }
}
```

Tạo `Domain/Entities/StockTransferLine.cs`:
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities
{
    public class StockTransferLine
    {
        public int Id { get; set; }

        [Required]
        public int StockTransferId { get; set; }

        [ForeignKey("StockTransferId")]
        public virtual StockTransfer? StockTransfer { get; set; }

        [Required]
        public int ProductId { get; set; }

        [ForeignKey("ProductId")]
        public virtual Product? Product { get; set; }

        [Required]
        public int LotId { get; set; }

        [ForeignKey("LotId")]
        public virtual Lot? Lot { get; set; }

        [Required]
        public int FromLocationId { get; set; }

        [ForeignKey("FromLocationId")]
        public virtual Location? FromLocation { get; set; }

        [Required]
        public int ToLocationId { get; set; }

        [ForeignKey("ToLocationId")]
        public virtual Location? ToLocation { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Qty { get; set; }
    }
}
```

- [ ] **Step 3: Tạo Stocktake và StocktakeLine Entities**

Tạo `Domain/Entities/Stocktake.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.Domain.Entities
{
    public class Stocktake
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string StocktakeNo { get; set; } = string.Empty;

        [Required]
        public int LocationId { get; set; }

        [ForeignKey("LocationId")]
        public virtual Location? Location { get; set; }

        [Required]
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        [Required]
        public StocktakeStatus Status { get; set; } = StocktakeStatus.Draft;

        public virtual ICollection<StocktakeLine> Lines { get; set; } = new List<StocktakeLine>();
    }
}
```

Tạo `Domain/Entities/StocktakeLine.cs`:
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities
{
    public class StocktakeLine
    {
        public int Id { get; set; }

        [Required]
        public int StocktakeId { get; set; }

        [ForeignKey("StocktakeId")]
        public virtual Stocktake? Stocktake { get; set; }

        [Required]
        public int ProductId { get; set; }

        [ForeignKey("ProductId")]
        public virtual Product? Product { get; set; }

        [Required]
        public int LotId { get; set; }

        [ForeignKey("LotId")]
        public virtual Lot? Lot { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal QtySystem { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal QtyCounted { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal QtyDiscrepancy { get; set; }
    }
}
```

- [ ] **Step 4: Đăng ký trong DbContext và cấu hình khóa Unique**

Sửa `Data/ApplicationDbContext.cs`:
```csharp
public DbSet<StockTransfer> StockTransfers { get; set; }
public DbSet<StockTransferLine> StockTransferLines { get; set; }
public DbSet<Stocktake> Stocktakes { get; set; }
public DbSet<StocktakeLine> StocktakeLines { get; set; }

// Trong OnModelCreating:
builder.Entity<StockTransfer>()
    .HasIndex(t => t.TransferNo)
    .IsUnique();

builder.Entity<Stocktake>()
    .HasIndex(s => s.StocktakeNo)
    .IsUnique();
```

- [ ] **Step 5: Chạy Migration và cập nhật database**

Run: `dotnet ef migrations add AddTransferAndStocktakeTables -o Data/Migrations`
Expected: Tạo migration thành công.
Run: `dotnet ef database update`
Expected: Tạo các bảng trên SQL Server.

- [ ] **Step 6: Commit code**

Run:
```bash
git add Domain/Enums/ Domain/Entities/ Data/ApplicationDbContext.cs Data/Migrations/
git commit -m "feat: implement stock transfer and stocktake entities"
```

---

### Task 4: Triển khai Logic Nghiệp vụ Nhập kho & Xuất kho (FEFO/FIFO)

**Files:**
- Create: `Services/IInventoryService.cs`
- Create: `Services/InventoryService.cs`
- Modify: `Program.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext`, `IGenericRepository` các bảng liên quan.
- Produces: `IInventoryService` để xử lý các giao dịch Nhập/Xuất bằng Transaction và trả về danh sách gợi ý phân bổ Lô.

- [ ] **Step 1: Tạo Service Interface cho Kho**

Tạo `Services/IInventoryService.cs`:
```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using WmsMes.Web.Domain.Entities;

namespace WmsMes.Web.Services
{
    public interface IInventoryService
    {
        Task<IEnumerable<StockBalance>> GetSuggestedLotsAsync(int productId, decimal qty);
        Task<bool> CompleteGoodsReceiptAsync(int receiptId, string userId);
        Task<bool> CompleteGoodsIssueAsync(int issueId, string userId);
    }
}
```

- [ ] **Step 2: Tạo Service Implementation cho Kho**

Tạo `Services/InventoryService.cs` (chứa toàn bộ logic CSDL Transaction và quy tắc FEFO/FIFO):
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
    public class InventoryService : IInventoryService
    {
        private readonly ApplicationDbContext _context;

        public InventoryService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<StockBalance>> GetSuggestedLotsAsync(int productId, decimal qty)
        {
            var product = await _context.Products.FindAsync(productId);
            if (product == null) return Enumerable.Empty<StockBalance>();

            // Query stock balances with active available quantity
            var query = _context.StockBalances
                .Include(sb => sb.Lot)
                .Include(sb => sb.Location)
                .Where(sb => sb.ProductId == productId && sb.QtyAvailable > 0);

            // FEFO/FIFO sorting
            if (product.ShelfLifeDays.HasValue)
            {
                query = query.OrderBy(sb => sb.Lot!.ExpiryDate); // FEFO
            }
            else
            {
                query = query.OrderBy(sb => sb.Lot!.Id); // FIFO (Lô tạo trước xuất trước)
            }

            var balances = await query.ToListAsync();
            var suggestions = new List<StockBalance>();
            decimal remainingQty = qty;

            foreach (var balance in balances)
            {
                if (remainingQty <= 0) break;

                decimal allocateQty = Math.Min(balance.QtyAvailable, remainingQty);
                suggestions.Add(new StockBalance
                {
                    ProductId = productId,
                    LotId = balance.LotId,
                    Lot = balance.Lot,
                    LocationId = balance.LocationId,
                    Location = balance.Location,
                    QtyAvailable = allocateQty
                });
                remainingQty -= allocateQty;
            }

            return suggestions;
        }

        public async Task<bool> CompleteGoodsReceiptAsync(int receiptId, string userId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var receipt = await _context.GoodsReceipts
                    .Include(r => r.Lines)
                    .FirstOrDefaultAsync(r => r.Id == receiptId);

                if (receipt == null || receipt.Status == DocumentStatus.Completed) return false;

                foreach (var line in receipt.Lines)
                {
                    // Find or create Lot
                    var lot = await _context.Lots.FirstOrDefaultAsync(l => l.LotNo == line.LotNo && l.ProductId == line.ProductId);
                    if (lot == null)
                    {
                        lot = new Lot
                        {
                            LotNo = line.LotNo,
                            ProductId = line.ProductId,
                            ExpiryDate = line.ExpiryDate,
                            ManufactureDate = line.ManufactureDate,
                            Qty = line.Qty
                        };
                        await _context.Lots.AddAsync(lot);
                        await _context.SaveChangesAsync();
                    }

                    // Find or create StockBalance
                    var balance = await _context.StockBalances
                        .FirstOrDefaultAsync(sb => sb.ProductId == line.ProductId && sb.LotId == lot.Id && sb.LocationId == line.LocationId);

                    if (balance == null)
                    {
                        balance = new StockBalance
                        {
                            ProductId = line.ProductId,
                            LotId = lot.Id,
                            LocationId = line.LocationId,
                            QtyAvailable = 0,
                            QtyReserved = 0,
                            QtyOnHold = 0
                        };
                        await _context.StockBalances.AddAsync(balance);
                    }

                    balance.QtyAvailable += line.Qty;

                    // Write StockTransaction
                    var stockTx = new StockTransaction
                    {
                        Type = TransactionType.Receipt,
                        ProductId = line.ProductId,
                        LotId = lot.Id,
                        LocationId = line.LocationId,
                        Qty = line.Qty,
                        TransactionDate = DateTime.UtcNow,
                        UserId = userId,
                        ReferenceNo = receipt.ReceiptNo
                    };
                    await _context.StockTransactions.AddAsync(stockTx);
                }

                receipt.Status = DocumentStatus.Completed;
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

        public async Task<bool> CompleteGoodsIssueAsync(int issueId, string userId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var issue = await _context.GoodsIssues
                    .Include(i => i.Lines)
                    .FirstOrDefaultAsync(i => i.Id == issueId);

                if (issue == null || issue.Status == DocumentStatus.Completed) return false;

                foreach (var line in issue.Lines)
                {
                    var balance = await _context.StockBalances
                        .FirstOrDefaultAsync(sb => sb.ProductId == line.ProductId && sb.LotId == line.LotId && sb.LocationId == line.LocationId);

                    if (balance == null || balance.QtyAvailable < line.Qty)
                    {
                        throw new InvalidOperationException("Not enough available stock. Negative stock is not allowed.");
                    }

                    balance.QtyAvailable -= line.Qty;

                    // Write StockTransaction
                    var stockTx = new StockTransaction
                    {
                        Type = TransactionType.Issue,
                        ProductId = line.ProductId,
                        LotId = line.LotId,
                        LocationId = line.LocationId,
                        Qty = -line.Qty,
                        TransactionDate = DateTime.UtcNow,
                        UserId = userId,
                        ReferenceNo = issue.IssueNo
                    };
                    await _context.StockTransactions.AddAsync(stockTx);
                }

                issue.Status = DocumentStatus.Completed;
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

- [ ] **Step 3: Đăng ký dịch vụ vào Program.cs**

Sửa `Program.cs` để cấu hình DI cho `InventoryService`:
```csharp
using WmsMes.Web.Services;

// Thêm vào trước builder.Build()
builder.Services.AddScoped<IInventoryService, InventoryService>();
```

- [ ] **Step 4: Commit code**

Run:
```bash
git add Services/IInventoryService.cs Services/InventoryService.cs Program.cs
git commit -m "feat: implement InventoryService with Receipt and Issue transactions"
```

---

### Task 5: Triển khai Logic Nghiệp vụ Kiểm kê (Stocktake)

**Files:**
- Modify: `Services/IInventoryService.cs`
- Modify: `Services/InventoryService.cs`

**Interfaces:**
- Consumes: `IInventoryService` từ Task 4.
- Produces: Phương thức `StartStocktakeAsync` và `ApproveStocktakeAsync` để kiểm kê khóa vị trí và phê duyệt chênh lệch.

- [ ] **Step 1: Khai báo các phương thức kiểm kê trong IInventoryService**

Sửa `Services/IInventoryService.cs` để thêm 2 phương thức mới:
```csharp
Task<bool> StartStocktakeAsync(int stocktakeId);
Task<bool> ApproveStocktakeAsync(int stocktakeId, string userId);
```

- [ ] **Step 2: Triển khai code xử lý kiểm kê trong InventoryService**

Sửa `Services/InventoryService.cs` để thêm code xử lý khóa kho (Freeze) và hoàn thành chênh lệch (Approve):
```csharp
public async Task<bool> StartStocktakeAsync(int stocktakeId)
{
    using var transaction = await _context.Database.BeginTransactionAsync();
    try
    {
        var stocktake = await _context.Stocktakes
            .FirstOrDefaultAsync(s => s.Id == stocktakeId);

        if (stocktake == null || stocktake.Status != StocktakeStatus.Draft) return false;

        // Get all balances at the specific Location
        var balances = await _context.StockBalances
            .Where(sb => sb.LocationId == stocktake.LocationId)
            .ToListAsync();

        foreach (var balance in balances)
        {
            // Freeze Stock: QtyAvailable -> QtyOnHold
            balance.QtyOnHold += balance.QtyAvailable;
            balance.QtyAvailable = 0;

            // Log lines to counting draft
            var line = new StocktakeLine
            {
                StocktakeId = stocktakeId,
                ProductId = balance.ProductId,
                LotId = balance.LotId,
                QtySystem = balance.QtyOnHold, // Tồn hệ thống tại thời điểm kiểm kê
                QtyCounted = 0,
                QtyDiscrepancy = 0
            };
            await _context.StocktakeLines.AddAsync(line);
        }

        stocktake.Status = StocktakeStatus.Counting;
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

public async Task<bool> ApproveStocktakeAsync(int stocktakeId, string userId)
{
    using var transaction = await _context.Database.BeginTransactionAsync();
    try
    {
        var stocktake = await _context.Stocktakes
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == stocktakeId);

        if (stocktake == null || stocktake.Status != StocktakeStatus.AwaitingApproval) return false;

        foreach (var line in stocktake.Lines)
        {
            var balance = await _context.StockBalances
                .FirstOrDefaultAsync(sb => sb.ProductId == line.ProductId && sb.LotId == line.LotId && sb.LocationId == stocktake.LocationId);

            if (balance != null)
            {
                // Thừa/Thiếu chênh lệch
                decimal discrepancy = line.QtyCounted - line.QtySystem;
                line.QtyDiscrepancy = discrepancy;

                // Cập nhật lại QtyAvailable bằng lượng đếm thực tế
                balance.QtyAvailable = line.QtyCounted;
                balance.QtyOnHold = Math.Max(0, balance.QtyOnHold - line.QtySystem); // Giải phóng QtyOnHold

                if (discrepancy != 0)
                {
                    // Tạo giao dịch điều chỉnh (Adjust)
                    var stockTx = new StockTransaction
                    {
                        Type = TransactionType.Adjust,
                        ProductId = line.ProductId,
                        LotId = line.LotId,
                        LocationId = stocktake.LocationId,
                        Qty = discrepancy,
                        TransactionDate = DateTime.UtcNow,
                        UserId = userId,
                        ReferenceNo = stocktake.StocktakeNo
                    };
                    await _context.StockTransactions.AddAsync(stockTx);
                }
            }
        }

        stocktake.Status = StocktakeStatus.Completed;
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
```

- [ ] **Step 3: Commit code**

Run:
```bash
git add Services/IInventoryService.cs Services/InventoryService.cs
git commit -m "feat: implement stocktake freeze and approval workflows"
```

---

### Task 6: Viết Unit Test tự động cho Nghiệp vụ Kho

**Files:**
- Create: `WmsMes.Tests/InventoryServiceTests.cs`

**Interfaces:**
- Consumes: `InventoryService`
- Produces: Bộ kiểm thử tự động xác minh tính chính xác của nghiệp vụ kho (Không cho âm kho, gợi ý FEFO/FIFO).

- [ ] **Step 1: Tạo file Unit Test cho Kho**

Tạo file `WmsMes.Tests/InventoryServiceTests.cs` sử dụng DbContext in-memory để chạy test:
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
    public class InventoryServiceTests
    {
        private ApplicationDbContext GetInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            return new ApplicationDbContext(options);
        }

        [Fact]
        public async Task GetSuggestedLotsAsync_OrdersByExpiryDate_FEFO()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            var product = new Product { Id = 1, Code = "P01", Name = "Pro 1", ShelfLifeDays = 30, BaseUomId = 1 };
            await context.Products.AddAsync(product);

            var lot1 = new Lot { Id = 1, LotNo = "LOT01", ProductId = 1, ExpiryDate = DateTime.Today.AddDays(10) };
            var lot2 = new Lot { Id = 2, LotNo = "LOT02", ProductId = 1, ExpiryDate = DateTime.Today.AddDays(5) }; // Expiries first!
            await context.Lots.AddRangeAsync(lot1, lot2);

            var bal1 = new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 10 };
            var bal2 = new StockBalance { ProductId = 1, LotId = 2, LocationId = 1, QtyAvailable = 10 };
            await context.StockBalances.AddRangeAsync(bal1, bal2);
            await context.SaveChangesAsync();

            var service = new InventoryService(context);

            // Act
            var suggestions = new List<StockBalance>(await service.GetSuggestedLotsAsync(1, 15));

            // Assert
            Assert.Equal(2, suggestions.Count);
            Assert.Equal(2, suggestions[0].LotId); // Lot 2 has earlier expiry, should suggest first
            Assert.Equal(10, suggestions[0].QtyAvailable);
            Assert.Equal(1, suggestions[1].LotId);
            Assert.Equal(5, suggestions[1].QtyAvailable);
        }

        [Fact]
        public async Task CompleteGoodsIssueAsync_ThrowsException_WhenStockIsInsufficient()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            var issue = new GoodsIssue { Id = 1, IssueNo = "GI01", Status = DocumentStatus.Draft };
            issue.Lines.Add(new GoodsIssueLine { ProductId = 1, LotId = 1, Qty = 50, LocationId = 1 });
            await context.GoodsIssues.AddAsync(issue);

            var bal = new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 10 }; // Only 10 available
            await context.StockBalances.AddAsync(bal);
            await context.SaveChangesAsync();

            var service = new InventoryService(context);

            // Act & Assert
            await Assert.ThrowsAnyAsync<Exception>(() => service.CompleteGoodsIssueAsync(1, "user1"));
        }
    }
}
```

- [ ] **Step 2: Chạy bộ Unit Test**

Run: `dotnet test`
Expected: Tất cả các Unit Test chạy thành công và vượt qua.

- [ ] **Step 3: Commit code**

Run:
```bash
git add WmsMes.Tests/InventoryServiceTests.cs
git commit -m "test: write unit tests for WMS Core workflows"
```

---

### Task 7: Cấu hình SignalR để cập nhật Tồn kho Realtime

**Files:**
- Create: `Hubs/InventoryHub.cs`
- Modify: `Program.cs`
- Modify: `Services/InventoryService.cs`

**Interfaces:**
- Consumes: Thư viện SignalR.
- Produces: `InventoryHub` gửi thông điệp cập nhật tồn kho tức thời xuống giao diện của thủ kho.

- [ ] **Step 1: Tạo Hub lớp trong thư mục Hubs**

Tạo `Hubs/InventoryHub.cs`:
```csharp
using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace WmsMes.Web.Hubs
{
    public class InventoryHub : Hub
    {
        public async Task NotifyStockChange()
        {
            await Clients.All.SendAsync("ReceiveStockUpdate");
        }
    }
}
```

- [ ] **Step 2: Đăng ký SignalR trong Program.cs**

Sửa `Program.cs` để thêm dịch vụ SignalR và cấu hình Routing:
```csharp
using WmsMes.Web.Hubs;

// 1. Thêm dịch vụ (trước builder.Build())
builder.Services.AddSignalR();

// 2. Map Hub (sau app.UseRouting() và UseAuthorization())
app.MapHub<InventoryHub>("/inventoryHub");
```

- [ ] **Step 3: Tiêm IHubContext để phát thông báo khi hoàn thành giao dịch**

Sửa `Services/InventoryService.cs` để tiêm `IHubContext<InventoryHub>` và gọi Hub phát đi tín hiệu:
```csharp
using Microsoft.AspNetCore.SignalR;
using WmsMes.Web.Hubs;

// Bổ sung thuộc tính & hàm dựng trong InventoryService:
private readonly IHubContext<InventoryHub> _hubContext;

public InventoryService(ApplicationDbContext context, IHubContext<InventoryHub> hubContext)
{
    _context = context;
    _hubContext = hubContext;
}

// Bổ sung lệnh gọi Hub cuối các phương thức CompleteGoodsReceiptAsync, CompleteGoodsIssueAsync, ApproveStocktakeAsync (sau transaction.CommitAsync()):
await _hubContext.Clients.All.SendAsync("ReceiveStockUpdate");
```

- [ ] **Step 4: Commit code**

Run:
```bash
git add Hubs/InventoryHub.cs Program.cs Services/InventoryService.cs
git commit -m "feat: integrate SignalR Hub for realtime stock balance alerts"
```

---

### Task 8: Thiết lập Giao diện Dashboards & Màn hình Chứng từ kho

**Files:**
- Create: `Controllers/InventoryController.cs`
- Create: `Views/Inventory/Index.cshtml`
- Create: `Views/GoodsReceipt/Create.cshtml`
- Modify: `Views/Shared/_Layout.cshtml`

**Interfaces:**
- Consumes: `IInventoryService`, API kết nối đến `InventoryHub`.
- Produces: Trang Dashboard cập nhật số lượng tồn kho tự động bằng javascript SignalR.

- [ ] **Step 1: Tạo InventoryController**

Tạo `Controllers/InventoryController.cs`:
```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using WmsMes.Web.Data;

namespace WmsMes.Web.Controllers
{
    [Authorize]
    public class InventoryController : Controller
    {
        private readonly ApplicationDbContext _context;

        public InventoryController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var balances = await _context.StockBalances
                .Include(sb => sb.Product)
                .Include(sb => sb.Lot)
                .Include(sb => sb.Location)
                .ToListAsync();
            return View(balances);
        }
    }
}
```

- [ ] **Step 2: Tạo giao diện Index của Dashboard tồn kho**

Tạo `Views/Inventory/Index.cshtml` (tích hợp SignalR client JS):
```html
@model IEnumerable<WmsMes.Web.Domain.Entities.StockBalance>
@{
    ViewData["Title"] = "Tồn kho Realtime";
}

<div class="container-fluid py-4">
    <h2>Bảng tồn kho Realtime</h2>
    <div class="card bg-dark text-white border-0 shadow-sm mt-3">
        <div class="card-body">
            <table class="table table-dark table-striped" id="stockTable">
                <thead>
                    <tr>
                        <th>Sản phẩm</th>
                        <th>Số Lô</th>
                        <th>Vị trí</th>
                        <th>Khả dụng (Available)</th>
                        <th>Giữ chỗ (Reserved)</th>
                        <th>Tạm giữ (On Hold)</th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var item in Model)
                    {
                        <tr>
                            <td>@item.Product?.Name</td>
                            <td>@item.Lot?.LotNo</td>
                            <td>@item.Location?.Name</td>
                            <td>@item.QtyAvailable</td>
                            <td>@item.QtyReserved</td>
                            <td>@item.QtyOnHold</td>
                        </tr>
                    }
                </tbody>
            </table>
        </div>
    </div>
</div>

@section Scripts {
    <script src="https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/6.0.1/signalr.min.js"></script>
    <script>
        const connection = new signalR.HubConnectionBuilder()
            .withUrl("/inventoryHub")
            .build();

        connection.on("ReceiveStockUpdate", function () {
            // Reload trang khi có thay đổi tồn kho từ SignalR
            location.reload();
        });

        connection.start().catch(err => console.error(err.toString()));
    </script>
}
```

- [ ] **Step 3: Thêm các menu Kho vào Layout sidebar**

Sửa `Views/Shared/_Layout.cshtml` để bổ sung liên kết đến trang Tồn kho:
```html
<!-- Chèn vào Sidebar menu -->
<li class="nav-item">
    <a class="nav-link text-white" asp-controller="Inventory" asp-action="Index">
        <i class="bi bi-box-seam"></i> Tồn kho Realtime
    </a>
</li>
```

- [ ] **Step 4: Chạy thử toàn bộ giao diện và chức năng**

Run: `dotnet run`
Expected: Truy cập `http://localhost:5000/Inventory` hiển thị trang Dashboard trống. Khi có bất kỳ giao dịch kho nào thành công, giao diện sẽ tự động cập nhật và phản ánh số lượng thay đổi trên bảng.

- [ ] **Step 5: Commit code**

Run:
```bash
git add Controllers/ Views/
git commit -m "feat: build realtime inventory dashboard view using signalr"
```
