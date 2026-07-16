# 06_Production_Module

# Production Module (Manufacturing Execution System - MES)

## Mục tiêu

Phân hệ Production chịu trách nhiệm quản lý toàn bộ quy trình sản xuất, từ lập kế hoạch đến khi tạo thành phẩm và nhập kho.

---

# 1. Phạm vi chức năng

- Production Planning
- Material Requirement Planning (MRP)
- Bill of Materials (BOM)
- Routing
- Work Center
- Work Order
- Material Reservation
- Production Execution
- Backflush
- Lot Generation
- Production Completion

---

# 2. Quy trình tổng thể

```text
Sales Forecast
      │
Production Planning
      │
MRP
      │
Create Work Order
      │
Approve Work Order
      │
Material Reservation
      │
Production Execution
      │
Backflush
      │
QC Inspection
      │
Finished Goods Receipt
```

---

# 3. Production Planning

Mục tiêu:

- Xác định nhu cầu sản xuất.
- Lập kế hoạch theo ngày/tuần/tháng.
- Cân đối năng lực sản xuất.

Đầu vào:

- Forecast
- Sales Order
- Current Inventory
- Safety Stock

Đầu ra:

- Planned Production

---

# 4. Material Requirement Planning (MRP)

MRP tính toán lượng nguyên vật liệu cần thiết dựa trên:

- BOM
- Tồn kho hiện tại
- Reservation
- Planned Orders

Kết quả:

- Danh sách vật tư thiếu
- Đề xuất mua
- Đề xuất sản xuất

Business Rules

- Chỉ tính vật tư đang hoạt động.
- Không tính vật tư đã ngừng sử dụng.
- Tính theo BOM Version có hiệu lực.

---

# 5. Bill of Materials (BOM)

BOM định nghĩa cấu trúc sản phẩm.

Bao gồm:

- BOM Header
- BOM Lines
- Version
- Effective Date

Hỗ trợ:

- Multi-level BOM
- Scrap Percent
- Alternate Components

Business Rules

- Một sản phẩm có nhiều phiên bản BOM.
- Chỉ một phiên bản được Active tại một thời điểm.

---

# 6. Routing

Routing mô tả quy trình sản xuất.

Thông tin:

- Step Number
- Step Name
- Work Center
- Standard Time
- Require QC

Ví dụ:

1. Chuẩn bị nguyên liệu
2. Trộn
3. Gia công
4. Đóng gói
5. QC

---

# 7. Work Order

Work Order là trung tâm của toàn bộ phân hệ MES.

Thuộc tính:

- Code
- Product
- Quantity
- Due Date
- BOM Version
- Routing Version
- Status

Lifecycle

Draft

↓

Pending Approval

↓

Approved

↓

In Progress

↓

Completed

↓

Closed

Business Rules

- Một Work Order chỉ tạo một Lot đầu ra.
- Không chỉnh sửa BOM sau khi Approve.

---

# 8. Material Reservation

Khi Work Order được Approve:

Available Stock

↓

Reserved Stock

↓

Production

↓

Consumed

Business Rules

- Reservation không làm giảm Physical Stock.
- Reservation chỉ giữ chỗ.

---

# 9. Production Execution

Công nhân thực hiện theo Routing.

Mỗi công đoạn ghi nhận:

- Start Time
- End Time
- Qty OK
- Qty Reject
- Qty Rework

Dashboard cập nhật theo thời gian thực.

---

# 10. Backflush

Backflush tự động:

- Trừ nguyên vật liệu
- Sinh Stock Transaction
- Sinh Lot Genealogy
- Cập nhật Inventory

Không cần Issue thủ công.

---

# 11. Lot Generation

Sau khi hoàn thành Work Order:

Work Order

↓

Generate Lot

↓

Finished Goods

↓

Warehouse

Ví dụ:

SP001-20260715-000001

Business Rules

- Lot Number duy nhất.
- Một Work Order chỉ sinh một Lot.

---

# 12. Production Completion

Điều kiện hoàn thành:

- Hoàn thành tất cả công đoạn.
- QC đạt.
- Backflush thành công.

Sau đó:

- Sinh Lot
- Nhập kho thành phẩm
- Đóng Work Order

---

# 13. Use Cases

UC-MES-01 Create BOM

UC-MES-02 Create Routing

UC-MES-03 Run MRP

UC-MES-04 Create Work Order

UC-MES-05 Approve Work Order

UC-MES-06 Execute Production

UC-MES-07 Backflush

UC-MES-08 Complete Work Order

UC-MES-09 Generate Lot

---

# 14. Business Rules

BR-MES-001 Một Work Order sinh đúng một Lot.

BR-MES-002 Không được Approve khi thiếu vật tư.

BR-MES-003 Reservation tạo sau Approve.

BR-MES-004 Backflush thực hiện khi báo hoàn thành.

BR-MES-005 Không cho tồn kho âm.

BR-MES-006 QC bắt buộc trước nhập kho thành phẩm.

BR-MES-007 Lot phải truy vết được toàn bộ nguyên vật liệu.

BR-MES-008 Work Order Closed không được chỉnh sửa.

---

# 15. AI Notes

Đây là phân hệ trung tâm của hệ thống.

AI nên hiểu theo thứ tự:

Planning

↓

MRP

↓

BOM

↓

Routing

↓

Work Order

↓

Reservation

↓

Execution

↓

Backflush

↓

QC

↓

Finished Goods

Tất cả báo cáo và truy vết đều được xây dựng từ dữ liệu phát sinh trong các bước trên.
