# 11_API_Design

# API Design

## Mục tiêu

Định nghĩa chuẩn REST API cho hệ thống WMS + MES nhằm đảm bảo các module giao tiếp nhất quán, dễ mở rộng và dễ tích hợp.

---

# 1. Nguyên tắc

- RESTful API
- JSON Request/Response
- Stateless
- Versioning: /api/v1
- JWT Authentication
- HTTPS

---

# 2. Chuẩn URL

GET    /api/v1/products

GET    /api/v1/products/{id}

POST   /api/v1/products

PUT    /api/v1/products/{id}

DELETE /api/v1/products/{id}

---

# 3. Authentication

POST /api/v1/auth/login

POST /api/v1/auth/refresh

POST /api/v1/auth/logout

Sử dụng JWT Bearer Token.

---

# 4. Warehouse API

- GET /warehouses
- GET /inventory
- POST /goods-receipts
- POST /goods-issues
- POST /transfers
- POST /stocktakes

---

# 5. Production API

- POST /mrp/run
- POST /work-orders
- PUT /work-orders/{id}/approve
- PUT /work-orders/{id}/start
- PUT /work-orders/{id}/complete

---

# 6. QC API

- GET /qc/checklists
- POST /qc/inspections
- PUT /qc/{id}/pass
- PUT /qc/{id}/reject
- PUT /qc/{id}/rework

---

# 7. Reporting API

- GET /reports/inventory
- GET /reports/production
- GET /reports/quality
- GET /reports/kpi
- GET /reports/traceability

---

# 8. HTTP Status

200 OK

201 Created

204 No Content

400 Bad Request

401 Unauthorized

403 Forbidden

404 Not Found

409 Conflict

500 Internal Server Error

---

# 9. Response Format

Success

{
  "success": true,
  "data": {}
}

Error

{
  "success": false,
  "message": "...",
  "errors": []
}

---

# 10. Validation Rules

- Validate ModelState.
- Kiểm tra Role và Permission.
- Kiểm tra Business Rules tại Service Layer.

---

# 11. AI Notes

- Controller chỉ xử lý HTTP.
- Business Logic nằm trong Service.
- Repository chỉ truy cập dữ liệu.
- API phải hỗ trợ phân trang, lọc và sắp xếp khi trả về danh sách.
