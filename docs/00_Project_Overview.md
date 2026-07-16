# 00_Project_Overview

# Tổng quan dự án

## 1. Giới thiệu

Hệ thống Warehouse & Manufacturing Execution System (WMS + MES) là phần mềm quản lý kho và sản xuất dành cho doanh nghiệp sản xuất vừa và nhỏ (SME).

Mục tiêu là số hóa toàn bộ quy trình từ nhập nguyên vật liệu, lập kế hoạch sản xuất, thực thi lệnh sản xuất, kiểm tra chất lượng, nhập kho thành phẩm và báo cáo.

---

# 2. Mục tiêu

- Quản lý tồn kho theo thời gian thực.
- Quản lý BOM nhiều cấp.
- Quản lý Routing.
- Quản lý Work Order.
- Backflush vật tư.
- Reservation vật tư.
- Quản lý Lot.
- Truy vết hai chiều.
- Dashboard Realtime.
- Báo cáo KPI.

---

# 3. Kiến trúc hệ thống

```
Planning
    │
MRP
    │
Work Order
    │
Reservation
    │
Production
    │
Backflush
    │
QC
    │
Finished Goods
    │
Shipment
```

---

# 4. Các module

- Warehouse Management
- Manufacturing Execution
- Quality Control
- Inventory
- Reporting
- Administration

---

# 5. Vai trò người dùng

- Administrator
- Production Manager
- Planner
- Warehouse Staff
- QC Staff
- Worker
- Director

---

# 6. Công nghệ

- ASP.NET Core MVC (.NET 8)
- Entity Framework Core
- SQL Server
- SignalR
- Bootstrap
- Razor Views

---

# 7. Nguyên tắc nghiệp vụ

- One Work Order = One Lot
- Backflush là cơ chế mặc định
- Reservation khi duyệt lệnh
- Không cho tồn kho âm
- FEFO/FIFO khi xuất
- QC trước khi nhập thành phẩm

---

# 8. Luồng nghiệp vụ tổng quát

1. Nhập nguyên vật liệu
2. Khai báo BOM
3. Khai báo Routing
4. Lập kế hoạch
5. Chạy MRP
6. Tạo Work Order
7. Duyệt Work Order
8. Giữ chỗ vật tư
9. Thực thi sản xuất
10. Backflush
11. QC
12. Nhập kho thành phẩm
13. Báo cáo

---

# 9. Tài liệu liên quan

Tiếp tục đọc:

- 01_Business_Domain.md
- 02_System_Architecture.md
- 03_User_Roles.md
- 04_Database_Model.md
- 05_Warehouse_Module.md
- 06_Production_Module.md
- 07_QC_Module.md
- 08_Report_Module.md
- 09_Workflow.md
- 10_Business_Rules.md
- 11_API_Design.md
- 12_Entity_Relationship.md
- 13_Acceptance_Test.md
- 14_Development_Guide.md
