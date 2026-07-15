# Thiết kế Giai đoạn 2: Quản lý Kho cốt lõi (WMS Core)

Tài liệu thiết kế chi tiết cho Giai đoạn 2 của hệ thống Quản lý Kho & Điều hành Sản xuất (WMS + MES). Giai đoạn này tập trung vào các tính năng Quản lý Nhập kho, Xuất kho, Điều chuyển, Kiểm kê và Tồn kho thời gian thực (Realtime Inventory) sử dụng cơ chế chứng từ giao dịch (Transaction-based).

---

## 1. Kiến trúc & Công nghệ bổ sung
* **SignalR**: Sử dụng để phát thông báo cập nhật tồn kho thời gian thực từ Server xuống Dashboard mà không cần tải lại trang.
* **Database Transactions**: Mọi hoạt động nhập/xuất/kiểm kê được đóng gói trong một Database Transaction duy nhất để bảo đảm tính toàn vẹn dữ liệu (ACID).
* **Quy tắc phân bổ lô (Lot Allocation)**: Hỗ trợ logic gợi ý lô tự động FEFO (Hàng hết hạn trước xuất trước) hoặc FIFO (Hàng nhập trước xuất trước), thủ kho có quyền điều chỉnh trước khi hoàn thành xuất.

---

## 2. Thiết kế Cơ sở dữ liệu (Database Schema)

### Thực thể tồn kho & Lịch sử
* **`Lot` (Quản lý Lô)**:
  * `Id` (Int, Khóa chính)
  * `LotNo` (Varchar, 100, Unique) - Mã lô (nhập thủ công theo nhà cung cấp hoặc tự sinh)
  * `ProductId` (Int, Khóa ngoại) -> `Product(Id)`
  * `ManufactureDate` (DateTime, Nullable) - Ngày sản xuất
  * `ExpiryDate` (DateTime, Nullable) - Hạn sử dụng
  * `Qty` (Decimal, 18, 2) - Số lượng ban đầu của lô
  * `WorkOrderId` (Int, Nullable) - Liên kết với Lệnh sản xuất (cho MES ở Phase 3)
* **`StockBalance` (Bảng cân đối tồn kho hiện tại)**:
  * `Id` (Int, Khóa chính)
  * `ProductId` (Int, Khóa ngoại) -> `Product(Id)`
  * `LotId` (Int, Khóa ngoại) -> `Lot(Id)`
  * `LocationId` (Int, Khóa ngoại) -> `Location(Id)`
  * `QtyAvailable` (Decimal, 18, 2) - Số lượng sẵn sàng xuất
  * `QtyReserved` (Decimal, 18, 2) - Số lượng đã giữ chỗ cho sản xuất
  * `QtyOnHold` (Decimal, 18, 2) - Số lượng tạm giữ (do kiểm kê hoặc QC)
  * *Unique constraint*: `(ProductId, LotId, LocationId)`
* **`StockTransaction` (Lịch sử giao dịch kho)**:
  * `Id` (Int, Khóa chính)
  * `Type` (Enum: `Receipt` = 0, `Issue` = 1, `Transfer` = 2, `Adjust` = 3)
  * `ProductId` (Int, Khóa ngoại) -> `Product(Id)`
  * `LotId` (Int, Khóa ngoại) -> `Lot(Id)`
  * `LocationId` (Int, Khóa ngoại) -> `Location(Id)`
  * `Qty` (Decimal, 18, 2) - Số lượng thay đổi (dương là nhập, âm là xuất)
  * `TransactionDate` (DateTime) - Thời gian giao dịch
  * `UserId` (Varchar, Khóa ngoại) -> `AspNetUsers(Id)`
  * `ReferenceNo` (Varchar, 100) - Mã phiếu tham chiếu (Ví dụ: Số phiếu nhập, xuất)

### Các bảng chứng từ (Documents)
* **`GoodsReceipt` (Phiếu nhập kho)** & **`GoodsReceiptLine` (Chi tiết nhập)**:
  * `GoodsReceipt`: `Id` (Int, PK), `ReceiptNo` (Varchar, Unique), `SupplierId` (Int, Nullable, FK), `ReceiptDate` (DateTime), `Status` (Enum: `Draft`, `Completed`)
  * `GoodsReceiptLine`: `Id` (Int, PK), `GoodsReceiptId` (Int, FK), `ProductId` (Int, FK), `LotNo` (Varchar), `ExpiryDate` (DateTime, Nullable), `Qty` (Decimal, 18, 2), `LocationId` (Int, FK)
* **`GoodsIssue` (Phiếu xuất kho)** & **`GoodsIssueLine` (Chi tiết xuất)**:
  * `GoodsIssue`: `Id` (Int, PK), `IssueNo` (Varchar, Unique), `IssueDate` (DateTime), `Status` (Enum: `Draft`, `Completed`)
  * `GoodsIssueLine`: `Id` (Int, PK), `GoodsIssueId` (Int, FK), `ProductId` (Int, FK), `LotId` (Int, FK), `Qty` (Decimal, 18, 2), `LocationId` (Int, FK)
* **`StockTransfer` (Phiếu chuyển kho)** & **`StockTransferLine` (Chi tiết chuyển)**:
  * `StockTransfer`: `Id` (Int, PK), `TransferNo` (Varchar, Unique), `TransferDate` (DateTime), `Status` (Enum: `Draft`, `Completed`)
  * `StockTransferLine`: `Id` (Int, PK), `StockTransferId` (Int, FK), `ProductId` (Int, FK), `LotId` (Int, FK), `FromLocationId` (Int, FK), `ToLocationId` (Int, FK), `Qty` (Decimal, 18, 2)
* **`Stocktake` (Phiếu kiểm kê)** & **`StocktakeLine` (Chi tiết kiểm kê)**:
  * `Stocktake`: `Id` (Int, PK), `StocktakeNo` (Varchar, Unique), `LocationId` (Int, FK - Vị trí bị khóa), `CreatedDate` (DateTime), `Status` (Enum: `Draft`, `Counting`, `AwaitingApproval`, `Completed`)
  * `StocktakeLine`: `Id` (Int, PK), `StocktakeId` (Int, FK), `ProductId` (Int, FK), `LotId` (Int, FK), `QtySystem` (Decimal, 18, 2), `QtyCounted` (Decimal, 18, 2), `QtyDiscrepancy` (Decimal, 18, 2)

---

## 3. Luồng Nghiệp vụ & Cài đặt Code

### 3.1 Nhập kho (Goods Receipt)
* Người dùng tạo phiếu nhập nháp, thêm các dòng sản phẩm kèm số Lô và Hạn sử dụng của Nhà cung cấp.
* Khi nhấn "Hoàn thành" (Complete):
  * Mở CSDL Transaction.
  * Nếu Số Lô chưa tồn tại trong bảng `Lot` ➔ Tạo mới bản ghi `Lot`.
  * Tìm hoặc tạo bản ghi `StockBalance` tương ứng với `(ProductId, LotId, LocationId)`.
  * Cộng số lượng vào `StockBalance.QtyAvailable`.
  * Lưu bản ghi `StockTransaction` loại `Receipt` (Số lượng dương).
  * Commit Transaction.

### 3.2 Xuất kho (Goods Issue)
* Người dùng chọn sản phẩm và nhập số lượng cần xuất.
* Hệ thống tự động gợi ý phân bổ lô dựa trên logic:
  * Nếu hàng có hạn sử dụng ➔ Sắp xếp `StockBalance` theo `Lot.ExpiryDate` tăng dần (FEFO).
  * Nếu hàng không có hạn sử dụng ➔ Sắp xếp theo ngày nhập cũ nhất (FIFO).
  * Điền số lượng đề xuất vào các Lô/Vị trí này cho đến khi đủ số lượng yêu cầu.
* Người dùng có quyền thay đổi Lô hoặc Vị trí thủ công trên giao diện.
* Khi nhấn "Hoàn thành" (Complete):
  * Mở CSDL Transaction.
  * Kiểm tra xem từng Lô/Vị trí đã chọn có đủ `QtyAvailable` không. **Không cho phép tồn kho âm** (Nếu thiếu ➔ Báo lỗi).
  * Trừ số lượng xuất khỏi `StockBalance.QtyAvailable`.
  * Lưu bản ghi `StockTransaction` loại `Issue` (Số lượng âm).
  * Commit Transaction.

### 3.3 Điều chuyển kho (Stock Transfer)
* Di chuyển hàng từ vị trí A sang vị trí B của cùng một Lô sản phẩm.
* Khi hoàn thành: Trừ `QtyAvailable` tại vị trí cũ, cộng `QtyAvailable` tại vị trí mới. Ghi nhận 2 giao dịch trong `StockTransaction` (một xuất âm, một nhập dương).

### 3.4 Kiểm kê (Stocktake)
* **Bắt đầu kiểm kê (Start Counting)**: Hệ thống khóa vị trí được chọn bằng cách chuyển toàn bộ số lượng tồn kho khả dụng tại vị trí đó sang trạng thái tạm giữ (`QtyAvailable ➔ QtyOnHold`, đặt `QtyAvailable = 0`).
* **Hoàn thành đếm**: Người dùng nhập số lượng thực tế (`QtyCounted`), hệ thống tính chênh lệch (`QtyDiscrepancy = QtyCounted - QtySystem`) và gửi phê duyệt.
* **Phê duyệt chênh lệch (Approve)**: 
  * Giải phóng số lượng thực tế đếm được từ `QtyOnHold` về `QtyAvailable`.
  * Nếu có chênh lệch (`QtyDiscrepancy != 0`): Cập nhật lại số dư tồn kho và ghi nhận lịch sử giao dịch loại `Adjust` với giá trị chênh lệch.
  * Đổi trạng thái phiếu kiểm kê thành `Completed` và mở khóa vị trí.

---

## 4. Thiết kế Giao diện người dùng UI/UX
* **Inventory Dashboard (Tồn kho Realtime)**:
  * Hiển thị bảng chi tiết tồn kho khả dụng, giữ chỗ và tạm giữ.
  * Tích hợp **SignalR Hub** cập nhật tự động số lượng khi có bất kỳ giao dịch kho nào thành công.
* **Giao diện Chứng từ (Receipt, Issue, Transfer, Stocktake)**:
  * Các trang danh sách có bộ lọc trạng thái và tìm kiếm.
  * Màn hình tạo mới phiếu xuất tích hợp bảng hiển thị gợi ý phân bổ lô FEFO/FIFO và cho phép chọn lại lô thủ công thông qua Dropdown.
  * Màn hình kiểm kê làm nổi bật chênh lệch số lượng thừa/thiếu bằng màu sắc trực quan (đỏ/xanh).
  * Phân quyền: Chỉ vai trò `Manager` hoặc `Admin` mới được duyệt chênh lệch kiểm kê.

---

## 5. Kế hoạch Kiểm tra (Verification Plan)
* **Kiểm thử tự động (Unit/Integration Tests)**:
  * Viết unit test cho `InventoryService.GetSuggestedLotsAsync(productId, qty)` để kiểm tra logic đề xuất lô FEFO/FIFO có chính xác không.
  * Viết unit test cho quy trình nhập kho để kiểm tra xem `StockBalance` và `StockTransaction` có được cập nhật đồng bộ và chính xác trong Transaction không.
  * Viết unit test chặn lỗi tồn kho âm khi xuất vượt quá lượng khả dụng.
* **Kiểm thử thủ công**:
  * Tạo phiếu nhập kho và nhập số Lô, kiểm tra DB xem có tạo mới Lô và cập nhật số dư khả dụng không.
  * Tạo phiếu xuất kho, kiểm tra xem hệ thống có tự động gợi ý lô cũ nhất trước không. Thử chọn lô khác và xuất hàng, kiểm tra số dư tồn kho.
  * Tạo phiếu kiểm kê, xác nhận vị trí bị khóa (không thể xuất hàng tại vị trí đó). Nhập chênh lệch và phê duyệt, kiểm tra số dư tồn kho khả dụng mới cập nhật và kiểm tra log trong `StockTransaction`.
