using System.Security.Cryptography;

namespace WmsMes.Tests;

public class DashboardViewTests
{
    [Fact]
    public void DashboardView_AndLayout_ExposeFactoryOeeRealtimeContract()
    {
        var dashboardPath = Path.Combine(ProjectRoot(), "Views", "Dashboard", "Index.cshtml");
        Assert.True(File.Exists(dashboardPath), $"Dashboard view was not found at {dashboardPath}.");

        var view = File.ReadAllText(dashboardPath);
        var loader = ReadDashboardLoader();
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
            loader);
        Assert.Contains(
            "https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/8.0.0/signalr.min.js",
            loader);
        Assert.Contains("/productionHub", view);
        Assert.Contains("/inventoryHub", view);
        Assert.Contains("ReceiveProgressUpdate", view);
        Assert.Contains("ReceiveStockUpdate", view);
        Assert.Contains("/Dashboard/GetLowStockAlert", view);
        Assert.Contains("id=\"low-stock-alert-container\"", view);
        Assert.Contains("fetchText(endpoints.lowStock)", view);
        Assert.Contains("lowStockAlertContainer.replaceChildren", view);
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
        var loader = ReadDashboardLoader();

        Assert.Contains(
            "sha384-b0GXujLkk9eYYSmcSfoyZbfyElGAQnDyY0skCHSG6w3JgTMFnz11ggrTAr7seu9f",
            loader);
        Assert.Contains(
            "sha384-/taWmisziXYpcfnYsumSUmNaiMvG/fF/OJOUCLnqCIYTrpOZy7WbFF6FfIxwOrfL",
            loader);
        Assert.Contains("script.crossOrigin = \"anonymous\"", loader);
    }

    [Fact]
    public void DashboardView_AwaitsNewPinnedFallbackScriptsBeforeInitializingOnce()
    {
        var view = ReadDashboardView();
        var loader = ReadDashboardLoader();

        const string chartLocalUrl = "/lib/chart.js/4.4.9/chart.umd.min.js";
        const string signalRLocalUrl = "/lib/microsoft-signalr/8.0.0/signalr.min.js";
        Assert.Contains("src=\"~/js/dashboard-loader.js\"", view);
        Assert.DoesNotContain("onerror=", view);
        Assert.Contains(chartLocalUrl, loader);
        Assert.Contains(signalRLocalUrl, loader);
        Assert.Contains("document.createElement(\"script\")", loader);
        Assert.Contains("await appendScript(asset.localUrl)", loader);
        Assert.Contains("Promise.all(requiredAssets.map(loadScript))", loader);
        Assert.Contains("initializationPromise ??=", loader);
        Assert.Contains(".then(initializeDashboard)", loader);

        AssertAssetMatchesSri(
            Path.Combine("wwwroot", "lib", "chart.js", "4.4.9", "chart.umd.min.js"),
            "b0GXujLkk9eYYSmcSfoyZbfyElGAQnDyY0skCHSG6w3JgTMFnz11ggrTAr7seu9f");
        AssertAssetMatchesSri(
            Path.Combine("wwwroot", "lib", "microsoft-signalr", "8.0.0", "signalr.min.js"),
            "/taWmisziXYpcfnYsumSUmNaiMvG/fF/OJOUCLnqCIYTrpOZy7WbFF6FfIxwOrfL");
    }

    [Fact]
    public void DashboardView_AggregatesProductionAndInventoryHubStatesIndependently()
    {
        var view = ReadDashboardView();

        Assert.Contains(
            "const hubStates = new Map([",
            view);
        Assert.Contains("[\"production\", \"connecting\"]", view);
        Assert.Contains("[\"inventory\", \"connecting\"]", view);
        Assert.Contains("const renderConnectionState = () =>", view);
        Assert.Contains("const connected = states.filter(state => state === \"connected\").length", view);
        Assert.Contains("if (connected === states.length)", view);
        Assert.Contains("else if (connected > 0)", view);
        Assert.Contains("`${connected}/${states.length} kênh thời gian thực`", view);
        Assert.Contains("hubStates.set(hubName, state)", view);
        Assert.Contains("startConnection(hubName, connection)", view);
        Assert.Equal(
            1,
            CountOccurrences(
                view,
                "setConnectionState(\"Dữ liệu thời gian thực\", \"is-live\")"));
    }

    [Theory]
    [InlineData("wwwroot/lib/chart.js/4.4.9/chart.umd.min.js")]
    [InlineData("wwwroot/lib/microsoft-signalr/8.0.0/signalr.min.js")]
    public void PinnedVendorAsset_IsExcludedFromGitTextNormalization(string assetPath)
    {
        var attributes = File.ReadAllLines(Path.Combine(ProjectRoot(), ".gitattributes"));

        Assert.Contains($"{assetPath} -text", attributes);
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

    private static string ReadDashboardLoader() =>
        File.ReadAllText(Path.Combine(ProjectRoot(), "wwwroot", "js", "dashboard-loader.js"));

    private static int CountOccurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static void AssertAssetMatchesSri(string relativePath, string expectedSha384)
    {
        var assetPath = Path.Combine(ProjectRoot(), relativePath);
        Assert.True(File.Exists(assetPath), $"Pinned fallback asset was not found at {assetPath}.");
        var actualSha384 = Convert.ToBase64String(
            SHA384.HashData(File.ReadAllBytes(assetPath)));
        Assert.Equal(expectedSha384, actualSha384);
    }

    private static string ProjectRoot() => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
