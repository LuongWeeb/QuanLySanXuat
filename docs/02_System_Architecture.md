# 02_System_Architecture

# Kiến trúc hệ thống

## Mục tiêu

Tài liệu này mô tả kiến trúc kỹ thuật của hệ thống WMS + MES để AI hiểu cấu trúc dự án trước khi sinh mã nguồn.

---

# Kiến trúc tổng thể

```
Client (Browser)
      │
ASP.NET Core MVC
      │
Controllers
      │
Application Services
      │
Domain Layer
      │
Repository
      │
Entity Framework Core
      │
SQL Server
```

SignalR hoạt động song song để cập nhật dữ liệu realtime.

---

# Các tầng

## Presentation Layer

Chứa:

- Controllers
- Razor Views
- ViewModels
- Validation
- Authentication

Nhiệm vụ:

- Nhận Request
- Validate dữ liệu
- Gọi Service
- Trả View hoặc JSON

---

## Application Layer

Chứa toàn bộ nghiệp vụ ứng dụng.

Ví dụ:

- InventoryService
- WorkOrderService
- BomService
- PlanningService
- ReportService
- QcService

Không truy cập trực tiếp Database.

---

## Domain Layer

Bao gồm:

- Entity
- Business Rules
- Enum
- Value Objects

Ví dụ:

- Product
- Lot
- WorkOrder
- Routing
- StockTransaction

Đây là nơi chứa logic nghiệp vụ cốt lõi.

---

## Infrastructure Layer

Bao gồm:

- DbContext
- Repository
- SignalR Hub
- Export Excel/PDF
- Logging

Ví dụ:

- ApplicationDbContext
- ProductRepository
- LotRepository
- WorkOrderRepository

---

# Entity Framework Core

Sử dụng:

- Code First
- Migration
- LINQ
- Transaction

Mỗi Aggregate được quản lý qua Repository.

---

# Dependency Injection

Các Service được đăng ký trong Program.cs.

Ví dụ:

- IProductService
- IInventoryService
- IWorkOrderService
- IQcService

---

# SignalR

Realtime cho:

- Inventory
- Dashboard
- Production Progress
- Notifications

---

# Authentication

Sử dụng:

- ASP.NET Core Identity
- Role Based Authorization
- Permission Based Authorization

---

# Logging

Audit Log ghi nhận:

- User
- Action
- Entity
- Timestamp
- Before
- After

---

# Coding Principles

- Separation of Concerns
- SOLID
- Dependency Injection
- Repository Pattern
- Service Layer Pattern
- Clean Architecture (tham khảo)

---

# AI Notes

Thứ tự AI nên đọc:

1. Project Overview
2. Business Domain
3. System Architecture
4. Database Model
5. Warehouse Module
6. Production Module

Sau đó mới bắt đầu sinh code.
