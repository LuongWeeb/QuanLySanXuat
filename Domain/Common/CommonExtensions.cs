using System.Globalization;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.Domain.Common;

public static class CommonExtensions
{
    private static readonly CultureInfo VietnameseCulture = CultureInfo.GetCultureInfo("vi-VN");

    public static string ToVietnameseString(this WorkOrderStatus status) => status switch
    {
        WorkOrderStatus.Draft => "Nháp",
        WorkOrderStatus.Pending => "Chờ duyệt",
        WorkOrderStatus.Approved => "Đã phê duyệt",
        WorkOrderStatus.InProgress => "Đang sản xuất",
        WorkOrderStatus.Completed => "Đã hoàn thành",
        WorkOrderStatus.Closed => "Đã đóng",
        _ => status.ToString()
    };

    public static string ToVietnameseString(this WorkOrderStepStatus status) => status switch
    {
        WorkOrderStepStatus.Pending => "Chờ bắt đầu",
        WorkOrderStepStatus.InProgress => "Đang sản xuất",
        WorkOrderStepStatus.Completed => "Đã hoàn thành",
        _ => status.ToString()
    };

    public static string ToVietnameseString(this DocumentStatus status) => status switch
    {
        DocumentStatus.Draft => "Nháp",
        DocumentStatus.Completed => "Đã hoàn thành",
        DocumentStatus.Cancelled => "Đã hủy",
        _ => status.ToString()
    };

    public static string ToVietnameseString(this ProductType type) => type switch
    {
        ProductType.RawMaterial => "Nguyên vật liệu",
        ProductType.WIP => "Bán thành phẩm",
        ProductType.FinishedGood => "Thành phẩm",
        _ => type.ToString()
    };

    public static string ToVietnameseString(this QCResult result) => result switch
    {
        QCResult.PASS => "Đạt",
        QCResult.REJECT => "Không đạt",
        QCResult.REWORK => "Làm lại",
        _ => result.ToString()
    };

    public static string ToVietnameseString(this StocktakeStatus status) => status switch
    {
        StocktakeStatus.Draft => "Nháp",
        StocktakeStatus.Counting => "Đang kiểm đếm",
        StocktakeStatus.AwaitingApproval => "Chờ phê duyệt",
        StocktakeStatus.Completed => "Đã hoàn thành",
        _ => status.ToString()
    };

    public static string ToVietnameseString(this TransactionType type) => type switch
    {
        TransactionType.Receipt => "Nhập kho",
        TransactionType.Issue => "Xuất kho",
        TransactionType.Transfer => "Chuyển kho",
        TransactionType.Adjust => "Điều chỉnh",
        TransactionType.Backflush => "Xuất kho tự động",
        _ => type.ToString()
    };

    public static string ToVietnameseNumber(this decimal value, string format = "N2") =>
        value.ToString(format, VietnameseCulture);

    public static string ToVietnameseInteger(this decimal value) =>
        value.ToString("N0", VietnameseCulture);

    public static string ToVietnameseNumber(this int value, string format = "N0") =>
        value.ToString(format, VietnameseCulture);

    public static string ToVietnameseDate(this DateTime value) =>
        value.ToString("dd/MM/yyyy", VietnameseCulture);

    public static string ToVietnameseDateTime(this DateTime value) =>
        value.ToString("dd/MM/yyyy HH:mm", VietnameseCulture);

    public static string ToVietnameseBusinessDateTime(
        this DateTime storedUtc,
        TimeZoneInfo businessTimeZone)
    {
        ArgumentNullException.ThrowIfNull(businessTimeZone);
        var utc = storedUtc.Kind switch
        {
            DateTimeKind.Utc => storedUtc,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(storedUtc, DateTimeKind.Utc),
            _ => storedUtc.ToUniversalTime()
        };

        return TimeZoneInfo.ConvertTimeFromUtc(utc, businessTimeZone)
            .ToVietnameseDateTime();
    }
}
