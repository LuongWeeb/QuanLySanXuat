using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.Services;
using WmsMes.Web.ViewModels;

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
        var older = Order(product, "WO-1", new DateTime(2026, 7, 20));
        var newer = Order(product, "WO-2", new DateTime(2026, 7, 25));
        newer.DailyProductionLogs.Add(new DailyProductionLog
        {
            Date = new DateTime(2026, 7, 22),
            QtyProduced = 2
        });
        context.WorkOrders.AddRange(older, newer);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var result = await Controller(context).Index();

        var model = Assert.IsAssignableFrom<IEnumerable<WorkOrder>>(Assert.IsType<ViewResult>(result).Model).ToList();
        Assert.Equal(new[] { "WO-2", "WO-1" }, model.Select(x => x.Code));
        Assert.All(model, x => Assert.NotNull(x.Product));
        Assert.Equal(2m, Assert.Single(model[0].DailyProductionLogs).QtyProduced);
    }

    [Fact]
    public async Task Index_SuppliesVietnamBusinessDateAcrossUtcDateBoundary()
    {
        await using var context = new ApplicationDbContext(Options($"WO_IndexBusinessDate_{Guid.NewGuid()}"));
        var controller = Controller(
            context,
            timeProvider: new FixedTimeProvider(
                new DateTimeOffset(2026, 7, 23, 18, 30, 0, TimeSpan.Zero)),
            businessTimeZone: VietnamTimeZone());

        var result = Assert.IsType<ViewResult>(await controller.Index());

        Assert.Equal(new DateTime(2026, 7, 24), result.ViewData["BusinessDate"]);
    }

    [Fact]
    public async Task Details_WhenMissing_ReturnsNotFound()
    {
        await using var context = new ApplicationDbContext(Options($"WO_Missing_{Guid.NewGuid()}"));
        var result = await Controller(context).Details(404);
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
        order.DailyProductionLogs.Add(new DailyProductionLog
        {
            Date = new DateTime(2026, 7, 23),
            QtyProduced = 4,
            Notes = "Ca sáng"
        });
        context.WorkOrders.Add(order);
        context.MaterialReservations.Add(new MaterialReservation
        {
            WorkOrder = order, Product = material, Lot = new Lot { LotNo = "LOT-1", Product = material },
            Location = new Location { Code = "A-01", Name = "A-01", Zone = new Zone { Code = "Z", Name = "Zone" } }, QtyReserved = 3
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var result = await Controller(context).Details(order.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<WorkOrderDetailsViewModel>(view.Model);
        Assert.Equal("Mixer", Assert.Single(model.Order.Steps).WorkCenter!.Name);
        Assert.Equal("Ca sáng", Assert.Single(model.Order.DailyProductionLogs).Notes);
        Assert.Equal("LOT-1", Assert.Single(model.Reservations).Lot!.LotNo);
    }

    [Fact]
    public async Task Details_BuildsComparativeCostAnalysisFromNewestStandardsAndActualUsage()
    {
        await using var context = new ApplicationDbContext(Options($"WO_CostAnalysis_{Guid.NewGuid()}"));
        var product = Product("FG");
        var material = Product("RM", manufactured: false);
        var firstCenter = new WorkCenter
        {
            Id = 101,
            Code = "WC-1",
            Name = "Cutting",
            HourlyLaborRate = 2m,
            HourlyMachineRate = 4m
        };
        var secondCenter = new WorkCenter
        {
            Id = 102,
            Code = "WC-2",
            Name = "Packing",
            HourlyLaborRate = 1m,
            HourlyMachineRate = 2m
        };
        var order = Order(product, "WO-COST", DateTime.UtcNow);
        order.Qty = 10m;
        order.Steps.Add(new WorkOrderStep
        {
            Id = 201,
            StepNumber = 10,
            StepName = "Cutting",
            WorkCenter = firstCenter,
            StartTime = new DateTime(2026, 7, 27, 1, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 7, 27, 1, 15, 0, DateTimeKind.Utc),
            QtyOK = 8m
        });
        order.Steps.Add(new WorkOrderStep
        {
            Id = 202,
            StepNumber = 20,
            StepName = "Packing",
            WorkCenter = secondCenter,
            QtyOK = 4m
        });
        context.WorkOrders.Add(order);
        context.BOMs.AddRange(
            new BOM { Id = 301, Product = product, Version = "OLD", IsActive = true, TotalMaterialCost = 99m },
            new BOM { Id = 302, Product = product, Version = "NEW", IsActive = true, TotalMaterialCost = 2m });
        context.Routings.AddRange(
            new Routing
            {
                Id = 401,
                Product = product,
                Name = "Old routing",
                Version = "OLD",
                IsActive = true,
                Steps =
                {
                    new RoutingStep
                    {
                        StepNumber = 20,
                        StepName = "Packing",
                        WorkCenter = secondCenter,
                        StandardTimeMinutes = 180m
                    }
                }
            },
            new Routing
            {
                Id = 402,
                Product = product,
                Name = "Current routing",
                Version = "NEW",
                IsActive = true,
                Steps =
                {
                    new RoutingStep
                    {
                        StepNumber = 10,
                        StepName = "Cutting",
                        WorkCenter = firstCenter,
                        StandardTimeMinutes = 30m
                    },
                    new RoutingStep
                    {
                        StepNumber = 20,
                        StepName = "Packing",
                        WorkCenter = secondCenter,
                        StandardTimeMinutes = 60m
                    }
                }
            });
        context.MaterialReservations.Add(new MaterialReservation
        {
            WorkOrder = order,
            Product = material,
            Lot = new Lot { LotNo = "RM-LOT", Product = material, UnitPrice = 1.5m },
            Location = new Location
            {
                Code = "A-01",
                Name = "A-01",
                Zone = new Zone { Code = "Z", Name = "Zone" }
            },
            QtyReserved = 3m
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var view = Assert.IsType<ViewResult>(await Controller(context).Details(order.Id));
        var analysis = Assert.IsType<WorkOrderDetailsViewModel>(view.Model).CostAnalysis;

        Assert.Equal(new CostComparisonViewModel(20m, 4.5m), analysis.MaterialCost);
        Assert.Equal(new CostComparisonViewModel(20m, 1.5m), analysis.LaborCost);
        Assert.Equal(new CostComparisonViewModel(40m, 3m), analysis.MachineCost);
        Assert.Equal(new CostComparisonViewModel(80m, 9m), analysis.TotalCost);
        Assert.Equal(new CostComparisonViewModel(8m, 2.25m), analysis.UnitCost);
    }

    [Fact]
    public async Task Details_WhenCostingInputsAreIncomplete_ReturnsZeroAnalysisWithoutThrowing()
    {
        await using var context = new ApplicationDbContext(Options($"WO_EmptyCostAnalysis_{Guid.NewGuid()}"));
        var order = Order(Product("FG-ZERO"), "WO-ZERO", DateTime.UtcNow);
        context.WorkOrders.Add(order);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var view = Assert.IsType<ViewResult>(await Controller(context).Details(order.Id));
        var analysis = Assert.IsType<WorkOrderDetailsViewModel>(view.Model).CostAnalysis;

        Assert.Equal(new CostComparisonViewModel(0m, 0m), analysis.MaterialCost);
        Assert.Equal(new CostComparisonViewModel(0m, 0m), analysis.LaborCost);
        Assert.Equal(new CostComparisonViewModel(0m, 0m), analysis.MachineCost);
        Assert.Equal(new CostComparisonViewModel(0m, 0m), analysis.TotalCost);
        Assert.Equal(new CostComparisonViewModel(0m, 0m), analysis.UnitCost);
    }

    [Fact]
    public async Task Details_RoundsDisplayedCostAwayFromZero()
    {
        await using var context = new ApplicationDbContext(Options($"WO_CostRounding_{Guid.NewGuid()}"));
        var product = Product("FG-ROUND");
        var material = Product("RM-ROUND", manufactured: false);
        var order = Order(product, "WO-ROUND", DateTime.UtcNow);
        order.Qty = 1m;
        context.WorkOrders.Add(order);
        context.BOMs.Add(new BOM
        {
            Product = product,
            Version = "V1",
            IsActive = true,
            TotalMaterialCost = 1.005m
        });
        context.MaterialReservations.Add(new MaterialReservation
        {
            WorkOrder = order,
            Product = material,
            Lot = new Lot { LotNo = "ROUND-LOT", Product = material, UnitPrice = 2.01m },
            Location = new Location
            {
                Code = "ROUND",
                Name = "Round",
                Zone = new Zone { Code = "R", Name = "Round" }
            },
            QtyReserved = 0.5m
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var view = Assert.IsType<ViewResult>(await Controller(context).Details(order.Id));
        var analysis = Assert.IsType<WorkOrderDetailsViewModel>(view.Model).CostAnalysis;

        Assert.Equal(1.01m, analysis.MaterialCost.Target);
        Assert.Equal(1.01m, analysis.MaterialCost.Actual);
    }

    [Fact]
    public async Task Details_TotalCostEqualsSumOfDisplayedRoundedComponents()
    {
        await using var context = new ApplicationDbContext(
            Options($"WO_CostComponentRounding_{Guid.NewGuid()}"));
        var product = Product("FG-COMPONENT-ROUND");
        var material = Product("RM-COMPONENT-ROUND", manufactured: false);
        var center = new WorkCenter
        {
            Code = "WC-ROUND",
            Name = "Rounding center",
            HourlyLaborRate = 0.30m,
            HourlyMachineRate = 0.30m
        };
        var order = Order(product, "WO-COMPONENT-ROUND", DateTime.UtcNow);
        order.Qty = 1m;
        order.Steps.Add(new WorkOrderStep
        {
            StepNumber = 10,
            StepName = "Rounding operation",
            WorkCenter = center,
            StartTime = new DateTime(2026, 7, 27, 1, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 7, 27, 1, 1, 0, DateTimeKind.Utc),
            QtyOK = 1m
        });
        context.WorkOrders.Add(order);
        context.BOMs.Add(new BOM
        {
            Product = product,
            Version = "V1",
            IsActive = true,
            TotalMaterialCost = 1.005m
        });
        context.MaterialReservations.Add(new MaterialReservation
        {
            WorkOrder = order,
            Product = material,
            Lot = new Lot
            {
                LotNo = "COMPONENT-ROUND-LOT",
                Product = material,
                UnitPrice = 1.005m
            },
            Location = new Location
            {
                Code = "COMPONENT-ROUND",
                Name = "Component round",
                Zone = new Zone { Code = "CR", Name = "Component round" }
            },
            QtyReserved = 1m
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var view = Assert.IsType<ViewResult>(await Controller(context).Details(order.Id));
        var analysis = Assert.IsType<WorkOrderDetailsViewModel>(view.Model).CostAnalysis;

        Assert.Equal(
            analysis.MaterialCost.Target + analysis.LaborCost.Target + analysis.MachineCost.Target,
            analysis.TotalCost.Target);
        Assert.Equal(
            analysis.MaterialCost.Actual + analysis.LaborCost.Actual + analysis.MachineCost.Actual,
            analysis.TotalCost.Actual);
        Assert.Equal(1.01m, analysis.TotalCost.Target);
        Assert.Equal(1.02m, analysis.TotalCost.Actual);
        Assert.Equal(1.01m, analysis.UnitCost.Target);
        Assert.Equal(1.02m, analysis.UnitCost.Actual);
    }

    [Fact]
    public async Task Details_SuppliesVietnamBusinessDateAcrossUtcDateBoundary()
    {
        await using var context = new ApplicationDbContext(Options($"WO_DetailsBusinessDate_{Guid.NewGuid()}"));
        var order = Order(Product("FG-DATE"), "WO-DATE", new DateTime(2026, 7, 24));
        context.WorkOrders.Add(order);
        await context.SaveChangesAsync();
        var controller = Controller(
            context,
            timeProvider: new FixedTimeProvider(
                new DateTimeOffset(2026, 7, 23, 18, 30, 0, TimeSpan.Zero)),
            businessTimeZone: VietnamTimeZone());

        var result = Assert.IsType<ViewResult>(await controller.Details(order.Id));

        Assert.Equal(new DateTime(2026, 7, 24), result.ViewData["BusinessDate"]);
    }

    [Fact]
    public async Task CreateGet_LoadsOnlyActiveManufacturedProducts()
    {
        await using var context = new ApplicationDbContext(Options($"WO_CreateGet_{Guid.NewGuid()}"));
        context.Products.AddRange(Product("VALID"), Product("INACTIVE", active: false), Product("PURCHASED", false));
        await context.SaveChangesAsync();

        var result = await Controller(context).Create();

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
        var order = new WorkOrderCreateInputModel { Code = "WO-NEW", ProductId = product.Id, Qty = 10, DueDate = DateTime.UtcNow.AddDays(2) };

        var result = await controller.Create(order);

        Assert.Equal(nameof(WorkOrderController.Index), Assert.IsType<RedirectToActionResult>(result).ActionName);
        var saved = await context.WorkOrders.SingleAsync();
        Assert.Equal(WorkOrderStatus.Draft, saved.Status);
        Assert.Equal("WO-NEW", saved.Code);
        Assert.Equal(string.Empty, saved.BomVersion);
        Assert.Equal(string.Empty, saved.RoutingVersion);
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
        var order = new WorkOrderCreateInputModel { Code = code, ProductId = product.Id, Qty = qty, DueDate = DateTime.UtcNow.AddDays(2) };

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
        var order = new WorkOrderCreateInputModel { Code = "WO-FORGED", ProductId = forgedId, Qty = 10, DueDate = DateTime.UtcNow.AddDays(1) };

        var result = await controller.Create(order);

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey(nameof(WorkOrderCreateInputModel.ProductId)));
        Assert.Empty(context.WorkOrders);
    }

    [Fact]
    public void CreateInputModel_ExposesOnlyIntendedBindableFieldsAndUsesMvcValidationMetadata()
    {
        var properties = typeof(WorkOrderCreateInputModel).GetProperties().Select(x => x.Name).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "Code", "DueDate", "ProductId", "Qty" }, properties);

        var invalid = new WorkOrderCreateInputModel();
        var results = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(invalid, new ValidationContext(invalid), results, validateAllProperties: true));
        Assert.Contains(results, x => x.MemberNames.Contains(nameof(WorkOrderCreateInputModel.Code)));
        Assert.Contains(results, x => x.MemberNames.Contains(nameof(WorkOrderCreateInputModel.ProductId)));
        Assert.Contains(results, x => x.MemberNames.Contains(nameof(WorkOrderCreateInputModel.Qty)));
        Assert.Contains(results, x => x.MemberNames.Contains(nameof(WorkOrderCreateInputModel.DueDate)));
    }

    [Fact]
    public async Task CreatePost_ConstructsEntityServerSideAndCannotOverpostEntityState()
    {
        var parameter = typeof(WorkOrderController).GetMethod(nameof(WorkOrderController.Create), new[] { typeof(WorkOrderCreateInputModel) })!.GetParameters().Single();
        Assert.Equal(typeof(WorkOrderCreateInputModel), parameter.ParameterType);

        await using var context = new ApplicationDbContext(Options($"WO_Overpost_{Guid.NewGuid()}"));
        var product = Product("FG");
        context.Products.Add(product);
        await context.SaveChangesAsync();
        var controller = Controller(context);

        await controller.Create(new WorkOrderCreateInputModel { Code = " WO-SAFE ", ProductId = product.Id, Qty = 4, DueDate = new DateTime(2026, 8, 1) });

        var saved = await context.WorkOrders.SingleAsync();
        Assert.True(saved.Id > 0); // database owns the identity
        Assert.Equal(WorkOrderStatus.Draft, saved.Status);
        Assert.Empty(saved.Steps);
        Assert.Equal("WO-SAFE", saved.Code);
        Assert.Equal(string.Empty, saved.BomVersion);
        Assert.Equal(string.Empty, saved.RoutingVersion);
    }

    [Theory]
    [InlineData(nameof(WorkOrderController.Approve))]
    [InlineData(nameof(WorkOrderController.Complete))]
    public void ApprovalActions_RequireAdminOrManagerAndAntiforgery(string actionName)
    {
        var method = typeof(WorkOrderController).GetMethod(actionName)!;
        var authorize = Assert.Single(method.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal("Admin,Manager", authorize.Roles);
        Assert.Single(method.GetCustomAttributes<ValidateAntiForgeryTokenAttribute>());
        Assert.Single(method.GetCustomAttributes<HttpPostAttribute>());
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
        var logger = new Mock<ILogger<WorkOrderController>>();
        var controller = Controller(context, service.Object, logger: logger.Object);

        await controller.Approve(7);

        Assert.Equal("Không thể phê duyệt lệnh sản xuất. Vui lòng thử lại hoặc liên hệ quản trị viên.", controller.TempData["StatusMessage"]);
        logger.Verify(x => x.Log(LogLevel.Error, It.IsAny<EventId>(), It.Is<It.IsAnyType>((_, _) => true), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
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
        var logger = new Mock<ILogger<WorkOrderController>>();
        var controller = Controller(context, service.Object, logger: logger.Object);

        await controller.Complete(8);

        Assert.Equal("Không thể hoàn thành lệnh sản xuất. Vui lòng thử lại hoặc liên hệ quản trị viên.", controller.TempData["StatusMessage"]);
        logger.Verify(x => x.Log(LogLevel.Error, It.IsAny<EventId>(), It.Is<It.IsAnyType>((_, _) => true), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    private static WorkOrderController Controller(
        ApplicationDbContext context,
        IWorkOrderService? service = null,
        string? userId = null,
        ILogger<WorkOrderController>? logger = null,
        TimeProvider? timeProvider = null,
        TimeZoneInfo? businessTimeZone = null)
    {
        var controller = new WorkOrderController(
            context,
            service ?? Mock.Of<IWorkOrderService>(),
            logger ?? Mock.Of<ILogger<WorkOrderController>>(),
            Mock.Of<IReportExportService>(),
            timeProvider ?? TimeProvider.System,
            businessTimeZone ?? TimeZoneInfo.Utc)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.TempData = new TempDataDictionary(controller.HttpContext, Mock.Of<ITempDataProvider>());
        if (userId != null)
            controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "test"));
        return controller;
    }

    private static TimeZoneInfo VietnamTimeZone() =>
        TimeZoneInfo.CreateCustomTimeZone(
            "Asia/Ho_Chi_Minh",
            TimeSpan.FromHours(7),
            "Vietnam",
            "Vietnam");

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private static Product Product(string code, bool manufactured = true, bool active = true) =>
        new() { Code = code, Name = code, IsManufactured = manufactured, IsActive = active };

    private static WorkOrder Order(Product? product, string code, DateTime dueDate) =>
        new() { Code = code, Product = product, ProductId = product?.Id ?? 0, Qty = 10, DueDate = dueDate, BomVersion = "B1", RoutingVersion = "R1" };
}
