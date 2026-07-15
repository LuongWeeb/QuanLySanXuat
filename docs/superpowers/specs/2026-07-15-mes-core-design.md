# Thiết kế Giai đoạn 3: Điều hành Sản xuất (MES Core)

Tài liệu thiết kế chi tiết cho Giai đoạn 3 của hệ thống Quản lý Kho & Điều hành Sản xuất (WMS + MES). Giai đoạn này tập trung vào các tính năng khai báo BOM, Quy trình công nghệ (Routing), Lập kế hoạch nhu cầu vật tư (MRP), Quản lý vòng đời Lệnh sản xuất (Work Order State), Giữ chỗ vật tư (Reservation), Ghi nhận tiến độ sản xuất và Trừ tồn tự động (Backflushing).

---

## 1. Kiến trúc & Công nghệ bổ sung
* **SignalR Production Hub**: Sử dụng để cập nhật tiến độ sản xuất theo thời gian thực (phần trăm hoàn thành của từng lệnh sản xuất) lên Dashboard giám sát của quản lý khi công nhân báo hoàn thành từng công đoạn.
* **Quy trình Lệnh đơn cấp (Single-level WO)**: Planner lập lệnh trực tiếp cho từng bán thành phẩm hoặc thành phẩm, không tự động rã lệnh con cấp dưới để đảm bảo tính linh hoạt điều phối của nhà xưởng.
* **Phân bổ và giữ chỗ vật tư (FEFO/FIFO Reservation)**: Khi duyệt lệnh, hệ thống tự động tìm các lô vật tư khả dụng theo FEFO (hết hạn trước giữ trước) hoặc FIFO để thực hiện giữ chỗ, đảm bảo không bị thiếu hàng khi sản xuất.

---

## 2. Thiết kế Cơ sở dữ liệu (Database Schema)

### 2.1 Định mức vật tư & Công nghệ (Master Data)
* **`BOM` (Định mức nguyên liệu - Header)**:
  * `Id` (Int, Khóa chính)
  * `ProductId` (Int, Khóa ngoại) -> `Product(Id)` (Thành phẩm/BTP)
  * `Version` (Varchar, 50) - Ví dụ: "V1.0", "V2.0"
  * `EffectiveDate` (DateTime) - Ngày bắt đầu có hiệu lực
  * `IsActive` (Bool) - Trạng thái kích hoạt (chỉ một BOM active cho một sản phẩm tại một thời điểm)
* **`BOMItem` (Chi tiết BOM - Lines)**:
  * `Id` (Int, Khóa chính)
  * `BomId` (Int, Khóa ngoại) -> `BOM(Id)`
  * `ComponentProductId` (Int, Khóa ngoại) -> `Product(Id)` (Nguyên liệu con)
  * `QtyPer` (Decimal, 18, 4) - Số lượng cần để sản xuất 1 đơn vị sản phẩm cha
  * `ScrapPercent` (Decimal, 5, 2) - Tỷ lệ hao hụt định mức (%)
* **`WorkCenter` (Tổ sản xuất / Máy móc)**:
  * `Id` (Int, Khóa chính)
  * `Code` (Varchar, 50, Unique) - Mã tổ sản xuất
  * `Name` (Nvarchar, 150) - Tên tổ sản xuất
  * `IsActive` (Bool)
* **`Routing` (Quy trình sản xuất - Header)**:
  * `Id` (Int, Khóa chính)
  * `ProductId` (Int, Khóa ngoại) -> `Product(Id)`
  * `Name` (Nvarchar, 250) - Tên quy trình
  * `Version` (Varchar, 50)
  * `IsActive` (Bool)
* **`RoutingStep` (Công đoạn sản xuất - Lines)**:
  * `Id` (Int, Khóa chính)
  * `RoutingId` (Int, Khóa ngoại) -> `Routing(Id)`
  * `StepNumber` (Int) - Thứ tự công đoạn (10, 20, 30...)
  * `StepName` (Nvarchar, 150) - Tên công đoạn (ví dụ: Pha chế, Đóng gói)
  * `WorkCenterId` (Int, Khóa ngoại) -> `WorkCenter(Id)`
  * `StandardTimeMinutes` (Decimal, 18, 2) - Thời gian định mức sản xuất
  * `RequireQC` (Bool) - Có yêu cầu QC hay không

### 2.2 Thực thi & Giao dịch sản xuất
* **`WorkOrder` (Lệnh sản xuất)**:
  * `Id` (Int, Khóa chính)
  * `Code` (Varchar, 50, Unique) - Mã lệnh (ví dụ: WO-20260715-001)
  * `ProductId` (Int, Khóa ngoại) -> `Product(Id)`
  * `Qty` (Decimal, 18, 2) - Số lượng cần sản xuất
  * `DueDate` (DateTime) - Hạn hoàn thành
  * `Status` (Enum: `Draft` = 0, `Pending` = 1, `Approved` = 2, `InProgress` = 3, `Completed` = 4, `Closed` = 5)
  * `BomVersion` (Varchar, 50) - Phiên bản BOM áp dụng khi duyệt
  * `RoutingVersion` (Varchar, 50) - Phiên bản Routing áp dụng khi duyệt
* **`WorkOrderStep` (Tiến độ công đoạn của Lệnh)**:
  * `Id` (Int, Khóa chính)
  * `WorkOrderId` (Int, Khóa ngoại) -> `WorkOrder(Id)` (Cascade Delete)
  * `StepNumber` (Int) - Số thứ tự công đoạn
  * `StepName` (Nvarchar, 150) - Tên công đoạn
  * `WorkCenterId` (Int, Khóa ngoại) -> `WorkCenter(Id)`
  * `StartTime` (DateTime, Nullable) - Thời gian bắt đầu thực tế
  * `EndTime` (DateTime, Nullable) - Thời gian kết thúc thực tế
  * `QtyOK` (Decimal, 18, 2) - Số lượng thành phẩm đạt
  * `QtyReject` (Decimal, 18, 2) - Số lượng phế phẩm
  * `QtyRework` (Decimal, 18, 2) - Số lượng cần làm lại
  * `Status` (Enum: `Pending` = 0, `InProgress` = 1, `Completed` = 2)
* **`MaterialReservation` (Giữ chỗ vật tư)**:
  * `Id` (Int, Khóa chính)
  * `WorkOrderId` (Int, Khóa ngoại) -> `WorkOrder(Id)` (Cascade Delete)
  * `ProductId` (Int, Khóa ngoại) -> `Product(Id)` (Nguyên liệu cần giữ chỗ)
  * `LotId` (Int, Khóa ngoại) -> `Lot(Id)` - Lô hàng cụ thể bị khóa
  * `LocationId` (Int, Khóa ngoại) -> `Location(Id)` - Vị trí của lô hàng bị khóa
  * `QtyReserved` (Decimal, 18, 2) - Số lượng giữ chỗ
* **`LotGenealogy` (Phả hệ Lô - Truy vết hai chiều)**:
  * `Id` (Int, Khóa chính)
  * `OutputLotId` (Int, Khóa ngoại) -> `Lot(Id)` (Lô thành phẩm đầu ra)
  * `InputLotId` (Int, Khóa ngoại) -> `Lot(Id)` (Lô nguyên vật liệu tiêu hao)
  * `QtyConsumed` (Decimal, 18, 2) - Số lượng thực tế tiêu hao

---

## 3. Luồng Nghiệp vụ & Cài đặt Code

### 3.1 Chạy MRP tương tác (MRP Calculation)
* Planner nhập lượng cần sản xuất, hệ thống lấy BOM đang Active của sản phẩm:
  * `GrossDemand = Số lượng sản xuất * QtyPer * (1 + ScrapPercent / 100)`.
  * `NetDemand = GrossDemand - Tổng QtyAvailable` (của nguyên vật liệu trên bảng `StockBalance`).
  * Trả về danh sách chênh lệch thiếu hụt để hiển thị trên màn hình MRP.

### 3.2 Duyệt Lệnh sản xuất & Giữ chỗ (Approve WO)
* Trạng thái WO chuyển từ `Draft/Pending` ➔ `Approved`:
  * Mở Transaction.
  * Tính tổng lượng nguyên liệu cần thiết. Nếu kho không đủ tồn khả dụng (`QtyAvailable`) ➔ **Hủy duyệt lệnh, báo lỗi thiếu hàng** (Tuân thủ BR-MES-002).
  * Thực hiện **Giữ chỗ (Reservation)**: Chọn các lô khả dụng theo FEFO/FIFO, chuyển số lượng tương ứng từ `StockBalance.QtyAvailable` sang `StockBalance.QtyReserved`. Ghi nhận thông tin vào `MaterialReservation`.
  * Đọc `RoutingStep` để sinh tự động các bản ghi `WorkOrderStep` tương ứng.
  * Commit Transaction.

### 3.3 Ghi nhận tiến độ sản xuất (Execution)
* Công nhân bấm bắt đầu công đoạn: Cập nhật `StartTime`, đổi trạng thái sang `InProgress`.
* Công nhân bấm báo cáo hoàn thành công đoạn: Mở Popup nhập số lượng `QtyOK`, `QtyReject`, `QtyRework`, cập nhật `EndTime`, đổi trạng thái sang `Completed`.
* **Ràng buộc thứ tự**: Công đoạn sau chỉ được bắt đầu nếu toàn bộ các công đoạn trước đó đã hoàn thành (Status = `Completed`).

### 3.4 Hoàn thành Lệnh & Backflushing (Complete WO)
* Khi công đoạn cuối cùng của WO được báo cáo hoàn thành:
  * Mở Transaction.
  * **Tạo Lô thành phẩm tự động**: Định dạng Lô: `MãSP-YYYYMMDD-STT` (STT tăng dần trong ngày). Tạo mới bản ghi `Lot` liên kết với `WorkOrderId`. Tạo bản ghi `StockBalance` tại Vị trí chờ QC (WIP/FG Zone) với `QtyAvailable = QtyOK` (của bước cuối).
  * **Thực hiện Backflush (Trừ kho)**:
    * Lấy danh sách giữ chỗ trong `MaterialReservation` của WO này.
    * Trừ số lượng khỏi `StockBalance.QtyReserved` (không ảnh hưởng đến `QtyAvailable` vì đã bị trừ từ bước duyệt lệnh).
    * Ghi nhận giao dịch `StockTransaction` loại `Backflush` (Số lượng âm).
    * Ghi nhận phả hệ lô vào `LotGenealogy` (liên kết Lô thành phẩm đầu ra với các Lô nguyên liệu đã tiêu hao thực tế).
  * Chuyển trạng thái WO sang `Completed`.
  * Commit Transaction.

---

## 4. Thiết kế Giao diện người dùng UI/UX
* **MRP Screen (Lập kế hoạch tương tác)**:
  * Planner nhập số lượng sản xuất dự kiến ➔ Nhấn "Tính toán" ➔ Hiển thị bảng thiếu hụt màu đỏ.
  * Tích hợp nút "Tạo lệnh sản xuất nháp" cho những BTP bị thiếu hoặc xuất file Excel mua hàng.
* **Worker Terminal (Màn hình nhà xưởng)**:
  * Thiết kế responsive, giao diện tối giản với nút "BẮT ĐẦU" và "HOÀN THÀNH" kích thước lớn để công nhân dễ tương tác.
  * Popup nhập số liệu báo cáo đơn giản, trực quan.
* **Production Progress Dashboard (Màn hình giám sát realtime)**:
  * Hiển thị danh sách các WO đang chạy cùng thanh tiến độ phần trăm (%).
  * Tích hợp **SignalR Hub** cập nhật ngay tức khắc khi công nhân báo hoàn thành từng công đoạn.

---

## 5. Kế hoạch Kiểm tra (Verification Plan)
* **Kiểm thử tự động (Unit Tests)**:
  * Viết unit test kiểm tra thuật toán MRP (đầu vào, tính Gross/Net demand, hao hụt định mức).
  * Viết unit test xác nhận duyệt WO thành công sẽ sinh đúng giữ chỗ vật tư (`QtyReserved` tăng, `QtyAvailable` giảm, sinh đúng `MaterialReservation`).
  * Viết unit test chặn duyệt WO khi thiếu hàng.
  * Viết unit test chạy quy trình Backflushing khi xong công đoạn cuối (giải phóng giữ chỗ, sinh lô thành phẩm có định dạng đúng `MãSP-YYYYMMDD-STT`, ghi nhận đúng phả hệ lô `LotGenealogy`).
* **Kiểm thử thủ công**:
  * Tạo BOM và Routing cho một sản phẩm.
  * Vào màn hình MRP chạy thử, xác nhận bảng chênh lệch và bấm tạo Lệnh WO nháp.
  * Thử duyệt lệnh WO khi kho không đủ hàng ➔ Xác nhận báo lỗi.
  * Nhập thêm nguyên liệu vào kho và duyệt lại WO ➔ Truy cập SSMS kiểm tra bảng `StockBalance` xem lượng `QtyReserved` có nhảy đúng số lô được giữ chỗ không.
  * Đăng nhập vai trò Worker, lần lượt hoàn thành các công đoạn. Xác nhận không thể làm tắt công đoạn sau trước công đoạn trước.
  * Báo hoàn thành công đoạn cuối, kiểm tra xem CSDL có tự động sinh Lô thành phẩm và trừ lượng giữ chỗ nguyên liệu thông qua giao dịch `Backflush` không. Kiểm tra mối quan hệ trong bảng `LotGenealogy`.
