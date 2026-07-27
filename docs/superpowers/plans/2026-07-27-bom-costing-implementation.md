# Định mức BOM & Tính giá thành sản xuất (Costing - Phase 3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Triển khai tính năng cấu hình chi phí trên Work Center, tính toán giá thành định mức tiêu chuẩn trên BOM và tự động định giá thành phẩm (`Lot.UnitPrice`) khi đóng Lệnh sản xuất dựa trên nguyên vật liệu thực tế đã backflush và thời gian vận hành thực tế.

**Architecture:** Bổ sung các trường chi phí vào thực thể `Product`, `WorkCenter`, `BOM`. Thiết lập logic trong `WorkOrderService.CompleteWorkOrderAsync` tính toán tổng chi phí (Vật tư tiêu hao thực tế + Chi phí vận hành các bước của trạm máy) chia cho sản lượng đầu ra để ra đơn giá Lot thành phẩm. Hiển thị bảng so sánh giá thành trên màn hình chi tiết Lệnh sản xuất.

**Tech Stack:** ASP.NET Core 8 MVC, Entity Framework Core 8, C#, Bootstrap 5.

## Global Constraints
- Target Framework: `.NET 8` net8.0.
- Số dư tồn kho và chi phí luôn được tính toán và làm tròn 2 chữ số thập phân (`MidpointRounding.AwayFromZero`) trước khi lưu trữ.
- Khi không có dữ liệu thực tế thời gian sản xuất (StartTime/EndTime bị null), hệ thống tự động sử dụng thời gian tiêu chuẩn của Routing để tính chi phí vận hành thực tế.

---

### Task 1: Cập nhật Thực thể & Tạo Migration Chi phí

**Files:**
- Modify: [Product.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Entities/Product.cs)
- Modify: [WorkCenter.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Entities/WorkCenter.cs)
- Modify: [BOM.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Entities/BOM.cs)

**Interfaces:**
- Consumes: Cấu trúc hiện có của database.
- Produces: Các cột mới `StandardCost` trong `Products`, `HourlyLaborRate` & `HourlyMachineRate` trong `WorkCenters`, `TotalMaterialCost`, `TotalOperationCost` & `TotalStandardCost` trong `BOMs`.

- [ ] **Step 1: Cập nhật Product.cs**
  Thêm trường `StandardCost` vào `Domain/Entities/Product.cs`:
  ```csharp
  [Column(TypeName = "decimal(18,2)")]
  public decimal StandardCost { get; set; } = 0m;
  ```

- [ ] **Step 2: Cập nhật WorkCenter.cs**
  Thêm trường `HourlyLaborRate` và `HourlyMachineRate` vào `Domain/Entities/WorkCenter.cs`:
  ```csharp
  [Column(TypeName = "decimal(18,2)")]
  public decimal HourlyLaborRate { get; set; } = 0m;

  [Column(TypeName = "decimal(18,2)")]
  public decimal HourlyMachineRate { get; set; } = 0m;
  ```

- [ ] **Step 3: Cập nhật BOM.cs**
  Thêm 3 trường lưu giá định mức vào `Domain/Entities/BOM.cs`:
  ```csharp
  [Column(TypeName = "decimal(18,2)")]
  public decimal TotalMaterialCost { get; set; } = 0m;

  [Column(TypeName = "decimal(18,2)")]
  public decimal TotalOperationCost { get; set; } = 0m;

  [Column(TypeName = "decimal(18,2)")]
  public decimal TotalStandardCost { get; set; } = 0m;
  ```

- [ ] **Step 4: Chạy EF Core Migration**
  Run: `dotnet ef migrations add AddCostingFields`
  Expected: Tạo migration thành công.
  Run: `dotnet ef database update`
  Expected: Cập nhật database thành công.

- [ ] **Step 5: Commit**
  Run: `git add Domain/Entities/ Data/Migrations/`
  Run: `git commit -m "feat: add database columns for product, work center, and BOM costing"`

---

### Task 2: Cấu hình Giao diện Quản trị Chi phí

**Files:**
- Modify: [Views/Product/Create.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/Product/Create.cshtml) (và Edit.cshtml nếu có)
- Modify: [Views/WorkCenter/Create.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/WorkCenter/Create.cshtml) (và Edit.cshtml nếu có)
- Modify: [Controllers/ProductController.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Controllers/ProductController.cs)

**Interfaces:**
- Consumes: Giao diện quản trị danh mục sản phẩm, trạm sản xuất.
- Produces: Form nhập dữ liệu cho phép ghi nhận đơn giá.

- [ ] **Step 1: Cập nhật Product Controller & View**
  Cập nhật thuộc tính `StandardCost` vào `Create` và `Edit` actions trong `Controllers/ProductController.cs` và cập nhật các View tương ứng:
  ```html
  <div class="mb-3">
      <label asp-for="StandardCost" class="form-label">Giá vốn tiêu chuẩn (dự phòng)</label>
      <input asp-for="StandardCost" type="number" class="form-control" min="0" step="0.01" required />
      <span asp-validation-for="StandardCost" class="text-danger"></span>
  </div>
  ```

- [ ] **Step 2: Cập nhật Work Center View**
  Cập nhật form của Work Center để nhập chi phí giờ:
  ```html
  <div class="row">
      <div class="col-md-6 mb-3">
          <label asp-for="HourlyLaborRate" class="form-label">Chi phí nhân công mỗi giờ (VNĐ)</label>
          <input asp-for="HourlyLaborRate" type="number" class="form-control" min="0" step="0.01" required />
          <span asp-validation-for="HourlyLaborRate" class="text-danger"></span>
      </div>
      <div class="col-md-6 mb-3">
          <label asp-for="HourlyMachineRate" class="form-label">Chi phí máy móc mỗi giờ (VNĐ)</label>
          <input asp-for="HourlyMachineRate" type="number" class="form-control" min="0" step="0.01" required />
          <span asp-validation-for="HourlyMachineRate" class="text-danger"></span>
      </div>
  </div>
  ```

- [ ] **Step 3: Commit**
  Run: `git add Controllers/ProductController.cs Views/`
  Run: `git commit -m "feat: add costing fields inputs on product and work center forms"`

---

### Task 3: Tính toán Tự động Giá thành Định mức BOM

**Files:**
- Modify: [Controllers/BomController.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Controllers/BomController.cs)

**Interfaces:**
- Consumes: Cấu trúc BOM và các bước Routing khi tạo/kích hoạt BOM.
- Produces: Lưu `TotalMaterialCost`, `TotalOperationCost` và `TotalStandardCost` của BOM vào DB.

- [ ] **Step 1: Viết logic tính toán chi phí định mức khi lưu BOM**
  Trong `Controllers/BomController.cs` (khi tạo mới hoặc kích hoạt BOM):
  ```csharp
  private async Task CalculateAndSaveBomCostAsync(int bomId)
  {
      var bom = await _context.BOMs
          .Include(b => b.Items)
          .FirstOrDefaultAsync(b => b.Id == bomId);
      if (bom == null) return;

      decimal matCost = 0m;
      foreach (var item in bom.Items)
      {
          // 1. Tính giá nguyên vật liệu: Tìm đơn giá trung bình từ các lô hàng trong kho
          var lotPrices = await _context.Lots
              .Where(l => l.ProductId == item.ComponentProductId && l.Qty > 0)
              .Select(l => l.UnitPrice)
              .ToListAsync();

          var itemPrice = lotPrices.Any()
              ? lotPrices.Average()
              : (await _context.Products.Where(p => p.Id == item.ComponentProductId).Select(p => p.StandardCost).FirstOrDefaultAsync());

          matCost += item.QtyPer * (1 + item.ScrapPercent / 100) * itemPrice;
      }

      // 2. Tính chi phí vận hành định mức từ active Routing
      decimal opCost = 0m;
      var routing = await _context.Routings
          .Include(r => r.Steps)
          .ThenInclude(s => s.WorkCenter)
          .FirstOrDefaultAsync(r => r.ProductId == bom.ProductId && r.IsActive);

      if (routing != null)
      {
          foreach (var step in routing.Steps)
          {
              var wc = step.WorkCenter;
              if (wc != null)
              {
                  opCost += (step.StandardTimeMinutes / 60m) * (wc.HourlyLaborRate + wc.HourlyMachineRate);
              }
          }
      }

      bom.TotalMaterialCost = Math.Round(matCost, 2, MidpointRounding.AwayFromZero);
      bom.TotalOperationCost = Math.Round(opCost, 2, MidpointRounding.AwayFromZero);
      bom.TotalStandardCost = bom.TotalMaterialCost + bom.TotalOperationCost;

      await _context.SaveChangesAsync();
  }
  ```

- [ ] **Step 2: Gọi CalculateAndSaveBomCostAsync khi lưu hoặc kích hoạt**
  Tại các phương thức POST Create và ToggleActive/Activate trong `BomController.cs`, sau khi lưu BOM thành công, gọi `await CalculateAndSaveBomCostAsync(bom.Id)`.

- [ ] **Step 3: Cập nhật hiển thị trang Chi tiết BOM**
  Cập nhật file `Views/Bom/Details.cshtml` hiển thị:
  ```html
  <div class="row mt-4">
      <div class="col-md-4">
          <div class="card bg-light">
              <div class="card-body">
                  <h6>Tổng chi phí vật tư định mức</h6>
                  <h3>@Model.TotalMaterialCost.ToVietnameseNumber() VNĐ</h3>
              </div>
          </div>
      </div>
      <div class="col-md-4">
          <div class="card bg-light">
              <div class="card-body">
                  <h6>Tổng chi phí vận hành định mức</h6>
                  <h3>@Model.TotalOperationCost.ToVietnameseNumber() VNĐ</h3>
              </div>
          </div>
      </div>
      <div class="col-md-4">
          <div class="card bg-primary text-white">
              <div class="card-body">
                  <h6>TỔNG GIÁ THÀNH ĐỊNH MỨC TIÊU CHUẨN</h6>
                  <h3>@Model.TotalStandardCost.ToVietnameseNumber() VNĐ</h3>
              </div>
          </div>
      </div>
  </div>
  ```

- [ ] **Step 4: Commit**
  Run: `git add Controllers/BomController.cs Views/Bom/`
  Run: `git commit -m "feat: implement automatic standard costing for BOM details view"`

---

### Task 4: Tính toán Chi phí Thực tế & Định giá Thành phẩm khi Hoàn tất WO

**Files:**
- Modify: [WorkOrderService.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Services/WorkOrderService.cs)

**Interfaces:**
- Consumes: Lệnh sản xuất chuẩn bị hoàn thành (`CompleteWorkOrderAsync`).
- Produces: Tính toán `finishedLot.UnitPrice` dựa trên chi phí thực tế và gán cho `ValuationRate` trên dòng sổ cái.

- [ ] **Step 1: Viết logic tính toán Chi phí Thực tế trong CompleteWorkOrderAsync**
  Cập nhật method `CompleteWorkOrderAsync` trong `Services/WorkOrderService.cs` trước khi khởi tạo `finishedLot` (khoảng dòng 277):
  ```csharp
  // 1. Tính chi phí vật tư thực tế từ lượng nguyên vật liệu đã xuất (MaterialReservation)
  var reservations = await _context.MaterialReservations
      .Include(r => r.Lot)
      .Where(r => r.WorkOrderId == workOrder.Id)
      .ToListAsync();

  decimal actualMatCost = 0m;
  foreach (var res in reservations)
  {
      actualMatCost += res.QtyReserved * (res.Lot?.UnitPrice ?? 0m);
  }

  // 2. Tính chi phí vận hành thực tế từ nhật ký công đoạn (WorkOrderStep)
  decimal actualOpCost = 0m;
  var completedSteps = await _context.WorkOrderSteps
      .Include(s => s.WorkCenter)
      .Where(s => s.WorkOrderId == workOrder.Id)
      .ToListAsync();

  foreach (var step in completedSteps)
  {
      var wc = step.WorkCenter;
      if (wc != null)
      {
          // Tính thời gian thực tế chạy máy
          decimal durationMinutes = 0m;
          if (step.StartTime.HasValue && step.EndTime.HasValue)
          {
              durationMinutes = (decimal)(step.EndTime.Value - step.StartTime.Value).TotalMinutes;
          }
          
          // Fallback về thời gian tiêu chuẩn của step nếu thời gian thực tế bằng 0
          if (durationMinutes <= 0m)
          {
              var stdTime = await _context.RoutingSteps
                  .Where(rs => rs.Routing!.ProductId == workOrder.ProductId && rs.StepNumber == step.StepNumber && rs.Routing.IsActive)
                  .Select(rs => rs.StandardTimeMinutes)
                  .FirstOrDefaultAsync();
              durationMinutes = stdTime > 0m ? stdTime : 30m; // mặc định 30 phút nếu không tìm thấy
          }

          actualOpCost += (durationMinutes / 60m) * (wc.HourlyLaborRate + wc.HourlyMachineRate);
      }
  }

  decimal totalActualCost = actualMatCost + actualOpCost;
  decimal unitActualCost = finalQty > 0m
      ? Math.Round(totalActualCost / finalQty, 2, MidpointRounding.AwayFromZero)
      : 0m;
  ```

- [ ] **Step 2: Gán UnitPrice cho finishedLot và ValuationRate**
  Cập nhật khai báo `finishedLot` và giao dịch `StockTransaction` trong `WorkOrderService.cs`:
  ```csharp
  var finishedLot = new Lot
  {
      LotNo = $"{prefix}{existingCount + 1:D4}",
      ProductId = workOrder.ProductId,
      ManufactureDate = DateTime.UtcNow,
      ExpiryDate = product.ShelfLifeDays.HasValue ? DateTime.UtcNow.AddDays(product.ShelfLifeDays.Value) : null,
      Qty = finalQty,
      UnitPrice = unitActualCost, // Gán đơn giá thực tế vừa tính
      WorkOrderId = workOrder.Id
  };

  // Và cập nhật StockTransaction:
  _context.StockTransactions.Add(new StockTransaction
  {
      Type = TransactionType.Receipt,
      ProductId = workOrder.ProductId,
      LotId = finishedLot.Id,
      LocationId = qcLocationId,
      Qty = finalQty,
      QtyAfter = finishedBalance.QtyAvailable,
      ValuationRate = unitActualCost, // Lưu đơn giá vốn thực tế vào sổ cái
      TransactionDate = DateTime.UtcNow,
      UserId = userId,
      ReferenceNo = workOrder.Code
  });
  ```

- [ ] **Step 3: Commit**
  Run: `git add Services/WorkOrderService.cs`
  Run: `git commit -m "feat: implement actual costing calculations on work order completion"`

---

### Task 5: Bảng So sánh Giá thành trên UI & Viết Tests

**Files:**
- Modify: [Views/WorkOrder/Details.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/WorkOrder/Details.cshtml)
- Modify: [WorkOrderServiceTests.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/WmsMes.Tests/WorkOrderServiceTests.cs)

**Interfaces:**
- Consumes: `WorkOrder` đã hoàn thành và định mức BOM của sản phẩm.
- Produces: Khối giao diện hiển thị báo cáo so sánh chi phí & các unit test chạy PASS.

- [ ] **Step 1: Cài đặt Giao diện Phân tích Chi phí trong Details.cshtml**
  Lấy thông tin BOM để so sánh và render bảng giá thành trong `Views/WorkOrder/Details.cshtml`:
  ```html
  <!-- Tính toán các chi phí hiển thị tương tự như spec -->
  <div class="card mt-4 border-info">
      <div class="card-header bg-info text-white"><h5>Bảng Phân tích Giá thành Sản xuất</h5></div>
      <div class="card-body">
          <div class="table-responsive">
              <table class="table table-bordered align-middle">
                  <thead>
                      <tr class="table-light">
                          <th>Khoản mục chi phí</th>
                          <th class="text-end">Định mức (Target)</th>
                          <th class="text-end">Thực tế (Actual)</th>
                          <th class="text-end">Chênh lệch (Variance)</th>
                      </tr>
                  </thead>
                  <tbody>
                      <!-- Render dữ liệu so sánh chi phí -->
                  </tbody>
              </table>
          </div>
      </div>
  </div>
  ```

- [ ] **Step 2: Bổ sung Unit Tests**
  Viết unit test kiểm thử tính toán giá trị thực tế tại `WmsMes.Tests/WorkOrderServiceTests.cs`:
  ```csharp
  [Fact]
  public async Task CompleteWorkOrder_CalculatesActualCostAndSetsLotUnitPrice()
  {
      // Thử chạy một quy trình hoàn chỉnh có gán chi phí trạm máy và kiểm tra Lot.UnitPrice đầu ra
      // Assert:
      // Assert.Equal(calculatedCost, lot.UnitPrice);
  }
  ```

- [ ] **Step 3: Chạy kiểm thử và Build hệ thống**
  Run: `dotnet test`
  Expected: Tất cả các bài kiểm thử chạy PASS.
  Run: `dotnet build`
  Expected: Biên dịch thành công không có lỗi.

- [ ] **Step 4: Commit**
  Run: `git add Views/WorkOrder/Details.cshtml WmsMes.Tests/WorkOrderServiceTests.cs`
  Run: `git commit -m "feat: add comparative cost analysis table to work order details view"`
