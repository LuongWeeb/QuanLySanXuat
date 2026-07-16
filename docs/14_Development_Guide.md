# 14_Development_Guide

# Development Guide

## Mục tiêu

Hướng dẫn phát triển hệ thống WMS + MES theo kiến trúc thống nhất.

---

# Cấu trúc dự án

- Controllers
- Services
- Repositories
- Domain
- Infrastructure
- Data
- DTOs
- ViewModels
- Migrations

---

# Coding Convention

- PascalCase: Class, Method
- camelCase: Variable
- UPPER_CASE: Constants
- Async hậu tố `Async`

---

# Kiến trúc

Controller
→ Service
→ Repository
→ DbContext
→ SQL Server

Không truy cập DbContext trực tiếp từ Controller.

---

# Entity Framework Core

- Code First
- Migration
- Navigation Properties
- Transaction cho nghiệp vụ quan trọng

---

# Validation

- Data Annotation
- Fluent Validation (nếu áp dụng)
- Business Rule tại Service Layer

---

# Logging

Ghi AuditLog cho:
- Đăng nhập
- CRUD
- Approve
- Backflush
- QC

---

# Error Handling

- Dùng Exception Middleware.
- Trả về HTTP Status phù hợp.
- Không trả stack trace cho client.

---

# Security

- JWT Authentication
- Role + Permission
- HTTPS
- Chống CSRF (MVC)
- Kiểm tra quyền ở Controller và Service

---

# Performance

- Phân trang.
- Lazy/Eager Loading hợp lý.
- Index cho khóa tìm kiếm.
- Cache dữ liệu ít thay đổi.

---

# Git Workflow

main
→ develop
→ feature/*
→ pull request
→ review
→ merge

---

# AI Notes

AI nên:
- Sinh Repository trước.
- Sau đó Service.
- Tiếp theo Controller.
- Cuối cùng View/API.

Luôn tuân thủ SOLID, Separation of Concerns và Business Rules trong `10_Business_Rules.md`.
