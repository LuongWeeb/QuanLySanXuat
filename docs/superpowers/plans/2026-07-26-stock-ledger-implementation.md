# Sổ cái Kho & Quy trình Chứng từ (Stock Ledger Foundation) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Chuyển đổi hệ thống quản lý kho sang cơ chế dựa trên Sổ cái Kho (Stock Ledger Entry), cho phép lưu vết lịch sử số dư chính xác và hỗ trợ quy trình Hủy phiếu kho (Receipt/Issue Cancellation) đảo ngược giao dịch an toàn.

**Architecture:** Ghi nhận mọi giao dịch kho dưới dạng các dòng Sổ cái Kho (`StockTransaction`) không thể xóa (Immutable), cập nhật đồng thời số dư trên bảng `StockBalance` bằng cơ chế Lock đồng bộ trong cùng một Database Transaction. Khi hủy phiếu, sinh các dòng sổ cái đối ứng đảo chiều và hoàn trả lại số dư.

**Tech Stack:** ASP.NET Core 8 MVC, Entity Framework Core 8, C#, SQL Server, xUnit.

## Global Constraints
- Target Framework: `.NET 8` (`net8.0`).
- Không làm gãy bất kỳ kiểm thử hiện có nào trong `WmsMes.Tests` khi chưa thực hiện chuyển đổi.
- Đảm bảo các giao dịch kho và sản xuất luôn chạy trong Database Transaction.
- Không cho phép tồn kho âm ở bất kỳ vị trí và lô hàng nào.

---

### Task 1: Cập nhật Cấu trúc Dữ liệu & Tạo Migration

**Files:**
- Modify: [StockTransaction.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Entities/StockTransaction.cs)
- Modify: [DocumentStatus.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Enums/DocumentStatus.cs)
- Modify: [CommonExtensions.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Common/CommonExtensions.cs)

**Interfaces:**
- Consumes: Cấu trúc hiện có của `StockTransaction` và `DocumentStatus`.
- Produces: Cột mới `QtyAfter`, `ValuationRate`, `IsCancelled` trong database và trạng thái `DocumentStatus.Cancelled`.

- [ ] **Step 1: Cập nhật StockTransaction.cs**
  Thêm các thuộc tính mới vào `Domain/Entities/StockTransaction.cs`:
  ```csharp
  [Column(TypeName = "decimal(18,2)")]
  public decimal QtyAfter { get; set; }

  [Column(TypeName = "decimal(18,2)")]
  public decimal ValuationRate { get; set; }

  public bool IsCancelled { get; set; } = false;
  ```

- [ ] **Step 2: Cập nhật DocumentStatus.cs**
  Bổ sung trạng thái `Cancelled` vào `Domain/Enums/DocumentStatus.cs`:
  ```csharp
  namespace WmsMes.Web.Domain.Enums;

  public enum DocumentStatus
  {
      Draft = 0,
      Completed = 1,
      Cancelled = 2
  }
  ```

- [ ] **Step 3: Cập nhật CommonExtensions.cs**
  Bổ sung bản dịch trạng thái `Cancelled` trong `Domain/Common/CommonExtensions.cs`:
  ```csharp
  public static string ToVietnameseString(this DocumentStatus status) => status switch
  {
      DocumentStatus.Draft => "Nháp",
      DocumentStatus.Completed => "Đã hoàn thành",
      DocumentStatus.Cancelled => "Đã hủy",
      _ => status.ToString()
  };
  ```

- [ ] **Step 4: Tạo EF Core Migration và cập nhật Cơ sở dữ liệu**
  Run command: `dotnet ef migrations add AddStockLedgerFields`
  Expected: Tạo migration thành công.
  Run command: `dotnet ef database update`
  Expected: Cập nhật schema cơ sở dữ liệu thành công.

- [ ] **Step 5: Ghi nhận thay đổi**
  Run: `git add Domain/Entities/StockTransaction.cs Domain/Enums/DocumentStatus.cs Domain/Common/CommonExtensions.cs Data/Migrations/`
  Run: `git commit -m "feat: add stock ledger fields and document status cancelled"`

---

### Task 2: Cập nhật Posting Engine trong Dịch vụ Kho

**Files:**
- Modify: [InventoryService.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Services/InventoryService.cs)

**Interfaces:**
- Consumes: `CompleteGoodsReceiptCoreAsync` và `CompleteGoodsIssueCoreAsync`.
- Produces: Việc ghi sổ cái `StockTransaction` lưu trữ `QtyAfter` và `ValuationRate` chính xác theo từng lô hàng/vị trí.

- [ ] **Step 1: Cập nhật logic ghi sổ Nhập kho trong CompleteGoodsReceiptCoreAsync**
  Cập nhật dòng từ line 196 đến line 232 trong `Services/InventoryService.cs`:
  ```csharp
  var balance = await _context.StockBalances
      .FirstOrDefaultAsync(sb =>
          sb.ProductId == line.ProductId &&
          sb.LotId == lot.Id &&
          sb.LocationId == line.LocationId);

  if (balance is null)
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

  await _context.StockTransactions.AddAsync(new StockTransaction
  {
      Type = TransactionType.Receipt,
      ProductId = line.ProductId,
      LotId = lot.Id,
      LocationId = line.LocationId,
      Qty = line.Qty,
      QtyAfter = balance.QtyAvailable,
      ValuationRate = lot.UnitPrice,
      IsCancelled = false,
      TransactionDate = DateTime.UtcNow,
      UserId = userId,
      ReferenceNo = receipt.ReceiptNo
  });
  ```

- [ ] **Step 2: Cập nhật logic ghi sổ Xuất kho trong CompleteGoodsIssueCoreAsync**
  Cập nhật logic tạo `StockTransaction` khi xuất kho trong `CompleteGoodsIssueCoreAsync` của `Services/InventoryService.cs`:
  ```csharp
  // Sau khi trừ balance.QtyAvailable
  var lot = await _context.Lots.FindAsync(line.LotId);
  var valuationRate = lot?.UnitPrice ?? 0m;

  await _context.StockTransactions.AddAsync(new StockTransaction
  {
      Type = TransactionType.Issue,
      ProductId = line.ProductId,
      LotId = line.LotId,
      LocationId = line.LocationId,
      Qty = -line.Qty, // Lượng xuất ghi âm trên sổ cái
      QtyAfter = balance.QtyAvailable,
      ValuationRate = valuationRate,
      IsCancelled = false,
      TransactionDate = DateTime.UtcNow,
      UserId = userId,
      ReferenceNo = issue.IssueNo
  });
  ```

- [ ] **Step 3: Kiểm thử lại logic hiện có**
  Run: `dotnet test`
  Expected: Tất cả các bài kiểm thử biên dịch và chạy thành công mà không bị hỏng cấu trúc.

- [ ] **Step 4: Commit**
  Run: `git add Services/InventoryService.cs`
  Run: `git commit -m "feat: implement running balance and valuation rate in stock transactions"`

---

### Task 3: Phát triển API Nghiệp vụ Hủy phiếu

**Files:**
- Modify: [IInventoryService.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Services/IInventoryService.cs)
- Modify: [InventoryService.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Services/InventoryService.cs)

**Interfaces:**
- Consumes: `receiptId`, `issueId`, `userId`.
- Produces: Hàm `CancelGoodsReceiptAsync(int receiptId, string userId)` và `CancelGoodsIssueAsync(int issueId, string userId)` trả về `Task<bool>`.

- [ ] **Step 1: Định nghĩa phương thức trong IInventoryService.cs**
  Thêm vào `Services/IInventoryService.cs`:
  ```csharp
  Task<bool> CancelGoodsReceiptAsync(int receiptId, string userId);
  Task<bool> CancelGoodsIssueAsync(int issueId, string userId);
  ```

- [ ] **Step 2: Triển khai CancelGoodsReceiptAsync trong InventoryService.cs**
  Thêm logic kiểm tra tồn kho âm và tạo bút toán đảo vào `Services/InventoryService.cs`:
  ```csharp
  public async Task<bool> CancelGoodsReceiptAsync(int receiptId, string userId)
  {
      await using var transaction = await BeginTransactionIfRelationalAsync();
      try
      {
          var receipt = await _context.GoodsReceipts
              .Include(r => r.Lines)
              .FirstOrDefaultAsync(r => r.Id == receiptId);

          if (receipt is null || receipt.Status != DocumentStatus.Completed)
          {
              return false;
          }

          foreach (var line in receipt.Lines)
          {
              var balance = await _context.StockBalances
                  .FirstOrDefaultAsync(sb =>
                      sb.ProductId == line.ProductId &&
                      sb.LocationId == line.LocationId); // Tìm theo Product và Location để kiểm soát Lot

              if (balance is null || balance.QtyAvailable < line.Qty)
              {
                  throw new InvalidOperationException($"Không thể hủy phiếu nhập. Số lượng khả dụng hiện tại ở vị trí đã chọn không đủ để trừ hoàn lại (Cần {line.Qty}, Hiện có {balance?.QtyAvailable ?? 0m}).");
              }

              balance.QtyAvailable -= line.Qty;

              var lot = await _context.Lots.FirstOrDefaultAsync(l => l.LotNo == line.LotNo && l.ProductId == line.ProductId);
              if (lot is not null)
              {
                  lot.Qty = Math.Max(0m, lot.Qty - line.Qty);
              }

              await _context.StockTransactions.AddAsync(new StockTransaction
              {
                  Type = TransactionType.Receipt,
                  ProductId = line.ProductId,
                  LotId = lot?.Id ?? 0,
                  LocationId = line.LocationId,
                  Qty = -line.Qty, // Lượng âm đảo ngược
                  QtyAfter = balance.QtyAvailable,
                  ValuationRate = lot?.UnitPrice ?? line.UnitPrice,
                  IsCancelled = true,
                  TransactionDate = DateTime.UtcNow,
                  UserId = userId,
                  ReferenceNo = receipt.ReceiptNo
              });
          }

          receipt.Status = DocumentStatus.Cancelled;
          await _context.SaveChangesAsync();
          await CommitIfRelationalAsync(transaction);
          return true;
      }
      catch
      {
          await RollbackIfRelationalAsync(transaction);
          throw;
      }
  }
  ```

- [ ] **Step 3: Triển khai CancelGoodsIssueAsync trong InventoryService.cs**
  Thêm vào `Services/InventoryService.cs`:
  ```csharp
  public async Task<bool> CancelGoodsIssueAsync(int issueId, string userId)
  {
      await using var transaction = await BeginTransactionIfRelationalAsync();
      try
      {
          var issue = await _context.GoodsIssues
              .Include(i => i.Lines)
              .FirstOrDefaultAsync(i => i.Id == issueId);

          if (issue is null || issue.Status != DocumentStatus.Completed)
          {
              return false;
          }

          foreach (var line in issue.Lines)
          {
              var balance = await _context.StockBalances
                  .FirstOrDefaultAsync(sb =>
                      sb.ProductId == line.ProductId &&
                      sb.LotId == line.LotId &&
                      sb.LocationId == line.LocationId);

              if (balance is null)
              {
                  balance = new StockBalance
                  {
                      ProductId = line.ProductId,
                      LotId = line.LotId,
                      LocationId = line.LocationId,
                      QtyAvailable = 0,
                      QtyReserved = 0,
                      QtyOnHold = 0
                  };
                  await _context.StockBalances.AddAsync(balance);
              }

              balance.QtyAvailable += line.Qty;

              var lot = await _context.Lots.FindAsync(line.LotId);

              await _context.StockTransactions.AddAsync(new StockTransaction
              {
                  Type = TransactionType.Issue,
                  ProductId = line.ProductId,
                  LotId = line.LotId,
                  LocationId = line.LocationId,
                  Qty = line.Qty, // Lượng dương đảo ngược lượng xuất âm ban đầu
                  QtyAfter = balance.QtyAvailable,
                  ValuationRate = lot?.UnitPrice ?? 0m,
                  IsCancelled = true,
                  TransactionDate = DateTime.UtcNow,
                  UserId = userId,
                  ReferenceNo = issue.IssueNo
              });
          }

          issue.Status = DocumentStatus.Cancelled;
          await _context.SaveChangesAsync();
          await CommitIfRelationalAsync(transaction);
          return true;
      }
      catch
      {
          await RollbackIfRelationalAsync(transaction);
          throw;
      }
  }
  ```

- [ ] **Step 4: Commit**
  Run: `git add Services/IInventoryService.cs Services/InventoryService.cs`
  Run: `git commit -m "feat: add goods receipt and goods issue cancellation services"`

---

### Task 4: Viết Unit Tests Cho Quy trình Hủy phiếu

**Files:**
- Modify: [InventoryServiceTests.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/WmsMes.Tests/InventoryServiceTests.cs)

**Interfaces:**
- Consumes: Dịch vụ `InventoryService`.
- Produces: Kiểm thử tự động chạy và kiểm chứng đúng sai của logic hủy phiếu.

- [ ] **Step 1: Thêm test CancelGoodsReceipt_Success**
  Thêm bài kiểm thử hủy phiếu nhập kho thành công trong `WmsMes.Tests/InventoryServiceTests.cs`:
  ```csharp
  [Fact]
  public async Task CancelGoodsReceipt_Success_ReversesStockAndWritesTransaction()
  {
      // Arrange
      var options = new DbContextOptionsBuilder<ApplicationDbContext>()
          .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
          .Options;

      using (var context = new ApplicationDbContext(options))
      {
          context.Products.Add(new Product { Id = 1, Code = "PROD-01", Name = "Product 1", IsActive = true });
          context.Locations.Add(new Location { Id = 1, Code = "LOC-01", IsActive = true });
          context.Suppliers.Add(new Supplier { Id = 1, Name = "Supplier 1", IsActive = true });
          context.GoodsReceipts.Add(new GoodsReceipt
          {
              Id = 1,
              ReceiptNo = "GR-100",
              SupplierId = 1,
              Status = DocumentStatus.Draft,
              Lines = new List<GoodsReceiptLine>
              {
                  new() { ProductId = 1, LotNo = "LOT-100", Qty = 50, UnitPrice = 10, LocationId = 1 }
              }
          });
          await context.SaveChangesAsync();
      }

      using (var context = new ApplicationDbContext(options))
      {
          var service = new InventoryService(context);
          await service.CompleteGoodsReceiptAsync(1, "user-1");
      }

      // Act
      using (var context = new ApplicationDbContext(options))
      {
          var service = new InventoryService(context);
          var result = await service.CancelGoodsReceiptAsync(1, "user-1");
          Assert.True(result);
      }

      // Assert
      using (var context = new ApplicationDbContext(options))
      {
          var receipt = await context.GoodsReceipts.FindAsync(1);
          Assert.Equal(DocumentStatus.Cancelled, receipt!.Status);

          var balance = await context.StockBalances.FirstOrDefaultAsync(b => b.ProductId == 1 && b.LocationId == 1);
          Assert.Equal(0, balance!.QtyAvailable);

          var txs = await context.StockTransactions.OrderBy(t => t.Id).ToListAsync();
          Assert.Equal(2, txs.Count);
          Assert.True(txs[1].IsCancelled);
          Assert.Equal(-50m, txs[1].Qty);
          Assert.Equal(0m, txs[1].QtyAfter);
      }
  }
  ```

- [ ] **Step 2: Thêm test CancelGoodsReceipt_NegativeStockBlocked**
  Thêm bài kiểm thử chặn hủy khi không đủ hàng:
  ```csharp
  [Fact]
  public async Task CancelGoodsReceipt_InsufficientQty_ThrowsException()
  {
      // Arrange
      var options = new DbContextOptionsBuilder<ApplicationDbContext>()
          .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
          .Options;

      using (var context = new ApplicationDbContext(options))
      {
          context.Products.Add(new Product { Id = 1, Code = "PROD-01", Name = "Product 1", IsActive = true });
          context.Locations.Add(new Location { Id = 1, Code = "LOC-01", IsActive = true });
          context.Suppliers.Add(new Supplier { Id = 1, Name = "Supplier 1", IsActive = true });
          context.GoodsReceipts.Add(new GoodsReceipt
          {
              Id = 1,
              ReceiptNo = "GR-100",
              SupplierId = 1,
              Status = DocumentStatus.Draft,
              Lines = new List<GoodsReceiptLine>
              {
                  new() { ProductId = 1, LotNo = "LOT-100", Qty = 50, UnitPrice = 10, LocationId = 1 }
              }
          });
          await context.SaveChangesAsync();
      }

      using (var context = new ApplicationDbContext(options))
      {
          var service = new InventoryService(context);
          await service.CompleteGoodsReceiptAsync(1, "user-1");
      }

      // Giảm hàng đi thủ công để mô phỏng đã xuất đi
      using (var context = new ApplicationDbContext(options))
      {
          var balance = await context.StockBalances.FirstAsync();
          balance.QtyAvailable = 20; // Chỉ còn 20, không đủ để trừ 50 khi hủy
          await context.SaveChangesAsync();
      }

      // Act & Assert
      using (var context = new ApplicationDbContext(options))
      {
          var service = new InventoryService(context);
          await Assert.ThrowsAsync<InvalidOperationException>(() => service.CancelGoodsReceiptAsync(1, "user-1"));
      }
  }
  ```

- [ ] **Step 3: Chạy Unit Tests**
  Run: `dotnet test`
  Expected: PASS tất cả các bài test (bao gồm cả test mới).

- [ ] **Step 4: Commit**
  Run: `git add WmsMes.Tests/InventoryServiceTests.cs`
  Run: `git commit -m "test: add cancellation tests for goods receipts"`

---

### Task 5: Tích hợp API và Giao diện UI Hủy phiếu

**Files:**
- Modify: [InventoryController.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Controllers/InventoryController.cs)
- Modify: [Receipts.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/Inventory/Receipts.cshtml)
- Modify: [Issues.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/Inventory/Issues.cshtml)
- Modify: [Transactions.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/Inventory/Transactions.cshtml)

**Interfaces:**
- Consumes: Điểm cuối HTTP POST trên `InventoryController`.
- Produces: Nút hủy trên giao diện danh sách phiếu kho và hiển thị thông tin số dư lũy kế trên sổ cái.

- [ ] **Step 1: Thêm Action CancelReceipt và CancelIssue vào Controller**
  Thêm các API xử lý trong `Controllers/InventoryController.cs`:
  ```csharp
  [HttpPost]
  [ValidateAntiForgeryToken]
  [Authorize(Roles = "Admin,Warehouse,Manager")]
  public async Task<IActionResult> CancelReceipt(int id)
  {
      if (_inventoryService is null)
      {
          throw new InvalidOperationException("IInventoryService is required.");
      }

      var userId = HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
      try
      {
          var success = await _inventoryService.CancelGoodsReceiptAsync(id, userId ?? "system");
          if (success)
          {
              TempData["StatusMessage"] = "Đã hủy phiếu nhập kho và hoàn trả số dư thành công.";
          }
          else
          {
              TempData["ErrorMessage"] = "Không thể hủy phiếu nhập kho.";
          }
      }
      catch (Exception ex)
      {
          TempData["ErrorMessage"] = ex.Message;
      }

      return RedirectToAction(nameof(Receipts));
  }

  [HttpPost]
  [ValidateAntiForgeryToken]
  [Authorize(Roles = "Admin,Warehouse,Manager")]
  public async Task<IActionResult> CancelIssue(int id)
  {
      if (_inventoryService is null)
      {
          throw new InvalidOperationException("IInventoryService is required.");
      }

      var userId = HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
      try
      {
          var success = await _inventoryService.CancelGoodsIssueAsync(id, userId ?? "system");
          if (success)
          {
              TempData["StatusMessage"] = "Đã hủy phiếu xuất kho và thu hồi số dư thành công.";
          }
          else
          {
              TempData["ErrorMessage"] = "Không thể hủy phiếu xuất kho.";
          }
      }
      catch (Exception ex)
      {
          TempData["ErrorMessage"] = ex.Message;
      }

      return RedirectToAction(nameof(Issues));
  }
  ```

- [ ] **Step 2: Thêm Nút hủy trên Views/Inventory/Receipts.cshtml**
  Tìm đoạn hiển thị bảng chi tiết trong Receipts.cshtml và thêm nút Hủy dưới dạng form nhỏ bảo mật bằng AntiForgeryToken:
  ```html
  @if (receipt.Status == DocumentStatus.Completed)
  {
      <form asp-action="CancelReceipt" asp-route-id="@receipt.Id" method="post" onsubmit="return confirm('Bạn có chắc chắn muốn hủy phiếu nhập kho này? Số lượng tồn kho sẽ bị trừ hoàn lại.');" class="d-inline">
          <button type="submit" class="btn btn-sm btn-outline-danger">Hủy phiếu</button>
      </form>
  }
  else if (receipt.Status == DocumentStatus.Cancelled)
  {
      <span class="badge bg-danger">Đã hủy</span>
  }
  ```

- [ ] **Step 3: Thêm Nút hủy trên Views/Inventory/Issues.cshtml**
  Tương tự trong Issues.cshtml:
  ```html
  @if (issue.Status == DocumentStatus.Completed)
  {
      <form asp-action="CancelIssue" asp-route-id="@issue.Id" method="post" onsubmit="return confirm('Bạn có chắc chắn muốn hủy phiếu xuất kho này? Số lượng tồn kho sẽ được trả lại.');" class="d-inline">
          <button type="submit" class="btn btn-sm btn-outline-danger">Hủy phiếu</button>
      </form>
  }
  else if (issue.Status == DocumentStatus.Cancelled)
  {
      <span class="badge bg-danger">Đã hủy</span>
  }
  ```

- [ ] **Step 4: Cập nhật hiển thị sổ cái trên Views/Inventory/Transactions.cshtml**
  Thêm cột "Số dư sau GD" (`QtyAfter`) và "Đơn giá vốn" (`ValuationRate`) vào danh sách giao dịch trong `Transactions.cshtml`:
  ```html
  <thead>
      <tr>
          <!-- Các cột cũ -->
          <th>Số lượng thay đổi</th>
          <th>Số dư sau GD</th>
          <th>Đơn giá vốn</th>
          <th>Trạng thái</th>
      </tr>
  </thead>
  <tbody>
      @foreach (var tx in Model)
      {
          <tr class="@(tx.IsCancelled ? "text-muted text-decoration-line-through" : "")">
              <!-- Các cột cũ -->
              <td>@tx.Qty.ToVietnameseNumber()</td>
              <td>@tx.QtyAfter.ToVietnameseNumber()</td>
              <td>@tx.ValuationRate.ToVietnameseNumber() VNĐ</td>
              <td>
                  @if(tx.IsCancelled) { <span class="badge bg-danger">Đã hủy</span> }
                  else { <span class="badge bg-success">Hợp lệ</span> }
              </td>
          </tr>
      }
  </tbody>
  ```

- [ ] **Step 5: Chạy ứng dụng để xác nhận biên dịch**
  Run: `dotnet build`
  Expected: Build thành công không có lỗi biên dịch.

- [ ] **Step 6: Commit**
  Run: `git add Controllers/InventoryController.cs Views/Inventory/`
  Run: `git commit -m "feat: integrate cancellation actions and update transactions UI"`
