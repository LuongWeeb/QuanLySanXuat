# Thiết kế Phân hệ Kiểm soát Chất lượng Tiêu chuẩn & Cách ly (Quality Control - Phase 6)

## 1. Tổng quan
Tài liệu này đặc tả thiết kế kỹ thuật cho **Phân hệ Kiểm soát Chất lượng Tiêu chuẩn & Cách ly (Quality Control Module)** thuộc hệ thống WMS + MES.

Mục tiêu chính:
1. Cho phép Quản lý QC thiết lập các Mẫu tiêu chí kiểm định (`QCChecklist`) cho từng sản phẩm với tham số định lượng (khoảng `MinVal` - `MaxVal`) hoặc định tính.
2. Hỗ trợ kiểm định chất lượng ở 2 khâu quan trọng:
   *   **Kiểm định Đầu vào (Inward QC):** Kiểm tra vật tư khi nhập kho từ Nhà cung cấp (theo Phiếu Nhập kho `GoodsReceipt`).
   *   **Kiểm định Thành phẩm (Final FG QC):** Kiểm tra thành phẩm khi hoàn thành Lệnh sản xuất (`WorkOrder`).
3. Tự động chấm điểm `IsOK` dựa trên số liệu đo lường thực tế so với tiêu chuẩn.
4. Xử lý giải phóng kho tự động: Nếu **PASS**, tự động chuyển số dư từ Tạm giữ (`QtyOnHold`) sang Khả dụng (`QtyAvailable`). Nếu **REJECT**, tự động di dời lô hàng về Kho cách ly phế phẩm (`QC-QUARANTINE`).

---

## 2. Thay đổi Cơ sở dữ liệu (Database Schema)

### 2.1 Cập nhật Thực thể Kiểm định Chất lượng (`QCInspection`)
*   **File:** [QCInspection.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Entities/QCInspection.cs)
*   **Điều chỉnh:**
    *   Cho phép `WorkOrderId` nhận giá trị Nullable (`int? WorkOrderId`).
    *   Bổ sung `GoodsReceiptId` (`int? GoodsReceiptId`).
    *   Bổ sung `Type` (`QCInspectionType` Enum: `InwardQC`, `FinalFGQC`).

### 2.2 Enum Loại Kiểm định (`QCInspectionType`)
*   **File mới:** `Domain/Enums/QCInspectionType.cs`
    ```csharp
    namespace WmsMes.Web.Domain.Enums;

    public enum QCInspectionType
    {
        InwardQC = 1,
        FinalFGQC = 2
    }
    ```

---

## 3. Quy trình Nghiệp vụ & Dịch vụ (Core Services)

### 3.1 Dịch vụ Đánh giá Tự động & Xử lý Kho (`QcService.cs`)
*   **Tự động đối chiếu thông số (`EvaluateLinesAsync`):**
    *   Duyệt qua danh sách tiêu chí `QCInspectionLine`.
    *   Nếu tham số có khoảng giá trị `[MinVal, MaxVal]`, parse chuỗi `ValueInspected` sang kiểu `decimal`.
    *   Nếu $ValueInspected < MinVal$ hoặc $ValueInspected > MaxVal$, tự động đánh dấu `IsOK = false`.
*   **Xử lý Giải phóng Kho khi PASS (`ReleasePassStockAsync`):**
    *   Tìm số dư `StockBalance` của Lô hàng đang ở trạng thái Tạm giữ `QtyOnHold > 0`.
    *   Giảm `QtyOnHold` và tăng `QtyAvailable` tương ứng.
    *   Ghi nhận nhật ký `StockTransaction` kiểu giải phóng cách ly QC.
*   **Xử lý Cách ly khi REJECT (`ConsolidateHoldInQuarantineAsync`):**
    *   Chuyển toàn bộ số dư của Lô hàng sang Vị trí `QC-QUARANTINE`.

---

## 4. Cải tiến Giao diện Người dùng (UI/UX)

### 4.1 Quản lý Mẫu tiêu chuẩn QC (`QcChecklistController.cs`)
*   Màn hình danh sách Mẫu kiểm định QC cho từng sản phẩm.
*   Form tạo mới & chỉnh sửa cho phép thêm nhiều dòng tiêu chí đo lường (Tên tham số, MinVal, MaxVal, Đơn vị tính).

### 4.2 Màn hình Thực thi Kiểm định QC (`QcController.cs`)
*   Trang danh sách các Lô hàng đang chờ kiểm định QC (gồm Lô mới nhập kho từ PO và Lô mới sản xuất từ WO).
*   Form nhập kết quả kiểm định (`CreateInspection.cshtml`):
    *   Tự động nạp danh sách tiêu chí kiểm định của sản phẩm tương ứng.
    *   Cho phép kỹ thuật viên QC nhập số liệu đo thực tế, tải lên ảnh bằng chứng (Evidence Path).
    *   Hiển thị ngay nhãn trạng thái PASS/FAIL trên từng dòng.

---

## 5. Kế hoạch Kiểm thử & Xác minh (Verification Plan)

### 5.1 Automated Tests (xUnit)
Bổ sung các kiểm thử trong `WmsMes.Tests/QcServiceTests.cs`:
1.  **Test Tự động chấm IsOK theo dải Min/Max:**
    *   Tạo Checklist có MinVal = 10, MaxVal = 20.
    *   Nhập ValueInspected = "15" -> Xác minh `IsOK == true`.
    *   Nhập ValueInspected = "25" -> Xác minh `IsOK == false` và `QCInspection.Result == REJECT`.
2.  **Test Giải phóng Kho khi PASS:**
    *   Tạo Lô hàng đang bị giữ `QtyOnHold = 50`.
    *   Nộp kết quả QC PASS -> Xác minh `QtyOnHold == 0` và `QtyAvailable == 50`.
3.  **Test Cách ly Kho khi REJECT:**
    *   Nộp kết quả QC REJECT -> Xác minh số dư bị di dời về vị trí `QC-QUARANTINE`.

---

## 6. Các bước triển khai cho Codex (Implementation Steps)

- [ ] **Bước 1:** Cập nhật `QCInspection.cs` (thêm GoodsReceiptId, Type, nullable WorkOrderId) và tạo Enum `QCInspectionType`. Chạy EF Core Migration.
- [ ] **Bước 2:** Xây dựng `QcChecklistController.cs` và các màn hình View quản lý Mẫu tiêu chí kiểm định sản phẩm.
- [ ] **Bước 3:** Nâng cấp `QcService.cs` xử lý đánh giá tự động dải Min/Max, giải phóng `QtyOnHold` khi PASS và chuyển sang `QC-QUARANTINE` khi REJECT.
- [ ] **Bước 4:** Viết các bài kiểm thử Unit Test trong `WmsMes.Tests/QcServiceTests.cs`.
- [ ] **Bước 5:** Xây dựng màn hình danh sách chờ QC và Form thực thi kiểm định QC trực quan.
- [ ] **Bước 6:** Thêm menu "Kiểm soát Chất lượng (QC)" vào Sidebar Layout.
