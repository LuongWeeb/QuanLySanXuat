# Task 1 Report: Report Export Service and Controller Endpoints

## Status

Implemented Task 1 on `feature/export-reporting-docker`. No Task 2 Docker files were created or modified.

## Implementation

- Added ClosedXML `0.102.2` and QuestPDF `2023.12.6` package references.
- Added `IReportExportService` with the approved Excel and PDF export APIs.
- Added `ReportExportService`:
  - Loads stock balances without tracking, including product/UOM, lot, and location/zone data.
  - Applies the optional warehouse filter through `Location.Zone.WarehouseId`.
  - Produces an XLSX workbook with the seven requested Vietnamese columns, `#1E293B`/white bold headers, `#,##0.00` quantity formatting, `dd/MM/yyyy` expiry formatting, filtering, frozen headers, and fitted columns.
  - Loads a work order with product and ordered operation steps/work centers.
  - Produces an A4 PDF work-order ticket with work-order metadata, operations table, pagination, and a rendered Code 39 barcode for the work-order code.
- Registered `IReportExportService` as scoped in application DI.
- Added `InventoryController.ExportExcel(int? warehouseId)` at `GET export-excel` with the XLSX MIME type and dated `TonKho_*.xlsx` filename.
- Added `WorkOrderController.ExportPdf(int id)` at `GET export-pdf/{id:int}` with the PDF MIME type and dated `LenhSanXuat_{id}_*.pdf` filename.
- Kept exporter dependencies as optional trailing controller constructor parameters so existing direct controller construction remains source-compatible while production DI supplies the registered service.
- Added four focused tests covering Excel bytes/signature, PDF bytes/signature, inventory controller file response, and work-order controller file response.

## Files

- Modified `WmsMes.Web.csproj`
- Created `Services/IReportExportService.cs`
- Created `Services/ReportExportService.cs`
- Modified `Controllers/InventoryController.cs`
- Modified `Controllers/WorkOrderController.cs`
- Modified `Program.cs`
- Created `WmsMes.Tests/ReportExportTests.cs`
- Created `.superpowers/sdd/task-1-report.md`

## TDD Evidence

### RED

Command:

```powershell
dotnet test WmsMes.sln --filter "FullyQualifiedName~ReportExportTests"
```

Result: exit code `1`. The focused test project failed to compile for the expected missing-feature reasons:

- `CS0246`: `ReportExportService` did not exist.
- `CS0246`: `IReportExportService` did not exist.
- `CS1739`: `InventoryController` had no `reportExportService` constructor parameter.
- `CS1061`: `InventoryController.ExportExcel` did not exist.
- `CS1729`: `WorkOrderController` had no four-argument constructor.
- `CS1061`: `WorkOrderController.ExportPdf` did not exist.

No unrelated failure appeared in the RED output.

### GREEN

Command:

```powershell
dotnet test WmsMes.sln --filter "FullyQualifiedName~ReportExportTests"
```

Result: exit code `0`; `4` passed, `0` failed, `0` skipped. Duration: `336 ms`.

## Full Verification

Command:

```powershell
dotnet test WmsMes.sln
```

Result: exit code `0`; `135` passed, `0` failed, `0` skipped. Duration: `2 s`.

Additional review command:

```powershell
git diff --check
```

Result: exit code `0`; no whitespace errors. Git emitted only the repository's expected LF-to-CRLF working-copy notices.

## Self-Review

- Re-read the Task 1 brief and checked every requested file/API/output against the diff.
- Confirmed warehouse filtering uses the actual entity relationship and remains optional.
- Confirmed all export queries use `AsNoTracking` and eagerly load the navigation data required after query completion.
- Confirmed controller responses propagate the filter/ID, return the requested MIME types, and use dated download filenames.
- Confirmed the PDF includes work-order metadata, production operations ordered by step number, and a UTF-8 QR code generated from the unmodified work-order code.
- Confirmed the ClosedXML and QuestPDF versions exactly match the brief; QRCoder `1.8.0` was added to encode the full work-order code without Code 39 character loss.
- Confirmed no Docker, compose, or `.dockerignore` file was touched.
- Confirmed existing controller tests remain source-compatible and the full test suite is green.

## Concerns

- `QuestPDF.Settings.License` is configured as `LicenseType.Community`, which is required for document generation with this package. The deploying organization must confirm that it meets QuestPDF Community license eligibility before production deployment; otherwise the configured license type must be changed appropriately.

## Code Review Fix Addendum

### Important Finding Addressed

The original Code 39 renderer replaced unsupported characters with `-`. Consequently, valid work-order codes such as `WO_001` produced bars for `WO-001` even though the visible label still showed the original value. The renderer was replaced with a PNG QR code produced by QRCoder `1.8.0`. The service now:

- Passes `workOrder.Code` unchanged to the QR generator.
- Forces UTF-8 encoding with UTF-8 ECI, supporting the controller's full string input rather than a limited Code 39 alphabet.
- Embeds the generated QR image in the QuestPDF document and continues to display the exact work-order code underneath it.

### Review-Fix TDD Evidence

#### RED: underscore regression

Command:

```powershell
dotnet test WmsMes.sln --filter "FullyQualifiedName~ReportExportTests.ExportWorkOrderToPdf_ReturnsPdfDocument"
```

Result: exit code `1`; `1` failed, `0` passed. The `WO_001` regression generated a PDF, but `Assert.Contains("/Subtype /Image", ...)` failed because the existing lossy Code 39 renderer emitted vector bars rather than an exact-payload QR image.

#### First GREEN attempt diagnostic

Command:

```powershell
dotnet test WmsMes.sln --filter "FullyQualifiedName~ReportExportTests.ExportWorkOrderToPdf_ReturnsPdfDocument"
```

Result: exit code `1` with `CS1744`. The restored QRCoder `1.8.0` API names the short-overload module-size parameter `size`, not `pixelsPerModule`. Package XML documentation was inspected and the call was corrected without changing the design.

#### GREEN: underscore regression

Command:

```powershell
dotnet test WmsMes.sln --filter "FullyQualifiedName~ReportExportTests.ExportWorkOrderToPdf_ReturnsPdfDocument"
```

Result: exit code `0`; `1` passed, `0` failed, `0` skipped.

The regression was then strengthened at the exported-PDF boundary: it extracts the embedded QR image stream for `WO_001` and `WO-001` and asserts that they differ, proving the two values formerly collapsed by Code 39 now produce distinct encoded graphics.

### Strengthened Excel/PDF Coverage

- The Excel test now reopens the generated XLSX with ClosedXML.
- It verifies all seven requested Vietnamese headers and exported product/lot/location/quantity/UOM/expiry values.
- It seeds another warehouse and proves `warehouseId` excludes that warehouse's stock row.
- It verifies bold white headers, `#1E293B` background, `#,##0.00` number format, and `dd/MM/yyyy` date format.
- The PDF test uses an underscore-containing code, verifies that an image is embedded, and verifies underscore/hyphen values produce different QR image streams.

Focused command after final test strengthening:

```powershell
dotnet test WmsMes.sln --filter "FullyQualifiedName~ReportExportTests"
```

Result: exit code `0`; `4` passed, `0` failed, `0` skipped. Duration: `639 ms`.

### Final Review-Fix Verification

Command:

```powershell
dotnet test WmsMes.sln
```

Result: exit code `0`; `135` passed, `0` failed, `0` skipped. Duration: `3 s`.
