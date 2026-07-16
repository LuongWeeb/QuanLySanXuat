# 07_QC_Module

# Quality Control (QC)

## Mục tiêu

Phân hệ QC đảm bảo chỉ các lô đạt tiêu chuẩn mới được nhập kho thành phẩm, đồng thời lưu đầy đủ dữ liệu phục vụ truy vết và cải tiến chất lượng.

---

# 1. Phạm vi

- QC Checklist
- QC Inspection
- Pass / Reject / Rework
- Evidence
- Lot QC
- Quality Report

---

# 2. QC Checklist

Checklist được cấu hình theo:

- Sản phẩm
- Công đoạn
- Work Center

Mỗi checklist gồm:

- Tiêu chí
- Giá trị chuẩn
- Đơn vị đo
- Bắt buộc/Không bắt buộc

---

# 3. QC Inspection

Thông tin lưu:

- Work Order
- Operation
- Lot
- Inspector
- Inspection Time
- Result
- Note
- Evidence

---

# 4. Kết quả QC

## PASS

- Cho phép nhập kho thành phẩm.
- Đóng công đoạn QC.

## REJECT

- Không nhập kho.
- Ghi nguyên nhân.
- Chuyển xử lý hàng lỗi.

## REWORK

- Quay lại công đoạn được chỉ định.
- Theo dõi số lần tái chế.

---

# 5. Evidence

Cho phép lưu:

- Hình ảnh
- Video
- File PDF
- Biên bản
- Ghi chú

---

# 6. Quy trình QC

Production Complete

↓

QC Inspection

↓

PASS ?

├── YES → Finished Goods Receipt

└── NO

    ├── Reject

    └── Rework

---

# 7. Business Rules

BR-QC-001 Chỉ QC được phép kết luận.

BR-QC-002 PASS mới được nhập kho.

BR-QC-003 Reject bắt buộc có lý do.

BR-QC-004 Rework phải chỉ rõ công đoạn quay lại.

BR-QC-005 Evidence được khuyến khích lưu với Reject/Rework.

BR-QC-006 Kết quả QC gắn với Lot để truy vết.

---

# 8. Use Cases

UC-QC-01 Tạo Checklist

UC-QC-02 Kiểm tra Lot

UC-QC-03 Pass

UC-QC-04 Reject

UC-QC-05 Rework

UC-QC-06 Xem lịch sử QC

---

# 9. Báo cáo chất lượng

- Tỷ lệ đạt
- Tỷ lệ lỗi
- Tỷ lệ tái chế
- Lỗi theo công đoạn
- Lỗi theo sản phẩm
- Xu hướng chất lượng

---

# 10. AI Notes

QC là cổng kiểm soát cuối cùng trước khi thành phẩm được nhập kho.

Mọi dữ liệu QC phải liên kết với:

- Work Order
- Work Order Step
- Lot
- Product

để phục vụ truy vết hai chiều và báo cáo chất lượng.
