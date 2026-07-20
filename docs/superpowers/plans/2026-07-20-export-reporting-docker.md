# [Feature] Export Reporting Engine (Excel/PDF) & Docker Containerization Implementation Plan

> **For agentic workers (Codex / Antigravity):** REQUIRED SUB-SKILL: Use TDD & step-by-step verification. Follow exact file paths and test execution commands. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tích hợp bộ công cụ Xuất Báo cáo Excel / PDF chuyên nghiệp cho Phiếu Nhập/Xuất kho, Báo cáo tồn kho & Lệnh sản xuất, đồng thời đóng gói dự án với Docker & docker-compose giúp sẵn sàng triển khai trên Server.

**Architecture:** Bổ sung gói NuGet `ClosedXML` để tạo file Excel `.xlsx` và `QuestPDF` (hoặc HTML Report Formatter) xuất file `.pdf`. Xây dựng `ReportExportService` phục vụ xuất dữ liệu báo cáo. Thêm `Dockerfile` đa tầng (multi-stage) và `docker-compose.yml` kết hợp ASP.NET Core 8 và SQL Server 2022.

**Tech Stack:** ASP.NET Core 8 MVC / Web API, ClosedXML 0.102.x, QuestPDF 2023.12.x, Docker & docker-compose, xUnit (.NET 8).

---

## Global Constraints
- Target Framework: `.NET 8` (`net8.0`)
- Giữ nguyên toàn bộ 131 unit tests hiện có trong `WmsMes.Tests`.
- Đảm bảo file Excel xuất ra có header đẹp, định dạng số/ngày tháng đúng chuẩn tiếng Việt.

---

### Task 1: Tích hợp ClosedXML & QuestPDF và Xây dựng ReportExportService

**Files:**
- Modify: `WmsMes.Web.csproj`
- Create: `Services/IReportExportService.cs`
- Create: `Services/ReportExportService.cs`
- Modify: `Controllers/InventoryController.cs`
- Modify: `Controllers/WorkOrderController.cs`
- Modify: `Program.cs`
- Test: `WmsMes.Tests/ReportExportTests.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext` & Data Entities
- Produces: `Task<byte[]> ExportStockBalanceExcelAsync()`, `Task<byte[]> ExportWorkOrderPdfAsync(int workOrderId)`

- [ ] **Step 1: Bổ sung Packages vào WmsMes.Web.csproj**

Thêm `PackageReference`:
```xml
<PackageReference Include="ClosedXML" Version="0.102.2" />
<PackageReference Include="QuestPDF" Version="2023.12.6" />
```

- [ ] **Step 2: Tạo Services/IReportExportService.cs**

```csharp
namespace WmsMes.Web.Services;

public interface IReportExportService
{
    Task<byte[]> ExportStockBalanceToExcelAsync(int? warehouseId = null);
    Task<byte[]> ExportWorkOrderToPdfAsync(int workOrderId);
}
```

- [ ] **Step 3: Viết Failing Test trong WmsMes.Tests/ReportExportTests.cs**

```csharp
public class ReportExportTests
{
    [Fact]
    public async Task ExportStockBalanceToExcel_ReturnsNonEmptyByteArray()
    {
        // Setup InMemory DB với StockBalances
        // Act: ExportStockBalanceToExcelAsync()
        // Assert: Trả về byte[] không rỗng (> 1000 bytes)
    }
}
```

- [ ] **Step 4: Chạy test để xác nhận FAIL**

Run: `dotnet test WmsMes.sln --filter "FullyQualifiedName~ReportExportTests"`
Expected: `FAIL`

- [ ] **Step 5: Triển khai ReportExportService.cs với ClosedXML & QuestPDF**

- Sử dụng `ClosedXML.Excel.XLWorkbook` để tạo bảng dữ liệu tồn kho: Mã SP, Tên SP, Lô, Vị trí, Số lượng khả dụng, Đơn vị tính, Hạn dùng.
- Định dạng tiêu đề cột (bold, background color #1E293B, text white), format cột số `#,##0.00`.
- Sử dụng `QuestPDF` tạo mẫu PDF Lệnh sản xuất (Work Order Ticket): Mã lệnh, Sản phẩm, Số lượng mục tiêu, Các công đoạn sản xuất (Operations) & Mã QR Code/Barcode.

- [ ] **Step 6: Đăng ký Service & Thêm Controller Actions**

Đăng ký `builder.Services.AddScoped<IReportExportService, ReportExportService>();` trong `Program.cs`.
Bổ sung action trong `InventoryController.cs`:
```csharp
[HttpGet("export-excel")]
public async Task<IActionResult> ExportExcel(int? warehouseId)
{
    var bytes = await _reportExportService.ExportStockBalanceToExcelAsync(warehouseId);
    return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"TonKho_{DateTime.Now:yyyyMMdd}.xlsx");
}
```

- [ ] **Step 7: Chạy lại test để xác nhận PASS**

Run: `dotnet test WmsMes.sln`
Expected: `Passed! - All tests pass`

---

### Task 2: Đóng gói Docker & Docker Compose cho Hệ thống WMS-MES

**Files:**
- Create: `Dockerfile`
- Create: `.dockerignore`
- Create: `docker-compose.yml`

**Interfaces:**
- Consumes: Môi trường Docker Desktop / Docker Engine
- Produces: Hệ thống chạy hoàn chỉnh gồm 2 container `wmsmes-web` (C# ASP.NET Core) và `wmsmes-db` (SQL Server 2022).

- [ ] **Step 1: Tạo .dockerignore**

```text
**/.git
**/.vs
**/.vscode
**/bin
**/obj
**/out
**/.agents
```

- [ ] **Step 2: Tạo Dockerfile (Multi-stage build cho ASP.NET Core 8)**

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["WmsMes.Web.csproj", "./"]
RUN dotnet restore "WmsMes.Web.csproj"
COPY . .
RUN dotnet build "WmsMes.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "WmsMes.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "WmsMes.Web.dll"]
```

- [ ] **Step 3: Tạo docker-compose.yml**

```yaml
version: '3.8'

services:
  wmsmes-db:
    image: mcr.microsoft.com/mssql/server:2022-latest
    container_name: wmsmes-db
    environment:
      - ACCEPT_EULA=Y
      - MSSQL_SA_PASSWORD=YourSecurePassword123!
    ports:
      - "1433:1433"
    volumes:
      - sqldata:/var/opt/mssql/data

  wmsmes-web:
    build:
      context: .
      dockerfile: Dockerfile
    container_name: wmsmes-web
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ConnectionStrings__DefaultConnection=Server=wmsmes-db;Database=WmsMesDb;User Id=sa;Password=YourSecurePassword123!;TrustServerCertificate=True;MultipleActiveResultSets=true
    ports:
      - "5000:8080"
    depends_on:
      - wmsmes-db

volumes:
  sqldata:
```

- [ ] **Step 4: Test Build dự án**

Run: `dotnet build WmsMes.sln`
Expected: `Build succeeded. 0 Error(s)`

---

## Verification Plan

### Automated Tests
- Chạy `dotnet test WmsMes.sln` đảm bảo 100% Unit Tests (bao gồm test xuất Excel/PDF mới) vượt qua.

### Manual Verification
- Gọi URL `GET /Inventory/ExportExcel` và kiểm tra file Excel `TonKho_YYYYMMDD.xlsx` tải về có đầy đủ dữ liệu và định dạng.
- Chạy thử `docker compose build` để kiểm tra quá trình đóng gói Docker thành công.
