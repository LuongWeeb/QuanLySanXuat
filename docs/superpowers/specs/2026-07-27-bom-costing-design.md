# Thiết kế Hệ thống Tính Giá thành & Định mức BOM (Costing - Phase 3)

## 1. Tổng quan
Tài liệu này đặc tả thiết kế kỹ thuật cho **Phase 3: Định mức BOM nâng cao & Tính giá thành sản xuất (BOM Costing & Work Center Rates)** thuộc lộ trình cải tiến hệ thống WMS + MES.

Mục tiêu chính là tích hợp cơ chế quản lý chi phí tài chính vào hoạt động sản xuất:
*   Cấu hình chi phí nhân công và máy móc trên từng Work Center (Trung tâm sản xuất).
*   Tính toán Giá thành Định mức tiêu chuẩn trên BOM dựa trên định mức vật tư và quy trình Routing.
*   Tự động tính toán Giá thành Thực tế (Actual Cost) khi hoàn thành Lệnh sản xuất (Work Order) để định giá đơn giá của Lô hàng thành phẩm đầu ra (`Lot.UnitPrice`), làm cơ sở cho giá vốn hàng bán (COGS) sau này.

---

## 2. Thay đổi Cơ sở dữ liệu (Database Schema)

### 2.1 Cấu hình Sản phẩm (`Product`)
Bổ sung giá vốn tiêu chuẩn dự phòng cho sản phẩm (sử dụng khi chưa có lịch sử giá nhập kho thực tế của Lot).
*   **File:** [Product.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Entities/Product.cs)
*   **Thuộc tính bổ sung:**
    ```csharp
    [Column(TypeName = "decimal(18,2)")]
    public decimal StandardCost { get; set; } = 0m;
    ```

### 2.2 Cấu hình Chi phí Trung tâm sản xuất (`WorkCenter`)
Bổ sung đơn giá chi phí nhân công và chi phí chạy máy mỗi giờ.
*   **File:** [WorkCenter.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Entities/WorkCenter.cs)
*   **Thuộc tính bổ sung:**
    ```csharp
    [Column(TypeName = "decimal(18,2)")]
    public decimal HourlyLaborRate { get; set; } = 0m;

    [Column(TypeName = "decimal(18,2)")]
    public decimal HourlyMachineRate { get; set; } = 0m;
    ```

### 2.3 Quản lý giá định mức trên Định mức vật tư (`BOM`)
Bổ sung các trường lưu tổng giá trị định mức dự kiến của BOM.
*   **File:** [BOM.cs](file:///d:/Qu%E1%BA%A3n%20l%C3%BD%20s%E1%BA%A3n%20xu%E1%BA%A5t/Domain/Entities/BOM.cs)
*   **Thuộc tính bổ sung:**
    ```csharp
    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalMaterialCost { get; set; } = 0m;

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalOperationCost { get; set; } = 0m;

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalStandardCost { get; set; } = 0m;
    ```

---

## 3. Logic Tính toán Giá thành (Costing Calculations)

### 3.1 Tính toán Giá thành tiêu chuẩn của BOM (Standard Cost)
Khi người dùng lưu hoặc kích hoạt một phiên bản BOM, hệ thống sẽ tự động tính toán lại các trường chi phí định mức:

1.  **Chi phí vật tư định mức (`TotalMaterialCost`):**
    *   Đối với mỗi vật tư thành phần (`BOMItem`):
        *   Tìm giá nguyên vật liệu: Lấy **đơn giá trung bình** từ các `Lot` hiện có của vật tư này. Nếu chưa có lô hàng nào nhập kho (tồn kho trống và chưa có lịch sử mua), lấy giá dự phòng **`Product.StandardCost`**.
        *   Chi phí vật tư = `QtyPer * (1 + ScrapPercent / 100) * Đơn giá nguyên vật liệu`.
    *   `TotalMaterialCost` = Tổng chi phí vật tư của tất cả BOM Items.
2.  **Chi phí vận hành định mức (`TotalOperationCost`):**
    *   Lấy quy trình Routing đang được kích hoạt (`IsActive == true`) của sản phẩm đó.
    *   Với mỗi công đoạn trong Routing (`RoutingStep`):
        *   Chi phí nhân công = `StandardTimeMinutes / 60 * WorkCenter.HourlyLaborRate`.
        *   Chi phí máy móc = `StandardTimeMinutes / 60 * WorkCenter.HourlyMachineRate`.
    *   `TotalOperationCost` = Tổng chi phí vận hành định mức của các công đoạn.
3.  **Tổng giá thành định mức tiêu chuẩn (`TotalStandardCost`):**
    *   `TotalStandardCost = TotalMaterialCost + TotalOperationCost`.

---

### 3.2 Tính toán Giá thành thực tế của Lệnh sản xuất (Actual Production Cost)
Khi người dùng bấm **Hoàn thành Lệnh sản xuất (Complete Work Order)**, hệ thống sẽ tính toán tổng giá trị chi phí thực tế phát sinh để gán đơn giá cho Lô hàng thành phẩm mới tạo:

1.  **Chi phí vật tư thực tế tiêu hao (Actual Material Cost):**
    *   Dựa trên danh sách vật tư đã được xuất tự động (Backflush) từ các Giữ chỗ (`MaterialReservation`).
    *   $\text{ActualMaterialCost} = \sum (\text{QtyReserved} \times \text{Lot.UnitPrice})$.
2.  **Chi phí vận hành thực tế (Actual Operation Cost):**
    *   Dựa trên nhật ký công đoạn sản xuất thực tế (`WorkOrderStep`):
        *   Thời gian thực tế: $T_{\text{actual}} = (\text{EndTime} - \text{StartTime}).\text{TotalMinutes}$.
        *   Nếu $T_{\text{actual}}$ chưa được ghi nhận (bị null hoặc bằng 0), hệ thống tự động lấy thời gian tiêu chuẩn $T_{\text{standard}}$ của công đoạn từ Routing để dự phòng.
        *   Chi phí vận hành của công đoạn = $T / 60 \times (\text{WorkCenter.HourlyLaborRate} + \text{WorkCenter.HourlyMachineRate})$.
    *   $\text{ActualOperationCost} = \sum (\text{Chi phí vận hành thực tế từng công đoạn})$.
3.  **Tổng chi phí thực tế:**
    *   $\text{TotalActualCost} = \text{ActualMaterialCost} + \text{ActualOperationCost}$.
4.  **Đơn giá thành phẩm sản xuất ra (`Lot.UnitPrice`):**
    *   $$\text{Lot.UnitPrice} = \frac{\text{TotalActualCost}}{\text{QtyOK}}$$
    *   *Chú ý:* Giá trị `Lot.UnitPrice` này sẽ được gán cho Lô thành phẩm mới tạo ra trong kho và đồng thời được lưu vào thuộc tính `ValuationRate` trên dòng Sổ cái Kho (`StockTransaction`) tương ứng để đồng bộ số liệu tài chính.

---

## 4. Cải tiến Giao diện Người dùng (UI/UX)

### 4.1 Bổ sung trường nhập liệu Giá tiêu chuẩn & Chi phí giờ
*   **Trang Danh mục Sản phẩm (Thêm/Sửa):** Thêm ô nhập liệu `StandardCost` (định dạng số, hỗ trợ hiển thị VNĐ).
*   **Trang Cấu hình Work Center (Thêm/Sửa):** Thêm 2 ô nhập liệu `HourlyLaborRate` và `HourlyMachineRate`.

### 4.2 Trang Chi tiết BOM (`Views/Bom/Details.cshtml`)
*   Hiển thị bảng chi tiết giá thành tiêu chuẩn định mức:
    *   Tổng chi phí nguyên vật liệu định mức.
    *   Tổng chi phí nhân công định mức.
    *   Tổng chi phí máy móc vận hành định mức.
    *   Tổng chi phí tiêu chuẩn của 1 đơn vị thành phẩm.

### 4.3 Trang Chi tiết Lệnh sản xuất (`Views/WorkOrder/Details.cshtml`)
Thêm một phân đoạn **"Bảng phân tích Giá thành sản xuất (Production Cost Analysis)"** hiển thị so sánh:

| Khoản mục chi phí | Định mức (Target) | Thực tế (Actual) | Chênh lệch (Variance) |
| :--- | :--- | :--- | :--- |
| **Chi phí vật tư** | TargetMaterialCost | ActualMaterialCost | VarianceMaterial |
| **Chi phí nhân công** | TargetLaborCost | ActualLaborCost | VarianceLabor |
| **Chi phí vận hành máy** | TargetMachineCost | ActualMachineCost | VarianceMachine |
| **TỔNG CỘNG** | **TargetTotal** | **ActualTotal** | **VarianceTotal** |
| **Giá thành đơn vị** | **UnitTarget** | **UnitActual** | **VarianceUnit** |

---

## 5. Kế hoạch Kiểm thử & Xác minh (Verification Plan)

### 5.1 Automated Tests (xUnit)
Bổ sung các test case trong `WmsMes.Tests/WorkOrderServiceTests.cs` hoặc `BOMTests.cs`:
1.  **Test Tính toán BOM Costing tự động:**
    *   Tạo BOM gồm 2 vật tư, cấu hình giá Product StandardCost và Lot UnitPrice. Cấu hình WorkCenter rates và Routing steps.
    *   Gọi hàm tính toán/kích hoạt BOM. Xác minh các trường `TotalMaterialCost`, `TotalOperationCost` và `TotalStandardCost` được lưu chính xác trong database.
2.  **Test Giá vốn Thành phẩm khi hoàn thành Lệnh sản xuất:**
    *   Thực hiện chạy và hoàn thành Lệnh sản xuất.
    *   Xác minh `Lot.UnitPrice` của lô hàng thành phẩm mới tạo ra có giá trị bằng đúng công thức: `(Tổng vật tư thực tế + Tổng vận hành thực tế) / QtyOK`.
    *   Kiểm tra dòng `StockTransaction` (Sổ cái) nhập kho có trường `ValuationRate` khớp với giá vốn thành phẩm vừa tính.

---

## 6. Các bước triển khai cho Codex (Implementation Steps)

- [ ] **Bước 1:** Cập nhật database schema (thêm trường StandardCost vào `Product`, các Rate vào `WorkCenter`, và các Cost vào `BOM`). Tạo và chạy Migration.
- [ ] **Bước 2:** Cập nhật các màn hình quản trị (Product, WorkCenter, BOM) để hỗ trợ nhập liệu và hiển thị các trường chi phí mới.
- [ ] **Bước 3:** Viết hàm tự động cập nhật chi phí định mức khi tạo/kích hoạt BOM trong `BomService` hoặc `BomController`.
- [ ] **Bước 4:** Cập nhật logic hoàn thành Lệnh sản xuất trong `WorkOrderService.cs` (CompleteWorkOrderAsync) để tính toán chi phí thực tế và lưu vào đơn giá `Lot.UnitPrice` cùng sổ cái.
- [ ] **Bước 5:** Viết các Unit Tests tương ứng kiểm thử tính chính xác của thuật toán phân bổ chi phí.
- [ ] **Bước 6:** Thiết kế và tích hợp bảng so sánh Giá thành sản xuất vào View Chi tiết Lệnh sản xuất.
