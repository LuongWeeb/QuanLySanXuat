using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Services;
using Xunit;

namespace WmsMes.Tests;

public class ProductionPlanControllerTests
{
    [Fact]
    public void Controller_RestrictsAccessToProductionPlanningRoles()
    {
        var controllerType = ControllerType();

        var authorize = Assert.Single(controllerType.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal("Admin,Planner,Manager", authorize.Roles);
    }

    [Fact]
    public async Task Create_RejectsMismatchedOrNonPositivePlanLines()
    {
        await using var context = CreateContext();
        context.Products.Add(new Product
        {
            Id = 1,
            Code = "FG-01",
            Name = "Finished Good",
            IsActive = true,
            IsManufactured = true
        });
        await context.SaveChangesAsync();
        var service = new Mock<IProductionPlanService>(MockBehavior.Strict);
        var controller = CreateController(context, service.Object);
        var action = ControllerType().GetMethod(
            "Create",
            [typeof(ProductionPlan), typeof(List<int>), typeof(List<decimal>)]);
        Assert.NotNull(action);

        var task = Assert.IsAssignableFrom<Task<IActionResult>>(action!.Invoke(
            controller,
            [new ProductionPlan { PlanNo = "PP-1" }, new List<int> { 1 }, new List<decimal>()]));
        var result = await task;

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Empty(await context.ProductionPlans.ToListAsync());
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Details_WithMrpRequested_ExposesAggregateResults()
    {
        await using var context = CreateContext();
        var plan = new ProductionPlan { Id = 7, PlanNo = "PP-7" };
        var expected = Array.Empty<WmsMes.Web.DTOs.MrpResultDto>();
        var service = new Mock<IProductionPlanService>();
        service.Setup(candidate => candidate.GetByIdAsync(7)).ReturnsAsync(plan);
        service.Setup(candidate => candidate.CalculatePlanRequirementsAsync(7)).ReturnsAsync(expected);
        var controller = CreateController(context, service.Object);
        var action = ControllerType().GetMethod("Details", [typeof(int), typeof(bool)]);
        Assert.NotNull(action);

        var task = Assert.IsAssignableFrom<Task<IActionResult>>(action!.Invoke(controller, [7, true]));
        var view = Assert.IsType<ViewResult>(await task);

        Assert.Same(plan, view.Model);
        Assert.Same(expected, view.ViewData["MrpResults"]);
        Assert.Equal(true, view.ViewData["MrpRun"]);
        service.VerifyAll();
    }

    private static Type ControllerType()
    {
        var type = typeof(ApplicationDbContext).Assembly.GetType(
            "WmsMes.Web.Controllers.ProductionPlanController");
        Assert.NotNull(type);
        return type!;
    }

    private static Controller CreateController(
        ApplicationDbContext context,
        IProductionPlanService service)
    {
        return Assert.IsAssignableFrom<Controller>(
            Activator.CreateInstance(ControllerType(), context, service));
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}
