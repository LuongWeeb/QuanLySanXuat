# Thiết kế Màn hình Dashboard Giám sát Nhà máy & Báo cáo OEE (Phase 8)

## 1. Tổng quan
Tài liệu này đặc tả thiết kế kỹ thuật cho **Phase 8: Factory KPI & Realtime OEE Dashboard (Màn hình Dashboard Giám sát Nhà máy & Báo cáo OEE theo Thời gian thực)** thuộc hệ thống WMS + MES.

Mục tiêu chính:
1. Xây dựng dịch vụ `OeeService` tính toán chỉ số Hiệu suất Thiết bị Tổng thể (OEE - Overall Equipment Effectiveness) trên từng Trạm sản xuất (`WorkCenter`).
2. Hiển thị bảng điều khiển trực quan (Executive Dashboard) hỗ trợ Ban giám đốc và Quản lý nhà máy theo dõi:
   *   Chỉ số OEE (Khả dụng x Hiệu suất x Chất lượng) dạng đồng hồ Gauge theo thời gian thực.
   *   Biểu đồ so sánh Sản lượng Định mức vs Sản lượng Thực tế (Chart.js Bar Chart).
   *   Biểu đồ Xu hướng Phế phẩm & Lỗi chất lượng (Line Chart).
   *   Phân tích Giá trị & Tuổi tồn kho (Inventory Valuation & Aging Donut Chart).
3. Cập nhật số liệu tự động theo thời gian thực thông qua SignalR (`ProductionHub`).

---

## 2. Công thức & Thuật toán Tính OEE (`OeeService`)

Chỉ số OEE của một Trạm sản xuất (`WorkCenter`) được tính toán theo công thức chuẩn công nghiệp:

$$\text{OEE} = \text{Availability (Độ khả dụng)} \times \text{Performance (Hiệu suất)} \times \text{Quality (Chất lượng)}$$

### 2.1 Độ khả dụng - Availability ($A$)
$$\text{Availability} = \frac{\text{Thời gian vận hành thực tế}}{\text{Thời gian khả dụng kế hoạch}}$$
*   **Thời gian vận hành thực tế ($T_{\text{run}}$):** Tổng số phút thực tế các công đoạn chạy tại Trạm = $\sum (\text{EndTime} - \text{StartTime}).\text{TotalMinutes}$.
*   **Thời gian khả dụng kế hoạch ($T_{\text{plan}}$):** Tổng số phút khả dụng trong ca làm việc (Mặc định 480 phút/ngày $\times$ số ngày trong kỳ).

### 2.2 Hiệu suất - Performance ($P$)
$$\text{Performance} = \frac{\text{Tổng sản lượng sản xuất thực tế}}{\text{Năng suất định mức trong thời gian vận hành}}$$
*   **Tổng sản lượng sản xuất thực tế ($Q_{\text{total}}$):** $\sum (\text{QtyOK} + \text{QtyReject} + \text{QtyRework})$.
*   **Năng suất định mức ($Q_{\text{target}}$):** $\frac{T_{\text{run}}}{\text{StandardTimeMinutes}}$.

### 2.3 Chất lượng - Quality ($Q$)
$$\text{Quality} = \frac{\text{Sản lượng đạt (QtyOK)}}{\text{Tổng sản lượng sản xuất thực tế (} Q_{\text{total}} \text{)}}$$

### 2.4 Đánh giá Mức độ OEE (Status Color Indicator)
*   **OEE $\ge$ 85%:** Đạt chuẩn đẳng cấp thế giới (World Class - Màu Xanh Lá).
*   **65% $\le$ OEE < 85%:** Đạt mức trung bình (Màu Vàng).
*   **OEE < 65%:** Mức cảnh báo cần cải thiện (Màu Đỏ).

---

## 3. Quy trình Dữ liệu & Dịch vụ (Core Services)

### 3.1 Giao diện `IOeeService` & Dịch vụ `OeeService`
*   **File mới:** `Services/IOeeService.cs` & `Services/OeeService.cs`
*   **Phương thức:**
    *   `GetWorkCenterOeeAsync(int workCenterId, DateTime startDate, DateTime endDate)`: Trả về DTO `OeeMetricsDto` chứa $A, P, Q, OEE$.
    *   `GetAllWorkCentersOeeAsync(DateTime startDate, DateTime endDate)`: Trả về danh sách OEE của tất cả các WorkCenter.
    *   `GetInventoryAgingAnalyticsAsync()`: Trả về phân tích tuổi tồn kho (<30 ngày, 30-60 ngày, 60-90 ngày, >90 ngày).
    *   `GetProductionProgressAnalyticsAsync()`: Trả về tiến độ sản xuất thực tế vs kế hoạch.

### 3.2 Tích hợp SignalR Realtime (`DashboardHub`)
*   Khi công nhân bấm **Hoàn thành công đoạn** trên Trạm vận hành (`WorkerController`), hệ thống phát một SignalR event tới tất cả các client đang mở Dashboard để tự động cập nhật biểu đồ mà không cần F5 lại trang.

---

## 4. Giao diện Người dùng (UI/UX)

### 4.1 Màn hình Dashboard Nhà máy (`DashboardController.cs`)
*   **URL:** `/Dashboard` hoặc `/Home/FactoryDashboard`
*   **Các khối giao diện:**
    1.  **Hàng KPI Thẻ Thông Số:** Tổng sản lượng hôm nay, Tỷ lệ OEE trung bình toàn nhà máy, Tỷ lệ phế phẩm (Scrap Rate), Tổng giá trị tồn kho.
    2.  **Khối Đồng hồ OEE Trạm máy:** Hiển thị thẻ card cho từng `WorkCenter` với vòng tròn OEE %, đính kèm chỉ số con (A%, P%, Q%).
    3.  **Biểu đồ Tiến độ Sản xuất (Bar Chart):** So sánh sản lượng định mức vs thực tế của các Lệnh sản xuất đang chạy.
    4.  **Biểu đồ Phân tích Tuổi tồn kho (Donut Chart):** Phân bổ tồn kho theo thời gian lưu kho.

---

## 5. Kế hoạch Kiểm thử & Xác minh (Verification Plan)

### 5.1 Automated Tests (xUnit)
Bổ sung kiểm thử trong `WmsMes.Tests/OeeServiceTests.cs`:
1.  **Test Công thức OEE:**
    *   Tạo dữ liệu giả lập cho 1 WorkCenter với thời gian chạy thực tế 240 phút (Availability = 50%), sản xuất 100 sản phẩm với định mức 240 phút (Performance = 100%), trong đó 90 sản phẩm đạt, 10 sản phẩm phế (Quality = 90%).
    *   Gọi `GetWorkCenterOeeAsync`. Xác minh $OEE = 50\% \times 100\% \times 90\% = 45\%$.

---

## 6. Các bước triển khai cho Codex (Implementation Steps)

- [ ] **Bước 1:** Định nghĩa `OeeMetricsDto.cs` và triển khai `IOeeService.cs` / `OeeService.cs` chứa các thuật toán tính OEE, tiến độ sản xuất và phân tích tuổi tồn kho. Đăng ký DI.
- [ ] **Bước 2:** Viết các Unit Tests kiểm thử công thức OEE trong `WmsMes.Tests/OeeServiceTests.cs`.
- [ ] **Bước 3:** Tạo `DashboardController.cs` (hoặc mở rộng `HomeController.cs`) trả về View Dashboard và API dữ liệu JSON cho Chart.js.
- [ ] **Bước 4:** Thiết kế màn hình View `Dashboard/Index.cshtml` tích hợp thư viện Chart.js, các thẻ Gauge OEE và SignalR realtime client.
- [ ] **Bước 5:** Thêm liên kết menu "Dashboard Nhà máy & OEE" vào vị trí nổi bật trên Sidebar Layout.
