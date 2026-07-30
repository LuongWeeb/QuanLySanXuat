namespace WmsMes.Tests;

public class DashboardViewTests
{
    [Fact]
    public void DashboardView_AndLayout_ExposeFactoryOeeRealtimeContract()
    {
        var dashboardPath = Path.Combine(ProjectRoot(), "Views", "Dashboard", "Index.cshtml");
        Assert.True(File.Exists(dashboardPath), $"Dashboard view was not found at {dashboardPath}.");

        var view = File.ReadAllText(dashboardPath);
        var layout = File.ReadAllText(
            Path.Combine(ProjectRoot(), "Views", "Shared", "_Layout.cshtml"));

        Assert.Contains("Dashboard Giám sát Nhà máy & Báo cáo OEE", view);
        Assert.Contains("id=\"oee-cards-container\"", view);
        Assert.Contains("/Dashboard/GetOeeData", view);
        Assert.Contains("/Dashboard/GetAgingData", view);
        Assert.Contains("/Dashboard/GetProductionProgressData", view);
        Assert.Contains("/Dashboard/GetProductionQualityData", view);
        Assert.Contains(
            "https://cdn.jsdelivr.net/npm/chart.js@4.4.9/dist/chart.umd.min.js",
            view);
        Assert.Contains(
            "https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/8.0.0/signalr.min.js",
            view);
        Assert.Contains("/productionHub", view);
        Assert.Contains("/inventoryHub", view);
        Assert.Contains("ReceiveProgressUpdate", view);
        Assert.Contains("ReceiveStockUpdate", view);
        Assert.DoesNotContain("location.reload", view);

        Assert.Contains("asp-controller=\"Dashboard\"", layout);
        Assert.Contains("asp-action=\"Index\"", layout);
        Assert.Contains("Dashboard Nhà máy & OEE", layout);
    }

    [Fact]
    public void DashboardView_ProvidesAccessibleResponsiveStatesAndSafeRendering()
    {
        var dashboardPath = Path.Combine(ProjectRoot(), "Views", "Dashboard", "Index.cshtml");
        Assert.True(File.Exists(dashboardPath), $"Dashboard view was not found at {dashboardPath}.");

        var view = File.ReadAllText(dashboardPath);

        Assert.Contains("aria-live=\"polite\"", view);
        Assert.Contains("role=\"status\"", view);
        Assert.Contains("setAttribute(\"role\", \"progressbar\")", view);
        Assert.Contains("aria-valuemin", view);
        Assert.Contains("aria-valuemax", view);
        Assert.Contains("textContent", view);
        Assert.DoesNotContain("innerHTML", view);
        Assert.Contains("Intl.NumberFormat(\"vi-VN\"", view);
        Assert.Contains("bg-success", view);
        Assert.Contains("bg-warning", view);
        Assert.Contains("bg-danger", view);
        Assert.Contains("chart.destroy()", view);
        Assert.Contains("Promise.all", view);
        Assert.Contains("Không có dữ liệu", view);
        Assert.Contains("Không thể tải dữ liệu", view);
    }

    [Fact]
    public void DashboardView_CoalescesRealtimeRefreshesWithoutAbortingInflightRequests()
    {
        var view = ReadDashboardView();

        Assert.Contains("let refreshInProgress = false", view);
        Assert.Contains("let refreshPending = false", view);
        Assert.Contains("if (refreshInProgress)", view);
        Assert.Contains("refreshPending = true", view);
        Assert.Contains("if (refreshPending)", view);
        Assert.Contains("if (refreshTimer) return", view);
        Assert.Contains("refreshTimer = undefined", view);
        Assert.DoesNotContain("AbortController", view);
        Assert.DoesNotContain("activeRequest?.abort()", view);
        Assert.DoesNotContain("window.clearTimeout(refreshTimer)", view);
    }

    [Fact]
    public void DashboardView_ProvidesSanitizedScreenReaderSummariesForBothCharts()
    {
        var view = ReadDashboardView();

        Assert.Contains("aria-describedby=\"production-chart-summary\"", view);
        Assert.Contains("id=\"production-chart-summary\"", view);
        Assert.Contains("id=\"production-chart-summary-body\"", view);
        Assert.Contains("aria-describedby=\"aging-chart-summary\"", view);
        Assert.Contains("id=\"aging-chart-summary\"", view);
        Assert.Contains("id=\"aging-chart-summary-body\"", view);
        Assert.Contains("renderProductionSummary(productionData)", view);
        Assert.Contains("renderAgingSummary(agingData)", view);
        Assert.Contains("replaceChildren()", view);
        Assert.Contains("textContent", view);
        Assert.DoesNotContain("innerHTML", view);
    }

    [Fact]
    public void DashboardView_UsesAccessibleWarningContrastAndMetricLabels()
    {
        var view = ReadDashboardView();

        Assert.Contains("#7a4b00", view);
        Assert.Contains("metric.setAttribute(\"role\", \"group\")", view);
        Assert.Contains("metric.setAttribute(\"aria-label\"", view);
        Assert.Contains("Mức độ sẵn sàng", view);
        Assert.Contains("Hiệu suất", view);
        Assert.Contains("Chất lượng", view);
    }

    [Fact]
    public void DashboardView_ShowsAVisibleFallbackWhenChartJsIsUnavailable()
    {
        var view = ReadDashboardView();

        Assert.Contains("id=\"chart-error\"", view);
        Assert.Contains("typeof window.Chart !== \"function\"", view);
        Assert.Contains("Không thể hiển thị biểu đồ", view);
    }

    [Fact]
    public void DashboardView_RendersTodayOutputScrapKpiAndAccessibleQualityLineChart()
    {
        var view = ReadDashboardView();

        Assert.Contains("id=\"today-production-output\"", view);
        Assert.Contains("id=\"scrap-rate\"", view);
        Assert.Contains("id=\"production-quality-chart\"", view);
        Assert.Contains("type: \"line\"", view);
        Assert.Contains("aria-describedby=\"quality-chart-summary\"", view);
        Assert.Contains("id=\"quality-chart-summary-body\"", view);
        Assert.Contains("renderQualitySummary(qualityData)", view);
        Assert.Contains("renderQualityChart(qualityData)", view);
        Assert.Contains("Phế phẩm", view);
        Assert.Contains("Tỷ lệ chất lượng", view);
    }

    [Fact]
    public void DashboardView_UsesClearAgingBucketsIncludingUnknownAge()
    {
        var view = ReadDashboardView();

        Assert.Contains("Dưới 30 ngày", view);
        Assert.Contains("30 đến dưới 60 ngày", view);
        Assert.Contains("60 đến 90 ngày", view);
        Assert.Contains("Trên 90 ngày", view);
        Assert.Contains("Không rõ tuổi", view);
        Assert.Contains("aging.unknownAge", view);
    }

    [Fact]
    public void DashboardView_PinsCdnAssetsWithVerifiedSriAndAnonymousCrossOrigin()
    {
        var view = ReadDashboardView();

        Assert.Contains(
            "integrity=\"sha384-b0GXujLkk9eYYSmcSfoyZbfyElGAQnDyY0skCHSG6w3JgTMFnz11ggrTAr7seu9f\"",
            view);
        Assert.Contains(
            "integrity=\"sha384-/taWmisziXYpcfnYsumSUmNaiMvG/fF/OJOUCLnqCIYTrpOZy7WbFF6FfIxwOrfL\"",
            view);
        Assert.Equal(2, CountOccurrences(view, "crossorigin=\"anonymous\""));
    }

    [Fact]
    public void DashboardView_DisablesFetchCachingAndOffersVisibleRetry()
    {
        var view = ReadDashboardView();

        Assert.Contains("cache: \"no-store\"", view);
        Assert.Contains("id=\"dashboard-retry\"", view);
        Assert.Contains("retryButton.addEventListener(\"click\", refreshDashboard)", view);
    }

    private static string ReadDashboardView() =>
        File.ReadAllText(Path.Combine(ProjectRoot(), "Views", "Dashboard", "Index.cshtml"));

    private static int CountOccurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string ProjectRoot() => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
