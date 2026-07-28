# Thiết kế Phân hệ Mua hàng & Bán hàng Tích hợp (Buying & Selling Integration)

## 1. Tổng quan
Tài liệu này đặc tả thiết kế kỹ thuật cho **Phân hệ Mua hàng & Bán hàng Tích hợp (Buying & Selling Integration)** nhằm khép kín toàn bộ chu trình ERP từ nhu cầu Đơn bán hàng của khách -> Kế hoạch sản xuất & MRP -> Đơn mua hàng -> Nhập kho -> Xuất kho giao hàng.

Mục tiêu chính:
1. Quản lý Đơn bán hàng (`SalesOrder`) để ghi nhận nhu cầu của khách hàng.
2. Tự động sinh Yêu cầu mua hàng (`PurchaseRequest`) từ kết quả chạy MRP của Kế hoạch sản xuất khi phát hiện vật tư bị thiếu (`NetDemand > 0`).
3. Quản lý Đơn mua hàng (`PurchaseOrder`) lập từ Yêu cầu mua hàng gửi Nhà cung cấp.
4. Tích hợp liên kết Đơn mua hàng (PO) vào Phiếu Nhập kho (`GoodsReceipt`) và Đơn bán hàng (SO) vào Phiếu Xuất kho (`GoodsIssue`), tự động theo dõi tiến độ giao nhận và đóng đơn.

---

## 2. Thay đổi Cơ sở dữ liệu (Database Schema)

### 2.1 Phân hệ Bán hàng (Selling)
*   **Thực thể `SalesOrder` (Đơn bán hàng):**
    *   *File:* `Domain/Entities/SalesOrder.cs`
    *   *Thuộc tính:* `Id`, `OrderNo` (string), `CustomerId` (int), `OrderDate` (DateTime), `DeliveryDate` (DateTime), `Status` (DocumentStatus: Draft, Completed, Cancelled).
*   **Thực thể `SalesOrderItem` (Chi tiết đơn bán hàng):**
    *   *File:* `Domain/Entities/SalesOrderItem.cs`
    *   *Thuộc tính:* `Id`, `SalesOrderId` (int), `ProductId` (int), `Qty` (decimal), `UnitPrice` (decimal), `DeliveredQty` (decimal).

### 2.2 Phân hệ Mua hàng (Buying)
*   **Thực thể `PurchaseRequest` (Yêu cầu mua hàng):**
    *   *File:* `Domain/Entities/PurchaseRequest.cs`
    *   *Thuộc tính:* `Id`, `RequestNo` (string), `RequestDate` (DateTime), `RequiredDate` (DateTime), `Status` (DocumentStatus), `ProductionPlanId` (int?, liên kết với Kế hoạch sản xuất đã chạy MRP).
*   **Thực thể `PurchaseRequestItem` (Chi tiết yêu cầu mua hàng):**
    *   *File:* `Domain/Entities/PurchaseRequestItem.cs`
    *   *Thuộc tính:* `Id`, `PurchaseRequestId` (int), `ProductId` (int), `Qty` (decimal).

*   **Thực thể `PurchaseOrder` (Đơn mua hàng):**
    *   *File:* `Domain/Entities/PurchaseOrder.cs`
    *   *Thuộc tính:* `Id`, `OrderNo` (string), `SupplierId` (int), `OrderDate` (DateTime), `ExpectedDeliveryDate` (DateTime), `Status` (DocumentStatus), `PurchaseRequestId` (int?, liên kết với PR).
*   **Thực thể `PurchaseOrderItem` (Chi tiết đơn mua hàng):**
    *   *File:* `Domain/Entities/PurchaseOrderItem.cs`
    *   *Thuộc tính:* `Id`, `PurchaseOrderId` (int), `ProductId` (int), `Qty` (decimal), `UnitPrice` (decimal), `ReceivedQty` (decimal).

### 2.3 Liên kết trên Chứng từ Kho (Stock Vouchers)
*   **Thực thể `GoodsReceipt`:** Bổ sung `PurchaseOrderId` (int?, FK nullable).
*   **Thực thể `GoodsIssue`:** Bổ sung `SalesOrderId` (int?, FK nullable).

---

## 3. Quy trình Nghiệp vụ & Dịch vụ (Core Services)

### 3.1 Tự động sinh Yêu cầu mua hàng từ MRP
*   **Dịch vụ:** `IPurchaseRequestService`
*   **Phương thức:** `GenerateRequestFromMrpAsync(int productionPlanId, string userId)`
*   **Luồng xử lý:**
    1.  Gọi `ProductionPlanService.CalculatePlanRequirementsAsync(productionPlanId)` để lấy danh sách vật tư.
    2.  Lọc ra các vật tư có `NetDemand > 0`.
    3.  Tạo bản ghi `PurchaseRequest` ở trạng thái `Draft` với `RequestNo = $"PR-{plan.PlanNo}"`.
    4.  Với mỗi vật tư thiếu, tạo `PurchaseRequestItem` với `Qty = NetDemand`.

### 3.2 Lập Đơn mua hàng từ Yêu cầu mua hàng
*   **Dịch vụ:** `IPurchaseOrderService`
*   **Phương thức:** `CreateOrderFromRequestAsync(int purchaseRequestId, int supplierId, string userId)`
*   **Luồng xử lý:**
    1.  Tải danh sách vật tư trong `PurchaseRequest`.
    2.  Tạo mới `PurchaseOrder` gắn với `SupplierId` đã chọn.
    3.  Với mỗi item, lấy đơn giá tiêu chuẩn `Product.StandardCost` (hoặc giá mua gần nhất) để điền `UnitPrice`.

### 3.3 Tích hợp Nhập kho theo Đơn mua hàng (PO Receipt)
*   Trong `InventoryService.CompleteGoodsReceiptCoreAsync`:
    *   Nếu `GoodsReceipt.PurchaseOrderId` có giá trị:
        *   Duyệt qua các dòng nhập kho, tìm `PurchaseOrderItem` tương ứng theo `ProductId`.
        *   Cập nhật `PurchaseOrderItem.ReceivedQty += line.Qty`.
        *   Kiểm tra nếu tất cả các dòng trong `PurchaseOrder` đã giao đủ (`ReceivedQty >= Qty`), tự động chuyển trạng thái `PurchaseOrder.Status = DocumentStatus.Completed`.

### 3.4 Tích hợp Xuất kho theo Đơn bán hàng (SO Issue)
*   Trong `InventoryService.CompleteGoodsIssueCoreAsync`:
    *   Nếu `GoodsIssue.SalesOrderId` có giá trị:
        *   Duyệt qua các dòng xuất kho, tìm `SalesOrderItem` tương ứng theo `ProductId`.
        *   Cập nhật `SalesOrderItem.DeliveredQty += line.Qty`.
        *   Kiểm tra nếu tất cả các dòng trong `SalesOrder` đã xuất đủ (`DeliveredQty >= Qty`), tự động chuyển trạng thái `SalesOrder.Status = DocumentStatus.Completed`.

---

## 4. Cải tiến Giao diện Người dùng (UI/UX)

1.  **Màn hình Đơn bán hàng (`SalesOrderController`):**
    *   Danh sách Đơn bán hàng (`Index.cshtml`), Form tạo mới (`Create.cshtml`), Chi tiết (`Details.cshtml`).
2.  **Màn hình Kế hoạch sản xuất (`ProductionPlan/Details.cshtml`):**
    *   Bổ sung nút **"Tạo Yêu cầu mua hàng (PR)"** hiển thị sau khi chạy MRP nếu có vật tư thiếu.
3.  **Màn hình Yêu cầu & Đơn mua hàng (`PurchaseOrderController`):**
    *   Danh sách Yêu cầu mua hàng & Đơn mua hàng, Form tạo PO từ PR.
4.  **Trang Tạo Phiếu Nhập kho (`CreateReceipt.cshtml`):**
    *   Thêm Dropdown **"Chọn Đơn mua hàng (PO)"**. Khi chọn, tự động nạp Nhà cung cấp và toàn bộ dòng vật tư cần nhập.
5.  **Trang Tạo Phiếu Xuất kho (`CreateIssue.cshtml`):**
    *   Thêm Dropdown **"Chọn Đơn bán hàng (SO)"**. Khi chọn, tự động nạp Khách hàng và các thành phẩm cần xuất.

---

## 5. Kế hoạch Kiểm thử & Xác minh (Verification Plan)

### 5.1 Automated Tests (xUnit)
Bổ sung các kiểm thử trong `WmsMes.Tests`:
1.  **Test Tự động sinh PR từ MRP:**
    *   Chạy MRP trên một Kế hoạch sản xuất có thiếu vật tư. Gọi `GenerateRequestFromMrpAsync`. Xác minh `PurchaseRequest` được tạo thành công với số lượng đúng bằng `NetDemand`.
2.  **Test Nhập kho theo PO tự động đóng Đơn mua hàng:**
    *   Tạo PO 100 sản phẩm -> Hoàn tất Nhập kho 100 sản phẩm theo PO -> Xác minh `PurchaseOrderItem.ReceivedQty == 100` và `PurchaseOrder.Status == DocumentStatus.Completed`.
3.  **Test Xuất kho theo SO tự động đóng Đơn bán hàng:**
    *   Tạo SO 50 sản phẩm -> Hoàn tất Xuất kho 50 sản phẩm theo SO -> Xác minh `SalesOrderItem.DeliveredQty == 50` và `SalesOrder.Status == DocumentStatus.Completed`.

---

## 6. Các bước triển khai cho Codex (Implementation Steps)

- [ ] **Bước 1:** Định nghĩa 6 thực thể mới (`SalesOrder`, `SalesOrderItem`, `PurchaseRequest`, `PurchaseRequestItem`, `PurchaseOrder`, `PurchaseOrderItem`), bổ sung `PurchaseOrderId` vào `GoodsReceipt` và `SalesOrderId` vào `GoodsIssue`. Cập nhật `ApplicationDbContext` và chạy EF Migration.
- [ ] **Bước 2:** Xây dựng các dịch vụ `SalesOrderService`, `PurchaseRequestService`, `PurchaseOrderService` cùng các kiểm thử Unit Test tương ứng trong `WmsMes.Tests`.
- [ ] **Bước 3:** Cập nhật `InventoryService.cs` để theo dõi `ReceivedQty` trên PO và `DeliveredQty` trên SO khi hoàn tất phiếu kho.
- [ ] **Bước 4:** Xây dựng `SalesOrderController.cs` và `PurchaseOrderController.cs` cùng các màn hình View MVC (Index, Create, Details).
- [ ] **Bước 5:** Nâng cấp `CreateReceipt.cshtml` và `CreateIssue.cshtml` hỗ trợ chọn PO/SO để nạp dữ liệu tự động.
- [ ] **Bước 6:** Cập nhật màn hình Kế hoạch sản xuất bổ sung nút "Tạo Yêu cầu mua hàng từ MRP" và chèn menu Mua hàng/Bán hàng trên Sidebar Layout.
