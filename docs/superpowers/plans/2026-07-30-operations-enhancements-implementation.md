# Operations Enhancements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bổ sung trường ghi nhận lý do chênh lệch Nhập/Xuất, triển khai Biên bản Kiểm kê Kho PDF (`/api/print/cyclecount/{id}`) có chữ ký 3 bên để truy xuất trách nhiệm thất thoát và xây dựng Widget Cảnh báo Tồn kho sắp hết kèm nút 1-click tự động tạo Yêu cầu Mua hàng (`PurchaseRequest`).

**Architecture:** Bổ sung các trường `VarianceReason` và `ReasonNote` vào DB. Nâng cấp `PrintController` kết xuất file PDF biên bản kiểm kê kho chuẩn QuestPDF. Thêm API tạo `PurchaseRequest` từ các sản phẩm dưới định mức `MinStock`.

**Tech Stack:** ASP.NET Core 8 MVC, EF Core 8, QuestPDF, Bootstrap 5, C#.

## Global Constraints
- Target Framework: `.NET 8` net8.0.
- Biên bản Kiểm kho PDF phải tuân thủ khổ giấy A4, có 3 khối chữ ký xác nhận trách nhiệm: Thủ kho đếm, Kiểm toán/QC, và Giám đốc/Trưởng kho duyệt.
- Số lượng mua đề xuất cho sản phẩm sắp hết = `MaxStock - Tổng_Tồn_Khả_Dụng`.

---

### Task 1: Cập nhật Thực thể & Migration Database

**Files:**
- Modify: [GoodsReceiptLine.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Entities/GoodsReceiptLine.cs)
- Modify: [GoodsIssueLine.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Entities/GoodsIssueLine.cs)
- Modify: [CycleCountItem.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Entities/CycleCountItem.cs)

**Interfaces:**
- Consumes: Cấu trúc cơ sở dữ liệu hiện có.
- Produces: Các cột `VarianceReason` trong `GoodsReceiptLines`/`GoodsIssueLines` và `ReasonNote` trong `CycleCountItems`.

- [ ] **Step 1: Cập nhật các thực thể**
  Trong `GoodsReceiptLine.cs` & `GoodsIssueLine.cs`:
  ```csharp
  [MaxLength(250)]
  public string? VarianceReason { get; set; }
  ```

  Trong `CycleCountItem.cs`:
  ```csharp
  [MaxLength(250)]
  public string? ReasonNote { get; set; }
  ```

- [ ] **Step 2: Chạy EF Core Migration**
  Run: `dotnet ef migrations add AddOperationsEnhancementsFields`
  Run: `dotnet ef database update`
  Expected: Cập nhật DB thành công.

- [ ] **Step 3: Commit**
  Run: `git add Domain/Entities/ Data/Migrations/`
  Run: `git commit -m "feat: add variance reason and cycle count reason note fields"`

---

### Task 2: In Biên bản Kiểm kho PDF & Phiếu Nhập/Xuất Kho

**Files:**
- Modify: [PrintController.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Controllers/PrintController.cs)

**Interfaces:**
- Consumes: API HTTP GET `/api/print/cyclecount/{id}`.
- Produces: File PDF Biên bản Kiểm kê Kho khổ A4 chuẩn QuestPDF.

- [ ] **Step 1: Triển khai endpoint In Biên bản Kiểm kho PDF**
  Thêm endpoint `cyclecount/{id}` vào `PrintController.cs`:
  ```csharp
  [HttpGet("cyclecount/{id}")]
  public async Task<IActionResult> PrintCycleCount(int id)
  {
      var order = await _context.CycleCountOrders
          .Include(c => c.Warehouse)
          .Include(c => c.Items).ThenInclude(i => i.Product)
          .Include(c => c.Items).ThenInclude(i => i.Location)
          .Include(c => c.Items).ThenInclude(i => i.Lot)
          .FirstOrDefaultAsync(c => c.Id == id);

      if (order == null) return NotFound("Đợt kiểm kê không tồn tại.");

      var document = Document.Create(container =>
      {
          container.Page(page =>
          {
              page.Size(PageSizes.A4);
              page.Margin(30);
              page.Header().Column(col =>
              {
                  col.Item().Text("BIÊN BẢN KIỂM KÊ VÀ ĐỐI CHIẾU TỒN KHO").FontSize(18).Bold().AlignCenter();
                  col.Item().Text($"Mã đợt đếm: {order.CountNumber} | Kho: {order.Warehouse?.Name} | Ngày đếm: {order.CreatedAt:dd/MM/yyyy HH:mm}").FontSize(9).AlignCenter();
              });

              page.Content().PaddingVertical(10).Table(table =>
              {
                  table.ColumnsDefinition(columns =>
                  {
                      columns.RelativeColumn(1); // SKU
                      columns.RelativeColumn(2); // Ten SP
                      columns.RelativeColumn(1); // Vi tri
                      columns.RelativeColumn(1); // Lot
                      columns.RelativeColumn(1); // Ton he thong
                      columns.RelativeColumn(1); // Dem thuc te
                      columns.RelativeColumn(1); // Chenh lech
                      columns.RelativeColumn(1.5f); // Ly do
                  });

                  table.Header(header =>
                  {
                      header.Cell().Text("Mã SKU").Bold();
                      header.Cell().Text("Sản phẩm").Bold();
                      header.Cell().Text("Vị trí").Bold();
                      header.Cell().Text("Số Lô").Bold();
                      header.Cell().Text("Hệ thống").Bold();
                      header.Cell().Text("Thực tế").Bold();
                      header.Cell().Text("Chênh lệch").Bold();
                      header.Cell().Text("Lý do thất thoát").Bold();
                  });

                  foreach (var item in order.Items)
                  {
                      table.Cell().Text(item.Product?.Code ?? "");
                      table.Cell().Text(item.Product?.Name ?? "");
                      table.Cell().Text(item.Location?.Code ?? "");
                      table.Cell().Text(item.Lot?.LotNo ?? "");
                      table.Cell().Text(item.SystemQty.ToString("N0"));
                      table.Cell().Text((item.CountedQty ?? item.SystemQty).ToString("N0"));
                      table.Cell().Text(item.VarianceQty.ToString("N0"));
                      table.Cell().Text(item.ReasonNote ?? "");
                  }
              });

              page.Footer().Row(row =>
              {
                  row.RelativeItem().Column(c => { c.Item().Text("Người kiểm đếm").Bold().AlignCenter(); c.Item().Text("(Ký, ghi rõ họ tên)").FontSize(8).AlignCenter(); });
                  row.RelativeItem().Column(c => { c.Item().Text("Kiểm toán / QC").Bold().AlignCenter(); c.Item().Text("(Ký, ghi rõ họ tên)").FontSize(8).AlignCenter(); });
                  row.RelativeItem().Column(c => { c.Item().Text("Trưởng kho / Quản lý").Bold().AlignCenter(); c.Item().Text("(Ký, ghi rõ họ tên)").FontSize(8).AlignCenter(); });
              });
          });
      });

      var pdfBytes = document.GeneratePdf();
      return File(pdfBytes, "application/pdf", $"Stocktake_Report_{order.CountNumber}.pdf");
  }
  ```

- [ ] **Step 2: Commit**
  Run: `git add Controllers/PrintController.cs`
  Run: `git commit -m "feat: add stocktake audit report PDF print endpoint"`

---

### Task 3: Cảnh báo Tồn kho Thấp & 1-Click Tạo Yêu cầu Mua hàng

**Files:**
- Modify: [PurchaseOrderController.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Controllers/PurchaseOrderController.cs)
- Modify: [Views/Dashboard/Index.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/Dashboard/Index.cshtml)
- Modify: [Views/Inventory/Index.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/Inventory/Index.cshtml)

**Interfaces:**
- Consumes: Thông tin tồn kho thực tế và `Product.MinStock`.
- Produces: Widget cảnh báo và endpoint tự động tạo PR từ tồn kho thấp.

- [ ] **Step 1: Viết Action CreateRequestFromLowStock trong PurchaseOrderController.cs**
  ```csharp
  [HttpPost]
  [ValidateAntiForgeryToken]
  public async Task<IActionResult> CreateRequestFromLowStock()
  {
      var products = await _context.Products.Where(p => p.IsActive && p.MinStock > 0).ToListAsync();
      var itemsToRequest = new List<PurchaseRequestItem>();

      foreach (var product in products)
      {
          var currentStock = await _context.StockBalances
              .Where(sb => sb.ProductId == product.Id)
              .SumAsync(sb => sb.QtyAvailable);

          if (currentStock < product.MinStock)
          {
              var neededQty = (product.MaxStock > product.MinStock ? product.MaxStock : product.MinStock * 2) - currentStock;
              itemsToRequest.Add(new PurchaseRequestItem
              {
                  ProductId = product.Id,
                  Qty = Math.Max(1m, neededQty)
              });
          }
      }

      if (itemsToRequest.Any())
      {
          var pr = new PurchaseRequest
          {
              RequestNo = $"PR-LOWSTOCK-{DateTime.UtcNow:yyyyMMddHHmmss}",
              RequestDate = DateTime.UtcNow,
              RequiredDate = DateTime.UtcNow.AddDays(3),
              Status = DocumentStatus.Draft,
              Items = itemsToRequest
          };

          _context.PurchaseRequests.Add(pr);
          await _context.SaveChangesAsync();
          TempData["StatusMessage"] = $"Đã tự động tạo Yêu cầu Mua hàng mã {pr.RequestNo} cho các sản phẩm dưới định mức tồn tối thiểu.";
      }
      else
      {
          TempData["StatusMessage"] = "Hiện tại không có sản phẩm nào bị tồn kho dưới mức tối thiểu.";
      }

      return RedirectToAction("Requests");
  }
  ```

- [ ] **Step 2: Thêm Widget Cảnh báo trên Views**
  Hiển thị banner cảnh báo sản phẩm tồn kho < MinStock trên `Views/Dashboard/Index.cshtml` và `Views/Inventory/Index.cshtml` cùng nút bấm "1-Click Tạo Yêu cầu Mua hàng (PR)".

- [ ] **Step 3: Commit**
  Run: `git add Controllers/ Views/`
  Run: `git commit -m "feat: add low-stock warning widget and 1-click purchase request creation"`

---

### Task 4: Viết Unit Tests Kiểm chứng

**Files:**
- Create: [OperationsEnhancementsTests.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/WmsMes.Tests/OperationsEnhancementsTests.cs)

**Interfaces:**
- Consumes: API In ấn PDF & CreateRequestFromLowStock.
- Produces: Kết quả test PASS.

- [ ] **Step 1: Viết test PrintStocktakePdf_ReturnsValidPdfBytes**
  Kiểm tra endpoint in PDF biên bản kiểm kho trả về luồng PDF hợp lệ.

- [ ] **Step 2: Viết test CreateRequestFromLowStock_CreatesPrForLowStockProducts**
  Kiểm tra tính đúng đắn của logic tính toán lượng mua đề xuất.

- [ ] **Step 3: Chạy Unit Tests**
  Run: `dotnet test WmsMes.Tests/WmsMes.Tests.csproj`
  Expected: PASS tất cả bài test.

- [ ] **Step 4: Commit**
  Run: `git add WmsMes.Tests/`
  Run: `git commit -m "test: add unit tests for stocktake pdf printing and low stock pr creation"`
