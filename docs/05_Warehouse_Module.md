# 05_Warehouse_Module

# Warehouse Management Module (WMS)

## Mục tiêu

Phân hệ Warehouse Management (WMS) chịu trách nhiệm quản lý toàn bộ vòng đời của hàng hóa trong kho, từ khi nhập kho, lưu trữ, điều chuyển, xuất kho đến kiểm kê và theo dõi tồn kho theo thời gian thực.

---

# 1. Phạm vi chức năng

Phân hệ bao gồm:

- Quản lý SKU
- Quản lý Đơn vị tính (UOM)
- Quản lý Nhà kho
- Quản lý Khu (Zone)
- Quản lý Vị trí (Location)
- Quản lý Lot
- Nhập kho
- Xuất kho
- Điều chuyển
- Kiểm kê
- Tồn kho Realtime
- Dashboard kho
- Cảnh báo tồn kho

---

# 2. Cấu trúc kho

Một nhà kho vật lý được chia thành ba khu logic:

- Material Zone (Nguyên vật liệu)
- WIP Zone (Bán thành phẩm)
- Finished Goods Zone (Thành phẩm)

Business Rules

- Một Location chỉ thuộc một Zone.
- Một Zone chỉ thuộc một Warehouse.
- Một Warehouse có nhiều Zone.

---

# 3. Quản lý SKU

Thông tin:

- SKU Code
- SKU Name
- Product Type
- Base UOM
- IsManufactured
- Lot Tracking
- Shelf Life

Validation

- SKU Code duy nhất.
- Base UOM bắt buộc.
- Thành phẩm tự sản xuất phải có BOM.

---

# 4. Nhập kho (Goods Receipt)

Các loại nhập:

- Purchase Receipt
- Production Receipt
- Return Receipt
- Adjustment Receipt

Workflow

Supplier
→ Receipt
→ QC (nếu cần)
→ Stock Transaction
→ Stock Balance Update

Business Rules

- SKU quản lý Lot phải nhập Lot.
- SKU quản lý HSD phải nhập Expiry Date.
- Receipt luôn sinh Stock Transaction.

---

# 5. Xuất kho (Goods Issue)

Các loại xuất:

- Production Consumption (Backflush)
- Sales Shipment
- Transfer
- Scrap

Business Rules

- Không cho xuất vượt Available Quantity.
- FEFO ưu tiên nếu có hạn sử dụng.
- FIFO áp dụng khi không có HSD.

---

# 6. Điều chuyển kho

Workflow

Source Location
→ Transfer
→ Destination Location

Business Rules

- Tạo đồng thời một Transaction Out và một Transaction In.
- Không thay đổi Lot.

---

# 7. Kiểm kê

Các loại:

- Cycle Count
- Full Inventory
- Emergency Count

Business Rules

- Khi kiểm kê chuyển Qty sang trạng thái On Hold.
- Điều chỉnh tồn phải được phê duyệt.

---

# 8. Reservation

Reservation được tạo khi Work Order được Approve.

Stock

Available

↓

Reserved

↓

Backflush

↓

Consumed

Business Rules

- Reservation không làm giảm Physical Stock.
- Chỉ giảm Available Quantity.

---

# 9. Backflush

Backflush tự động:

- Chọn Lot theo FEFO
- Trừ tồn
- Sinh Transaction
- Cập nhật LotGenealogy

Không cần Issue thủ công.

---

# 10. Realtime Inventory

Theo dõi:

- Qty Available
- Qty Reserved
- Qty On Hold

Cập nhật thông qua SignalR.

---

# 11. Inventory Warning

Bao gồm:

- Min Stock
- Max Stock
- Expiry Warning
- Slow Moving

---

# 12. Use Cases

UC-WMS-01 Nhập NVL

UC-WMS-02 Xuất thành phẩm

UC-WMS-03 Điều chuyển

UC-WMS-04 Kiểm kê

UC-WMS-05 Reservation

UC-WMS-06 Backflush

UC-WMS-07 Dashboard

---

# 13. Business Rules

BR-WMS-001 SKU Code duy nhất.

BR-WMS-002 Không tồn kho âm.

BR-WMS-003 FEFO cho hàng có HSD.

BR-WMS-004 FIFO cho hàng không HSD.

BR-WMS-005 Reservation chỉ tạo khi Work Order Approved.

BR-WMS-006 Backflush chỉ thực hiện khi Production Completed.

BR-WMS-007 Mọi thay đổi tồn kho đều sinh Stock Transaction.

BR-WMS-008 Lot được giữ xuyên suốt vòng đời sản phẩm.

---

# AI Notes

AI nên coi Warehouse là nguồn dữ liệu trung tâm của toàn hệ thống.

Các module Production, QC và Reporting đều phụ thuộc trực tiếp vào dữ liệu tồn kho, Lot và StockTransaction.
