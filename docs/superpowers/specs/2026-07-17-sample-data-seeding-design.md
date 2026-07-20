# Thiết kế: Seeding Dữ liệu mẫu Hệ thống (WMS & MES)

Tài liệu này đặc tả thiết kế và danh sách dữ liệu mẫu toàn diện sẽ được nạp (seed) vào hệ thống tự động khi khởi chạy ứng dụng. Dữ liệu mẫu giúp người dùng và nhà phát triển kiểm thử ngay lập tức toàn bộ chuỗi quy trình từ nhập kho nguyên liệu, sản xuất, kiểm tra chất lượng đến lưu trữ thành phẩm.

## Mục tiêu
1. Tự động nạp dữ liệu danh mục cốt lõi (Sản phẩm, Nhà cung cấp, Khách hàng, Tổ chức sản xuất, Định mức BOM, Tiêu chí QC).
2. Tạo trạng thái kho ban đầu hợp lệ (không âm) của nguyên vật liệu để sẵn sàng chạy lệnh sản xuất.
3. Thiết lập một số giao dịch mẫu (Lệnh sản xuất với các trạng thái khác nhau, Phiếu kiểm tra QC, Lịch sử thẻ kho) để trực quan hóa giao diện báo cáo/dashboard.

---

## 1. Chi tiết Dữ liệu mẫu Seeding

### 1.1. Dữ liệu Danh mục (Master Data)

#### Đơn vị tính (UnitOfMeasure)
Sử dụng các đơn vị tính sẵn có trong hệ thống:
* `KG` - Kilogram
* `PCS` - Cái/Chiếc
* `LITER` - Lít
* `BAG` - Bao

#### Sản phẩm (Product)
| Mã sản phẩm | Tên sản phẩm | Loại (ProductType) | Sản xuất? (IsManufactured) | Đơn vị tính | Quản lý lô? (IsLotTracked) | Hạn sử dụng (ShelfLifeDays) |
|---|---|---|---|---|---|---|
| `RM-FRAME-01` | Khung xe hợp kim nhôm | RawMaterial | Không | PCS | Có | Null |
| `RM-WHEEL-01` | Cặp bánh xe 26 inch | RawMaterial | Không | PCS | Có | Null |
| `RM-CHAIN-01` | Bộ xích líp Shimano | RawMaterial | Không | PCS | Có | Null |
| `RM-SADDLE-01` | Yên xe thể thao | RawMaterial | Không | PCS | Không | Null |
| `RM-ABS-01` | Hạt nhựa ABS cao cấp | RawMaterial | Không | KG | Có | Null |
| `RM-STRAP-01` | Dây quai mũ bảo hiểm | RawMaterial | Không | PCS | Không | Null |
| `PROD-BIKE-01` | Xe đạp địa hình thể thao MTB-26 | FinishedGood | Có | PCS | Có | Null |
| `PROD-HELM-01` | Mũ bảo hiểm thể thao ProtectPro | FinishedGood | Có | PCS | Có | 1095 |

#### Nhà cung cấp (Supplier)
* `SUPP-HN-01` - Công ty Phụ tùng Xe đạp Hữu Nghị
* `SUPP-NP-01` - Tổng kho Hạt nhựa miền Bắc

#### Khách hàng (Customer)
* `CUST-DECA-01` - Chuỗi siêu thị thể thao Decathlon Việt Nam
* `CUST-HN-01` - Đại lý bán lẻ xe đạp thể thao Hà Nội

#### Tổ chức Sản xuất (WorkCenter)
* `WC-ASM-01` - Xưởng lắp ráp khung xe cơ khí (Trạng thái: Hoạt động)
* `WC-FIN-01` - Trạm hoàn thiện, cân chỉnh & đóng gói (Trạng thái: Hoạt động)
* `WC-MOLD-01` - Xưởng ép nhựa vỏ mũ bảo hiểm (Trạng thái: Hoạt động)

#### Định mức nguyên vật liệu (BOM & BOMItem)
1. Định mức cho Xe đạp địa hình (`PROD-BIKE-01`) - Phiên bản `V1.0`:
   * `RM-FRAME-01`: 1 cái
   * `RM-WHEEL-01`: 1 cái
   * `RM-CHAIN-01`: 1 cái
   * `RM-SADDLE-01`: 1 cái
2. Định mức cho Mũ bảo hiểm ProtectPro (`PROD-HELM-01`) - Phiên bản `V1.0`:
   * `RM-ABS-01`: 0.5 KG (Hao hụt: 2.0%)
   * `RM-STRAP-01`: 1 cái

#### Quy trình công nghệ (Routing & RoutingStep)
1. Quy trình Xe đạp (`PROD-BIKE-01`) - Phiên bản `V1.0`:
   * Công đoạn 10: "Lắp ráp khung và bánh xe" tại `WC-ASM-01` (Thời gian: 30 phút, QC: Không)
   * Công đoạn 20: "Lắp xích, yên xe và cân chỉnh" tại `WC-FIN-01` (Thời gian: 15 phút, QC: Có)
2. Quy trình Mũ bảo hiểm (`PROD-HELM-01`) - Phiên bản `V1.0`:
   * Công đoạn 10: "Ép nhựa vỏ mũ bảo hiểm" tại `WC-MOLD-01` (Thời gian: 10 phút, QC: Không)
   * Công đoạn 20: "Lắp quai đeo và dán mút xốp" tại `WC-FIN-01` (Thời gian: 5 phút, QC: Có)

#### Tiêu chí Kiểm định QC (QCChecklist & QCChecklistItem)
1. Checklist Xe đạp (`PROD-BIKE-01`) ở công đoạn 20:
   * "Kiểm tra độ bám phanh lực bóp" (Min: 15.00, Max: 30.00, Đơn vị: N, Bắt buộc)
   * "Kiểm tra độ chắc chắn khung sườn" (Dạng Đạt/Không đạt, Bắt buộc)
2. Checklist Mũ bảo hiểm (`PROD-HELM-01`) ở công đoạn 20:
   * "Kiểm tra độ chịu lực va đập vỏ mũ" (Min: 200.00, Max: 300.00, Đơn vị: J, Bắt buộc)
   * "Kiểm tra độ chắc chắn quai đeo" (Dạng Đạt/Không đạt, Bắt buộc)

---

### 1.2. Dữ liệu Giao dịch mẫu (Transaction Data)

#### 1. Tồn kho ban đầu
* **Phiếu nhập kho `GR-20260715-01`** (Trạng thái: `Completed`):
  * Người dùng: `warehouse@wmsmes.com`
  * Nhập hàng từ `SUPP-HN-01` vào kệ nguyên liệu chính `LOC-RAW-01`
  * Dữ liệu các dòng:
    * `RM-FRAME-01` (Lô `L-FRAME-001`): 100 chiếc
    * `RM-WHEEL-01` (Lô `L-WHEEL-001`): 200 chiếc
    * `RM-CHAIN-01` (Lô `L-CHAIN-001`): 150 chiếc
    * `RM-SADDLE-01` (Lô `L-SAD-001`): 150 chiếc
* **Phiếu nhập kho `GR-20260715-02`** (Trạng thái: `Completed`):
  * Nhập hàng từ `SUPP-NP-01` vào kệ nguyên liệu hạt nhựa `LOC-RAW-02`
  * Dữ liệu các dòng:
    * `RM-ABS-01` (Lô `L-ABS-001`): 500 KG
    * `RM-STRAP-01` (Lô `L-STRAP-001`): 300 chiếc

#### 2. Các Lệnh sản xuất (Work Orders)
1. **`WO-20260717-01` (Trạng thái: `Completed`)**
   * Sản phẩm: `PROD-BIKE-01` | Số lượng yêu cầu: 10 chiếc.
   * Lịch sử thực thi:
     * Công đoạn 10 (`Completed`, Thực tế chạy 300 phút) tại `WC-ASM-01`.
     * Công đoạn 20 (`Completed`, Thực tế chạy 150 phút) tại `WC-FIN-01`.
   * Nguyên liệu tiêu thụ (Backflush):
     * Giảm số lượng khả dụng (`QtyAvailable`) và tạo giao dịch trừ kho của:
       * `RM-FRAME-01` (Lô `L-FRAME-001`): 10 chiếc
       * `RM-WHEEL-01` (Lô `L-WHEEL-001`): 10 chiếc
       * `RM-CHAIN-01` (Lô `L-CHAIN-001`): 10 chiếc
       * `RM-SADDLE-01` (Lô `L-SAD-001`): 10 chiếc
   * Nhập kho thành phẩm:
     * Tạo Lô thành phẩm mới `PROD-BIKE-01-20260717-01` số lượng 10 chiếc tại kệ thành phẩm `LOC-FG-01`.
     * Sinh phả hệ lô (`LotGenealogy`) ánh xạ giữa Lô thành phẩm này và các lô nguyên vật liệu đã dùng.
2. **`WO-20260717-02` (Trạng thái: `InProgress`)**
   * Sản phẩm: `PROD-HELM-01` | Số lượng yêu cầu: 50 chiếc.
   * Lịch sử thực thi:
     * Công đoạn 10 (`Completed`, Thực tế chạy 500 phút) tại `WC-MOLD-01`.
     * Công đoạn 20 (`Pending`) tại `WC-FIN-01`.
   * Giữ chỗ vật tư (Reservation):
     * Chuyển trạng thái tồn kho khả dụng thành giữ chỗ (`QtyAvailable` -> `QtyReserved`):
       * `RM-ABS-01` (Lô `L-ABS-001`): 25 KG
       * `RM-STRAP-01` (Lô `L-STRAP-001`): 50 chiếc
3. **`WO-20260717-03` (Trạng thái: `Draft`)**
   * Sản phẩm: `PROD-BIKE-01` | Số lượng yêu cầu: 5 chiếc.
   * Các công đoạn đều ở trạng thái `Pending`. Chưa có giữ chỗ vật tư hay tiêu hao nào.

#### 3. Kiểm tra QC mẫu
Ghi nhận kết quả QC đạt cho lô xe đạp hoàn thành:
* Phiếu QC Inspection cho Lệnh sản xuất `WO-20260717-01` và lô thành phẩm `PROD-BIKE-01-20260717-01`.
* Kết quả: `Passed`. Người kiểm duyệt: `qc@wmsmes.com`.
* Chi tiết kiểm duyệt:
  * Kiểm tra độ bám phanh lực bóp: Thực tế: `22.50` (Đạt, yêu cầu: 15.00 - 30.00).
  * Kiểm tra độ chắc chắn khung sườn: Thực tế: `1.00` (Đạt - Ok).

---

## 2. Giải pháp kỹ thuật và Thiết kế chi tiết

Chúng ta sẽ mở rộng lớp `DbSeeder` để thực hiện tuần tự việc seeding theo cấu trúc phụ thuộc của CSDL:
```csharp
public static class DbSeeder
{
    // Các phương thức cũ giữ nguyên:
    // - SeedRolesAndUsersAsync
    // - SeedQcInfrastructureAsync
    // - SeedUnitOfMeasuresAsync
    // - SeedWarehouseStructureAsync

    // Phương thức mới:
    public static async Task SeedComprehensiveSampleDataAsync(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        // 1. Seed Khách hàng & Nhà cung cấp
        // 2. Seed Sản phẩm mẫu
        // 3. Seed Tổ chức sản xuất (WorkCenter, BOM, Routing)
        // 4. Seed QC Checklist mẫu
        // 5. Seed Phiếu nhập kho ban đầu (GoodsReceipt, Lots, StockBalance, StockTransactions)
        // 6. Seed Lệnh sản xuất mẫu & QC Inspections & Lot Genealogy
    }
}
```

### Cách gọi trong `Program.cs`
Cập nhật block chạy khởi động database:
```csharp
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var dbContext = services.GetRequiredService<ApplicationDbContext>();
        
        await DbSeeder.SeedRolesAndUsersAsync(roleManager, userManager);
        await DbSeeder.SeedQcInfrastructureAsync(dbContext);
        await DbSeeder.SeedUnitOfMeasuresAsync(dbContext);
        await DbSeeder.SeedWarehouseStructureAsync(dbContext);
        
        // Gọi hàm seeding toàn diện mẫu mới
        await DbSeeder.SeedComprehensiveSampleDataAsync(dbContext, userManager);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}
```

---

## 3. Kế hoạch xác minh (Verification Plan)

### Kiểm thử thủ công:
1. Chạy lại lệnh khởi động dự án: `dotnet run`
2. Kiểm tra log hiển thị của ứng dụng xem có lỗi xảy ra trong quá trình khởi tạo dữ liệu mẫu hay không.
3. Đăng nhập vào các giao diện:
   * Danh mục Sản phẩm: Xem danh sách 8 sản phẩm nguyên vật liệu / thành phẩm.
   * Quản lý Kho: Xem tồn kho ban đầu có đúng số lượng như đã mô tả trên các kệ hay không.
   * Dashboard / Quản lý Lệnh sản xuất: Xem 3 lệnh sản xuất tương ứng với các trạng thái Draft, InProgress, Completed.
   * Kiểm tra QC: Xác nhận kết quả QC Inspection cho lô xe đạp hoàn thành.
