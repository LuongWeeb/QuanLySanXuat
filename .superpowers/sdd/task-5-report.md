# Task 5 Report: MRP UX

## Status

Implemented the MRP product dropdown and hardened the calculation flow against invalid or forged input.

## Changes

- `MrpController` now loads only active manufactured products, ordered by code, for GET and every POST return path.
- POST validates positive quantity and verifies the submitted product is active and manufactured before calling `IMrpService`.
- Validation and MRP service failures return the form with the submitted product/quantity preserved.
- The MRP view uses an accessible product `<select>`, field validation, and the existing Bootstrap panel/table styling while retaining calculation results.
- Added the single shared `.nav-section-title` sidebar style.
- Added controller and view contract tests for filtering, valid POST results/selection, forged product and quantity rejection, service failure, and accessible markup.

## TDD Evidence

- RED: `dotnet test WmsMes.Tests/WmsMes.Tests.csproj --no-restore --filter FullyQualifiedName~MrpControllerTests`
  - Failed to compile because `Index` was not async and `MrpController` did not accept `ApplicationDbContext`, matching the missing behavior under test.
- GREEN focused: same command passed 7/7 tests.

## Verification Evidence

- `dotnet test WmsMes.sln --no-restore`: passed 67/67 tests, 0 failed.
- `dotnet build WmsMes.sln --no-restore`: succeeded with 0 warnings and 0 errors.
- `git diff --check`: passed.

## Concerns

- The external view reference was unavailable; the implementation intentionally follows the current `Mrp/Index` Bootstrap and `ops-panel` conventions as directed by the brief.
