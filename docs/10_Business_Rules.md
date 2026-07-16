# 10_Business_Rules

# Business Rules

## Mục tiêu

Tài liệu tập hợp toàn bộ quy tắc nghiệp vụ của hệ thống WMS + MES. Đây là tài liệu tham chiếu chính để thiết kế CSDL, API và xử lý nghiệp vụ.

---

# I. Quy tắc chung

BR-001 Mỗi Product có mã (SKU Code) duy nhất.

BR-002 Không cho phép tồn kho âm.

BR-003 Mọi thay đổi tồn kho đều phải sinh StockTransaction.

BR-004 Mọi thao tác quan trọng phải ghi AuditLog.

---

# II. Quy tắc Warehouse

BR-WMS-001 Một Location chỉ thuộc một Zone.

BR-WMS-002 Một Zone chỉ thuộc một Warehouse.

BR-WMS-003 SKU quản lý lô bắt buộc nhập Lot.

BR-WMS-004 SKU có hạn sử dụng bắt buộc nhập Expiry Date.

BR-WMS-005 FEFO áp dụng cho hàng có HSD.

BR-WMS-006 FIFO áp dụng cho hàng không có HSD.

BR-WMS-007 Kiểm kê chênh lệch phải được phê duyệt trước khi điều chỉnh.

---

# III. Quy tắc Production

BR-MES-001 Một Work Order chỉ tạo đúng một Lot thành phẩm.

BR-MES-002 Chỉ Work Order đã Approve mới được sản xuất.

BR-MES-003 Không được thay đổi BOM sau khi Work Order được duyệt.

BR-MES-004 Reservation được tạo khi Approve Work Order.

BR-MES-005 Reservation chỉ giảm Available Quantity, không giảm Physical Stock.

BR-MES-006 Backflush thực hiện khi báo hoàn thành sản xuất.

---

# IV. Quy tắc BOM & Routing

BR-BOM-001 Một Product có thể có nhiều phiên bản BOM.

BR-BOM-002 Chỉ một BOM được Active tại một thời điểm.

BR-ROT-001 Routing phải có ít nhất một công đoạn.

BR-ROT-002 Thứ tự công đoạn phải tăng dần.

---

# V. Quy tắc QC

BR-QC-001 Chỉ QC được kết luận PASS / REJECT / REWORK.

BR-QC-002 Chỉ Lot PASS mới được nhập kho thành phẩm.

BR-QC-003 REJECT bắt buộc có lý do.

BR-QC-004 REWORK phải chỉ rõ công đoạn quay lại.

---

# VI. Quy tắc Lot & Traceability

BR-LOT-001 Lot Number là duy nhất.

BR-LOT-002 Lot phải liên kết với Product.

BR-LOT-003 Lot thành phẩm liên kết với Work Order.

BR-LOT-004 LotGenealogy lưu quan hệ Input Lot và Output Lot.

BR-LOT-005 Hệ thống phải hỗ trợ truy vết xuôi và truy vết ngược.

---

# VII. Quy tắc Reporting

BR-RPT-001 Dashboard cập nhật theo thời gian thực.

BR-RPT-002 Báo cáo chỉ đọc, không chỉnh sửa dữ liệu.

BR-RPT-003 Giá thành được tính theo Work Order.

---

# VIII. Quy tắc Phân quyền

BR-SEC-001 Chỉ Admin được quản lý người dùng.

BR-SEC-002 Chỉ Manager được Approve Work Order.

BR-SEC-003 Director chỉ có quyền xem báo cáo.

BR-SEC-004 Mọi API phải kiểm tra Role và Permission.

---

# AI Notes

Khi sinh mã nguồn:

- Kiểm tra Business Rules tại Service Layer.
- Dùng Transaction cho nghiệp vụ kho và sản xuất.
- Không đặt Business Rules trong giao diện (UI).
