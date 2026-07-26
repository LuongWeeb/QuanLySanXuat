using System.Globalization;
using WmsMes.Web.Domain.Common;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Tests;

public class CommonExtensionsTests
{
    public static TheoryData<WorkOrderStatus, string> WorkOrderStatuses => new()
    {
        { WorkOrderStatus.Draft, "Nháp" },
        { WorkOrderStatus.Pending, "Chờ duyệt" },
        { WorkOrderStatus.Approved, "Đã phê duyệt" },
        { WorkOrderStatus.InProgress, "Đang sản xuất" },
        { WorkOrderStatus.Completed, "Đã hoàn thành" },
        { WorkOrderStatus.Closed, "Đã đóng" }
    };

    public static TheoryData<WorkOrderStepStatus, string> WorkOrderStepStatuses => new()
    {
        { WorkOrderStepStatus.Pending, "Chờ bắt đầu" },
        { WorkOrderStepStatus.InProgress, "Đang sản xuất" },
        { WorkOrderStepStatus.Completed, "Đã hoàn thành" }
    };

    public static TheoryData<DocumentStatus, string> DocumentStatuses => new()
    {
        { DocumentStatus.Draft, "Nháp" },
        { DocumentStatus.Completed, "Đã hoàn thành" }
    };

    public static TheoryData<ProductType, string> ProductTypes => new()
    {
        { ProductType.RawMaterial, "Nguyên vật liệu" },
        { ProductType.WIP, "Bán thành phẩm" },
        { ProductType.FinishedGood, "Thành phẩm" }
    };

    public static TheoryData<QCResult, string> QcResults => new()
    {
        { QCResult.PASS, "Đạt" },
        { QCResult.REJECT, "Không đạt" },
        { QCResult.REWORK, "Làm lại" }
    };

    public static TheoryData<StocktakeStatus, string> StocktakeStatuses => new()
    {
        { StocktakeStatus.Draft, "Nháp" },
        { StocktakeStatus.Counting, "Đang kiểm đếm" },
        { StocktakeStatus.AwaitingApproval, "Chờ phê duyệt" },
        { StocktakeStatus.Completed, "Đã hoàn thành" }
    };

    public static TheoryData<TransactionType, string> TransactionTypes => new()
    {
        { TransactionType.Receipt, "Nhập kho" },
        { TransactionType.Issue, "Xuất kho" },
        { TransactionType.Transfer, "Chuyển kho" },
        { TransactionType.Adjust, "Điều chỉnh" },
        { TransactionType.Backflush, "Xuất kho tự động" }
    };

    [Theory]
    [MemberData(nameof(WorkOrderStatuses))]
    public void ToVietnameseString_TranslatesEveryWorkOrderStatus(WorkOrderStatus value, string expected) =>
        Assert.Equal(expected, value.ToVietnameseString());

    [Theory]
    [MemberData(nameof(WorkOrderStepStatuses))]
    public void ToVietnameseString_TranslatesEveryWorkOrderStepStatus(WorkOrderStepStatus value, string expected) =>
        Assert.Equal(expected, value.ToVietnameseString());

    [Theory]
    [MemberData(nameof(DocumentStatuses))]
    public void ToVietnameseString_TranslatesEveryDocumentStatus(DocumentStatus value, string expected) =>
        Assert.Equal(expected, value.ToVietnameseString());

    [Theory]
    [MemberData(nameof(ProductTypes))]
    public void ToVietnameseString_TranslatesEveryProductType(ProductType value, string expected) =>
        Assert.Equal(expected, value.ToVietnameseString());

    [Theory]
    [MemberData(nameof(QcResults))]
    public void ToVietnameseString_TranslatesEveryQcResult(QCResult value, string expected) =>
        Assert.Equal(expected, value.ToVietnameseString());

    [Theory]
    [MemberData(nameof(StocktakeStatuses))]
    public void ToVietnameseString_TranslatesEveryStocktakeStatus(StocktakeStatus value, string expected) =>
        Assert.Equal(expected, value.ToVietnameseString());

    [Theory]
    [MemberData(nameof(TransactionTypes))]
    public void ToVietnameseString_TranslatesEveryTransactionType(TransactionType value, string expected) =>
        Assert.Equal(expected, value.ToVietnameseString());

    [Fact]
    public void DisplayHelpers_AreDeterministicUnderNonVietnameseCurrentCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

            Assert.Equal("1.234,50", 1234.5m.ToVietnameseNumber());
            Assert.Equal("1.234,500", 1234.5m.ToVietnameseNumber("N3"));
            Assert.Equal("1.235", 1234.5m.ToVietnameseInteger());
            Assert.Equal("1.234", 1234.ToVietnameseNumber());
            Assert.Equal("31/12/2026", new DateTime(2026, 12, 31).ToVietnameseDate());
            Assert.Equal("31/12/2026 21:05", new DateTime(2026, 12, 31, 21, 5, 0).ToVietnameseDateTime());
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    public void ToVietnameseBusinessDateTime_ConvertsStoredUtcKindsAcrossBusinessDateBoundary(
        DateTimeKind kind)
    {
        var storedTimestamp = DateTime.SpecifyKind(
            new DateTime(2026, 7, 23, 18, 30, 0),
            kind);
        var businessTimeZone = TimeZoneInfo.CreateCustomTimeZone(
            "Asia/Ho_Chi_Minh",
            TimeSpan.FromHours(7),
            "Vietnam",
            "Vietnam");

        var display = storedTimestamp.ToVietnameseBusinessDateTime(businessTimeZone);

        Assert.Equal("24/07/2026 01:30", display);
    }
}
