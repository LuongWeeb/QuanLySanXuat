using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.Services;

namespace WmsMes.Tests;

public class DailyProductionLogControllerTests
{
    [Fact]
    public void AddDailyLog_UsesNarrowValidatedInputAndAntiforgery()
    {
        var inputType = typeof(WorkOrderController).Assembly.GetType(
            "WmsMes.Web.ViewModels.DailyProductionLogInputModel");

        Assert.NotNull(inputType);
        Assert.Equal(
            new[] { "Date", "Notes", "QtyProduced" },
            inputType!.GetProperties().Select(property => property.Name).OrderBy(name => name));
        Assert.Contains(inputType.GetProperty("Date")!.GetCustomAttributes(),
            attribute => attribute is RequiredAttribute);
        Assert.Contains(inputType.GetProperty("QtyProduced")!.GetCustomAttributes(),
            attribute => attribute is RangeAttribute);
        Assert.Equal(250, Assert.Single(inputType.GetProperty("Notes")!
            .GetCustomAttributes<StringLengthAttribute>()).MaximumLength);

        var method = typeof(WorkOrderController).GetMethod("AddDailyLog");
        Assert.NotNull(method);
        Assert.Single(method!.GetCustomAttributes<HttpPostAttribute>());
        Assert.Single(method.GetCustomAttributes<ValidateAntiForgeryTokenAttribute>());
        Assert.Equal(new[] { typeof(int), inputType }, method.GetParameters()
            .Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public async Task AddDailyLog_ForInProgressOrder_PersistsNormalizedLogAndCommits()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var order = await AddOrderAsync(context, WorkOrderStatus.InProgress);

        var result = await InvokeAddDailyLogAsync(
            Controller(context),
            order.Id,
            new DateTime(2026, 7, 23, 17, 45, 0),
            3.5m,
            "  Ca chiều  ");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(WorkOrderController.Details), redirect.ActionName);
        context.ChangeTracker.Clear();
        var saved = await context.DailyProductionLogs.SingleAsync();
        Assert.Equal(new DateTime(2026, 7, 23), saved.Date);
        Assert.Equal(3.5m, saved.QtyProduced);
        Assert.Equal("Ca chiều", saved.Notes);
    }

    [Fact]
    public async Task AddDailyLog_WhenOrderMissing_ReturnsNotFoundWithoutInsert()
    {
        await using var context = InMemoryContext();

        var result = await InvokeAddDailyLogAsync(
            Controller(context), 404, DateTime.Today, 1m, string.Empty);

        Assert.IsType<NotFoundResult>(result);
        Assert.Empty(context.DailyProductionLogs);
    }

    [Fact]
    public async Task AddDailyLog_WhenOrderIsNotInProgress_RejectsWithVietnameseFeedback()
    {
        await using var context = InMemoryContext();
        var order = await AddOrderAsync(context, WorkOrderStatus.Draft);
        var controller = Controller(context);

        var result = await InvokeAddDailyLogAsync(
            controller, order.Id, DateTime.Today, 1m, string.Empty);

        Assert.Equal(nameof(WorkOrderController.Details),
            Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Contains("đang sản xuất", controller.TempData["StatusMessage"]!.ToString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Empty(context.DailyProductionLogs);
    }

    public static IEnumerable<object?[]> InvalidInputs()
    {
        yield return [null, 1m, string.Empty, "Date"];
        yield return [DateTime.Today, 0m, string.Empty, "QtyProduced"];
        yield return [DateTime.Today, -1m, string.Empty, "QtyProduced"];
        yield return [DateTime.Today, 1m, new string('x', 251), "Notes"];
    }

    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public async Task AddDailyLog_WithInvalidInput_ReturnsDetailsWithFieldError(
        DateTime? date,
        decimal quantity,
        string notes,
        string expectedField)
    {
        await using var context = InMemoryContext();
        var order = await AddOrderAsync(context, WorkOrderStatus.InProgress);
        var controller = Controller(context);

        var result = await InvokeAddDailyLogAsync(
            controller, order.Id, date, quantity, notes);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal(nameof(WorkOrderController.Details), view.ViewName);
        Assert.IsType<WorkOrder>(view.Model);
        Assert.Contains(expectedField, controller.ModelState.Keys);
        Assert.Empty(context.DailyProductionLogs);
    }

    [Fact]
    public async Task AddDailyLog_DoesNotDuplicateExistingMvcValidationErrors()
    {
        await using var context = InMemoryContext();
        var order = await AddOrderAsync(context, WorkOrderStatus.InProgress);
        var controller = Controller(context);
        controller.ModelState.AddModelError("Date", "Ngày sản xuất là bắt buộc.");

        await InvokeAddDailyLogAsync(controller, order.Id, null, 1m, string.Empty);

        Assert.Single(controller.ModelState["Date"]!.Errors);
    }

    [Fact]
    public async Task AddDailyLog_WhenDateIsAfterVietnamBusinessDate_RejectsWithoutInflatingProgress()
    {
        await using var context = InMemoryContext();
        var order = await AddOrderAsync(context, WorkOrderStatus.InProgress);
        var controller = Controller(
            context,
            new FixedTimeProvider(
                new DateTimeOffset(2026, 7, 23, 18, 30, 0, TimeSpan.Zero)),
            VietnamTimeZone());

        var result = await InvokeAddDailyLogAsync(
            controller,
            order.Id,
            new DateTime(2026, 7, 25),
            9m,
            "Không được ghi trước");

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal(nameof(WorkOrderController.Details), view.ViewName);
        var returnedOrder = Assert.IsType<WorkOrder>(view.Model);
        Assert.Equal(0m, returnedOrder.DailyProductionLogs.Sum(log => log.QtyProduced));
        var error = Assert.Single(controller.ModelState["Date"]!.Errors);
        Assert.Contains("tương lai", error.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(context.DailyProductionLogs);
    }

    [Fact]
    public async Task AddDailyLog_WhenSaveFails_RollsBackRelationalInsert()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new FailAfterSaveDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var order = await AddOrderAsync(context, WorkOrderStatus.InProgress);
        context.FailAfterSave = true;
        var controller = Controller(context);

        var result = await InvokeAddDailyLogAsync(
            controller, order.Id, DateTime.Today, 2m, "rollback");

        Assert.Equal(nameof(WorkOrderController.Details),
            Assert.IsType<RedirectToActionResult>(result).ActionName);
        await using var verification = new ApplicationDbContext(options);
        Assert.Empty(await verification.DailyProductionLogs.AsNoTracking().ToListAsync());
        Assert.Contains("Không thể", controller.TempData["StatusMessage"]!.ToString());
    }

    [Fact]
    public async Task AddDailyLog_WhenFailureAlreadyCompletedTransaction_PreservesLocalizedFailure()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new ThrowWhenRollingBackCompletedTransactionInterceptor())
            .Options;
        await using var context = new FailAfterSaveDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var order = await AddOrderAsync(context, WorkOrderStatus.InProgress);
        context.FailAfterSave = true;
        var controller = Controller(context);

        var result = await InvokeAddDailyLogAsync(
            controller, order.Id, DateTime.Today, 2m, "deadlock rollback");

        Assert.Equal(
            nameof(WorkOrderController.Details),
            Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Contains(
            "Không thể",
            controller.TempData["StatusMessage"]!.ToString(),
            StringComparison.OrdinalIgnoreCase);
        await using var verification = new ApplicationDbContext(options);
        Assert.Empty(await verification.DailyProductionLogs.AsNoTracking().ToListAsync());
    }

    private static async Task<IActionResult> InvokeAddDailyLogAsync(
        WorkOrderController controller,
        int id,
        DateTime? date,
        decimal quantity,
        string notes)
    {
        var inputType = typeof(WorkOrderController).Assembly.GetType(
            "WmsMes.Web.ViewModels.DailyProductionLogInputModel");
        Assert.NotNull(inputType);
        var input = Activator.CreateInstance(inputType!);
        Assert.NotNull(input);
        inputType.GetProperty("Date")!.SetValue(input, date);
        inputType.GetProperty("QtyProduced")!.SetValue(input, quantity);
        inputType.GetProperty("Notes")!.SetValue(input, notes);
        var method = typeof(WorkOrderController).GetMethod("AddDailyLog");
        Assert.NotNull(method);

        var task = Assert.IsAssignableFrom<Task<IActionResult>>(
            method!.Invoke(controller, [id, input]));
        return await task;
    }

    private static ApplicationDbContext InMemoryContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"DailyLogs_{Guid.NewGuid()}")
            .Options);

    private static async Task<WorkOrder> AddOrderAsync(
        ApplicationDbContext context,
        WorkOrderStatus status)
    {
        var product = new Product
        {
            Code = $"FG-{Guid.NewGuid():N}",
            Name = "Finished good",
            BaseUom = new UnitOfMeasure
            {
                Code = $"EA-{Guid.NewGuid():N}",
                Name = "Each"
            },
            IsManufactured = true,
            IsActive = true
        };
        var order = new WorkOrder
        {
            Code = $"WO-{Guid.NewGuid():N}",
            Product = product,
            Qty = 10,
            DueDate = DateTime.Today.AddDays(2),
            Status = status,
            BomVersion = "B1",
            RoutingVersion = "R1"
        };
        context.WorkOrders.Add(order);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return order;
    }

    private static WorkOrderController Controller(
        ApplicationDbContext context,
        TimeProvider? timeProvider = null,
        TimeZoneInfo? businessTimeZone = null)
    {
        var controller = new WorkOrderController(
            context,
            Mock.Of<IWorkOrderService>(),
            Mock.Of<ILogger<WorkOrderController>>(),
            Mock.Of<IReportExportService>(),
            timeProvider ?? TimeProvider.System,
            businessTimeZone ?? TimeZoneInfo.Utc)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.TempData = new TempDataDictionary(
            controller.HttpContext,
            Mock.Of<ITempDataProvider>());
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

    private sealed class FailAfterSaveDbContext(
        DbContextOptions<ApplicationDbContext> options) : ApplicationDbContext(options)
    {
        public bool FailAfterSave { get; set; }

        public override async Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            var result = await base.SaveChangesAsync(cancellationToken);
            if (FailAfterSave)
                throw new InvalidOperationException("Injected failure after database write.");
            return result;
        }
    }

    private sealed class ThrowWhenRollingBackCompletedTransactionInterceptor
        : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The transaction has completed; it is no longer usable.");
    }
}
