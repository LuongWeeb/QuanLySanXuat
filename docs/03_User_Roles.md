# 03_User_Roles

# User Roles & Access Control

## Mục tiêu

Tài liệu mô tả đầy đủ các vai trò (Actors), trách nhiệm, quyền hạn và phạm vi truy cập trong hệ thống WMS + MES.

---

# Tổng quan

Hệ thống áp dụng mô hình RBAC (Role-Based Access Control), kết hợp Permission-Based Authorization để phân quyền chi tiết.

Các vai trò chính:

1. System Administrator
2. Production Manager
3. Production Planner
4. Warehouse Staff
5. Production Worker / Team Leader
6. Quality Control (QC)
7. Director / Management

---

# 1. System Administrator

## Trách nhiệm

- Quản lý người dùng
- Quản lý vai trò
- Quản lý quyền
- Cấu hình hệ thống
- Theo dõi Audit Log

## Quyền

- Toàn quyền hệ thống
- CRUD User
- CRUD Role
- CRUD Permission
- Xem Dashboard
- Quản lý cấu hình

---

# 2. Production Manager

## Trách nhiệm

- Quản lý sản xuất
- Duyệt Work Order
- Quản lý BOM
- Quản lý Routing
- Theo dõi tiến độ

## Quyền

- Approve Work Order
- Xem Dashboard
- Quản lý BOM
- Quản lý Routing
- Theo dõi KPI

---

# 3. Production Planner

## Trách nhiệm

- Lập kế hoạch
- Chạy MRP
- Tạo Work Order

## Quyền

- Create Planning
- Run MRP
- Create Work Order
- Xem tồn kho

---

# 4. Warehouse Staff

## Trách nhiệm

- Nhập kho
- Xuất kho
- Điều chuyển
- Kiểm kê

## Quyền

- Goods Receipt
- Goods Issue
- Transfer
- Stock Take
- Inventory Lookup

---

# 5. Production Worker / Team Leader

## Trách nhiệm

- Báo bắt đầu công đoạn
- Báo hoàn thành
- Khai báo số lượng đạt/lỗi

## Quyền

- Execute Operation
- Update Progress
- Scan QR Work Order

---

# 6. Quality Control (QC)

## Trách nhiệm

- Kiểm tra chất lượng
- Pass
- Reject
- Rework

## Quyền

- QC Inspection
- Upload Evidence
- Approve Finished Lot

---

# 7. Director / Management

## Trách nhiệm

- Theo dõi hoạt động doanh nghiệp

## Quyền

- Dashboard
- KPI
- Cost Report
- Inventory Report
- Traceability Report

---

# Ma trận RBAC

| Chức năng | Admin | Manager | Planner | Warehouse | Worker | QC | Director |
|-----------|:----:|:------:|:-------:|:---------:|:------:|:--:|:--------:|
| User Management | ✔ | | | | | | |
| BOM | ✔ | ✔ | | | | | |
| Routing | ✔ | ✔ | | | | | |
| Planning | ✔ | ✔ | ✔ | | | | |
| MRP | ✔ | ✔ | ✔ | | | | |
| Work Order | ✔ | ✔ | ✔ | | | | |
| Inventory | ✔ | | | ✔ | | | ✔ |
| QC | ✔ | | | | | ✔ | |
| Dashboard | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |

---

# Business Rules

- Chỉ Admin được quản lý User.
- Chỉ Manager được duyệt Work Order.
- Planner không được Approve.
- Worker không được sửa BOM.
- QC không được sửa tồn kho.
- Director chỉ có quyền đọc dữ liệu.

---

# AI Notes

Khi sinh code:

- Controller phải kiểm tra Role.
- Service phải kiểm tra Permission.
- UI chỉ hiển thị chức năng phù hợp với từng vai trò.
