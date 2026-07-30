# Thiết kế Phân hệ Tối ưu Vận hành & Cảnh báo Tồn kho (Operations Enhancements)

## 1. Tổng quan
Tài liệu này đặc tả thiết kế kỹ thuật cho **Phân hệ Tối ưu Vận hành Kho & Cảnh báo Tồn kho (Operations Enhancements)** nhằm giải quyết các yêu cầu thực tế trong vận hành nhà máy:

1. Ghi nhận và hiển thị **Lý do chênh lệch Giao/Nhận kho** (Ví dụ: Giao 10 nhưng Nhận 9 do hư hỏng/vận chuyển) trên các dòng phiếu kho và xuất in phiếu PDF.
2. Nâng cấp Phân hệ Kiểm kho: Bổ sung trường ghi nhận lý do thất thoát/hư hỏng trên từng dòng kiểm đếm và kết xuất **Biên bản Kiểm kê Kho PDF (`/api/print/cyclecount/{id}`)** có đầy đủ khối chữ ký xác nhận của Thủ kho và Quản lý để truy xuất trách nhiệm.
3. Bổ sung **Widget Cảnh báo Tồn kho dưới Định mức (`MinStock`)** trên Bảng điều khiển kèm tính năng 1-click tự động tạo Yêu cầu mua hàng (`PurchaseRequest`).

---

## 2. Thay đổi Cơ sở dữ liệu (Database Schema)

### 2.1 Bổ sung trường Lý do Chênh lệch trên Dòng phiếu Kho
*   **Thực thể `GoodsReceiptLine`:** Bổ sung `VarianceReason` (`string?`, MaxLength 250).
*   **Thực thể `GoodsIssueLine`:** Bổ sung `VarianceReason` (`string?`, MaxLength 250).

### 2.2 Bổ sung trường Lý do Thất thoát trên Dòng Kiểm kho
*   **Thực thể `CycleCountItem`:** Bổ sung `ReasonNote` (`string?`, MaxLength 250 - ví dụ: "Thất thoát do rách bao bì", "Hư hỏng ẩm mốc").

---

## 3. Chi tiết Chức năng & API In ấn PDF

### 3.1 Ghi nhận Lý do chênh lệch & In phiếu Nhập/Xuất kho PDF
*   Trên màn hình `CreateReceipt.cshtml` và `CreateIssue.cshtml`: Khi số lượng nhận/xuất thực tế khác với số lượng yêu cầu ban đầu, cho phép nhập chuỗi `VarianceReason`.
*   Cập nhật `PrintController.cs` khi xuất file PDF phiếu Nhập/Xuất kho: Bổ sung cột "Lý do chênh lệch" trong bảng danh sách vật tư.

### 3.2 Kết xuất Biên bản Kiểm kê Kho PDF (`PrintController.cs`)
*   **Endpoint mới:** `[HttpGet("api/print/cyclecount/{id}")]`
*   **Đặc tả bố cục Biên bản PDF (QuestPDF):**
    *   *Tiêu đề:* **BIÊN BẢN KIỂM KÊ VÀ ĐỐI CHIẾU TỒN KHO**
    *   *Thông tin chung:* Mã đợt kiểm kê (`CountNumber`), Tên Kho (`Warehouse.Name`), Ngày đếm, Người lập (`CreatedBy`), Người duyệt (`ApprovedBy`).
    *   *Bảng chi tiết:* Mã SKU, Tên sản phẩm, Vị trí, Số lô, Tồn hệ thống (`SystemQty`), Đếm thực tế (`CountedQty`), Chênh lệch (`VarianceQty`), Lý do thất thoát (`ReasonNote`), Giá trị chênh lệch (VNĐ).
    *   *Chân trang:* 3 khối chữ ký xác nhận trách nhiệm: **Người kiểm đếm (Thủ kho)**, **Nhân viên Kiểm toán/QC**, và **Trưởng kho/Giám đốc duyệt**.

### 3.3 Widget Cảnh báo Tồn kho sắp hết & 1-Click Tạo PR
*   **Logic Cảnh báo:** Truy vấn các sản phẩm active có tổng `QtyAvailable` trên tất cả các vị trí kho nhỏ hơn `Product.MinStock`.
*   **Hiển thị:** Bổ sung khối Alert trên `Dashboard/Index.cshtml` và `Inventory/Index.cshtml` liệt kê danh sách các mã hàng sắp hết.
*   **Hành động 1-Click:** Thêm nút **"Tạo Yêu cầu Mua hàng tự động (PR)"** gọi endpoint `[HttpPost] /PurchaseOrder/CreateRequestFromLowStock`. Hệ thống tự động tạo `PurchaseRequest` chứa các sản phẩm thiếu với số lượng đề xuất = `Product.MaxStock - Tổng_Tồn_Khả_Dụng`.

---

## 4. Kế hoạch Kiểm thử & Xác minh (Verification Plan)

### 4.1 Automated Tests (xUnit)
Bổ sung kiểm thử trong `WmsMes.Tests/OperationsEnhancementsTests.cs`:
1.  **Test In Biên bản Kiểm kho PDF:**
    *   Gọi `/api/print/cyclecount/{id}` với ID đợt kiểm kê hợp lệ -> Xác minh trả về stream file PDF (`application/pdf`) có độ dài > 0.
2.  **Test Tự động tạo PR từ Cảnh báo Tồn kho thấp:**
    *   Tạo sản phẩm A có `MinStock = 10`, `MaxStock = 50`, hiện tại tồn kho = 2.
    *   Gọi `CreateRequestFromLowStock` -> Xác minh tạo mới `PurchaseRequest` chứa sản phẩm A với số lượng = 48.

---

## 5. Các bước triển khai cho Codex (Implementation Steps)

- [ ] **Bước 1:** Cập nhật database schema (`VarianceReason` trong `GoodsReceiptLine`/`GoodsIssueLine`, `ReasonNote` trong `CycleCountItem`). Chạy Migration.
- [ ] **Bước 2:** Bổ sung endpoint `/api/print/cyclecount/{id}` trong `PrintController.cs` kết xuất Biên bản kiểm kê kho PDF chuẩn QuestPDF.
- [ ] **Bước 3:** Cập nhật `CycleCountController` và View `ExecuteScan.cshtml`/`Details.cshtml` cho phép nhập `ReasonNote` trên từng dòng đếm.
- [ ] **Bước 4:** Xây dựng tính năng `CreateRequestFromLowStock` trong `PurchaseOrderController.cs` và hiển thị Widget cảnh báo tồn kho sắp hết trên `Dashboard/Index.cshtml` & `Inventory/Index.cshtml`.
- [ ] **Bước 5:** Viết các Unit Tests kiểm chứng trong `WmsMes.Tests`.
