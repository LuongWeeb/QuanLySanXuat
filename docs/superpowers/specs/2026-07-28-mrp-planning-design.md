# Thiết kế Hệ thống Kế hoạch Sản xuất & Chạy MRP Tổng hợp (Phase 4)

## 1. Tổng quan
Tài liệu này đặc tả thiết kế kỹ thuật cho **Phase 4: Lập Kế hoạch & MRP Thông minh (Production Planning & MRP)** thuộc lộ trình cải tiến hệ thống WMS + MES.

Mục tiêu chính là cho phép người lập kế hoạch (Planner) quản lý nhu cầu sản xuất thông qua các **Kế hoạch sản xuất (Production Plan)** gồm nhiều dòng sản phẩm đồng thời. Từ đó chạy MRP tổng hợp nhu cầu vật tư trên cả kế hoạch, tự động đề xuất số lượng thiếu hụt và tự động tạo hàng loạt các Lệnh sản xuất nháp (`WorkOrder` ở trạng thái `Draft`) tương ứng cho từng dòng sản phẩm chỉ với một cú nhấp chuột.

---

## 2. Thay đổi Cơ sở dữ liệu (Database Schema)

Chúng ta sẽ thêm 2 bảng thực thể mới để lưu trữ kế hoạch sản xuất:

### 2.1 Thực thể Kế hoạch sản xuất (`ProductionPlan`)
*   **File mới:** [ProductionPlan.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Entities/ProductionPlan.cs)
*   **Mã nguồn:**
    ```csharp
    using System.ComponentModel.DataAnnotations;
    using WmsMes.Web.Domain.Enums;

    namespace WmsMes.Web.Domain.Entities;

    public class ProductionPlan
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string PlanNo { get; set; } = string.Empty;

        [Required]
        public DateTime PlanDate { get; set; } = DateTime.UtcNow;

        [Required]
        public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

        public virtual ICollection<ProductionPlanItem> Items { get; set; } = new List<ProductionPlanItem>();
    }
    ```

### 2.2 Thực thể Chi tiết kế hoạch (`ProductionPlanItem`)
*   **File mới:** [ProductionPlanItem.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Entities/ProductionPlanItem.cs)
*   **Mã nguồn:**
    ```csharp
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;

    namespace WmsMes.Web.Domain.Entities;

    public class ProductionPlanItem
    {
        public int Id { get; set; }

        [Required]
        public int ProductionPlanId { get; set; }

        [ForeignKey(nameof(ProductionPlanId))]
        public virtual ProductionPlan? ProductionPlan { get; set; }

        [Required]
        public int ProductId { get; set; }

        [ForeignKey(nameof(ProductId))]
        public virtual Product? Product { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal PlannedQty { get; set; }

        // Liên kết tới Lệnh sản xuất được tạo ra từ dòng này
        public int? WorkOrderId { get; set; }

        [ForeignKey(nameof(WorkOrderId))]
        public virtual WorkOrder? WorkOrder { get; set; }
    }
    ```

*   **Đăng ký DbContext:** Đăng ký 2 DbSet `ProductionPlans` và `ProductionPlanItems` trong `ApplicationDbContext.cs`.

---

## 3. Dịch vụ Lập kế hoạch & Thuật toán MRP Tổng hợp

Tạo một dịch vụ mới `ProductionPlanService` chịu trách nhiệm xử lý các logic nghiệp vụ kế hoạch và chạy MRP tổng hợp.

### 3.1 Giao diện Dịch vụ (`IProductionPlanService.cs`)
*   **File mới:** [IProductionPlanService.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Services/IProductionPlanService.cs)
*   **Khai báo các phương thức:**
    ```csharp
    using WmsMes.Web.Domain.Entities;
    using WmsMes.Web.DTOs;

    namespace WmsMes.Web.Services;

    public interface IProductionPlanService
    {
        Task<ProductionPlan?> GetByIdAsync(int id);
        Task<bool> CreatePlanAsync(ProductionPlan plan);
        Task<IEnumerable<MrpResultDto>> CalculatePlanRequirementsAsync(int planId);
        Task<bool> GenerateWorkOrdersAsync(int planId, string userId);
        Task<bool> CompletePlanAsync(int planId);
    }
    ```

### 3.2 Thuật toán MRP Tổng hợp (`CalculatePlanRequirementsAsync`)
Thuật toán sẽ quét qua tất cả các sản phẩm cần sản xuất trong kế hoạch, truy vấn BOM hoạt động của từng sản phẩm, gom nhóm các nguyên vật liệu cần thiết và tính toán lượng thiếu hụt dựa trên tồn kho khả dụng hiện tại:

1.  **Thu thập danh sách sản phẩm & nhu cầu:**
    Lấy danh sách các `ProductionPlanItem` thuộc kế hoạch.
2.  **Phân rã BOM & Cộng dồn Nhu cầu thô (Gross Demand):**
    *   Với mỗi item, lấy BOM đang hoạt động (`IsActive == true`).
    *   Với mỗi nguyên vật liệu thành phần (`BOMItem`) trong BOM:
        *   Nhu cầu dòng = `item.PlannedQty * BOMItem.QtyPer * (1 + BOMItem.ScrapPercent / 100)`.
        *   Cộng dồn nhu cầu dòng này vào bản đồ nguyên vật liệu (`Dictionary<ProductId, decimal>`).
3.  **Đối chiếu Tồn kho Khả dụng (Stock Available):**
    *   Với mỗi nguyên vật liệu cần dùng, truy vấn tổng số lượng khả dụng `QtyAvailable` trên bảng `StockBalance` trên toàn bộ hệ thống kho.
4.  **Tính Nhu cầu thực tế (Net Demand):**
    *   `NetDemand = Math.Max(0, GrossDemand - StockAvailable)`.
    *   Trả về danh sách đối tượng `MrpResultDto` chứa chi tiết thông tin vật tư, lượng nhu cầu, lượng tồn kho và lượng thiếu hụt.

---

### 3.3 Tự động Tạo Lệnh sản xuất hàng loạt (`GenerateWorkOrdersAsync`)
Khi người dùng bấm nút "Tạo Lệnh sản xuất" trên kế hoạch sản xuất:

1.  Mở một Database Transaction.
2.  Duyệt qua từng dòng `ProductionPlanItem` trong kế hoạch:
    *   Nếu dòng này chưa có `WorkOrderId` (chưa được tạo):
        *   Tìm phiên bản BOM đang hoạt động và Routing đang hoạt động của sản phẩm đó.
        *   Tạo mới thực thể `WorkOrder` ở trạng thái `Draft` (Nháp):
            *   `Code = $"WO-{plan.PlanNo}-{product.Code}"`
            *   `ProductId = item.ProductId`
            *   `Qty = item.PlannedQty`
            *   `DueDate = plan.PlanDate.AddDays(7)` (Mặc định hoàn thành sau 7 ngày)
            *   `BomVersion = bom.Version`
            *   `RoutingVersion = routing.Version`
        *   Lưu `WorkOrder` vào DB và gán ID mới sinh vào `item.WorkOrderId`.
3.  Lưu các thay đổi và Commit Transaction.

---

## 4. Cải tiến Giao diện Người dùng (UI/UX)

Xây dựng bộ điều khiển `ProductionPlanController.cs` và các màn hình Razor tương ứng để quản lý kế hoạch sản xuất:

### 4.1 Trang Tạo mới Kế hoạch (`ProductionPlan/Create.cshtml`)
*   Cung cấp form nhập thông tin chung (Số kế hoạch, Ngày kế hoạch).
*   Bảng động JavaScript cho phép thêm dòng sản phẩm sản xuất (tự động gợi ý từ danh mục sản phẩm có thuộc tính `IsManufactured == true`).
*   Nút thêm dòng và xóa dòng trực quan.

### 4.2 Trang Chi tiết Kế hoạch (`ProductionPlan/Details.cshtml`)
Giao diện phân vùng trực quan:
1.  **Thông tin chung & Trạng thái:** Hiển thị Badge trạng thái (Draft, Completed).
2.  **Khối Hành động (Actions Block):**
    *   Nút **"Tính nhu cầu vật tư (MRP)"**: Hiển thị bảng kết quả MRP tổng hợp phía dưới thông qua Ajax hoặc tải lại trang.
    *   Nút **"Tạo Lệnh sản xuất"**: Chỉ hiển thị ở trạng thái `Draft`. Khi bấm, hệ thống tạo hàng loạt các Lệnh sản xuất nháp và hiển thị danh sách liên kết tới các lệnh đó.
    *   Nút **"Xác nhận Kế hoạch"**: Chuyển trạng thái kế hoạch sang `Completed` (Khóa chỉnh sửa).
3.  **Danh sách Lệnh sản xuất liên kết:** Hiển thị các Lệnh sản xuất đã được tạo ra từ kế hoạch này kèm trạng thái của chúng (Draft, InProgress, Completed).

---

## 5. Kế hoạch Kiểm thử & Xác minh (Verification Plan)

### 5.1 Automated Tests (xUnit)
Bổ sung các kiểm thử trong `WmsMes.Tests/ProductionPlanTests.cs`:
1.  **Test Thuật toán MRP Tổng hợp:**
    *   Tạo Kế hoạch sản xuất chứa 2 sản phẩm A và B. Cấu hình BOM của A và B dùng chung nguyên vật liệu X.
    *   Chạy `CalculatePlanRequirementsAsync`. Xác minh xem nhu cầu thô `GrossDemand` của vật liệu X có bằng tổng nhu cầu của cả 2 sản phẩm cộng lại hay không.
2.  **Test Tạo Lệnh sản xuất hàng loạt:**
    *   Tạo Kế hoạch sản xuất -> Gọi `GenerateWorkOrdersAsync`.
    *   Xác minh hệ thống tạo ra đúng số lượng `WorkOrder` tương ứng với các dòng sản phẩm trong kế hoạch, các lệnh đều ở trạng thái `Draft` và được liên kết chính xác về ID.

---

## 6. Các bước triển khai cho Codex (Implementation Steps)

- [ ] **Bước 1:** Tạo 2 thực thể `ProductionPlan` và `ProductionPlanItem` trong thư mục `Domain/Entities`. Đăng ký DbSet trong `ApplicationDbContext.cs` và chạy Migration.
- [ ] **Bước 2:** Xây dựng interface `IProductionPlanService.cs` và class `ProductionPlanService.cs` triển khai thuật toán MRP tổng hợp và tạo Lệnh sản xuất hàng loạt.
- [ ] **Bước 3:** Viết bộ Unit Tests trong `WmsMes.Tests` để kiểm chứng thuật toán MRP tổng hợp và logic tạo Lệnh sản xuất.
- [ ] **Bước 4:** Xây dựng `ProductionPlanController.cs` xử lý các action Index, Details, Create (POST), RunMRP, GenerateWorkOrders, Complete.
- [ ] **Bước 5:** Thiết kế các màn hình Razor View (Index, Create, Details) cho Kế hoạch sản xuất.
- [ ] **Bước 6:** Tích hợp liên kết menu Kế hoạch sản xuất vào Sidebar của Layout hệ thống.
