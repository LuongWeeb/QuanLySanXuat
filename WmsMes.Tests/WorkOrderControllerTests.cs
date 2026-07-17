using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.Services;

namespace WmsMes.Tests;

public class WorkOrderControllerTests
{
    private static DbContextOptions<ApplicationDbContext> Options(string name) =>
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options;

    [Fact]
    public async Task Index_ReturnsWorkOrdersNewestDueDateFirst()
    {
        await using var context = new ApplicationDbContext(Options($"WO_Index_{Guid.NewGuid()}"));
        var product = Product("FG");
        context.WorkOrders.AddRange(Order(product, "WO-1", new DateTime(2026, 7, 20)), Order(product, "WO-2", new DateTime(2026, 7, 25)));
        await context.SaveChangesAsync();

        var result = await new WorkOrderController(context, Mock.Of<IWorkOrderService>()).Index();

        var model = Assert.IsAssignableFrom<IEnumerable<WorkOrder>>(Assert.IsType<ViewResult>(result).Model).ToList();
        Assert.Equal(new[] { "WO-2", "WO-1" }, model.Select(x => x.Code));
        Assert.All(model, x => Assert.NotNull(x.Product));
    }

    [Fact]
    public async Task Details_WhenMissing_ReturnsNotFound()
    {
        await using var context = new ApplicationDbContext(Options($"WO_Missing_{Guid.NewGuid()}"));
        var result = await new WorkOrderController(context, Mock.Of<IWorkOrderService>()).Details(404);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Details_ReturnsOrderWithRoutingAndReservations()
    {
        await using var context = new ApplicationDbContext(Options($"WO_Details_{Guid.NewGuid()}"));
        var product = Product("FG");
        var material = Product("RM", false);
        var order = Order(product, "WO-DETAIL", DateTime.UtcNow);
        order.Steps.Add(new WorkOrderStep { StepNumber = 1, StepName = "Mix", WorkCenter = new WorkCenter { Code = "WC", Name = "Mixer" } });
        context.WorkOrders.Add(order);
        context.MaterialReservations.Add(new MaterialReservation
        {
            WorkOrder = order, Product = material, Lot = new Lot { LotNo = "LOT-1", Product = material },
            Location = new Location { Code = "A-01", Name = "A-01", Zone = new Zone { Code = "Z", Name = "Zone" } }, QtyReserved = 3
        });
        await context.SaveChangesAsync();

        var result = await new WorkOrderController(context, Mock.Of<IWorkOrderService>()).Details(order.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<WorkOrder>(view.Model);
        Assert.Equal("Mixer", Assert.Single(model.Steps).WorkCenter!.Name);
        var reservations = Assert.IsAssignableFrom<IEnumerable<MaterialReservation>>(view.ViewData["Reservations"]);
        Assert.Equal("LOT-1", Assert.Single(reservations).Lot!.LotNo);
    }

    [Fact]
    public async Task CreateGet_LoadsOnlyActiveManufacturedProducts()
    {
        await using var context = new ApplicationDbContext(Options($"WO_CreateGet_{Guid.NewGuid()}"));
        context.Products.AddRange(Product("VALID"), Product("INACTIVE", active: false), Product("PURCHASED", false));
        await context.SaveChangesAsync();

        var result = await new WorkOrderController(context, Mock.Of<IWorkOrderService>()).Create();

        Assert.IsType<ViewResult>(result);
        var products = Assert.IsAssignableFrom<IEnumerable<Product>>(Assert.IsType<ViewResult>(result).ViewData["Products"]);
        Assert.Equal("VALID", Assert.Single(products).Code);
    }

    [Fact]
    public async Task CreatePost_WithValidInput_PersistsDraftAndRedirects()
    {
        await using var context = new ApplicationDbContext(Options($"WO_CreatePost_{Guid.NewGuid()}"));
        var product = Product("FG");
        context.Products.Add(product);
        await context.SaveChangesAsync();
        var controller = Controller(context);
        var order = Order(null, "WO-NEW", DateTime.UtcNow.AddDays(2));
        order.ProductId = product.Id;
        order.Status = WorkOrderStatus.Completed;

        var result = await controller.Create(order);

        Assert.Equal(nameof(WorkOrderController.Index), Assert.IsType<RedirectToActionResult>(result).ActionName);
        var saved = await context.WorkOrders.SingleAsync();
        Assert.Equal(WorkOrderStatus.Draft, saved.Status);
        Assert.Equal("WO-NEW", saved.Code);
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("WO", 0)]
    public async Task CreatePost_WithInvalidFields_ReturnsViewWithoutPersisting(string code, decimal qty)
    {
        await using var context = new ApplicationDbContext(Options($"WO_Invalid_{Guid.NewGuid()}"));
        var product = Product("FG");
        context.Products.Add(product);
        await context.SaveChangesAsync();
        var controller = Controller(context);
        var order = Order(null, code, DateTime.UtcNow.AddDays(2));
        order.ProductId = product.Id;
        order.Qty = qty;

        var result = await controller.Create(order);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Empty(context.WorkOrders);
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<Product>>(controller.ViewData["Products"]));
    }

    [Fact]
    public async Task CreatePost_RejectsForgedProductId()
    {
        await using var context = new ApplicationDbContext(Options($"WO_Forged_{Guid.NewGuid()}"));
        context.Products.AddRange(Product("PURCHASED", false), Product("INACTIVE", active: false));
        await context.SaveChangesAsync();
        var forgedId = context.Products.First().Id;
        var controller = Controller(context);
        var order = Order(null, "WO-FORGED", DateTime.UtcNow.AddDays(1));
        order.ProductId = forgedId;

        var result = await controller.Create(order);

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey(nameof(WorkOrder.ProductId)));
        Assert.Empty(context.WorkOrders);
    }

    [Theory]
    [InlineData(true, "Đã phê duyệt")]
    [InlineData(false, "Không thể phê duyệt")]
    public async Task Approve_UsesAuthenticatedUserAndReportsServiceResult(bool success, string message)
    {
        await using var context = new ApplicationDbContext(Options($"WO_Approve_{Guid.NewGuid()}"));
        var service = new Mock<IWorkOrderService>();
        service.Setup(x => x.ApproveWorkOrderAsync(7, "planner-1")).ReturnsAsync(success);
        var controller = Controller(context, service.Object, "planner-1");

        var result = await controller.Approve(7);

        Assert.Equal(nameof(WorkOrderController.Details), Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Contains(message, controller.TempData["StatusMessage"]!.ToString());
        service.VerifyAll();
    }

    [Fact]
    public async Task Approve_WhenServiceThrows_ReportsError()
    {
        await using var context = new ApplicationDbContext(Options($"WO_ApproveError_{Guid.NewGuid()}"));
        var service = new Mock<IWorkOrderService>();
        service.Setup(x => x.ApproveWorkOrderAsync(7, "system")).ThrowsAsync(new InvalidOperationException("thiếu vật tư"));
        var controller = Controller(context, service.Object);

        await controller.Approve(7);

        Assert.Contains("thiếu vật tư", controller.TempData["StatusMessage"]!.ToString());
    }

    [Theory]
    [InlineData(true, "Đã hoàn thành")]
    [InlineData(false, "Không thể hoàn thành")]
    public async Task Complete_UsesAuthenticatedUserAndReportsServiceResult(bool success, string message)
    {
        await using var context = new ApplicationDbContext(Options($"WO_Complete_{Guid.NewGuid()}"));
        var service = new Mock<IWorkOrderService>();
        service.Setup(x => x.CompleteWorkOrderAsync(8, "manager-1")).ReturnsAsync(success);
        var controller = Controller(context, service.Object, "manager-1");

        await controller.Complete(8);

        Assert.Contains(message, controller.TempData["StatusMessage"]!.ToString());
        service.VerifyAll();
    }

    [Fact]
    public async Task Complete_WhenServiceThrows_ReportsError()
    {
        await using var context = new ApplicationDbContext(Options($"WO_CompleteError_{Guid.NewGuid()}"));
        var service = new Mock<IWorkOrderService>();
        service.Setup(x => x.CompleteWorkOrderAsync(8, "system")).ThrowsAsync(new InvalidOperationException("còn công đoạn"));
        var controller = Controller(context, service.Object);

        await controller.Complete(8);

        Assert.Contains("còn công đoạn", controller.TempData["StatusMessage"]!.ToString());
    }

    private static WorkOrderController Controller(ApplicationDbContext context, IWorkOrderService? service = null, string? userId = null)
    {
        var controller = new WorkOrderController(context, service ?? Mock.Of<IWorkOrderService>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.TempData = new TempDataDictionary(controller.HttpContext, Mock.Of<ITempDataProvider>());
        if (userId != null)
            controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "test"));
        return controller;
    }

    private static Product Product(string code, bool manufactured = true, bool active = true) =>
        new() { Code = code, Name = code, IsManufactured = manufactured, IsActive = active };

    private static WorkOrder Order(Product? product, string code, DateTime dueDate) =>
        new() { Code = code, Product = product, ProductId = product?.Id ?? 0, Qty = 10, DueDate = dueDate, BomVersion = "B1", RoutingVersion = "R1" };
}
