# Thiết kế: Tích hợp Giao diện người dùng (WMS & MES Frontend Integration)

Tài liệu này đặc tả thiết kế giao diện (UI/UX) và tích hợp Controller cho các tính năng nghiệp vụ còn thiếu trong hệ thống WMS + MES bao gồm: Quản lý Kho (Nhập/Xuất), Quản lý Lệnh sản xuất, Kiểm tra chất lượng (QC) và Dashboard thời gian thực.

---

## 1. Chia Giai đoạn phát triển (Phasing)

Để đảm bảo khả năng kiểm thử, tính toàn vẹn của mã nguồn và không vượt quá giới hạn ngữ cảnh của Codex, việc tích hợp giao diện được chia làm 3 Giai đoạn (Phases):

```
┌──────────────────────────────────────────────────────────┐
│ PHASE 1: Quản lý Kho (WMS UI)                            │
│ ├─ Phiếu Nhập kho (Goods Receipt) & Phiếu Xuất kho        │
│ └─ Thiết kế lại Cấu trúc Sidebar & Dọn dẹp Layout chung  │
└────────────────────────────┬─────────────────────────────┘
                             │
                             ▼
┌──────────────────────────────────────────────────────────┐
│ PHASE 2: Quản lý Sản xuất (MES UI)                       │
│ ├─ Lập & Phê duyệt Lệnh sản xuất (Work Orders CRUD)     │
│ └─ Cải tiến Dropdown chọn sản phẩm trên màn hình MRP     │
└────────────────────────────┬─────────────────────────────┘
                             │
                             ▼
┌──────────────────────────────────────────────────────────┐
│ PHASE 3: Đánh giá QC & Dashboard Real-time               │
│ ├─ Màn hình Kiểm định & Duyệt giải phóng Lô (QC)         │
│ └─ Dashboard chỉ số vận hành tích hợp SignalR Real-time  │
└──────────────────────────────────────────────────────────┘
```

---

## 2. Đặc tả các Giao diện & Controller

### 2.1. PHASE 1: Quản lý Kho (WMS UI)

#### Phiếu Nhập kho (Goods Receipt)
- **Tuyến đường (Route):** `/Inventory/Receipts` (Danh sách) và `/Inventory/CreateReceipt` (Tạo mới)
- **Quyền hạn:** `Admin`, `Warehouse`, `Manager`
- **Thiết kế form tạo mới:**
  * Chọn Nhà cung cấp (Dropdown nạp từ bảng `Suppliers`)
  * Chọn Sản phẩm/Nguyên vật liệu (Dropdown nạp từ bảng `Products`)
  * Nhập Số lô (`LotNo` - Bắt buộc nhập đối với sản phẩm quản lý theo Lô)
  * Nhập Số lượng (`Qty`) và Đơn giá (`UnitPrice`)
  * Chọn Vị trí cất hàng (Dropdown nạp từ bảng `Locations`)
- **Nghiệp vụ backend:** Khi bấm lưu, tự động gọi `IInventoryService.CompleteGoodsReceiptAsync` để cập nhật số dư khả dụng (`QtyAvailable`) và ghi nhận lịch sử giao dịch thẻ kho.

#### Phiếu Xuất kho (Goods Issue)
- **Tuyến đường (Route):** `/Inventory/Issues` (Danh sách) và `/Inventory/CreateIssue` (Tạo mới)
- **Quyền hạn:** `Admin`, `Warehouse`, `Manager`
- **Thiết kế form tạo mới:**
  * Chọn Khách hàng (Dropdown từ bảng `Customers`)
  * Chọn Sản phẩm (Dropdown từ bảng `Products`)
  * Chọn Lô hàng xuất (Dropdown hiển thị các Lô khả dụng tương ứng của sản phẩm đó kèm theo hạn sử dụng - áp dụng FEFO/FIFO)
  * Nhập Số lượng xuất (`Qty`)
  * Chọn Vị trí lấy hàng (Dropdown từ bảng `Locations`)
- **Nghiệp vụ backend:** Xác thực không cho xuất vượt quá số lượng khả dụng của Lô tại Vị trí đó (chống tồn kho âm). Gọi `IInventoryService.CompleteGoodsIssueAsync`.

---

### 2.2. PHASE 2: Quản lý Sản xuất (MES UI)

#### Quản lý Lệnh sản xuất (Work Orders)
- **Tuyến đường (Route):** `/WorkOrder/Index` (Danh sách), `/WorkOrder/Create` (Tạo mới), `/WorkOrder/Details/{id}` (Chi tiết)
- **Quyền hạn:** `Admin`, `Planner`, `Manager`
- **Hành động & Trạng thái:**
  * **Draft (Nháp):** Planner tạo lệnh mới gồm mã lệnh, chọn thành phẩm (Dropdown lấy các sản phẩm có `IsManufactured = true`), số lượng yêu cầu và hạn hoàn thành (`DueDate`).
  * **Approved (Đã duyệt):** Manager nhấn "Phê duyệt" tại trang Chi tiết. Hệ thống sẽ tự động đối chiếu BOM định mức, giữ chỗ (Reserve) nguyên vật liệu trong kho theo nguyên tắc FEFO/FIFO và tự động nạp các công đoạn sản xuất (Routing Steps) vào Lệnh.
  * **In Progress (Đang sản xuất):** Trạng thái tự động chuyển khi Công nhân bấm "Bắt đầu" công đoạn đầu tiên tại Trạm vận hành.
  * **Completed (Hoàn thành):** Manager bấm "Xác nhận hoàn thành" tại trang Chi tiết sau khi tất cả các bước Routing đã được công nhân hoàn thành. Hệ thống tự động trừ kho nguyên liệu đã giữ chỗ (Backflush), sinh Lô thành phẩm mới ở trạng thái "On Hold" (tạm giữ) tại vị trí kiểm định và tạo cây phả hệ Lô (`LotGenealogy`).

#### Cải tiến màn hình MRP
- **Tuyến đường (Route):** `/Mrp/Index`
- **Cải tiến:** Thay thế ô nhập số ID sản phẩm bằng ô chọn Dropdown danh sách sản phẩm sản xuất nhằm tăng trải nghiệm người dùng và hạn chế sai lệch dữ liệu.

---

### 2.3. PHASE 3: Đánh giá QC & Dashboard Real-time

#### Giao diện Kiểm định QC
- **Tuyến đường (Route):** `/Qc/Index` (Danh sách hàng chờ kiểm định) và `/Qc/Inspect?lotId={id}` (Thực hiện kiểm định)
- **Quyền hạn:** `Admin`, `QC`, `Manager`
- **Nghiệp vụ backend:**
  * Hệ thống tự động tìm kiếm bộ Tiêu chí kiểm tra chất lượng hoạt động (`QCChecklist` và `QCChecklistItem`) cho sản phẩm tương ứng của lô hàng.
  * Giao diện hiển thị các trường nhập thông số đo lường thực tế.
  * Khi bấm lưu kết quả, gọi `IQcService.SubmitQCInspectionAsync`. Nếu chọn kết quả **PASS**, giải phóng số lượng từ "On Hold" sang "Available" để lưu trữ hoặc xuất bán, đồng thời tự động tính giá thành sản xuất thực tế lưu vào Lô hàng. Nếu kết quả là **REJECT**, tự động chuyển lô hàng về vị trí cách ly `QC-QUARANTINE`.

#### Dashboard thời gian thực
- **Tuyến đường (Route):** `/Home/Index`
- **Tích hợp:** Sử dụng thư viện SignalR Client kết nối tới `ProductionHub` và `InventoryHub`. Khi có sự thay đổi về tồn kho (Nhập/Xuất/Giải phóng QC) hoặc trạng thái Lệnh sản xuất, Dashboard tự động cập nhật số liệu hiển thị (Lệnh đang chạy, lô hàng chờ QC, khối lượng tồn kho) mà không cần tải lại trang.
