# Phân hệ Kiểm soát Chất lượng Tiêu chuẩn & Cách ly (Quality Control) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Xây dựng Phân hệ Kiểm soát Chất lượng Tiêu chuẩn (QC), hỗ trợ cấu hình mẫu tiêu chí cho từng sản phẩm, tự động kiểm tra số liệu đo lường thực tế với dải Min/Max tiêu chuẩn, xử lý kiểm định đầu vào (Inward QC) & thành phẩm (Final FG QC), và tự động giải phóng kho khi PASS hoặc chuyển cách ly kho `QC-QUARANTINE` khi REJECT.

**Architecture:** Cập nhật thực thể `QCInspection` hỗ trợ cả `GoodsReceiptId` và `WorkOrderId`. Nâng cấp `QcService` xử lý tự động tính `IsOK` dựa trên dải `[MinVal, MaxVal]`, giải phóng `QtyOnHold -> QtyAvailable` khi PASS. Xây dựng màn hình quản lý mẫu checklist và màn hình thực thi kiểm định.

**Tech Stack:** ASP.NET Core 8 MVC, Entity Framework Core 8, C#, Bootstrap 5, Javascript.

## Global Constraints
- Target Framework: `.NET 8` net8.0.
- Mã Vị trí kho cách ly phế phẩm mặc định là `QC-QUARANTINE`.
- Khi kết quả kiểm định là `PASS`, toàn bộ số lượng bị tạm giữ `QtyOnHold` của Lô hàng sẽ được giải phóng thành `QtyAvailable`.

---

### Task 1: Cập nhật Thực thể QC & Migration Database

**Files:**
- Create: [QCInspectionType.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Enums/QCInspectionType.cs)
- Modify: [QCInspection.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Entities/QCInspection.cs)
- Modify: [ApplicationDbContext.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Data/ApplicationDbContext.cs)

**Interfaces:**
- Consumes: Thực thể `QCInspection` hiện tại.
- Produces: `WorkOrderId` có thể null, bổ sung `GoodsReceiptId` và `Type`.

- [ ] **Step 1: Tạo Enum QCInspectionType.cs**
  Tạo `Domain/Enums/QCInspectionType.cs`:
  ```csharp
  namespace WmsMes.Web.Domain.Enums;

  public enum QCInspectionType
  {
      InwardQC = 1,
      FinalFGQC = 2
  }
  ```

- [ ] **Step 2: Cập nhật QCInspection.cs**
  Mở `Domain/Entities/QCInspection.cs` và điều chỉnh:
  ```csharp
  public int? WorkOrderId { get; set; }

  [ForeignKey(nameof(WorkOrderId))]
  public virtual WorkOrder? WorkOrder { get; set; }

  public int? GoodsReceiptId { get; set; }

  [ForeignKey(nameof(GoodsReceiptId))]
  public virtual GoodsReceipt? GoodsReceipt { get; set; }

  [Required]
  public QCInspectionType Type { get; set; } = QCInspectionType.FinalFGQC;
  ```

- [ ] **Step 3: Chạy Migration Database**
  Run: `dotnet ef migrations add UpdateQCInspectionSchema`
  Run: `dotnet ef database update`
  Expected: Migration thành công.

- [ ] **Step 4: Commit**
  Run: `git add Domain/ Data/Migrations/`
  Run: `git commit -m "feat: update QCInspection schema to support inward QC and optional work order link"`

---

### Task 2: Nâng cấp Dịch vụ QcService & Thuật toán Đánh giá

**Files:**
- Modify: [QcService.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Services/QcService.cs)

**Interfaces:**
- Consumes: Kết quả nhập liệu kiểm định từ kỹ thuật viên QC.
- Produces: Đánh giá `IsOK` tự động và thực hiện bút toán giải phóng/cách ly kho.

- [ ] **Step 1: Nâng cấp EvaluateLinesAsync trong QcService.cs**
  Cập nhật logic tự động chấm `IsOK` theo khoảng `[MinVal, MaxVal]`:
  ```csharp
  private async Task EvaluateLinesAsync(QCInspection inspection, int productId)
  {
      var checklist = await _context.QCChecklists
          .Include(c => c.Items)
          .FirstOrDefaultAsync(c => c.ProductId == productId && c.IsActive);

      if (checklist == null) return;

      foreach (var line in inspection.Lines)
      {
          var item = checklist.Items.FirstOrDefault(i => i.ParameterName.Trim().Equals(line.ParameterName.Trim(), StringComparison.OrdinalIgnoreCase));
          if (item != null && (item.MinVal.HasValue || item.MaxVal.HasValue))
          {
              if (decimal.TryParse(line.ValueInspected, out var numVal))
              {
                  bool ok = true;
                  if (item.MinVal.HasValue && numVal < item.MinVal.Value) ok = false;
                  if (item.MaxVal.HasValue && numVal > item.MaxVal.Value) ok = false;
                  line.IsOK = ok;
              }
          }
      }
  }
  ```

- [ ] **Step 2: Nâng cấp SubmitQCInspectionAsync giải phóng kho khi PASS**
  Nếu `inspection.Result == QCResult.PASS`:
  ```csharp
  var holdBalances = await _context.StockBalances
      .Where(sb => sb.LotId == inspection.LotId && sb.QtyOnHold > 0)
      .ToListAsync();

  foreach (var balance in holdBalances)
  {
      var qtyToRelease = balance.QtyOnHold;
      balance.QtyOnHold = 0m;
      balance.QtyAvailable += qtyToRelease;

      _context.StockTransactions.Add(new StockTransaction
      {
          Type = TransactionType.Release,
          ProductId = lot.ProductId,
          LotId = lot.Id,
          LocationId = balance.LocationId,
          Qty = qtyToRelease,
          QtyAfter = balance.QtyAvailable,
          ValuationRate = lot.UnitPrice,
          TransactionDate = DateTime.UtcNow,
          UserId = userId,
          ReferenceNo = $"QC-PASS-{inspection.Id}"
      });
  }
  ```

- [ ] **Step 3: Commit**
  Run: `git add Services/QcService.cs`
  Run: `git commit -m "feat: enhance QcService with automatic min/max range evaluation and PASS stock release"`

---

### Task 3: Viết Unit Tests Cho Phân hệ Kiểm soát Chất lượng

**Files:**
- Modify: [QcServiceTests.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/WmsMes.Tests/QcServiceTests.cs) (hoặc tạo mới)

**Interfaces:**
- Consumes: `QcService`.
- Produces: Kiểm thử tự động PASS.

- [ ] **Step 1: Viết test EvaluateMinMaxRange_AutoSetsIsOK**
  Tạo bài test kiểm tra việc nhập thông số thực tế sẽ tự động đánh giá PASS/FAIL theo dải Min/Max.

- [ ] **Step 2: Viết test SubmitInspection_Pass_ReleasesQtyOnHoldToAvailable**
  Tạo bài test kiểm tra khi nộp phiếu QC PASS, `QtyOnHold` chuyển thành `QtyAvailable`.

- [ ] **Step 3: Chạy Unit Tests**
  Run: `dotnet test`
  Expected: PASS tất cả bài test.

- [ ] **Step 4: Commit**
  Run: `git add WmsMes.Tests/`
  Run: `git commit -m "test: add unit tests for QC automatic range evaluation and stock release"`

---

### Task 4: Xây dựng Controllers Quản lý Mẫu & Thực thi Kiểm định

**Files:**
- Create: [QcChecklistController.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Controllers/QcChecklistController.cs)
- Modify: [QcController.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Controllers/QcController.cs)

**Interfaces:**
- Consumes: Yêu cầu quản lý mẫu tiêu chí và thực thi kiểm định QC.
- Produces: Các endpoint MVC.

- [ ] **Step 1: Tạo QcChecklistController.cs**
  Xây dựng CRUD cho Mẫu tiêu chuẩn QC (`Index`, `Create`, `Edit`).

- [ ] **Step 2: Cập nhật QcController.cs**
  Bổ sung action `Pending` (danh sách lô hàng chờ QC) và `CreateInspection` (form kiểm định).

- [ ] **Step 3: Commit**
  Run: `git add Controllers/`
  Run: `git commit -m "feat: add QcChecklistController and update QcController"`

---

### Task 5: Thiết kế Giao diện Views & Tích hợp Menu Layout

**Files:**
- Create Views: `QcChecklist/Index.cshtml`, `Create.cshtml`, `Edit.cshtml`
- Create Views: `Qc/Pending.cshtml`, `CreateInspection.cshtml`, `Details.cshtml`
- Modify: [Views/Shared/_Layout.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/Shared/_Layout.cshtml)

**Interfaces:**
- Consumes: Bootstrap 5 & Javascript.
- Produces: Màn hình giao diện QC chuyên nghiệp.

- [ ] **Step 1: Tạo các View Mẫu Checklist & Thực thi Kiểm định**
  Tạo các View giao diện HTML hỗ trợ hiển thị dải Min/Max và nhập liệu nhanh số liệu đo.

- [ ] **Step 2: Thêm Menu vào Sidebar Layout**
  Thêm các liên kết "Mẫu tiêu chuẩn QC" và "Đợt kiểm định QC" vào Sidebar của `_Layout.cshtml`.

- [ ] **Step 3: Kiểm tra biên dịch dự án**
  Run: `dotnet build`
  Expected: Build thành công không lỗi.

- [ ] **Step 4: Commit**
  Run: `git add Views/`
  Run: `git commit -m "feat: complete UI views for quality control module"`
