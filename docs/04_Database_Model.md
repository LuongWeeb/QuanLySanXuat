# 04_Database_Model

# Database Model

## Mục tiêu

Mô tả mô hình dữ liệu cốt lõi của hệ thống WMS + MES để AI hiểu các Entity, quan hệ và quy tắc nghiệp vụ.

---

# Tổng quan

Các nhóm Entity:

- Master Data
- Warehouse
- Manufacturing
- Quality
- Reporting
- Security

---

# Product

## Mục đích

Quản lý SKU, nguyên vật liệu và thành phẩm.

## Thuộc tính

- Id
- Code
- Name
- Type
- IsManufactured
- BaseUomId
- MinStock
- MaxStock
- IsLotTracked

## Quan hệ

- Product 1-N BOM
- Product 1-N Lot
- Product 1-N StockBalance

## Business Rules

- Code duy nhất.
- Thành phẩm có thể có BOM.
- BTP là Product có IsManufactured=true.

---

# Lot

## Thuộc tính

- Id
- LotNo
- ProductId
- WorkOrderId
- ManufactureDate
- ExpiryDate
- Qty

## Business Rules

- Một WorkOrder sinh một Lot.
- LotNo là duy nhất.

---

# StockBalance

Theo dõi:

- QtyAvailable
- QtyReserved
- QtyOnHold

Khóa duy nhất:

(Product, Lot, Location)

---

# StockTransaction

Loại giao dịch:

- Receipt
- Issue
- Transfer
- Backflush
- Adjust

Mọi thay đổi tồn kho đều phải sinh Transaction.

---

# BOM

## Header

- Product
- Version
- Effective Date

## Line

- Component
- QtyPer
- ScrapPercent

BOM hỗ trợ nhiều cấp.

---

# Routing

Bao gồm:

- Step
- WorkCenter
- StandardTime
- RequireQC

---

# WorkOrder

Thuộc tính:

- Code
- Product
- Qty
- DueDate
- Status
- BomVersion

Trạng thái:

Draft → Pending → Approved → InProgress → Completed → Closed

---

# WorkOrderStep

Theo dõi:

- StartTime
- EndTime
- QtyOK
- QtyReject
- QtyRework

---

# MaterialReservation

Quản lý lượng vật tư giữ chỗ.

Business Rules

- Sinh khi Approve WorkOrder.
- Giải phóng khi Backflush.

---

# QCInspection

Lưu:

- Result
- CheckedBy
- Note
- Evidence

---

# LotGenealogy

Entity quan trọng nhất.

Quan hệ:

OutputLot

↓

InputLot

Giúp truy vết hai chiều.

---

# Security

Các Entity:

- User
- Role
- Permission
- AuditLog

---

# Quan hệ chính

Product
→ BOM
→ WorkOrder
→ Lot
→ StockTransaction

Lot
→ LotGenealogy

WorkOrder
→ WorkOrderStep

---

# AI Notes

Khi sinh code:

- Mỗi Entity có Repository.
- Entity Framework dùng Code First.
- Navigation Property phải phản ánh đúng quan hệ.
- Transaction bắt buộc cho nghiệp vụ kho và sản xuất.
