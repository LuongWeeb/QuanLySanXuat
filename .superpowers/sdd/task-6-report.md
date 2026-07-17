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

## Review fixes

- Blank optional checklist measurements are omitted from `QCInspection.Lines`; required blanks remain validation errors.
- The inspection view now renders all ModelState errors, including checklist and measurement-level validation.
- Submission requires an authenticated `NameIdentifier`; missing identity safely redirects and never calls the service.
- Duplicate QC submissions are rejected before mutation, and a unique database index on `QCInspection.LotId` provides the concurrency backstop when requests race.
- QC processing now handles every On Hold stock balance for the lot instead of an arbitrary first balance.

### Additional RED/GREEN evidence

1. RED: optional blank produced a second failing service line; missing identity called the service as `system`; view contract lacked an All validation summary.
2. RED: a second submission persisted a duplicate inspection; the EF model had no unique lot constraint; only one of two On Hold balances was released.
3. GREEN: 17 focused controller/concurrency tests passed after boundary and service/model changes.

### Final review verification

- Focused QC controller/service tests: 22 passed, 0 failed.
- Full suite: 84 passed, 0 failed.
- Build: succeeded, 0 warnings, 0 errors.
- EF migration script generation succeeded and contains `CREATE UNIQUE INDEX IX_QCInspections_LotId`.
- Live `database update` was not run because the configured SQL Server LocalDB instance is unavailable in this environment.

## Concurrency and quarantine redesign

- Removed the unsafe unique `QCInspection.LotId` migration and model constraint so a lot can retain inspection history after being placed On Hold again.
- Relational submissions now atomically claim positive On Hold rows outside `QC-QUARANTINE` with a conditional database update inside the existing transaction. A competing request that changes no rows returns false and cannot insert an inspection.
- Rejected quantity from multiple source locations is consolidated into the single existing-or-new `(Product, Lot, QC-QUARANTINE)` balance. Source balances retain their location and are zeroed, avoiding collisions with the existing unique stock-balance key.
- Quarantine stock is excluded from the pending-QC index, eligibility checks, and service claims.

### TDD evidence for redesign

1. RED: historical inspection was blocked by the unique lot index; multiple REJECT sources collided at quarantine; concurrent SQLite submissions both succeeded.
2. GREEN: relational SQLite concurrency permits exactly one successful claim and one inspection; historical inspection succeeds after new non-quarantine hold stock is added; two sources plus a pre-existing quarantine balance consolidate correctly.

### Final redesign verification

- Focused QC controller/service tests: 24 passed, 0 failed.
- Full suite: 86 passed, 0 failed.
- Build: succeeded, 0 warnings, 0 errors.
- Current migration SQL generation succeeded; no new migration is required because the unsafe unshipped migration was removed.
- `dotnet ef database update --no-build --project WmsMes.Web.csproj` failed because SQL Server LocalDB could not create/connect to the automatic instance; no database-update success is claimed.
