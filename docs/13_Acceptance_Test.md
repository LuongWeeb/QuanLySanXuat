# 13_Acceptance_Test

# Acceptance Test

## Mục tiêu

Định nghĩa các tiêu chí nghiệm thu theo chuẩn Given / When / Then.

---

## UC-01 Đăng nhập

**Given**
- Người dùng có tài khoản hợp lệ.

**When**
- Nhập đúng tài khoản và mật khẩu.

**Then**
- Đăng nhập thành công và chuyển đến Dashboard.

---

## UC-02 Nhập kho

**Given**
- SKU tồn tại.

**When**
- Tạo Goods Receipt hợp lệ.

**Then**
- Sinh StockTransaction và cập nhật StockBalance.

---

## UC-03 Tạo Work Order

Given
- BOM và Routing đã được cấu hình.

When
- Planner tạo Work Order.

Then
- Work Order ở trạng thái Draft.

---

## UC-04 Approve Work Order

Given
- Work Order ở trạng thái Pending.

When
- Manager phê duyệt.

Then
- Sinh Material Reservation.

---

## UC-05 Production

Given
- Work Order đã Approved.

When
- Worker hoàn thành sản xuất.

Then
- Ghi nhận Qty OK / Reject / Rework.

---

## UC-06 Backflush

Given
- Production Completed.

When
- Thực hiện Backflush.

Then
- Trừ tồn kho và sinh StockTransaction.

---

## UC-07 QC

Given
- Lot chờ kiểm tra.

When
- QC chọn PASS.

Then
- Cho phép nhập kho thành phẩm.

---

## UC-08 Traceability

Given
- Có Lot thành phẩm.

When
- Thực hiện truy vết.

Then
- Hiển thị đầy đủ nguyên vật liệu và Work Order.

---

## UC-09 Dashboard

Given
- Có dữ liệu phát sinh.

When
- Mở Dashboard.

Then
- Hiển thị KPI và dữ liệu realtime.

---

## Acceptance Checklist

- Đăng nhập thành công.
- Không tồn kho âm.
- Một Work Order sinh một Lot.
- QC PASS mới nhập kho.
- Dashboard cập nhật realtime.
- Báo cáo xuất Excel/PDF thành công.

---

## AI Notes

Mỗi Use Case nên có Integration Test và User Acceptance Test (UAT).
