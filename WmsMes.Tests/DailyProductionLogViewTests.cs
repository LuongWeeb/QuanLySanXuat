namespace WmsMes.Tests;

public class DailyProductionLogViewTests
{
    [Fact]
    public void Index_RendersSafeClampedProgressAndScheduleFeedback()
    {
        var view = ReadView("Index.cshtml");

        Assert.Contains("DailyProductionLogs.Sum", view);
        Assert.Contains("order.Qty > 0", view);
        Assert.Contains("Math.Clamp", view);
        Assert.Contains("CultureInfo.InvariantCulture", view);
        Assert.Contains("role=\"progressbar\"", view);
        Assert.Contains("aria-valuemin=\"0\"", view);
        Assert.Contains("aria-valuemax=\"100\"", view);
        Assert.Contains("Trễ hạn sản xuất!", view);
        Assert.Contains("Còn @daysRemaining.ToVietnameseNumber() ngày", view);
    }

    [Fact]
    public void Details_RendersProgressHistoryEmptyStateAndAccessibleQuickAddForm()
    {
        var view = ReadView("Details.cshtml");

        Assert.Contains("DailyProductionLogs.Sum", view);
        Assert.Contains("Math.Clamp", view);
        Assert.Contains("CultureInfo.InvariantCulture", view);
        Assert.Contains("Trễ hạn sản xuất!", view);
        Assert.Contains("Còn @daysRemaining.ToVietnameseNumber() ngày", view);
        Assert.Contains("Nhật ký sản lượng", view);
        Assert.Contains("table-responsive", view);
        Assert.Contains("OrderByDescending", view);
        Assert.Contains("Chưa có nhật ký sản lượng.", view);
        Assert.Contains("order.Status == WorkOrderStatus.InProgress", view);
        Assert.Contains("asp-action=\"AddDailyLog\"", view);
        Assert.Contains("asp-validation-summary=\"All\"", view);
        Assert.Contains("for=\"daily-log-date\"", view);
        Assert.Contains("for=\"daily-log-quantity\"", view);
        Assert.Contains("for=\"daily-log-notes\"", view);
        Assert.Contains("name=\"Date\"", view);
        Assert.Contains("name=\"QtyProduced\"", view);
        Assert.Contains("name=\"Notes\"", view);
        Assert.Contains("type=\"date\"", view);
        Assert.Contains("maxlength=\"250\"", view);
        Assert.Contains(
            "businessDate.ToString(\"yyyy-MM-dd\", CultureInfo.InvariantCulture)",
            view);
        Assert.Contains("max=\"@maxDateValue\"", view);
    }

    [Fact]
    public void WorkOrderViews_UseControllerSuppliedBusinessDateInsteadOfHostDate()
    {
        var index = ReadView("Index.cshtml");
        var details = ReadView("Details.cshtml");

        Assert.DoesNotContain("DateTime.Today", index);
        Assert.DoesNotContain("DateTime.Today", details);
        Assert.Contains("ViewData[\"BusinessDate\"]", index);
        Assert.Contains("ViewData[\"BusinessDate\"]", details);
        Assert.Contains("businessDate > order.DueDate.Date", index);
        Assert.Contains("order.DueDate.Date - businessDate", index);
        Assert.Contains("businessDate > order.DueDate.Date", details);
        Assert.Contains("order.DueDate.Date - businessDate", details);
        Assert.Contains(
            "businessDate.ToString(\"yyyy-MM-dd\", CultureInfo.InvariantCulture)",
            details);
    }

    [Fact]
    public void WorkOrderViews_SuppressScheduleWarningsWhenTargetQuantityIsInvalid()
    {
        var index = ReadView("Index.cshtml");
        var details = ReadView("Details.cshtml");

        Assert.Contains("var hasValidTarget = order.Qty > 0", index);
        Assert.Contains("hasValidTarget && !targetReached", index);
        Assert.Contains("var hasValidTarget = order.Qty > 0", details);
        Assert.Contains("hasValidTarget && !targetReached", details);
    }

    private static string ReadView(string fileName) =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "WorkOrder", fileName));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WmsMes.sln")))
            directory = directory.Parent;

        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
