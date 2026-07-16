# 01_Business_Domain

# Business Domain

## Mục tiêu

Tài liệu này mô tả toàn bộ nghiệp vụ của hệ thống WMS + MES để AI hiểu logic vận hành trước khi sinh mã nguồn.

---

# Các phân hệ

## 1. Warehouse Management (WMS)

Quản lý:
- SKU
- Nguyên vật liệu
- Bán thành phẩm
- Thành phẩm
- Lô (Lot)
- Hạn sử dụng
- Vị trí lưu kho
- Nhập
- Xuất
- Điều chuyển
- Kiểm kê
- Cảnh báo tồn kho

Business Rules

- SKU là duy nhất.
- Một vị trí chỉ thuộc một khu.
- Không cho tồn kho âm.
- FEFO áp dụng với hàng có HSD.
- FIFO áp dụng với hàng không có HSD.

---

## 2. Manufacturing Execution (MES)

Bao gồm:

- BOM
- Routing
- Planning
- MRP
- Work Order
- Reservation
- Backflush
- Production Execution

Business Rules

- Một Work Order sinh một Lot.
- BOM nhiều cấp.
- Backflush là mặc định.
- Reservation khi duyệt lệnh.
- Chỉ Work Order Approved mới được sản xuất.

---

## 3. Quality Control

Quản lý:

- QC Checklist
- QC Result
- Reject
- Rework
- Pass

Business Rules

- Chỉ Lot PASS mới nhập kho thành phẩm.
- Reject phải lưu lý do.
- Rework quay về công đoạn phù hợp.

---

## 4. Reporting

Bao gồm:

- Dashboard
- Inventory Report
- Production Report
- Bottleneck
- KPI
- Cost Report
- Traceability Report

---

## 5. Traceability

Hệ thống hỗ trợ truy vết hai chiều.

Backward Trace

Finished Goods
→ Work Order
→ Lot
→ BOM
→ Raw Materials

Forward Trace

Raw Material
→ Lot
→ Work Order
→ Finished Goods

LotGenealogy là bảng trung tâm cho toàn bộ chức năng truy vết.

---

## Luồng nghiệp vụ tổng thể

1. Nhập nguyên vật liệu
2. Quản lý tồn
3. Khai báo BOM
4. Khai báo Routing
5. Lập kế hoạch
6. Chạy MRP
7. Tạo Work Order
8. Approve
9. Reservation
10. Production
11. Backflush
12. QC
13. Finished Goods
14. Shipment
15. Reporting

---

## Thuật ngữ

- SKU
- BOM
- Routing
- Work Order
- Reservation
- Backflush
- Lot
- FEFO
- FIFO
- MRP
- OTD
- Yield
- Bottleneck
- QC

---

## AI Notes

AI nên đọc tài liệu theo thứ tự:

README.md

↓

00_Project_Overview.md

↓

01_Business_Domain.md

↓

Database

↓

Warehouse

↓

Production

↓

Business Rules

Điều này giúp AI hiểu đầy đủ nghiệp vụ trước khi sinh code.
