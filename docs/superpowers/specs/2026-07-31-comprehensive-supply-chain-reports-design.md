# Thiết kế Phân hệ Nâng cao Chuỗi Cung ứng, Báo cáo Tài chính Kho & Cảnh báo (Phase 9)

## 1. Tổng quan
Tài liệu này đặc tả thiết kế kỹ thuật cho **Phase 9: Nâng cấp Toàn diện Chuỗi Cung ứng, Báo cáo Tài chính Kho & Cảnh báo Thông minh (Comprehensive Supply Chain & Financial Reports)** thuộc hệ thống WMS + MES.

Mục tiêu chính:
1. **Quy trình Pick-Pack-Ship:** Tự động tạo Danh sách lấy hàng (`PickList`) tối ưu đường đi trong kho cho Đơn bán hàng và kết xuất Nhãn đóng gói Thùng/Pallet (`PackingSlip` PDF) có mã QR code.
2. **Báo cáo Tài chính Kho & Giá vốn COGS:** Xây dựng màn hình Báo cáo Giá trị Tồn kho & Giá vốn Hàng bán (COGS), hỗ trợ xuất file Excel định dạng đẹp qua `ClosedXML`.
3. **Trung tâm Cảnh báo Thông minh:** Tích hợp hệ thống thông báo đẩy SignalR Realtime Toast, đếm số thông báo chưa đọc trên Header và hỗ trợ Webhook/Telegram Bot khi phát sinh sự cố QC REJECT hoặc Tồn kho dưới mức tối thiểu.

---

## 2. Thay đổi Cơ sở dữ liệu (Database Schema)

### 2.1 Quy trình Pick-Pack-Ship (WMS Fulfillment)
*   **Thực thể `PickList` (Danh sách lấy hàng):**
    *   *File:* `Domain/Entities/PickList.cs`
    *   *Thuộc tính:* `Id`, `PickListNo` (string), `SalesOrderId` (int), `CreatedAt` (DateTime), `Status` (DocumentStatus: Draft, InProgress, Completed).
*   **Thực thể `PickListItem` (Chi tiết vị trí lấy hàng):**
    *   *File:* `Domain/Entities/PickListItem.cs`
    *   *Thuộc tính:* `Id`, `PickListId` (int), `ProductId` (int), `LocationId` (int), `LotId` (int), `QtyToPick` (decimal), `PickedQty` (decimal), `SequenceOrder` (int - thứ tự ưu tiên tối ưu đường đi theo mã Vị trí).
*   **Thực thể `PackingSlip` (Tem & Phiếu đóng gói):**
    *   *File:* `Domain/Entities/PackingSlip.cs`
    *   *Thuộc tính:* `Id`, `PackingNo` (string), `SalesOrderId` (int), `PackageNo` (int - Thùng số X/Y), `GrossWeight` (decimal), `Status` (DocumentStatus).

### 2.2 Trung tâm Thông báo (Notification Center)
*   **Thực thể `AppNotification`:**
    *   *File:* `Domain/Entities/AppNotification.cs`
    *   *Thuộc tính:* `Id`, `Title` (string), `Message` (string), `Severity` (string: Info, Warning, Danger), `CreatedAt` (DateTime), `IsRead` (bool), `UserId` (string?), `ReferenceUrl` (string?).

---

## 3. Quy trình Nghiệp vụ & Dịch vụ (Core Services)

### 3.1 Dịch vụ Tối ưu Đường đi Lấy hàng (`IPickListService`)
*   Khi tạo `PickList` cho một `SalesOrder`:
    1.  Duyệt qua các sản phẩm trong đơn bán hàng.
    2.  Truy vấn tồn khả dụng `StockBalance` tại các vị trí kho.
    3.  Sắp xếp các vị trí lấy hàng theo thứ tự ưu tiên: `Zone.Code` -> `Location.Code` để công nhân đi lấy hàng một vòng ngắn nhất mà không phải đi ngược lại.
    4.  Gán `SequenceOrder` cho từng dòng.

### 3.2 In Nhãn Đóng gói Shipping Label QR (`PrintController.cs`)
*   **Endpoint mới:** `[HttpGet("api/print/packingslip/{id}")]`
*   Kết xuất tem nhãn đóng gói khổ 100x100mm chuẩn QuestPDF chứa QR Code mã kiện hàng, Tên khách hàng, Mã Đơn bán hàng, Số thùng (ví dụ: Thùng 1 / 3) và danh sách sản phẩm bên trong.

### 3.3 Báo cáo Giá trị Tồn kho & Xuất Excel (`ReportController.cs`)
*   Tính toán Tổng giá trị tài chính kho = $\sum (\text{QtyAvailable} \times \text{Lot.UnitPrice})$.
*   **Xuất Excel bằng ClosedXML:** Endpoint `/Report/ExportStockValuationExcel` tự động kết xuất tệp `.xlsx` có tiêu đề, màu sắc thương hiệu, định dạng phân cách hàng nghìn và tổng cộng tài chính.

### 3.4 Trung tâm Cảnh báo Realtime (`INotificationService`)
*   Tự động bắn Notification + SignalR event khi:
    *   Phát sinh phiếu QC kiểm định có kết quả `REJECT`.
    *   Tồn kho sản phẩm giảm xuống dưới `MinStock`.
    *   Một `WorkOrder` hoặc `ProductionPlan` được hoàn thành.

---

## 4. Cải tiến Giao diện Người dùng (UI/UX)

1.  **Icon Chuông Thông báo trên Header Navbar (`_Layout.cshtml`):**
    *   Hiển thị badge đỏ đếm số thông báo chưa đọc.
    *   Dropdown hiển thị nhanh 5 thông báo mới nhất kèm mốc thời gian và liên kết trực tiếp tới chứng từ tương ứng.
2.  **Màn hình Pick List & Đóng gói (`PickListController.cs`):**
    *   Giao diện hướng dẫn công nhân kho lấy hàng theo thứ tự vị trí đã tối ưu.
3.  **Màn hình Báo cáo Tài chính Kho (`Views/Report/StockValuation.cshtml`):**
    *   Bảng báo cáo tài chính kho lọc theo Kho / Sản phẩm và nút **"Xuất Báo cáo Excel"**.

---

## 5. Kế hoạch Kiểm thử & Xác minh (Verification Plan)

### 5.1 Automated Tests (xUnit)
Bổ sung kiểm thử trong `WmsMes.Tests/SupplyChainReportsTests.cs`:
1.  **Test Tối ưu đường đi PickList:**
    *   Tạo tồn kho tại các vị trí `LOC-A-01` và `LOC-B-05`. Xác minh `PickList` tự động sắp xếp theo đúng thứ tự chuỗi vị trí.
2.  **Test Kết xuất Excel ClosedXML:**
    *   Gọi `/Report/ExportStockValuationExcel` -> Xác minh trả về byte stream tệp Excel (`application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`) dung lượng > 0.
3.  **Test Gửi Cảnh báo Realtime:**
    *   Tạo Notification -> Xác minh `IsRead == false` và số lượng thông báo chưa đọc tăng +1.

---

## 6. Các bước triển khai cho Codex (Implementation Steps)

- [ ] **Bước 1:** Định nghĩa các thực thể `PickList`, `PickListItem`, `PackingSlip`, `AppNotification`. Đăng ký `ApplicationDbContext` và chạy EF Core Migration.
- [ ] **Bước 2:** Xây dựng `PickListService` xử lý thuật toán tối ưu vị trí lấy hàng và `NotificationService` xử lý bắn thông báo.
- [ ] **Bước 3:** Thêm endpoint in nhãn đóng gói PDF `/api/print/packingslip/{id}` trong `PrintController.cs` và endpoint `/Report/ExportStockValuationExcel` xuất Excel qua ClosedXML.
- [ ] **Bước 4:** Xây dựng `PickListController.cs`, `ReportController.cs` và các màn hình View MVC tương ứng.
- [ ] **Bước 5:** Thêm Icon Chuông thông báo Realtime lên Header Navbar trong `_Layout.cshtml` và thêm menu Báo cáo Kho / Pick List vào Sidebar.
- [ ] **Bước 6:** Viết các Unit Tests trong `WmsMes.Tests` kiểm định toàn bộ phân hệ.
