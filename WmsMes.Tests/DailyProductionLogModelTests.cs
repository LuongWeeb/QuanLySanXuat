using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;

namespace WmsMes.Tests;

public class DailyProductionLogModelTests
{
    [Fact]
    public void Entity_ExposesRequiredDailyProductionFields()
    {
        var entityType = typeof(WorkOrder).Assembly.GetType(
            "WmsMes.Web.Domain.Entities.DailyProductionLog");

        Assert.NotNull(entityType);
        Assert.NotNull(entityType!.GetProperty("Id"));
        Assert.NotNull(entityType.GetProperty("WorkOrderId")?.GetCustomAttributes(
            typeof(RequiredAttribute), inherit: true).SingleOrDefault());
        Assert.NotNull(entityType.GetProperty("WorkOrder")?.GetCustomAttributes(
            typeof(ForeignKeyAttribute), inherit: true).SingleOrDefault());
        Assert.NotNull(entityType.GetProperty("Date")?.GetCustomAttributes(
            typeof(RequiredAttribute), inherit: true).SingleOrDefault());
        Assert.Equal("decimal(18,2)", Assert.Single(entityType.GetProperty("QtyProduced")!
            .GetCustomAttributes(typeof(ColumnAttribute), inherit: true)
            .Cast<ColumnAttribute>()).TypeName);
        Assert.Equal(250, Assert.Single(entityType.GetProperty("Notes")!
            .GetCustomAttributes(typeof(MaxLengthAttribute), inherit: true)
            .Cast<MaxLengthAttribute>()).Length);
        Assert.NotNull(typeof(WorkOrder).GetProperty("DailyProductionLogs"));
        Assert.NotNull(typeof(ApplicationDbContext).GetProperty("DailyProductionLogs"));
    }

    [Fact]
    public void Model_MapsDailyLogsWithCascadeForeignKeyAndWorkOrderIndex()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"DailyLogModel_{Guid.NewGuid()}")
            .Options;
        using var context = new ApplicationDbContext(options);

        var entity = context.Model.FindEntityType(
            "WmsMes.Web.Domain.Entities.DailyProductionLog");

        Assert.NotNull(entity);
        var foreignKey = Assert.Single(entity!.GetForeignKeys());
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
        Assert.Equal(nameof(WorkOrder), foreignKey.PrincipalEntityType.ClrType.Name);
        Assert.Equal("WorkOrderId", Assert.Single(foreignKey.Properties).Name);
        Assert.Contains(entity.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual(["WorkOrderId"]));
    }

    [Fact]
    public async Task Sqlite_DeleteWorkOrder_CascadesDailyProductionLogs()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var order = new WorkOrder
        {
            Code = "WO-CASCADE",
            Product = new Product
            {
                Code = "FG-CASCADE",
                Name = "Finished good",
                BaseUom = new UnitOfMeasure { Code = "EA-CASCADE", Name = "Each" }
            },
            Qty = 1,
            DueDate = DateTime.Today,
            BomVersion = "B1",
            RoutingVersion = "R1"
        };
        order.DailyProductionLogs.Add(new DailyProductionLog
        {
            Date = DateTime.Today,
            QtyProduced = 1
        });
        context.WorkOrders.Add(order);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        context.WorkOrders.Remove(await context.WorkOrders.SingleAsync());
        await context.SaveChangesAsync();

        Assert.Empty(await context.DailyProductionLogs.AsNoTracking().ToListAsync());
    }

    [Fact]
    public void AddDailyProductionLogMigration_CreatesTableAndUpdatesSnapshot()
    {
        var root = FindRepositoryRoot();
        var migrations = Directory.GetFiles(
            Path.Combine(root, "Data", "Migrations"),
            "*_AddDailyProductionLog.cs",
            SearchOption.TopDirectoryOnly);

        var migrationPath = Assert.Single(migrations);
        var migration = File.ReadAllText(migrationPath);
        Assert.Contains("name: \"DailyProductionLogs\"", migration);
        Assert.Contains("onDelete: ReferentialAction.Cascade", migration);
        Assert.Contains("IX_DailyProductionLogs_WorkOrderId", migration);
        Assert.DoesNotContain("CycleCount", migration);

        var designer = File.ReadAllText(Path.ChangeExtension(migrationPath, ".Designer.cs"));
        Assert.Contains("DailyProductionLog", designer);
        Assert.DoesNotContain("CycleCount", designer);

        var snapshot = File.ReadAllText(Path.Combine(
            root, "Data", "Migrations", "ApplicationDbContextModelSnapshot.cs"));
        Assert.Contains("DailyProductionLog", snapshot);
        Assert.Contains("DailyProductionLogs", snapshot);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WmsMes.sln")))
            directory = directory.Parent;

        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
