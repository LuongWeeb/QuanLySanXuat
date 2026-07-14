# Thiết kế Giai đoạn 1: Nền tảng, Phân quyền & Danh mục (Foundation, Identity & Master Data)

Tài liệu thiết kế chi tiết cho Giai đoạn 1 của hệ thống Quản lý Kho & Điều hành Sản xuất (WMS + MES). Giai đoạn này tập trung thiết lập dự án, cơ chế bảo mật (Identity, Cookie & JWT) và các danh mục dữ liệu cốt lõi (Master Data).

---

## 1. Công nghệ & Kiến trúc
* **Framework**: ASP.NET Core MVC (.NET 8)
* **ORM**: Entity Framework Core (EF Core) Code First
* **Cơ sở dữ liệu**: SQL Server (Cấu hình chuỗi kết nối trong `appsettings.json`, kết nối từ SSMS để quản lý)
* **Giao diện**: Razor Views + Bootstrap 5 + CSS Custom (Theme Slate-Blue hiện đại)
* **Cấu trúc dự án**: Single-Project Monolith (Một project duy nhất phân tách thư mục theo các tầng)
  * `/Domain/Entities` - Thực thể CSDL
  * `/Data` - DbContext & Fluent API Configuration
  * `/Repositories` - Repository Pattern truy xuất DB
  * `/Services` - Service Layer xử lý logic nghiệp vụ và phân quyền
  * `/Controllers` - Điều hướng HTTP và Web API
  * `/Views` - Giao diện Razor Views

---

## 2. Thiết kế Cơ sở dữ liệu (Database Schema)

### Thực thể Bảo mật (Security Entities - ASP.NET Core Identity)
* **`ApplicationUser`** (kế thừa `IdentityUser`):
  * `FullName` (Nvarchar, 100) - Họ và tên
  * `IsActive` (Bool, mặc định true) - Trạng thái hoạt động
  * `CreatedAt` (DateTime) - Thời gian tạo tài khoản
* **`ApplicationRole`** (kế thừa `IdentityRole`):
  * Lưu các vai trò chính: `Admin`, `Manager`, `Planner`, `Warehouse`, `Worker`, `QC`, `Director`.

### Thực thể Danh mục (Master Data Entities)
* **`UnitOfMeasure` (Đơn vị tính - UOM)**:
  * `Id` (Int, Khóa chính)
  * `Code` (Varchar, 50, Unique) - Mã đơn vị (ví dụ: KG, PCS, BAG)
  * `Name` (Nvarchar, 100) - Tên đơn vị
* **`Product` (Sản phẩm / SKU)**:
  * `Id` (Int, Khóa chính)
  * `Code` (Varchar, 100, Unique) - SKU Code
  * `Name` (Nvarchar, 250) - Tên sản phẩm
  * `Type` (Enum: `RawMaterial` = 0, `WIP` = 1, `FinishedGood` = 2)
  * `IsManufactured` (Bool) - Hàng tự sản xuất (true với WIP và FinishedGood)
  * `BaseUomId` (Int, Khóa ngoại) -> `UnitOfMeasure(Id)`
  * `MinStock` (Decimal, 18, 2) - Tồn kho tối thiểu
  * `MaxStock` (Decimal, 18, 2) - Tồn kho tối đa
  * `IsLotTracked` (Bool) - Có quản lý theo Lô (Lot) hay không
  * `ShelfLifeDays` (Int, Nullable) - Số ngày hạn sử dụng
* **`Warehouse` (Nhà kho)**:
  * `Id` (Int, Khóa chính)
  * `Code` (Varchar, 50, Unique) - Mã kho (ví dụ: WH01, WH02)
  * `Name` (Nvarchar, 150) - Tên kho
* **`Zone` (Khu vực lưu trữ)**:
  * `Id` (Int, Khóa chính)
  * `Code` (Varchar, 50, Unique) - Mã khu (ví dụ: Z-RAW, Z-WIP, Z-FG)
  * `Name` (Nvarchar, 150) - Tên khu
  * `WarehouseId` (Int, Khóa ngoại) -> `Warehouse(Id)` (Cascade Delete)
* **`Location` (Vị trí lưu kho)**:
  * `Id` (Int, Khóa chính)
  * `Code` (Varchar, 50, Unique) - Mã vị trí (ví dụ: LOC-01-01, LOC-01-02)
  * `Name` (Nvarchar, 150) - Tên vị trí
  * `ZoneId` (Int, Khóa ngoại) -> `Zone(Id)` (Cascade Delete)
* **`Supplier` (Nhà cung cấp)** & **`Customer` (Khách hàng)**:
  * `Id` (Int, Khóa chính)
  * `Code` (Varchar, 50, Unique) - Mã đối tác
  * `Name` (Nvarchar, 250) - Tên đối tác
  * `Address` (Nvarchar, 500) - Địa chỉ
  * `Phone` (Varchar, 20) - Điện thoại
  * `Email` (Varchar, 100) - Email

---

## 3. Xác thực & Phân quyền (Security Workflow)
* **Giao diện Web MVC**: Sử dụng **Cookie-based Authentication**. Đăng nhập qua màn hình login truyền thống.
* **REST API (`/api/v1/...`)**: Sử dụng **JWT Token Authentication**. Cung cấp API `POST /api/v1/auth/login` để lấy token.
* **Seed Data**: Tự động tạo vai trò (Roles) và các tài khoản mẫu khi ứng dụng chạy lần đầu:
  * `admin@wmsmes.com` (Vai trò Admin)
  * `manager@wmsmes.com` (Vai trò Production Manager)
  * `planner@wmsmes.com` (Vai trò Production Planner)
  * `warehouse@wmsmes.com` (Vai trò Warehouse Staff)
  * `worker@wmsmes.com` (Vai trò Production Worker)
  * `qc@wmsmes.com` (Vai trò QC Staff)
  * `director@wmsmes.com` (Vai trò Director)
  * Mật khẩu mặc định: `Password123!`

---

## 4. Thiết kế Giao diện UI/UX
* **Theme**: Phong cách Slate-Blue hiện đại.
* **Font**: Font chữ `Inter` (sans-serif) sạch sẽ, hiện đại.
* **Layout**:
  * Sidebar bên trái chứa Menu điều hướng phân quyền (chỉ hiển thị các chức năng mà vai trò hiện tại được phép truy cập).
  * Main Content ở giữa hiển thị danh sách dạng bảng, tích hợp tìm kiếm nhanh, lọc.
  * Form thêm mới/chỉnh sửa sử dụng **Modal Dialog** để thao tác trực tiếp, tăng độ mượt mà.
* **Trực quan hóa cấu trúc kho**: Thiết kế giao diện Tree View lồng nhau hiển thị Kho -> Khu vực -> Vị trí.

---

## 5. Kế hoạch Kiểm tra (Verification Plan)
* **Kiểm thử tự động**:
  * Viết Unit Tests cho `ProductService` và `WarehouseService` kiểm tra tính hợp lệ của việc thêm sản phẩm, trùng mã SKU, ràng buộc tồn kho âm.
* **Kiểm thử thủ công**:
  * Đăng nhập lần lượt bằng các tài khoản mẫu (Admin, Warehouse, Worker...) để kiểm tra phân quyền hiển thị Menu.
  * Thử thêm/sửa/xóa các danh mục UOM, Product, Warehouse, Zone, Location qua giao diện.
  * Dùng SSMS kết nối vào cơ sở dữ liệu để đối chiếu xem cấu trúc bảng và các khóa ngoại có được EF Core sinh ra chính xác không.
