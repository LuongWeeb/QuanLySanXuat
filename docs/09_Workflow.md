# 09_Workflow

# End-to-End Business Workflow

## Mục tiêu

Tài liệu mô tả toàn bộ luồng nghiệp vụ của hệ thống WMS + MES từ khi tiếp nhận nguyên vật liệu đến khi hoàn thành sản phẩm và tạo báo cáo.

---

# Workflow tổng thể

Supplier
↓
Goods Receipt
↓
Warehouse Inventory
↓
Production Planning
↓
MRP
↓
Create Work Order
↓
Approve Work Order
↓
Material Reservation
↓
Production Execution
↓
Backflush
↓
QC Inspection
↓
Finished Goods Receipt
↓
Sales / Shipment
↓
Reporting

---

# Workflow 01 - Nhập nguyên vật liệu

1. Nhà cung cấp giao hàng.
2. Thủ kho tạo phiếu nhập.
3. Khai báo Lot và HSD.
4. Kiểm tra số lượng.
5. Sinh Stock Transaction.
6. Cập nhật Stock Balance.

---

# Workflow 02 - Lập kế hoạch sản xuất

1. Planner nhập Forecast hoặc Sales Order.
2. Kiểm tra tồn kho.
3. Chạy MRP.
4. Xác định vật tư thiếu.
5. Đề xuất mua hoặc sản xuất.

---

# Workflow 03 - Tạo Work Order

1. Planner tạo Work Order.
2. Chọn BOM Version.
3. Chọn Routing.
4. Nhập số lượng.
5. Chuyển trạng thái Draft.

---

# Workflow 04 - Duyệt Work Order

1. Production Manager xem Work Order.
2. Kiểm tra vật tư.
3. Approve.
4. Sinh Material Reservation.

---

# Workflow 05 - Thực hiện sản xuất

1. Công nhân bắt đầu công đoạn.
2. Ghi Start Time.
3. Thực hiện sản xuất.
4. Ghi End Time.
5. Cập nhật Qty OK, Reject, Rework.

---

# Workflow 06 - Backflush

1. Báo hoàn thành.
2. Hệ thống chọn Lot theo FEFO.
3. Trừ nguyên vật liệu.
4. Sinh Stock Transaction.
5. Sinh LotGenealogy.

---

# Workflow 07 - Kiểm tra chất lượng

1. QC kiểm tra.
2. PASS -> Nhập kho thành phẩm.
3. REJECT -> Hàng lỗi.
4. REWORK -> Quay lại sản xuất.

---

# Workflow 08 - Nhập kho thành phẩm

1. Sinh Lot thành phẩm.
2. Tạo phiếu nhập.
3. Cập nhật tồn kho.
4. Đóng Work Order.

---

# Workflow 09 - Truy vết

Backward Trace:
Finished Goods -> Work Order -> Raw Material

Forward Trace:
Raw Material -> Finished Goods

---

# Workflow 10 - Báo cáo

1. Dashboard realtime.
2. KPI.
3. Inventory Report.
4. Production Report.
5. QC Report.
6. Cost Report.

---

# Business Rules

- Không tồn kho âm.
- Một Work Order sinh một Lot.
- QC PASS mới nhập kho.
- Backflush tự động.
- Reservation khi Approve.

---

# AI Notes

AI nên coi các workflow trên là trình tự chuẩn để thiết kế Controller, Service và Transaction trong hệ thống.
