# Số hóa Giao dịch & Quét mã (Barcode/QR - Phase 2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Xây dựng tính năng kết xuất nhãn PDF QR Code (cho Lô hàng và Vị trí) và tích hợp cơ chế quét lai (máy quét phần cứng + camera webcam) trên các form giao dịch kho và trạm vận hành sản xuất.

**Architecture:** Bổ sung `PrintController` tạo tệp PDF nhãn 100x50mm từ `QuestPDF` và `QRCoder`. Sử dụng Javascript trên Frontend để nhận diện phím nhấn từ máy quét phần cứng hoặc thư viện `html5-qrcode` từ camera để tự động hóa các thao tác điền thông tin và focus dòng.

**Tech Stack:** ASP.NET Core 8 MVC, QuestPDF, QRCoder, Javascript, Bootstrap 5.

## Global Constraints
- Target Framework: `.NET 8` (`net8.0`).
- Nhãn in ra phải tuân thủ khổ tem tiêu chuẩn `100mm x 50mm`.
- Tích hợp quét camera phải hoạt động ổn định thông qua thư viện `html5-qrcode` tải từ CDN.
- Các hành động tự động trên Trạm vận hành phải kiểm soát trạng thái đúng luật.

---

### Task 1: Xây dựng PrintController & API In ấn Nhãn

**Files:**
- Create: [PrintController.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Controllers/PrintController.cs)

**Interfaces:**
- Consumes: `id` (của Location hoặc Lot).
- Produces: API HTTP GET `/api/print/location/{id}` và `/api/print/lot/{id}` trả về tệp PDF (`application/pdf`).

- [ ] **Step 1: Khởi tạo PrintController.cs**
  Tạo tệp mới `Controllers/PrintController.cs` và khai báo cấu trúc cơ bản:
  ```csharp
  using Microsoft.AspNetCore.Authorization;
  using Microsoft.AspNetCore.Mvc;
  using Microsoft.EntityFrameworkCore;
  using QRCoder;
  using QuestPDF.Fluent;
  using QuestPDF.Helpers;
  using QuestPDF.Infrastructure;
  using WmsMes.Web.Data;

  namespace WmsMes.Web.Controllers;

  [Authorize]
  [Route("api/[controller]")]
  [ApiController]
  public class PrintController : ControllerBase
  {
      private readonly ApplicationDbContext _context;

      public PrintController(ApplicationDbContext context)
      {
          _context = context;
      }
  }
  ```

- [ ] **Step 2: Triển khai API in nhãn Vị trí kho**
  Thêm endpoint `location/{id}` vào `PrintController.cs`. Nhãn hiển thị mã QR cùng tên Warehouse/Zone và mã Vị trí in to:
  ```csharp
  [HttpGet("location/{id}")]
  public async Task<IActionResult> PrintLocation(int id)
  {
      var location = await _context.Locations
          .Include(l => l.Zone)
          .ThenInclude(z => z!.Warehouse)
          .FirstOrDefaultAsync(l => l.Id == id);

      if (location is null)
      {
          return NotFound("Vị trí không tồn tại.");
      }

      using var qrGenerator = new QRCodeGenerator();
      using var qrCodeData = qrGenerator.CreateQrCode(location.Code, QRCodeGenerator.ECCLevel.Q);
      using var qrCode = new PngByteQRCode(qrCodeData);
      var qrCodeBytes = qrCode.GetGraphic(20);

      var document = Document.Create(container =>
      {
          container.Page(page =>
          {
              page.Size(new PageSize(283, 141)); // Khổ 100mm x 50mm quy đổi sang points (72 points = 1 inch)
              page.Margin(10);
              page.Content().Row(row =>
              {
                  row.RelativeItem(1).AlignCenter().Image(qrCodeBytes);
                  row.RelativeItem(1.2f).Column(col =>
                  {
                      col.Item().Text(location.Zone?.Warehouse?.Name ?? "WMS WAREHOUSE").FontSize(8).Bold();
                      col.Item().Text($"Khu vực: {location.Zone?.Code}").FontSize(8);
                      col.Item().Spacing(5);
                      col.Item().Text(location.Code).FontSize(16).Bold().FontColor(Colors.Blue.Darken4);
                  });
              });
          });
      });

      var pdfBytes = document.GeneratePdf();
      return File(pdfBytes, "application/pdf", $"Label_Loc_{location.Code}.pdf");
  }
  ```

- [ ] **Step 3: Triển khai API in nhãn Lô hàng**
  Thêm endpoint `lot/{id}` vào `PrintController.cs`. Nhãn hiển thị mã QR số Lot, SKU sản phẩm, tên sản phẩm và các ngày hạn dùng:
  ```csharp
  [HttpGet("lot/{id}")]
  public async Task<IActionResult> PrintLot(int id)
  {
      var lot = await _context.Lots
          .Include(l => l.Product)
          .FirstOrDefaultAsync(l => l.Id == id);

      if (lot is null)
      {
          return NotFound("Lô hàng không tồn tại.");
      }

      using var qrGenerator = new QRCodeGenerator();
      using var qrCodeData = qrGenerator.CreateQrCode(lot.LotNo, QRCodeGenerator.ECCLevel.Q);
      using var qrCode = new PngByteQRCode(qrCodeData);
      var qrCodeBytes = qrCode.GetGraphic(20);

      var document = Document.Create(container =>
      {
          container.Page(page =>
          {
              page.Size(new PageSize(283, 141)); // Khổ 100mm x 50mm
              page.Margin(10);
              page.Content().Row(row =>
              {
                  row.RelativeItem(1).AlignCenter().Image(qrCodeBytes);
                  row.RelativeItem(1.2f).Column(col =>
                  {
                      col.Item().Text(lot.Product?.Name ?? "SẢN PHẨM").FontSize(8).Bold();
                      col.Item().Text($"SKU: {lot.Product?.Code}").FontSize(8);
                      col.Item().Text($"NSX: {lot.ManufactureDate?.ToString("dd/MM/yyyy") ?? "N/A"}").FontSize(7);
                      col.Item().Text($"HSD: {lot.ExpiryDate?.ToString("dd/MM/yyyy") ?? "N/A"}").FontSize(7);
                      col.Item().Spacing(3);
                      col.Item().Text(lot.LotNo).FontSize(14).Bold().FontColor(Colors.Green.Darken4);
                  });
              });
          });
      });

      var pdfBytes = document.GeneratePdf();
      return File(pdfBytes, "application/pdf", $"Label_Lot_{lot.LotNo}.pdf");
  }
  ```

- [ ] **Step 4: Commit**
  Run: `git add Controllers/PrintController.cs`
  Run: `git commit -m "feat: add PrintController with location and lot label printing API"`

---

### Task 2: Tích hợp nút In nhãn QR Code lên UI Sơ đồ kho

**Files:**
- Modify: [Warehouse/Index.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/Warehouse/Index.cshtml)

**Interfaces:**
- Consumes: API in nhãn `/api/print/location/{id}` và `/api/print/lot/{id}`.
- Produces: Nút in hiển thị trên Modal chi tiết kho.

- [ ] **Step 1: Thêm nút in nhãn Vị trí vào Header Modal**
  Mở `Views/Warehouse/Index.cshtml` tìm thẻ `h2` trong `<div class="modal-header">` (khoảng dòng 43-46) và cập nhật thêm nút In:
  ```html
  <div class="modal-header d-flex justify-content-between align-items-center w-100">
      <h2 class="modal-title fs-5" id="locationStockModalLabel-@location.Id">Tồn kho tại vị trí: @location.Code</h2>
      <div class="d-flex gap-2 align-items-center">
          <a href="/api/print/location/@location.Id" target="_blank" class="btn btn-sm btn-outline-primary" aria-label="In mã QR Vị trí">
              <i class="bi bi-printer"></i> In tem QR Vị trí
          </a>
          <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Đóng"></button>
      </div>
  </div>
  ```

- [ ] **Step 2: Thêm liên kết In nhãn Lot trên từng dòng tồn kho**
  Trong `Views/Warehouse/Index.cshtml`, tìm bảng hiển thị tồn kho và thêm cột thao tác in tem Lot:
  ```html
  <thead>
      <tr>
          <th>Mã SKU</th>
          <th>Tên sản phẩm</th>
          <th>Số lô</th>
          <th class="text-end">Khả dụng</th>
          <th class="text-end">Giữ chỗ</th>
          <th class="text-end">Tạm giữ</th>
          <th>In tem</th>
      </tr>
  </thead>
  <tbody>
  @foreach (var balance in locationBalances)
  {
      <tr>
          <td><code>@balance.Product?.Code</code></td>
          <td>@balance.Product?.Name</td>
          <td><code>@balance.Lot?.LotNo</code></td>
          <td class="text-end">@balance.QtyAvailable.ToVietnameseNumber()</td>
          <td class="text-end">@balance.QtyReserved.ToVietnameseNumber()</td>
          <td class="text-end">@balance.QtyOnHold.ToVietnameseNumber()</td>
          <td>
              <a href="/api/print/lot/@balance.LotId" target="_blank" class="btn btn-sm btn-link text-decoration-none py-0" title="In nhãn QR Lot">
                  <i class="bi bi-printer"></i> tem Lot
              </a>
          </td>
      </tr>
  }
  </tbody>
  ```

- [ ] **Step 3: Commit**
  Run: `git add Views/Warehouse/Index.cshtml`
  Run: `git commit -m "feat: add location and lot print label buttons to warehouse mapping page"`

---

### Task 3: Viết Kiểm thử tự động In tem nhãn

**Files:**
- Create: [PrintControllerTests.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/WmsMes.Tests/PrintControllerTests.cs)

**Interfaces:**
- Consumes: `PrintController`.
- Produces: Kiểm thử tự động bảo đảm PDF kết xuất thành công và có kiểu nội dung (ContentType) chính xác.

- [ ] **Step 1: Tạo file test PrintControllerTests.cs**
  Tạo tệp kiểm thử mới trong `WmsMes.Tests/PrintControllerTests.cs` kiểm tra việc in nhãn thành công:
  ```csharp
  using Microsoft.AspNetCore.Mvc;
  using Microsoft.EntityFrameworkCore;
  using WmsMes.Web.Controllers;
  using WmsMes.Web.Data;
  using WmsMes.Web.Domain.Entities;
  using Xunit;

  namespace WmsMes.Tests;

  public class PrintControllerTests
  {
      [Fact]
      public async Task PrintLocation_ReturnsPdfFileResult_WhenLocationExists()
      {
          // Arrange
          var options = new DbContextOptionsBuilder<ApplicationDbContext>()
              .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
              .Options;

          using (var context = new ApplicationDbContext(options))
          {
              var warehouse = new Warehouse { Code = "WH01", Name = "Kho A", IsActive = true };
              context.Warehouses.Add(warehouse);
              await context.SaveChangesAsync();

              var zone = new Zone { Code = "ZONE-01", Name = "Khu 1", WarehouseId = warehouse.Id };
              context.Zones.Add(zone);
              await context.SaveChangesAsync();

              context.Locations.Add(new Location { Id = 10, Code = "LOC-10", ZoneId = zone.Id, IsActive = true });
              await context.SaveChangesAsync();
          }

          // Act
          using (var context = new ApplicationDbContext(options))
          {
              var controller = new PrintController(context);
              var result = await controller.PrintLocation(10);

              // Assert
              var fileResult = Assert.IsType<FileContentResult>(result);
              Assert.Equal("application/pdf", fileResult.ContentType);
              Assert.True(fileResult.FileContents.Length > 0);
          }
      }
  }
  ```

- [ ] **Step 2: Chạy kiểm thử tự động**
  Run: `dotnet test`
  Expected: PASS tất cả các bài test bao gồm bài test in ấn mới.

- [ ] **Step 3: Commit**
  Run: `git add WmsMes.Tests/PrintControllerTests.cs`
  Run: `git commit -m "test: add unit tests for print label API endpoints"`

---

### Task 4: Tích hợp Giao diện Quét Lai (Hybrid Barcode Scanning)

**Files:**
- Modify: [Inventory/CreateReceipt.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/Inventory/CreateReceipt.cshtml)
- Modify: [Inventory/CreateIssue.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/Inventory/CreateIssue.cshtml)

**Interfaces:**
- Consumes: Máy quét vạch (bàn phím ảo), Thư viện `html5-qrcode` qua CDN.
- Produces: Ô nhập liệu quét nhanh tại đầu form tự động thêm dòng/điền dữ liệu tương ứng.

- [ ] **Step 1: Thêm Thư viện CDN html5-qrcode và Modal Camera vào Layout**
  Thêm liên kết CDN thư viện trong cả 2 file `CreateReceipt.cshtml` và `CreateIssue.cshtml` trong khối `@section Scripts` hoặc đầu trang:
  ```html
  <script src="https://unpkg.com/html5-qrcode" type="text/javascript"></script>

  <!-- Modal camera quét QR -->
  <div class="modal fade" id="cameraScanModal" tabindex="-1" aria-labelledby="cameraScanModalLabel" aria-hidden="true">
      <div class="modal-dialog modal-dialog-centered">
          <div class="modal-content">
              <div class="modal-header">
                  <h5 class="modal-title" id="cameraScanModalLabel">Quét QR bằng Camera</h5>
                  <button type="button" class="btn-close" id="btn-close-camera" data-bs-dismiss="modal" aria-label="Close"></button>
              </div>
              <div class="modal-body">
                  <div id="reader" style="width: 100%;"></div>
              </div>
          </div>
      </div>
  </div>
  ```

- [ ] **Step 2: Thêm Thanh quét mã trên CreateReceipt.cshtml**
  Chèn khối HTML thanh quét mã vào ngay trên thẻ `<form>` trong `Views/Inventory/CreateReceipt.cshtml` và triển khai Javascript xử lý:
  ```html
  <div class="card mb-3 border-primary">
      <div class="card-body py-2 d-flex align-items-center gap-2">
          <div class="input-group input-group-lg">
              <span class="input-group-text"><i class="bi bi-qr-code-scan"></i></span>
              <input type="text" id="barcode-scanner-input" class="form-control" placeholder="Quét mã SKU sản phẩm, Số lô hoặc Vị trí..." autofocus />
              <button type="button" class="btn btn-outline-primary" id="btn-camera-scan" data-bs-toggle="modal" data-bs-target="#cameraScanModal">
                  <i class="bi bi-camera"></i> Quét bằng Camera
              </button>
          </div>
      </div>
  </div>
  ```

- [ ] **Step 3: Cài đặt kịch bản xử lý mã quét trong CreateReceipt.cshtml**
  Nhúng danh mục Sản phẩm và Vị trí dạng JSON và viết mã Javascript đối chiếu xử lý:
  ```html
  <script>
      const productsMap = @Html.Raw(Json.Serialize(((IEnumerable<Product>)ViewBag.Products).Select(p => new { p.Id, p.Code })));
      const locationsMap = @Html.Raw(Json.Serialize(((IEnumerable<Location>)ViewBag.Locations).Select(l => new { l.Id, l.Code })));

      document.getElementById('barcode-scanner-input').addEventListener('keypress', function(e) {
          if (e.key === 'Enter') {
              e.preventDefault();
              processScan(this.value.trim());
              this.value = '';
          }
      });

      function processScan(code) {
          if (!code) return;

          // 1. Kiểm tra xem có khớp với mã Vị trí kho không
          const foundLocation = locationsMap.find(l => l.Code.toUpperCase() === code.toUpperCase());
          if (foundLocation) {
              const lastRow = document.querySelector('#receipt-lines tr:last-child');
              if (lastRow) {
                  const selectLoc = lastRow.querySelector('select[data-field="LocationId"]');
                  if (selectLoc) {
                      selectLoc.value = foundLocation.id;
                      showScanNotification("Đã chọn vị trí: " + foundLocation.code, "success");
                      document.getElementById('barcode-scanner-input').focus();
                      return;
                  }
              }
          }

          // 2. Kiểm tra xem có khớp với mã Sản phẩm (SKU) không
          const foundProduct = productsMap.find(p => p.Code.toUpperCase() === code.toUpperCase());
          if (foundProduct) {
              // Tự động nhấn thêm dòng
              document.getElementById('add-receipt-line').click();
              setTimeout(() => {
                  const lastRow = document.querySelector('#receipt-lines tr:last-child');
                  const selectProd = lastRow.querySelector('select[data-field="ProductId"]');
                  if (selectProd) {
                      selectProd.value = foundProduct.id;
                      showScanNotification("Đã thêm sản phẩm: " + foundProduct.code, "success");
                  }
              }, 50);
              return;
          }

          // 3. Nếu không phải SKU hay Vị trí, mặc định coi là Số lô (LotNo)
          const lastRow = document.querySelector('#receipt-lines tr:last-child');
          if (lastRow) {
              const inputLot = lastRow.querySelector('input[data-field="LotNo"]');
              if (inputLot) {
                  inputLot.value = code;
                  showScanNotification("Đã điền số lô: " + code, "info");
                  return;
              }
          }
      }

      function showScanNotification(msg, type) {
          console.log(msg); // Hoặc hiển thị Toast Notification Bootstrap
      }

      // Quét Camera với html5-qrcode
      let html5QrcodeScanner = null;
      document.getElementById('cameraScanModal').addEventListener('shown.bs.modal', function () {
          html5QrcodeScanner = new Html5Qrcode("reader");
          html5QrcodeScanner.start(
              { facingMode: "environment" },
              { fps: 10, qrbox: 250 },
              qrCodeMessage => {
                  processScan(qrCodeMessage);
                  document.getElementById('btn-close-camera').click();
              },
              errorMessage => { /* Bỏ qua log lỗi quét không trúng */ }
          ).catch(err => console.error(err));
      });

      document.getElementById('cameraScanModal').addEventListener('hidden.bs.modal', function () {
          if (html5QrcodeScanner) {
              html5QrcodeScanner.stop().then(() => {
                  html5QrcodeScanner = null;
              });
          }
      });
  </script>
  ```

- [ ] **Step 4: Thêm Thanh quét mã và xử lý trên CreateIssue.cshtml**
  Thêm tương tự vào `Views/Inventory/CreateIssue.cshtml`. Tại đây, khi quét Số lô hoặc Vị trí, JavaScript sẽ tìm kiếm Option phù hợp trong Dropdown tồn khả dụng (`data-stock-selection`):
  ```javascript
  function processScan(code) {
      if (!code) return;

      const lastRow = document.querySelector('#issue-lines tr:last-child');
      if (!lastRow) return;

      const stockSelect = lastRow.querySelector('select[data-stock-selection]');
      if (!stockSelect) return;

      // Duyệt qua tất cả các Option để tìm chuỗi chứa mã quét (LotNo hoặc LocationCode)
      let matchedValue = "";
      for (let i = 0; i < stockSelect.options.length; i++) {
          const opt = stockSelect.options[i];
          if (opt.text.toUpperCase().includes(code.toUpperCase())) {
              matchedValue = opt.value;
              break;
          }
      }

      if (matchedValue) {
          stockSelect.value = matchedValue;
          // Kích hoạt event change để cập nhật các thẻ input ẩn ProductId, LotId, LocationId
          stockSelect.dispatchEvent(new Event('change'));
          showScanNotification("Đã chọn dòng tồn khớp mã quét: " + code, "success");
      } else {
          showScanNotification("Không tìm thấy lô hàng/vị trí phù hợp cho mã: " + code, "warning");
      }
  }
  ```

- [ ] **Step 5: Commit**
  Run: `git add Views/Inventory/CreateReceipt.cshtml Views/Inventory/CreateIssue.cshtml`
  Run: `git commit -m "feat: integrate hybrid barcode/qr scanning in create receipt and create issue pages"`

---

### Task 5: Quét Lệnh Sản Xuất tại Trạm Vận Hành

**Files:**
- Modify: [Worker/Index.cshtml](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Views/Worker/Index.cshtml)

**Interfaces:**
- Consumes: Máy quét vạch tại trạm vận hành.
- Produces: Tự động kích hoạt form hoặc focus input dựa trên mã WO quét được.

- [ ] **Step 1: Thêm Thanh quét vào đầu trang Trạm vận hành**
  Thêm mã HTML và Script vào `Views/Worker/Index.cshtml` để nhận diện mã Lệnh sản xuất:
  ```html
  <div class="card mb-4 border-warning">
      <div class="card-body py-2">
          <div class="input-group">
              <span class="input-group-text bg-warning text-dark"><i class="bi bi-qr-code-scan"></i></span>
              <input type="text" id="worker-scanner-input" class="form-control form-control-lg" placeholder="Quét mã Lệnh sản xuất (Work Order Code) để Bắt đầu/Hoàn thành nhanh..." autofocus />
          </div>
      </div>
  </div>
  ```

- [ ] **Step 2: Viết mã xử lý tự động hóa công đoạn sản xuất**
  Thêm mã JavaScript lắng nghe sự kiện quét lệnh sản xuất:
  ```html
  <script>
      document.getElementById('worker-scanner-input').addEventListener('keypress', function(e) {
          if (e.key === 'Enter') {
              e.preventDefault();
              processWorkerScan(this.value.trim());
              this.value = '';
          }
      });

      function processWorkerScan(woCode) {
          if (!woCode) return;

          // Duyệt qua tất cả các thẻ card tìm mã WorkOrder Code khớp
          const cards = document.querySelectorAll('.worker-card');
          let matchedCard = null;

          for (const card of cards) {
              const codeEl = card.querySelector('.eyebrow');
              if (codeEl && codeEl.textContent.trim().toUpperCase() === woCode.toUpperCase()) {
                  matchedCard = card;
                  break;
              }
          }

          if (matchedCard) {
              // Cuộn thẻ card vào tầm nhìn
              matchedCard.scrollIntoView({ behavior: 'smooth', block: 'center' });
              matchedCard.style.border = "3px solid #ffc107";
              setTimeout(() => matchedCard.style.border = "", 2000);

              // 1. Nếu có nút BẮT ĐẦU (Form Start), tự động submit
              const startForm = matchedCard.querySelector('form[action*="Start"]');
              if (startForm) {
                  startForm.submit();
                  return;
              }

              // 2. Nếu có Form HOÀN THÀNH, tự động focus vào input số lượng đạt
              const qtyInput = matchedCard.querySelector('input[name="qtyOk"]');
              if (qtyInput) {
                  qtyInput.focus();
              }
          }
      }
  </script>
  ```

- [ ] **Step 3: Biên dịch toàn dự án để xác nhận không lỗi**
  Run: `dotnet build`
  Expected: Build thành công không có lỗi biên dịch.

- [ ] **Step 4: Commit**
  Run: `git add Views/Worker/Index.cshtml`
  Run: `git commit -m "feat: integrate work order scanning automation on worker station page"`
