# Thiết kế Phân hệ Kiểm kê Kho bằng Mã vạch & Điều chỉnh Sổ cái (Stock Reconciliation - Phase 7)

## 1. Tổng quan
Tài liệu này đặc tả thiết kế kỹ thuật cho **Phân hệ Kiểm kê Kho bằng Mã vạch & Điều chỉnh Sổ cái (Barcoded Stock Reconciliation & Cycle Count)** thuộc hệ thống WMS + MES.

Mục tiêu chính:
1. Cho phép Thủ kho tạo Đợt kiểm kê (`CycleCountOrder`) theo Kho/Khu vực. Hệ thống tự động snapshot số dư tồn kho (`SystemQty`) tại thời điểm tạo đợt kiểm kê.
2. Hỗ trợ giao diện đếm hàng thông minh với máy quét mã vạch/QR code (Hybrid Barcode/QR Scanning):
   *   Quét mã **Vị trí** để tự động lọc danh sách các lô hàng có tại vị trí đó.
   *   Quét mã **Lot** để tự động tăng số lượng đếm thực tế (`CountedQty`).
3. Cung cấp màn hình Đối chiếu & Duyệt kiểm kê cho Quản lý (Manager): Hiển thị chênh lệch (`VarianceQty`), tự động tính toán tổng giá trị chênh lệch (VNĐ).
4. Tự động cập nhật Sổ cái Kho khi duyệt: Tự động cập nhật `StockBalance` và ghi nhận các bút toán `StockTransaction` thuộc loại `TransactionType.Adjust` bảo đảm tính toàn vẹn dữ liệu tồn kho.

---

## 2. Mô hình Dữ liệu (Data Model)

Sử dụng 2 thực thể đã khai báo trong hệ thống:
*   **Thực thể `CycleCountOrder`:** Lưu thông tin đợt kiểm kê (`CountNumber`, `WarehouseId`, `Status`, `CreatedAt`, `CreatedBy`, `CompletedAt`, `ApprovedBy`).
    *   Trạng thái (`Status`): `Draft` -> `InProgress` -> `Completed` -> `Approved` / `Cancelled`.
*   **Thực thể `CycleCountItem`:** Lưu thông tin kiểm đếm từng dòng lô hàng tại từng vị trí (`ProductId`, `LocationId`, `LotId`, `SystemQty`, `CountedQty`, `VarianceQty`).
    *   Thuộc tính tính toán: `VarianceQty = (CountedQty ?? SystemQty) - SystemQty`.

---

## 3. Quy trình Nghiệp vụ & Dịch vụ (Core Services)

Xây dựng dịch vụ `ICycleCountService` và class `CycleCountService`:

### 3.1 Khởi tạo Đợt kiểm kê (`CreateOrderAsync`)
1. Nhận `WarehouseId` và tên người tạo.
2. Truy vấn tất cả số dư `StockBalance` trong kho đó có tổng `QtyAvailable + QtyReserved + QtyOnHold > 0`.
3. Tạo mới `CycleCountOrder` ở trạng thái `Draft` với `CountNumber = $"CC-{DateTime.UtcNow:yyyyMMddHHmmss}"`.
4. Tạo các dòng `CycleCountItem` tương ứng với `SystemQty = balance.QtyAvailable`.

### 3.2 Cập nhật Kết quả đếm từ Mã quét (`UpdateCountedQtysAsync`)
*   Nhận danh sách `(ItemId, CountedQty)` từ màn hình đếm hàng bằng máy quét.
*   Cập nhật `CountedQty` vào các dòng `CycleCountItem`.
*   Chuyển trạng thái đợt kiểm kê sang `Completed`.

### 3.3 Duyệt & Điều chỉnh Sổ cái (`ApproveAndAdjustLedgerAsync`)
Khi Quản lý bấm "Duyệt & Điều chỉnh Sổ cái":
1. Kiểm tra trạng thái `CycleCountOrder` phải là `Completed`.
2. Mở một Database Transaction.
3. Duyệt qua tất cả các dòng `CycleCountItem` có `VarianceQty != 0`:
   *   Tìm bản ghi `StockBalance` tương ứng theo `ProductId`, `LocationId`, `LotId`.
   *   Nếu chưa có `StockBalance` (trường hợp đếm thấy lô hàng thừa chưa có trong vị trí này): Tạo mới `StockBalance`.
   *   Cập nhật `balance.QtyAvailable += item.VarianceQty`.
   *   Ghi bút toán Sổ cái `StockTransaction`:
       *   `Type = TransactionType.Adjust`
       *   `ProductId = item.ProductId`
       *   `LotId = item.LotId`
       *   `LocationId = item.LocationId`
       *   `Qty = item.VarianceQty`
       *   `QtyAfter = balance.QtyAvailable`
       *   `ValuationRate = item.Lot?.UnitPrice ?? 0m`
       *   `TransactionDate = DateTime.UtcNow`
       *   `UserId = managerUserId`
       *   `ReferenceNo = order.CountNumber`
4. Cập nhật `CycleCountOrder.Status = "Approved"`, `ApprovedBy = managerUserId`.
5. Commit Transaction.

---

## 4. Giao diện Người dùng (UI/UX)

Xây dựng bộ điều khiển `CycleCountController.cs` và các Views tương ứng:

### 4.1 Màn hình Danh sách Đợt kiểm kê (`Views/CycleCount/Index.cshtml`)
*   Hiển thị danh sách các đợt kiểm kê, mã đợt đếm, kho kiểm kê, trạng thái, người đếm và nút thao tác.

### 4.2 Màn hình Đếm hàng bằng Mã quét (`Views/CycleCount/ExecuteScan.cshtml`)
*   Tích hợp thanh quét mã lai (Hybrid Barcode/QR Input + Camera).
*   **Khi quét mã Vị trí:** Tự động cuộn đến và làm nổi bật (Highlight) danh sách các sản phẩm ở vị trí đó.
*   **Khi quét mã Lot:** Tự động tăng số lượng đếm `CountedQty` lên +1 (hoặc nhảy tới ô nhập số lượng thực tế đếm được).

### 4.3 Màn hình Báo cáo Chênh lệch & Duyệt (`Views/CycleCount/Details.cshtml`)
*   Hiển thị bảng so sánh đối chiếu chi tiết:
    *   Tồn hệ thống vs Đếm thực tế vs Chênh lệch.
    *   Dòng thừa kho hiển thị chữ màu Xanh, dòng thiếu kho hiển thị chữ màu Đỏ.
    *   Hiển thị **Tổng giá trị chênh lệch tài chính (VNĐ)** = $\sum (\text{VarianceQty} \times \text{Lot.UnitPrice})$.
*   Nút **"Duyệt & Điều chỉnh Sổ cái"** (Chỉ hiển thị cho vai trò Manager/Admin).

---

## 5. Kế hoạch Kiểm thử & Xác minh (Verification Plan)

### 5.1 Automated Tests (xUnit)
Bổ sung kiểm thử trong `WmsMes.Tests/CycleCountTests.cs`:
1.  **Test Tạo Đợt kiểm kê snapshot đúng SystemQty:**
    *   Tạo tồn kho 100 sản phẩm tại LOC-RAW-01. Tạo đợt kiểm kê. Xác minh `SystemQty == 100`.
2.  **Test Duyệt kiểm kê điều chỉnh Sổ cái chính xác:**
    *   Đếm thực tế = 90 (thiếu 10).
    *   Gọi `ApproveAndAdjustLedgerAsync`.
    *   Xác minh `StockBalance.QtyAvailable == 90`.
    *   Xác minh tạo bút toán `StockTransaction` kiểu `Adjust` với `Qty == -10`.

---

## 6. Các bước triển khai cho Codex (Implementation Steps)

- [ ] **Bước 1:** Xây dựng `ICycleCountService.cs` và `CycleCountService.cs` triển khai các phương thức khởi tạo đợt đếm, ghi nhận quét mã và duyệt điều chỉnh Sổ cái. Đăng ký trong DI.
- [ ] **Bước 2:** Viết bộ Unit Tests trong `WmsMes.Tests/CycleCountTests.cs` kiểm chứng thuật toán điều chỉnh kho và ghi sổ cái.
- [ ] **Bước 3:** Xây dựng `CycleCountController.cs` xử lý các action Index, Create, ExecuteScan, SaveScan, Details, Approve.
- [ ] **Bước 4:** Thiết kế các màn hình Razor View (Index, Create, ExecuteScan, Details) thân thiện với thiết bị di động và máy quét vạch.
- [ ] **Bước 5:** Thêm liên kết menu "Kiểm kê kho (Stocktake)" vào Sidebar của Layout hệ thống.
