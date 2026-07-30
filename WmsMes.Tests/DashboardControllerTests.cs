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
                     nameof(DashboardController.GetProductionProgressData)
                 })
        {
            var method = typeof(DashboardController).GetMethod(action);

            Assert.NotNull(method);
            Assert.Single(method.GetCustomAttributes<HttpGetAttribute>());
        }
    }

    [Fact]
    public void Index_ReturnsView()
    {
        var controller = new DashboardController(Mock.Of<IOeeService>());

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
        var controller = new DashboardController(service.Object);
        var beforeCall = DateTime.UtcNow;

        var result = Assert.IsType<JsonResult>(await controller.GetOeeData());

        var afterCall = DateTime.UtcNow;
        Assert.Same(expected, result.Value);
        Assert.NotNull(capturedStart);
        Assert.NotNull(capturedEnd);
        Assert.InRange(capturedEnd.Value, beforeCall, afterCall);
        Assert.Equal(TimeSpan.FromDays(6), capturedEnd.Value - capturedStart.Value);
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
        var controller = new DashboardController(service.Object);

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
        var controller = new DashboardController(service.Object);

        var result = Assert.IsType<JsonResult>(await controller.GetProductionProgressData());

        Assert.Same(expected, result.Value);
        service.VerifyAll();
    }
}
