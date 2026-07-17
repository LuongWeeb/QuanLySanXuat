# Task 6 Report: QC UI

## Implemented

- Added authorized `QcController` (`Admin,QC,Manager`) with pending On Hold lot index.
- Added eligible-lot inspection loading with the latest active product checklist and its items.
- Added a dedicated POST input model, antiforgery, authenticated user identity, server-side entity construction, checklist/item/result/required/numeric validation, safe failure handling, and PRG.
- Kept threshold evaluation and PASS/REJECT stock transitions in `IQcService.SubmitQCInspectionAsync`.
- Added accessible Bootstrap index and dynamic inspection views with status messaging and validation UI.

## TDD evidence

1. RED: focused tests failed to compile because `QcController` did not exist.
2. GREEN: controller/model/views added; 10 focused tests passed.
3. RED: numeric checklist measurement test failed because `not-a-number` was accepted.
4. GREEN: numeric input validation added for bounded checklist items; 11 focused tests passed.

## Verification

- `dotnet test WmsMes.Tests\WmsMes.Tests.csproj --no-restore --filter FullyQualifiedName~QcControllerTests`: 11 passed, 0 failed.
- `dotnet test WmsMes.Tests\WmsMes.Tests.csproj --no-restore`: 78 passed, 0 failed.
- `dotnet build WmsMes.Web.csproj --no-restore`: succeeded, 0 warnings, 0 errors.

## Notes

- The controller accepts only PASS or REJECT because those are the two outcomes specified for this UI; `REWORK` remains a domain enum value but is not exposed here.
- Measurement values remain strings to match the existing service DTO/entity contract. Numeric checklist items are syntax-validated at the boundary; service-owned business evaluation determines whether values are within checklist thresholds.
