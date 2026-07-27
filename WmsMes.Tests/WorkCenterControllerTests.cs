using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.ViewModels;

namespace WmsMes.Tests;

public class WorkCenterControllerTests
{
    [Fact]
    public async Task CreatePost_RoundsRatesAndPersistsWorkCenter()
    {
        await using var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"WorkCenter_Create_{Guid.NewGuid()}")
                .Options);
        var controller = Controller(context);
        var result = await controller.Create(new WorkCenterCreateInputModel
        {
            Code = "WC-COST",
            Name = "Cost center",
            HourlyLaborRate = 125_000.505m,
            HourlyMachineRate = 60_000.505m
        });

        Assert.IsType<RedirectToActionResult>(result);
        var workCenter = Assert.Single(await context.WorkCenters.ToListAsync());
        Assert.Equal(125_000.51m, workCenter.HourlyLaborRate);
        Assert.Equal(60_000.51m, workCenter.HourlyMachineRate);
    }

    [Fact]
    public async Task EditPost_RoundsAndPersistsBothRates()
    {
        await using var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"WorkCenter_Edit_{Guid.NewGuid()}")
                .Options);
        var workCenter = new WorkCenter { Code = "WC-EDIT", Name = "Editable center" };
        context.WorkCenters.Add(workCenter);
        await context.SaveChangesAsync();

        var result = await Controller(context).Edit(new WorkCenterRateInputModel
        {
            Id = workCenter.Id,
            HourlyLaborRate = 3.505m,
            HourlyMachineRate = 4.505m
        });

        Assert.IsType<RedirectToActionResult>(result);
        context.ChangeTracker.Clear();
        var persisted = Assert.Single(await context.WorkCenters.ToListAsync());
        Assert.Equal(3.51m, persisted.HourlyLaborRate);
        Assert.Equal(4.51m, persisted.HourlyMachineRate);
    }

    private static WorkCenterController Controller(ApplicationDbContext context)
    {
        var controller = new WorkCenterController(context)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.TempData = new TempDataDictionary(
            controller.HttpContext,
            Mock.Of<ITempDataProvider>());
        return controller;
    }
}
