# Thiết kế Giai đoạn 4: Kiểm soát Chất lượng & Báo cáo (QC & Reporting)

Tài liệu thiết kế chi tiết cho Giai đoạn 4 của hệ thống Quản lý Kho & Điều hành Sản xuất (WMS + MES). Giai đoạn này tập trung triển khai các tính năng quản lý tiêu chuẩn chất lượng (QC Checklist), kiểm tra chất lượng thực tế (QC Inspection), cơ chế khóa kho chờ QC, tính toán giá thành sản xuất thực tế theo Lô (Lot-based Costing), truy vết phả hệ lô đệ quy bằng đồ họa và các báo cáo KPI thời gian thực.

---

## 1. Kiến trúc & Công nghệ bổ sung
* **SVG & CSS**: Sử dụng để vẽ đồ họa sơ đồ cây phả hệ lô đệ quy trực quan trên trình duyệt (dễ thao tác thu phóng và làm nổi bật đường liên kết).
* **Chart.js**: Sử dụng để vẽ các biểu đồ tròn/cột chuyên nghiệp biểu diễn tỷ lệ lỗi (Scrap rate), tỷ lệ đạt (Yield rate), và xu hướng năng suất nhà xưởng.
* **SignalR Quality Hub**: Phát cảnh báo realtime tới cấp quản lý khi phát hiện lô hàng bị lỗi nặng (`REJECT`) hoặc chênh lệch tồn kho kiểm kê quá lớn.
* **EPPlus / ClosedXML**: Thư viện phục vụ kết xuất dữ liệu báo cáo ra file Excel (.xlsx).

---

## 2. Thiết kế Cơ sở dữ liệu (Database Schema)

### 2.1 Quản lý Tiêu chuẩn Chất lượng (QC Configurations)
* **`QCChecklist` (Phiếu tiêu chí QC - Header)**:
  * `Id` (Int, Khóa chính)
  * `ProductId` (Int, Khóa ngoại) -> `Product(Id)`
  * `StepNumber` (Int, Nullable) - Số thứ tự công đoạn cụ thể (nếu là QC công đoạn, để trống nếu là QC thành phẩm cuối cùng)
  * `Name` (Nvarchar, 250) - Tên phiếu tiêu chí
  * `IsActive` (Bool) - Chỉ một phiếu hoạt động cho một sản phẩm/công đoạn tại một thời điểm
* **`QCChecklistItem` (Chi tiết tiêu chí - Lines)**:
  * `Id` (Int, Khóa chính)
  * `QCChecklistId` (Int, Khóa ngoại) -> `QCChecklist(Id)` (Cascade Delete)
  * `ParameterName` (Nvarchar, 150) - Tên tiêu chí cần đo (ví dụ: Độ ẩm, Chiều rộng, Màu sắc)
  * `MinVal` (Decimal, 18, 4, Nullable) - Ngưỡng dưới cho phép
  * `MaxVal` (Decimal, 18, 4, Nullable) - Ngưỡng trên cho phép
  * `Unit` (Varchar, 50) - Đơn vị tính (%, mm, g...)
  * `IsRequired` (Bool) - Có bắt buộc đo đạc hay không

### 2.2 Thực thi kiểm tra chất lượng (QC Inspection)
* **`QCInspection` (Phiếu kết quả QC - Header)**:
  * `Id` (Int, Khóa chính)
  * `WorkOrderId` (Int, Khóa ngoại) -> `WorkOrder(Id)`
  * `LotId` (Int, Khóa ngoại) -> `Lot(Id)` (Lô thành phẩm cần kiểm QC)
  * `InspectionTime` (DateTime) - Thời gian thực hiện kiểm tra
  * `InspectorId` (Varchar, Khóa ngoại) -> `AspNetUsers(Id)` - Nhân viên QC thực hiện
  * `Result` (Enum: `PASS` = 0, `REJECT` = 1, `REWORK` = 2) - Kết luận cuối cùng
  * `Note` (Nvarchar, 500) - Ghi chú lý do nếu lỗi hoặc chỉ định công đoạn làm lại
  * `EvidencePath` (Nvarchar, 500, Nullable) - Đường dẫn ảnh chụp/file đính kèm
* **`QCInspectionLine` (Chi tiết kết quả - Lines)**:
  * `Id` (Int, Khóa chính)
  * `QCInspectionId` (Int, Khóa ngoại) -> `QCInspection(Id)` (Cascade Delete)
  * `ParameterName` (Nvarchar, 150) - Tên tiêu chí
  * `ValueInspected` (Varchar, 250) - Giá trị thực tế nhân viên QC đo được
  * `IsOK` (Bool) - Kết quả tự động đánh giá (Đạt/Không đạt)

### 2.3 Bổ sung phục vụ quản lý Giá thành & Giá trị Lô
* Bổ sung cột **`UnitPrice`** (Decimal, 18, 2) vào bảng **`GoodsReceiptLine`** (Giá mua NVL thực tế từ nhà cung cấp trên từng dòng phiếu).
* Bổ sung cột **`UnitPrice`** (Decimal, 18, 2) vào bảng **`Lot`** (Giá trị của Lô hàng, phục vụ tính giá vốn).

---

## 3. Luồng Nghiệp vụ & Cài đặt Code

### 3.1 Gating khóa kho Chờ QC và Tự động chuyển đổi trạng thái
* Khi Lệnh sản xuất hoàn thành (Phase 3) ➔ Lô thành phẩm tự động sinh ở trạng thái chờ QC và khóa tồn khả dụng: `QtyAvailable = 0` và `QtyOnHold = FinalQty` tại vị trí kiểm QC.
* Nhân viên QC mở phiếu kiểm tra, nhập số liệu thực tế (`ValueInspected`):
  * Hệ thống tự động so khớp: `MinVal <= ValueInspected <= MaxVal` ➔ cập nhật `IsOK`.
* Khi phê duyệt kết quả kiểm tra chất lượng:
  * Mở Transaction.
  * **Nếu kết quả = PASS**: Giải phóng khóa kho, chuyển số lượng `QtyOnHold ➔ QtyAvailable`. Tính giá thành sản xuất thực tế (CostPerUnit) và gán vào `Lot.UnitPrice`.
  * **Nếu kết quả = REJECT**: Giữ nguyên khóa kho `QtyOnHold`, tự động tạo phiếu điều chuyển nội bộ (`StockTransfer`) chuyển lô hàng này sang **Vị trí Cách ly hàng lỗi (Quarantine Location)**.
  * **Nếu kết quả = REWORK**: Chuyển số lượng trở lại WIP Zone của công đoạn làm lại được chỉ định.
  * Commit Transaction.

### 3.2 Thuật toán Tính toán Giá thành Lô thực tế (Lot-based Costing)
* Khi phê duyệt QC PASS, hệ thống chạy hàm tính giá thành:
  * `TotalMaterialCost = Tổng (Số lượng NVL tiêu hao của lô X * Đơn giá mua Lot.UnitPrice của lô X)` (được lấy từ phả hệ `LotGenealogy`).
  * `TotalLaborCost = Tổng (StandardTimeMinutes của từng công đoạn) * Đơn giá giờ công của WorkCenter`.
  * `ProductionCostPerUnit = (TotalMaterialCost + TotalLaborCost) / QtyOK`.
  * Lưu `ProductionCostPerUnit` vào trường `Lot.UnitPrice` của lô thành phẩm để tính giá vốn bán hàng.

### 3.3 Thuật toán Quét phả hệ Lô đệ quy
* **Truy vết ngược (Backward Trace)**: Nhận `OutputLotId` ➔ Truy vấn đệ quy bảng `LotGenealogy` để tìm tất cả `InputLotId` ➔ Nếu là WIP tiếp tục đệ quy tìm gốc NVL ➔ Trả về cấu trúc cây phân cấp JSON.
* **Truy vết xuôi (Forward Trace)**: Nhận `InputLotId` ➔ Tìm tất cả `OutputLotId` trực tiếp và gián tiếp ➔ Trả về cấu trúc cây con cháu JSON.

---

## 4. Thiết kế Giao diện người dùng UI/UX
* **QC Terminal (Màn hình kiểm QC)**:
  * Popup nhập số liệu trực quan, cảnh báo lỗi (NG) bằng màu viền đỏ tự động khi nhập ngoài khoảng cho phép.
  * Hỗ trợ upload ảnh chụp chứng cứ.
* **Báo cáo Truy vết đồ họa (Traceability Viewer)**:
  * Dựng sơ đồ cây phân cấp trực quan bằng SVG. Mỗi Lô hàng là một Node hiển thị màu sắc theo trạng thái kiểm QC. Cho phép click và thu phóng trực quan.
* **Báo cáo Giá thành & Nhập-Xuất-Tồn**:
  * Các bảng dữ liệu sạch sẽ, hỗ trợ phân trang và tìm kiếm nhanh. Tích hợp nút xuất Excel (.xlsx) thông qua thư viện phía Server.
* **Realtime KPI Dashboard**:
  * Hiển thị biểu đồ Yield rate và Scrap rate của xưởng theo thời gian thực (SignalR đẩy số liệu).

---

## 5. Kế hoạch Kiểm tra (Verification Plan)
* **Kiểm thử tự động (Unit Tests)**:
  * Viết unit test tự động so khớp giá trị số QC: Nhập ngoài khoảng ➔ `IsOK = false`, trong khoảng ➔ `IsOK = true`.
  * Viết unit test duyệt QC PASS ➔ xác nhận chuyển `QtyOnHold ➔ QtyAvailable`.
  * Viết unit test duyệt QC REJECT ➔ xác nhận chuyển lô sang Vị trí Cách ly hàng lỗi.
  * Viết unit test thuật toán tính giá thành: Cộng dồn giá thực tế của các lô NVL tiêu thụ và so khớp với kết quả kỳ vọng.
  * Viết unit test thuật toán đệ quy phả hệ lô (xác định đúng cây cha/con).
* **Kiểm thử thủ công**:
  * Tạo một phiếu nhập kho NVL có nhập giá mua (ví dụ: Cát mua giá 5.000đ/kg).
  * Chạy Lệnh WO sản xuất Thành phẩm (ví dụ: Thủy tinh), hoàn thành sản xuất.
  * Vào màn hình QC kiểm tra, nhập số liệu đạt ➔ bấm PASS.
  * Kiểm tra xem tồn kho khả dụng của Thủy tinh có tăng lên không, và Lô Thủy tinh trong DB có đơn giá đúng bằng chi phí Cát tiêu hao cộng chi phí nhân công không.
  * Vào màn hình Truy vết nhập mã lô Thủy tinh vừa làm ➔ Kiểm tra xem sơ đồ cây SVG có hiển thị đúng Lô cát đã tiêu hao ở trên không.
