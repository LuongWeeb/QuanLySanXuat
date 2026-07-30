using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using WmsMes.Web.Controllers;
using WmsMes.Web.DTOs;
using WmsMes.Web.Services;

namespace WmsMes.Tests;

public class DashboardControllerTests
{
    [Fact]
    public void ControllerAndJsonEndpoints_HaveRequiredAttributes()
    {
        Assert.Single(typeof(DashboardController).GetCustomAttributes<AuthorizeAttribute>());

        foreach (var action in new[]
                 {
                     nameof(DashboardController.GetOeeData),
                     nameof(DashboardController.GetAgingData),
                     nameof(DashboardController.GetProductionProgressData),
                     nameof(DashboardController.GetProductionQualityData)
                 })
        {
            var method = typeof(DashboardController).GetMethod(action);

            Assert.NotNull(method);
            Assert.Single(method.GetCustomAttributes<HttpGetAttribute>());
            var responseCache = Assert.Single(
                method.GetCustomAttributes<ResponseCacheAttribute>());
            Assert.True(responseCache.NoStore);
            Assert.Equal(ResponseCacheLocation.None, responseCache.Location);
        }
    }

    [Fact]
    public void Index_ReturnsView()
    {
        var controller = Controller(Mock.Of<IOeeService>());

        Assert.IsType<ViewResult>(controller.Index());
    }

    [Fact]
    public async Task GetOeeData_ReturnsLatestSevenDayWindowAsJson()
    {
        var expected = new[]
        {
            new OeeMetricsDto { WorkCenterId = 1, WorkCenterCode = "WC-01", Oee = 87.5m }
        };
        DateTime? capturedStart = null;
        DateTime? capturedEnd = null;
        var service = new Mock<IOeeService>(MockBehavior.Strict);
        service.Setup(item => item.GetAllWorkCentersOeeAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>()))
            .Callback<DateTime, DateTime>((start, end) =>
            {
                capturedStart = start;
                capturedEnd = end;
            })
            .ReturnsAsync(expected);
        var controller = Controller(service.Object);

        var result = Assert.IsType<JsonResult>(await controller.GetOeeData());

        Assert.Same(expected, result.Value);
        Assert.Equal(
            new DateTime(2026, 7, 24, 17, 0, 0, DateTimeKind.Utc),
            capturedStart);
        Assert.Equal(
            new DateTime(2026, 7, 31, 17, 0, 0, DateTimeKind.Utc),
            capturedEnd);
        Assert.Equal(TimeSpan.FromDays(7), capturedEnd - capturedStart);
        service.VerifyAll();
    }

    [Fact]
    public async Task GetAgingData_ReturnsServiceDataAsJson()
    {
        var expected = new InventoryAgingDto
        {
            LessThan30Days = 125m,
            MoreThan90Days = 50m
        };
        var service = new Mock<IOeeService>(MockBehavior.Strict);
        service.Setup(item => item.GetInventoryAgingAnalyticsAsync())
            .ReturnsAsync(expected);
        var controller = Controller(service.Object);

        var result = Assert.IsType<JsonResult>(await controller.GetAgingData());

        Assert.Same(expected, result.Value);
        service.VerifyAll();
    }

    [Fact]
    public async Task GetProductionProgressData_ReturnsServiceDataAsJson()
    {
        var expected = new[]
        {
            new ProductionProgressDto
            {
                WorkOrderId = 7,
                WorkOrderCode = "WO-007",
                PlannedQuantity = 20m,
                ActualProducedQuantity = 12m
            }
        };
        var service = new Mock<IOeeService>(MockBehavior.Strict);
        service.Setup(item => item.GetProductionProgressAnalyticsAsync())
            .ReturnsAsync(expected);
        var controller = Controller(service.Object);

        var result = Assert.IsType<JsonResult>(await controller.GetProductionProgressData());

        Assert.Same(expected, result.Value);
        service.VerifyAll();
    }

    [Fact]
    public async Task GetProductionQualityData_ReturnsServiceDataForSameSevenBusinessDates()
    {
        var expected = new ProductionQualityAnalyticsDto
        {
            TodayProductionOutput = 12m,
            ScrapRate = 5m
        };
        var service = new Mock<IOeeService>(MockBehavior.Strict);
        service.Setup(item => item.GetProductionQualityAnalyticsAsync(
                new DateTime(2026, 7, 24, 17, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 7, 31, 17, 0, 0, DateTimeKind.Utc)))
            .ReturnsAsync(expected);
        var controller = Controller(service.Object);

        var result = Assert.IsType<JsonResult>(
            await controller.GetProductionQualityData());

        Assert.Same(expected, result.Value);
        service.VerifyAll();
    }

    private static DashboardController Controller(IOeeService service) =>
        new(
            service,
            new FixedTimeProvider(
                new DateTimeOffset(2026, 7, 30, 18, 30, 0, TimeSpan.Zero)),
            TimeZoneInfo.CreateCustomTimeZone(
                "Asia/Ho_Chi_Minh",
                TimeSpan.FromHours(7),
                "Vietnam",
                "Vietnam"));

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
