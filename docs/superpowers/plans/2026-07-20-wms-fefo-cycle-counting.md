# [Feature] WMS Advanced: FEFO/FIFO Picking Recommendation & Cycle Counting Implementation Plan

> **For agentic workers (Codex / Antigravity):** REQUIRED SUB-SKILL: Use TDD & step-by-step verification. Follow exact file paths and test execution commands. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Xây dựng tính năng Gợi ý Xuất kho Tối ưu theo FEFO (Hạn dùng trước) / FIFO (Nhập trước) và Hệ thống Quản lý Kiểm kê Kho Định kỳ (Cycle Counting) với cơ chế tự động cân bằng chênh lệch tồn kho.

**Architecture:** Mở rộng `InventoryService` với thuật toán phân bổ Lô/Vị trí xuất hàng theo tiêu chí FEFO/FIFO. Bổ sung các Entity `CycleCountOrder` và `CycleCountItem` để quản lý đợt kiểm kê, tự động snapshot tồn hệ thống, tính toán lệch (Variance) và duyệt phiếu điều chỉnh kho tự động.

**Tech Stack:** ASP.NET Core 8 MVC / Web API, Entity Framework Core 8, xUnit (.NET 8).

---

## Global Constraints
- Target Framework: `.NET 8` (`net8.0`)
- Giữ nguyên toàn bộ 114 unit tests hiện có trong `WmsMes.Tests`.
- Tự động bỏ qua các lô hàng đang nằm tại Khu vực Cách ly / Tạm giữ QC (`QuarantineLocationCode`).

---

### Task 1: Thuật toán Gợi ý Xuất kho Tối ưu (FEFO / FIFO Picking Strategy)

**Files:**
- Create: `DTOs/PickingRecommendationDto.cs`
- Modify: `Services/IInventoryService.cs`
- Modify: `Services/InventoryService.cs`
- Modify: `Controllers/InventoryController.cs`
- Test: `WmsMes.Tests/FifoFefoPickingTests.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext` & `StockBalance`
- Produces: `Task<List<PickingRecommendationDto>> GetPickingRecommendationsAsync(int productId, decimal requiredQty, PickingStrategy strategy)`

- [ ] **Step 1: Tạo DTOs/PickingRecommendationDto.cs & Enum PickingStrategy**

```csharp
namespace WmsMes.Web.DTOs;

public enum PickingStrategy
{
    FEFO = 1, // First-Expired, First-Out (Hạn dùng trước xuất trước)
    FIFO = 2  // First-In, First-Out (Nhập trước xuất trước)
}

public sealed class PickingRecommendationDto
{
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int LocationId { get; set; }
    public string LocationCode { get; set; } = string.Empty;
    public int LotId { get; set; }
    public string LotNo { get; set; } = string.Empty;
    public DateTime? ExpiryDate { get; set; }
    public DateTime ManufactureDate { get; set; }
    public decimal AvailableQty { get; set; }
    public decimal RecommendedQty { get; set; }
}
```

- [ ] **Step 2: Viết Failing Test cho FEFO & FIFO Strategy trong WmsMes.Tests/FifoFefoPickingTests.cs**

```csharp
public class FifoFefoPickingTests
{
    [Fact]
    public async Task GetPickingRecommendations_FEFO_ReturnsEarliestExpiringLotFirst()
    {
        // Setup InMemory DB với 2 Lot (Lot A hạn dùng tháng 10, Lot B hạn dùng tháng 8)
        // Act: GetPickingRecommendationsAsync(productId, 15, PickingStrategy.FEFO)
        // Assert: Lot B phải đứng trước Lot A
    }
}
```

- [ ] **Step 3: Chạy test để xác nhận FAIL**

Run: `dotnet test WmsMes.sln --filter "FullyQualifiedName~FifoFefoPickingTests"`
Expected: `FAIL` (phương thức chưa được triển khai)

- [ ] **Step 4: Triển khai GetPickingRecommendationsAsync trong InventoryService.cs**

Thêm logic lọc & sắp xếp:
- Lọc `StockBalances` theo `ProductId`, `QtyAvailable > 0`, và `Location.Code != QcService.QuarantineLocationCode`.
- Nếu `FEFO`: sắp xếp `.OrderBy(x => x.Lot.ExpiryDate ?? DateTime.MaxValue).ThenBy(x => x.Lot.ManufactureDate)`.
- Nếu `FIFO`: sắp xếp `.OrderBy(x => x.Lot.ManufactureDate).ThenBy(x => x.Id)`.
- Duyệt danh sách và tính toán số lượng phân bổ `RecommendedQty` cho từng dòng cho tới khi đủ `requiredQty`.

- [ ] **Step 5: Bổ sung API Endpoint trong InventoryController.cs**

```csharp
[HttpGet("api/inventory/picking-recommendations")]
public async Task<IActionResult> GetPickingRecommendations(int productId, decimal requiredQty, PickingStrategy strategy = PickingStrategy.FEFO)
{
    var result = await _inventoryService.GetPickingRecommendationsAsync(productId, requiredQty, strategy);
    return Ok(result);
}
```

- [ ] **Step 6: Chạy lại test để xác nhận PASS**

Run: `dotnet test WmsMes.sln`
Expected: `Passed! - All tests pass`

---

### Task 2: Quản lý Kiểm kê Kho Định kỳ (Cycle Counting Engine)

**Files:**
- Create: `Domain/Entities/CycleCountOrder.cs`
- Create: `Domain/Entities/CycleCountItem.cs`
- Create: `Services/ICycleCountService.cs`
- Create: `Services/CycleCountService.cs`
- Modify: `Data/ApplicationDbContext.cs`
- Modify: `Program.cs`
- Test: `WmsMes.Tests/CycleCountTests.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext`, `IInventoryService`
- Produces: Quy trình tạo phiếu kiểm kê, cập nhật thực tế, tính chênh lệch & tự động tạo giao dịch điều chỉnh kho.

- [ ] **Step 1: Tạo Entities CycleCountOrder.cs & CycleCountItem.cs**

```csharp
namespace WmsMes.Web.Domain.Entities;

public class CycleCountOrder
{
    public int Id { get; set; }
    public string CountNumber { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    public string Status { get; set; } = "Draft"; // Draft, InProgress, Completed, Approved, Cancelled
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? CompletedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public List<CycleCountItem> Items { get; set; } = new();
}

public class CycleCountItem
{
    public int Id { get; set; }
    public int CycleCountOrderId { get; set; }
    public CycleCountOrder? CycleCountOrder { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public int LocationId { get; set; }
    public Location? Location { get; set; }
    public int LotId { get; set; }
    public Lot? Lot { get; set; }
    public decimal SystemQty { get; set; }
    public decimal? CountedQty { get; set; }
    public decimal VarianceQty => (CountedQty ?? SystemQty) - SystemQty;
}
```

- [ ] **Step 2: Cập nhật ApplicationDbContext.cs & đăng ký ICycleCountService**

Thêm `DbSet<CycleCountOrder>` và `DbSet<CycleCountItem>` vào [ApplicationDbContext.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Data/ApplicationDbContext.cs).
Đăng ký `builder.Services.AddScoped<ICycleCountService, CycleCountService>();` trong [Program.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Program.cs).

- [ ] **Step 3: Viết Unit Test trong WmsMes.Tests/CycleCountTests.cs**

Thêm test kiểm tra:
1. `CreateCycleCountOrderAsync`: chụp đúng số lượng tồn hệ thống `SystemQty`.
2. `ApproveCycleCountAsync`: tự động điều chỉnh tồn kho khi số lượng kiểm kê `CountedQty != SystemQty`.

- [ ] **Step 4: Triển khai CycleCountService.cs**

Viết phương thức:
- `CreateCycleCountOrderAsync(int warehouseId, string createdBy)`: Snapshot toàn bộ `StockBalance` trong kho vào `CycleCountItem`.
- `RecordCountResultsAsync(int orderId, List<CountResultDto> results)`: Cập nhật `CountedQty`.
- `ApproveAndAdjustStockAsync(int orderId, string approvedBy)`: Gọi `InventoryService.AdjustStockAsync` để điều chỉnh chênh lệch tồn kho khi phiếu được duyệt.

- [ ] **Step 5: Chạy toàn bộ Unit Tests để xác nhận PASS**

Run: `dotnet test WmsMes.sln`
Expected: `Passed! - All tests pass`

---

## Verification Plan

### Automated Tests
- Chạy `dotnet test WmsMes.sln` đảm bảo 100% Unit Tests (bao gồm test FEFO/FIFO Picking và Cycle Counting) vượt qua.

### Manual Verification
- Gọi API `GET /api/inventory/picking-recommendations?productId=1&requiredQty=50&strategy=1` để kiểm tra danh sách gợi ý lô hàng hết hạn sớm nhất.
- Tạo phiếu kiểm kê thử nghiệm qua API/UI, nhập số thực tế và duyệt phiếu để xác nhận tồn kho được cập nhật tự động.
