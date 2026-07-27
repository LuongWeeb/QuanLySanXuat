# Thiết kế Hệ thống Số hóa Giao dịch & Quét mã (Barcode/QR - Phase 2)

## 1. Tổng quan
Tài liệu này đặc tả thiết kế kỹ thuật cho **Phase 2: Số hóa Giao dịch & Quét mã (Barcode/QR & Operations)** thuộc lộ trình cải tiến hệ thống WMS + MES.

Mục tiêu chính là tích hợp công nghệ mã vạch (Barcode/QR Code) vào các hoạt động kho (Nhập/Xuất/Chuyển) và trạm sản xuất (Worker Station) nhằm loại bỏ việc nhập liệu thủ công bằng tay, tăng tốc độ xử lý và giảm thiểu sai sót. Đồng thời, xây dựng hệ thống tạo và in nhãn QR Code chuyên dụng bằng QuestPDF và QRCoder.

---

## 2. In nhãn QR Code (QuestPDF & QRCoder)

Xây dựng bộ điều khiển `PrintController.cs` chịu trách nhiệm kết xuất nhãn PDF chất lượng cao. Các nhãn này được thiết kế theo kích thước chuẩn công nghiệp `100mm x 50mm` (phù hợp với các dòng máy in tem nhiệt phổ biến).

### 2.1 Endpoints In ấn
*   **Mã nhãn Vị trí kho (Location Label):**
    *   *Route:* `[HttpGet("api/print/location/{id}")]`
    *   *Tham số:* `id` (Mã ID của Vị trí trong DB).
    *   *Nội dung nhãn:* QR Code chứa chuỗi mã Vị trí (e.g. `LOC-RAW-01`), Tên Warehouse, Tên Zone, và chữ in lớn của mã Vị trí để công nhân đọc bằng mắt thường.
*   **Mã nhãn Lô hàng (Lot Label):**
    *   *Route:* `[HttpGet("api/print/lot/{id}")]`
    *   *Tham số:* `id` (Mã ID của Lô hàng trong DB).
    *   *Nội dung nhãn:* QR Code chứa số Lot (e.g. `LOT-100`), Mã SKU sản phẩm, Tên sản phẩm, Ngày sản xuất, Hạn sử dụng, và số Lot in lớn.

### 2.2 Cấu trúc PDF Nhãn (QuestPDF Layout)
*   Sử dụng thư viện `QRCoder` để tạo ảnh mã QR dưới dạng `byte[]`:
    ```csharp
    using var qrGenerator = new QRCodeGenerator();
    using var qrCodeData = qrGenerator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
    using var qrCode = new PngByteQRCode(qrCodeData);
    byte[] qrCodeBytes = qrCode.GetGraphic(20);
    ```
*   Sử dụng `QuestPDF` vẽ nhãn 2 cột:
    *   Cột trái: QR Code dạng vuông (`.Width(120).Height(120)`).
    *   Cột phải: Thông tin văn bản căn lề trái, font chữ to rõ (Outfit/Inter).

---

## 3. Tích hợp Quét mã Giao diện (Web Hybrid Scanning)

Hệ thống sẽ hỗ trợ cơ chế quét lai (Hybrid) tại các màn hình: **Tạo Phiếu Nhập kho**, **Tạo Phiếu Xuất kho**, và **Trạm Vận hành (Worker Station)**.

### 3.1 Giao diện Quét (Frontend UI Layout)
Tại đầu mỗi form nhập liệu, thêm khối HTML chứa ô quét:
```html
<div class="card mb-3 border-primary">
    <div class="card-body py-2 d-flex align-items-center gap-2">
        <div class="input-group input-group-lg">
            <span class="input-group-text"><i class="bi-qr-code-scan"></i></span>
            <input type="text" id="barcode-scanner-input" class="form-control" placeholder="Quét mã vạch (Lot, vị trí, sản phẩm)..." autofocus />
            <button type="button" class="btn btn-outline-primary" id="btn-camera-scan" aria-label="Quét bằng camera"><i class="bi-camera"></i> Quét Camera</button>
        </div>
    </div>
</div>
```

### 3.2 Cơ chế Quét phần cứng (Keyboard Emulator)
Hệ thống máy quét chuyên dụng (USB/Bluetooth) tự động điền giá trị mã vạch vào ô `#barcode-scanner-input` và gửi sự kiện phím `Enter` (KeyCode 13).
*   **JavaScript Listener:**
    ```javascript
    document.getElementById('barcode-scanner-input').addEventListener('keypress', function(e) {
        if (e.key === 'Enter') {
            e.preventDefault();
            const scannedValue = this.value.trim();
            if (scannedValue) {
                processScannedCode(scannedValue);
            }
            this.value = ''; // Clear để quét tiếp
        }
    });
    ```
*   **Tự động Focus:** Khi tải trang, ô `#barcode-scanner-input` được `autofocus`. Sau mỗi lần quét xử lý xong, hệ thống tự động gọi `.focus()` để sẵn sàng quét dòng tiếp theo.

### 3.3 Cơ chế Quét Camera (Webcam Scanning)
*   Tải thư viện `html5-qrcode` qua CDN.
*   Khi nhấn `#btn-camera-scan`, mở một Bootstrap Modal chứa khung hình máy ảnh (`<div id="reader"></div>`).
*   Khi camera nhận diện được mã QR, hệ thống đóng modal, gửi mã đã quét vào hàm `processScannedCode(decodedText)` và phát ra âm thanh Beep báo hiệu thành công.

---

## 4. Xử lý Logic Quét cho từng Giao diện

Hàm xử lý `processScannedCode(code)` sẽ có logic khác nhau tùy thuộc vào từng màn hình.

### 4.1 Màn hình Tạo Phiếu Nhập kho (`CreateReceipt.cshtml`)
Dữ liệu sản phẩm (SKU) và Vị trí (Code) được nhúng sẵn dưới dạng JSON ẩn trong thẻ script để đối chiếu nhanh.
*   **Quy trình quét:**
    1.  **Bước 1: Quét mã SKU sản phẩm (ví dụ: `RM-FRAME-01`):**
        *   Hệ thống kiểm tra xem SKU có khớp với danh mục sản phẩm không.
        *   Nếu khớp, tự động thêm một dòng mới vào bảng `receipt-lines` và chọn sản phẩm đó.
    2.  **Bước 2: Quét mã Lot (nhận diện tem nhãn lô hàng):**
        *   Điền giá trị quét vào ô `LotNo` của dòng hiện tại đang nhập.
    3.  **Bước 3: Quét mã Vị trí kho (ví dụ: `LOC-RAW-01`):**
        *   Hệ thống kiểm tra xem mã có khớp với mã Vị trí nào không.
        *   Nếu khớp, tự động chọn Vị trí tương ứng ở cột Vị trí trên dòng hiện tại. Focus lập tức quay lại ô quét tổng để chuẩn bị cho sản phẩm tiếp theo.

### 4.2 Màn hình Tạo Phiếu Xuất kho (`CreateIssue.cshtml`)
*   Phiếu xuất kho yêu cầu công nhân chọn dòng tồn kho khả dụng từ dropdown `data-stock-selection` chứa thông tin định dạng: `Mã SKU | Số lô | Vị trí`.
*   **Quy trình quét:**
    *   Khi công nhân quét mã **Lot** hoặc mã **Vị trí**:
    *   JavaScript sẽ duyệt qua tất cả các `<option>` của dropdown tồn kho khả dụng ở dòng hiện tại (hoặc dòng mới).
    *   Nếu nội dung option chứa chuỗi Lot hoặc Vị trí vừa quét, hệ thống tự động chọn option đó và điền các trường ẩn `ProductId`, `LotId`, `LocationId`.
    *   Nếu quét mã SKU, tự động tạo dòng mới và lọc các option thuộc SKU đó.

### 4.3 Màn hình Trạm Vận hành (`Views/Worker/Index.cshtml`)
*   **Quy trình quét:**
    *   Công nhân quét mã QR của **Lệnh sản xuất (Work Order Code)** (ví dụ: `WO-20260715-01`).
    *   JavaScript tìm kiếm thẻ `<article class="worker-card">` chứa mã Lệnh tương ứng.
    *   **Hành động tự động:**
        *   Nếu công đoạn đang ở trạng thái `Pending` (Chờ bắt đầu): Tự động thực thi submit Form Bắt đầu (`Start`).
        *   Nếu công đoạn đang ở trạng thái `InProgress` (Đang sản xuất): Tự động Focus vào ô nhập số lượng đạt (`qtyOk`), sẵn sàng cho công nhân nhập số lượng và nhấn Hoàn thành mà không cần click chuột chọn.

---

## 5. UI/UX In nhãn QR Code

### 5.1 Cập nhật trang Sơ đồ kho (`Views/Warehouse/Index.cshtml`)
*   **Trong Modal Vị trí kho:**
    *   Bên cạnh tiêu đề mã vị trí (e.g. `Tồn kho tại vị trí: LOC-RAW-01`), thêm nút **"In mã QR Vị trí"** (dạng nút Outline màu xanh lam với icon Máy in). Khi nhấn, mở ra tab mới kết xuất nhãn PDF của vị trí đó.
*   **Trong bảng chi tiết sản phẩm thuộc vị trí:**
    *   Bên cạnh mỗi số Lot, thêm một liên kết nhỏ hoặc icon máy in **"In nhãn Lot"** hướng tới endpoint in mã QR của Lot tương ứng.

---

## 6. Kế hoạch Kiểm thử & Xác minh (Verification Plan)

### 6.1 Automated Tests (xUnit)
Bổ sung các kiểm thử trong `WmsMes.Tests/ReportExportTests.cs` (hoặc kiểm thử controller mới):
1.  **Test in nhãn Vị trí:**
    *   Gọi endpoint API in nhãn vị trí với Id hợp lệ. Kiểm tra kết quả trả về là luồng Stream PDF (`application/pdf`) và độ dài file lớn hơn 0.
2.  **Test in nhãn Lô hàng:**
    *   Gọi endpoint API in nhãn lô hàng với Id hợp lệ. Kiểm tra kết quả trả về là luồng Stream PDF.
3.  **Test mã hóa QR Code:**
    *   Giải mã (Decode) hình ảnh QR sinh ra từ API và so sánh xem nội dung giải mã được có khớp chính xác với `Location.Code` hoặc `Lot.LotNo` hay không.

### 6.2 Manual Verification (Kiểm tra thủ công)
1.  Truy cập trang Sơ đồ kho, nhấn nút "In mã QR Vị trí" và "In nhãn Lot" để kiểm tra giao diện kết xuất file PDF của QuestPDF có đúng khổ nhãn 100x50mm và hiển thị đẹp mắt hay không.
2.  Mở màn hình Tạo phiếu nhập/xuất kho, dùng máy quét phần cứng quét thử một mã SKU, một số Lot và một mã vị trí để kiểm tra xem JavaScript có tự động thêm dòng và điền thông tin chính xác hay không.
3.  Truy cập Trạm vận hành, quét thử mã QR Lệnh sản xuất để đảm bảo hành động Focus/Start tự động hoạt động đúng thiết kế.
