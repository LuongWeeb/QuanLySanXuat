using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.Hubs;
using WmsMes.Web.Services;

namespace WmsMes.Tests;

public class SupplyChainReportServiceTests
{
    [Fact]
    public async Task CreatePickListForSalesOrderAsync_AllocatesAcrossBalancesInZoneThenLocationOrder()
    {
        await using var context = CreateContext();
        await SeedPickListOrderAsync(context, orderId: 1, quantity: 8m);
        context.StockBalances.AddRange(
            new StockBalance { ProductId = 1, LotId = 1, LocationId = 3, QtyAvailable = 4m },
            new StockBalance { ProductId = 1, LotId = 2, LocationId = 2, QtyAvailable = 3m },
            new StockBalance { ProductId = 1, LotId = 3, LocationId = 1, QtyAvailable = 5m });
        await context.SaveChangesAsync();

        var pickList = await new PickListService(context).CreatePickListForSalesOrderAsync(1);

        Assert.NotNull(pickList);
        Assert.Equal([1, 2], pickList!.Items.OrderBy(item => item.SequenceOrder).Select(item => item.LocationId));
        Assert.Equal([5m, 3m], pickList.Items.OrderBy(item => item.SequenceOrder).Select(item => item.QtyToPick));
        Assert.Equal([1, 2], pickList.Items.OrderBy(item => item.SequenceOrder).Select(item => item.SequenceOrder));
    }

    [Fact]
    public async Task CreatePickListForSalesOrderAsync_ReturnsNullAndDoesNotPersistWhenDeliveredQuantityMeetsOrderQuantity()
    {
        await using var context = CreateContext();
        await SeedPickListOrderAsync(context, orderId: 1, quantity: 5m, deliveredQuantity: 5m);
        context.StockBalances.Add(new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 5m });
        await context.SaveChangesAsync();

        var pickList = await new PickListService(context).CreatePickListForSalesOrderAsync(1);

        Assert.Null(pickList);
        Assert.Empty(await context.PickLists.ToListAsync());
    }

    [Fact]
    public async Task CreatePickListForSalesOrderAsync_ReturnsNullAndDoesNotPersistWhenNoStockCanBeAllocated()
    {
        await using var context = CreateContext();
        await SeedPickListOrderAsync(context, orderId: 1, quantity: 5m);

        var pickList = await new PickListService(context).CreatePickListForSalesOrderAsync(1);

        Assert.Null(pickList);
        Assert.Empty(await context.PickLists.ToListAsync());
    }

    [Theory]
    [InlineData(DocumentStatus.Completed)]
    [InlineData(DocumentStatus.Cancelled)]
    public async Task CreatePickListForSalesOrderAsync_ReturnsNullAndDoesNotPersistForNonDraftOrder(
        DocumentStatus status)
    {
        await using var context = CreateContext();
        await SeedPickListOrderAsync(context, orderId: 1, quantity: 5m, status: status);
        context.StockBalances.Add(new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 5m });
        await context.SaveChangesAsync();

        var pickList = await new PickListService(context).CreatePickListForSalesOrderAsync(1);

        Assert.Null(pickList);
        Assert.Empty(await context.PickLists.ToListAsync());
    }

    [Fact]
    public async Task CreatePickListForSalesOrderAsync_ReturnsNullForMissingOrder()
    {
        await using var context = CreateContext();

        var pickList = await new PickListService(context).CreatePickListForSalesOrderAsync(404);

        Assert.Null(pickList);
    }

    [Fact]
    public async Task CreatePickListForSalesOrderAsync_ReturnsExistingDraftPickListWithoutAddingDuplicateItems()
    {
        await using var context = CreateContext();
        await SeedPickListOrderAsync(context, orderId: 1, quantity: 5m);
        await SeedPickableStockAsync(context);
        var existing = new PickList
        {
            PickListNo = "PK-20260731-017",
            SalesOrderId = 1,
            Status = DocumentStatus.Draft,
            Items =
            {
                new PickListItem
                {
                    ProductId = 1,
                    LocationId = 1,
                    LotId = 1,
                    QtyToPick = 5m
                }
            }
        };
        context.PickLists.Add(existing);
        await context.SaveChangesAsync();

        var result = await new PickListService(context)
            .CreatePickListForSalesOrderAsync(1);

        Assert.Same(existing, result);
        Assert.Single(await context.PickLists.ToListAsync());
        Assert.Single(await context.PickListItems.ToListAsync());
        Assert.Equal(5m, (await context.PickListItems.SingleAsync()).QtyToPick);
    }

    [Fact]
    public async Task CreatePickListForSalesOrderAsync_AssignsUniqueDailyThreeDigitNumbers()
    {
        await using var context = CreateContext();
        await SeedPickListOrderAsync(context, orderId: 1, quantity: 1m);
        await SeedPickListOrderAsync(context, orderId: 2, quantity: 1m);
        await SeedPickableStockAsync(context);

        var service = new PickListService(context);
        var first = await service.CreatePickListForSalesOrderAsync(1);
        var second = await service.CreatePickListForSalesOrderAsync(2);

        Assert.Matches("^PK-[0-9]{8}-[0-9]{3}$", first!.PickListNo);
        Assert.NotEqual(first.PickListNo, second!.PickListNo);
        Assert.EndsWith("-001", first.PickListNo);
        Assert.EndsWith("-002", second.PickListNo);
    }

    [Fact]
    public async Task CreatePickListForSalesOrderAsync_OrdersEqualLocationCodesByLotId()
    {
        await using var context = CreateContext();
        await SeedPickListOrderAsync(context, orderId: 1, quantity: 2m);
        context.StockBalances.AddRange(
            new StockBalance { ProductId = 1, LotId = 3, LocationId = 1, QtyAvailable = 1m },
            new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 1m });
        await context.SaveChangesAsync();

        var pickList = await new PickListService(context).CreatePickListForSalesOrderAsync(1);

        Assert.Equal([1, 3], pickList!.Items.OrderBy(item => item.SequenceOrder).Select(item => item.LotId));
    }

    [Fact]
    public async Task CreatePickListForSalesOrderAsync_FailsBeforePersistingWhenDailyNumberRangeIsExhausted()
    {
        var now = new DateTimeOffset(2026, 7, 31, 23, 59, 59, TimeSpan.Zero);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = await CreateRelationalContextAsync(connection);
        await SeedPickListOrderAsync(context, orderId: 1, quantity: 1m);
        await SeedPickableStockAsync(context);
        var prefix = $"PK-{now:yyyyMMdd}-";
        context.PickLists.AddRange(Enumerable.Range(1, 999).Select(sequence => new PickList
        {
            PickListNo = $"{prefix}{sequence:000}",
            SalesOrderId = 1,
            Status = DocumentStatus.Completed
        }));
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PickListService(context, new FixedTimeProvider(now)).CreatePickListForSalesOrderAsync(1));

        Assert.Contains("exhausted", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(999, await context.PickLists.CountAsync());
    }

    [Fact]
    public async Task CreatePickListForSalesOrderAsync_RetriesAnActualSqlitePickListNumberCollision()
    {
        var now = new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"pick-list-collision-{Guid.NewGuid()}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using (var seedContext = await CreateRelationalContextAsync(connection))
        {
            await SeedPickListOrderAsync(seedContext, orderId: 1, quantity: 1m);
            await SeedPickListOrderAsync(seedContext, orderId: 2, quantity: 1m);
            await SeedPickableStockAsync(seedContext);
        }

        var interceptor = new InsertCompetingPickListInterceptor(connectionString);
        await using var context = await CreateRelationalContextAsync(connection, interceptor);

        var pickList = await new PickListService(context, new FixedTimeProvider(now))
            .CreatePickListForSalesOrderAsync(1);

        Assert.True(interceptor.InsertedCompetingPickList);
        Assert.Equal("PK-20260731-002", pickList!.PickListNo);
        Assert.Equal(
            ["PK-20260731-001", "PK-20260731-002"],
            await context.PickLists.OrderBy(list => list.PickListNo).Select(list => list.PickListNo).ToListAsync());
    }

    [Fact]
    public async Task CreatePickListForSalesOrderAsync_ReturnsCompetingDraftPickListWhenSameOrderRaces()
    {
        var now = new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"pick-list-order-race-{Guid.NewGuid()}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using (var seedContext = await CreateRelationalContextAsync(connection))
        {
            await SeedPickListOrderAsync(seedContext, orderId: 1, quantity: 4m);
            await SeedPickableStockAsync(seedContext);
        }

        var interceptor = new InsertCompetingDraftPickListInterceptor(connectionString);
        await using var context = await CreateRelationalContextAsync(connection, interceptor);

        var result = await new PickListService(context, new FixedTimeProvider(now))
            .CreatePickListForSalesOrderAsync(1);

        Assert.True(interceptor.InsertedCompetingPickList);
        Assert.Equal("PK-20260731-777", result!.PickListNo);
        Assert.Single(await context.PickLists.Where(list => list.SalesOrderId == 1).ToListAsync());
        Assert.Equal(4m, result.Items.Sum(item => item.QtyToPick));
    }

    [Fact]
    public async Task DraftPickListSalesOrderReservation_IsActuallyUniqueInTheRelationalDatabase()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = await CreateRelationalContextAsync(connection);
        await SeedPickListOrderAsync(context, orderId: 1, quantity: 1m);
        context.PickLists.AddRange(
            new PickList
            {
                PickListNo = "PK-20260731-001",
                SalesOrderId = 1,
                Status = DocumentStatus.Completed
            },
            new PickList
            {
                PickListNo = "PK-20260731-002",
                SalesOrderId = 1,
                Status = DocumentStatus.Draft
            });
        await context.SaveChangesAsync();
        context.PickLists.Add(new PickList
        {
            PickListNo = "PK-20260731-003",
            SalesOrderId = 1,
            Status = DocumentStatus.Draft
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(2, await context.PickLists.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task PickListNumbers_AreActuallyUniqueInTheRelationalDatabase()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = await CreateRelationalContextAsync(connection);
        await SeedPickListOrderAsync(context, orderId: 1, quantity: 1m);
        var number = $"PK-{DateTime.UtcNow:yyyyMMdd}-001";
        context.PickLists.Add(new PickList { PickListNo = number, SalesOrderId = 1 });
        await context.SaveChangesAsync();
        context.PickLists.Add(new PickList { PickListNo = number, SalesOrderId = 1 });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task CreatePickListForSalesOrderAsync_DoesNotRetryAnUnrelatedDatabaseUpdateError()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using (var seedContext = CreateContext(databaseName))
        {
            await SeedPickListOrderAsync(seedContext, orderId: 1, quantity: 1m);
            await SeedPickableStockAsync(seedContext);
        }

        var interceptor = new UnrelatedDbUpdateExceptionInterceptor();
        await using var context = CreateContext(databaseName, interceptor);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            new PickListService(context).CreatePickListForSalesOrderAsync(1));

        Assert.Equal(1, interceptor.SaveAttempts);
    }

    [Fact]
    public async Task SendNotificationAsync_PersistsUnreadNotificationAndBroadcastsRealtimeEvent()
    {
        await using var context = CreateContext();
        var client = new Mock<IClientProxy>();
        client.Setup(proxy => proxy.SendCoreAsync(
                "ReceiveNotification",
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                Assert.Single(context.AppNotifications);
                return Task.CompletedTask;
            });
        var clients = new Mock<IHubClients>();
        clients.SetupGet(items => items.All).Returns(client.Object);
        var hub = new Mock<IHubContext<NotificationHub>>();
        hub.SetupGet(item => item.Clients).Returns(clients.Object);

        await new NotificationService(context, hub.Object)
            .SendNotificationAsync("Low stock", "ITEM-01 is below minimum", "Warning", "/Inventory");

        var notification = Assert.Single(context.AppNotifications);
        Assert.Equal("Low stock", notification.Title);
        Assert.False(notification.IsRead);
        Assert.Equal("/Inventory", notification.ReferenceUrl);
        client.Verify(proxy => proxy.SendCoreAsync(
            "ReceiveNotification",
            It.Is<object?[]>(arguments => arguments.Length == 1 && ReferenceEquals(arguments[0], notification)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendNotificationAsync_WhenHubBroadcastFails_PersistsAndCompletes()
    {
        await using var context = CreateContext();
        var client = new Mock<IClientProxy>();
        client.Setup(proxy => proxy.SendCoreAsync(
                "ReceiveNotification",
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("hub unavailable"));
        var clients = new Mock<IHubClients>();
        clients.SetupGet(items => items.All).Returns(client.Object);
        var hub = new Mock<IHubContext<NotificationHub>>();
        hub.SetupGet(item => item.Clients).Returns(clients.Object);
        var logger = new Mock<ILogger<NotificationService>>();

        await new NotificationService(context, hub.Object, logger.Object)
            .SendNotificationAsync("QC REJECT", "LOT-01 was rejected", "Danger", "/Qc/Details/1");

        var persisted = Assert.Single(await context.AppNotifications.AsNoTracking().ToListAsync());
        Assert.Equal("QC REJECT", persisted.Title);
        Assert.False(persisted.IsRead);
        logger.Verify(candidate => candidate.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task GetRecentNotificationsAsync_ReturnsNewestNotificationsAndGetUnreadCountAsyncExcludesReadItems()
    {
        await using var context = CreateContext();
        context.AppNotifications.AddRange(
            new AppNotification { Id = 1, Title = "old", Message = "old", CreatedAt = new DateTime(2026, 7, 1), IsRead = false },
            new AppNotification { Id = 2, Title = "read", Message = "read", CreatedAt = new DateTime(2026, 7, 3), IsRead = true },
            new AppNotification { Id = 3, Title = "new", Message = "new", CreatedAt = new DateTime(2026, 7, 3), IsRead = false });
        await context.SaveChangesAsync();
        var service = new NotificationService(context);

        var recent = await service.GetRecentNotificationsAsync(take: 2);
        var unreadCount = await service.GetUnreadCountAsync();

        Assert.Equal(["new", "read"], recent.Select(notification => notification.Title));
        Assert.Equal(2, unreadCount);
    }

    [Fact]
    public void NotificationHub_RequiresAuthorizedConnections()
    {
        var authorize = Assert.Single(typeof(NotificationHub)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());
        Assert.Equal("Admin,Warehouse,Manager", authorize.Roles);
    }

    [Fact]
    public void ApplicationServices_ResolveRegisteredSupplyChainServices()
    {
        using var factory = new InventoryCancellationWebApplicationFactory();
        using var scope = factory.Services.CreateScope();

        Assert.IsType<PickListService>(scope.ServiceProvider.GetRequiredService<IPickListService>());
        Assert.IsType<NotificationService>(scope.ServiceProvider.GetRequiredService<INotificationService>());
        Assert.IsType<QcService>(scope.ServiceProvider.GetRequiredService<IQcService>());
        Assert.IsType<WorkOrderService>(scope.ServiceProvider.GetRequiredService<IWorkOrderService>());
        Assert.IsType<ProductionPlanService>(scope.ServiceProvider.GetRequiredService<IProductionPlanService>());
    }

    private static ApplicationDbContext CreateContext() => CreateContext(Guid.NewGuid().ToString());

    private static ApplicationDbContext CreateContext(
        string databaseName,
        params IInterceptor[] interceptors) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddInterceptors(interceptors)
            .Options);

    private static async Task<ApplicationDbContext> CreateRelationalContextAsync(
        SqliteConnection connection,
        params IInterceptor[] interceptors)
    {
        var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptors)
            .Options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    private static async Task SeedPickListOrderAsync(
        ApplicationDbContext context,
        int orderId,
        decimal quantity,
        decimal deliveredQuantity = 0m,
        DocumentStatus status = DocumentStatus.Draft)
    {
        if (!await context.Products.AnyAsync())
        {
            context.UnitOfMeasures.Add(new UnitOfMeasure { Id = 1, Code = "PCS", Name = "Pieces" });
            context.Products.Add(new Product { Id = 1, Code = "ITEM-01", Name = "Item", BaseUomId = 1 });
            context.Customers.Add(new Customer { Id = 1, Code = "CUS", Name = "Customer" });
            context.Warehouses.Add(new Warehouse { Id = 1, Code = "WH", Name = "Warehouse" });
            context.Zones.AddRange(
                new Zone { Id = 1, WarehouseId = 1, Code = "A", Name = "A" },
                new Zone { Id = 2, WarehouseId = 1, Code = "B", Name = "B" });
            context.Locations.AddRange(
                new Location { Id = 1, ZoneId = 1, Code = "A-01", Name = "A-01" },
                new Location { Id = 2, ZoneId = 1, Code = "A-02", Name = "A-02" },
                new Location { Id = 3, ZoneId = 2, Code = "B-01", Name = "B-01" });
            context.Lots.AddRange(
                new Lot { Id = 1, ProductId = 1, LotNo = "LOT-01", Qty = 10m },
                new Lot { Id = 2, ProductId = 1, LotNo = "LOT-02", Qty = 10m },
                new Lot { Id = 3, ProductId = 1, LotNo = "LOT-03", Qty = 10m });
        }

        context.SalesOrders.Add(new SalesOrder
        {
            Id = orderId,
            OrderNo = $"SO-{orderId:000}",
            CustomerId = 1,
            DeliveryDate = DateTime.UtcNow.AddDays(1),
            Status = status,
            Items = { new SalesOrderItem { ProductId = 1, Qty = quantity, DeliveredQty = deliveredQuantity } }
        });
        await context.SaveChangesAsync();
    }

    private static async Task SeedPickableStockAsync(ApplicationDbContext context)
    {
        context.StockBalances.Add(new StockBalance
        {
            ProductId = 1,
            LotId = 1,
            LocationId = 1,
            QtyAvailable = 10m
        });
        await context.SaveChangesAsync();
    }

    private sealed class UnrelatedDbUpdateExceptionInterceptor : SaveChangesInterceptor
    {
        public int SaveAttempts { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            SaveAttempts++;
            throw new DbUpdateException("Simulated unrelated database update failure.");
        }
    }

    private sealed class InsertCompetingPickListInterceptor(string connectionString) : SaveChangesInterceptor
    {
        private bool _hasInserted;

        public bool InsertedCompetingPickList => _hasInserted;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (_hasInserted || eventData.Context is null)
            {
                return result;
            }

            var pendingPickList = eventData.Context.ChangeTracker.Entries<PickList>()
                .Select(entry => entry.Entity)
                .SingleOrDefault(pickList => eventData.Context.Entry(pickList).State == EntityState.Added);
            if (pendingPickList is null)
            {
                return result;
            }

            _hasInserted = true;
            await using var competingConnection = new SqliteConnection(connectionString);
            await competingConnection.OpenAsync(cancellationToken);
            await using var competingContext = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseSqlite(competingConnection)
                    .Options);
            competingContext.PickLists.Add(new PickList
            {
                PickListNo = pendingPickList.PickListNo,
                SalesOrderId = 2
            });
            await competingContext.SaveChangesAsync(cancellationToken);

            return result;
        }
    }

    private sealed class InsertCompetingDraftPickListInterceptor(string connectionString) : SaveChangesInterceptor
    {
        private bool _hasInserted;

        public bool InsertedCompetingPickList => _hasInserted;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (_hasInserted || eventData.Context is null)
            {
                return result;
            }

            var pendingPickList = eventData.Context.ChangeTracker.Entries<PickList>()
                .Select(entry => entry.Entity)
                .SingleOrDefault(pickList => eventData.Context.Entry(pickList).State == EntityState.Added);
            if (pendingPickList is null)
            {
                return result;
            }

            _hasInserted = true;
            await using var competingConnection = new SqliteConnection(connectionString);
            await competingConnection.OpenAsync(cancellationToken);
            await using var competingContext = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseSqlite(competingConnection)
                    .Options);
            competingContext.PickLists.Add(new PickList
            {
                PickListNo = "PK-20260731-777",
                SalesOrderId = pendingPickList.SalesOrderId,
                Status = DocumentStatus.Draft,
                Items = pendingPickList.Items.Select(item => new PickListItem
                {
                    ProductId = item.ProductId,
                    LocationId = item.LocationId,
                    LotId = item.LotId,
                    QtyToPick = item.QtyToPick,
                    SequenceOrder = item.SequenceOrder
                }).ToList()
            });
            await competingContext.SaveChangesAsync(cancellationToken);

            return result;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
