using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using WmsMes.Web.Data;
using WmsMes.Web.Data.Migrations;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Tests;

public class SupplyChainMigrationTests
{
    [Fact]
    public async Task FinalIntegrityMigration_ReconcilesLegacyDuplicateDraftPickListsBeforeUniqueIndex()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlRawAsync(
            "DROP INDEX \"UX_PickLists_OneDraftPerSalesOrder\";");
        await context.Database.ExecuteSqlRawAsync(
            "CREATE INDEX \"IX_PickLists_SalesOrderId\" ON \"PickLists\" (\"SalesOrderId\");");

        var customer = new Customer { Code = "C-LEGACY", Name = "Legacy customer" };
        var order = new SalesOrder
        {
            OrderNo = "SO-LEGACY",
            Customer = customer,
            DeliveryDate = new DateTime(2026, 8, 10)
        };
        context.PickLists.AddRange(
            new PickList
            {
                PickListNo = "PK-LEGACY-001",
                SalesOrder = order,
                CreatedAt = new DateTime(2026, 7, 1),
                Status = DocumentStatus.Draft
            },
            new PickList
            {
                PickListNo = "PK-LEGACY-002",
                SalesOrder = order,
                CreatedAt = new DateTime(2026, 7, 2),
                Status = DocumentStatus.Draft
            });
        await context.SaveChangesAsync();

        var operations = new InspectableFinalIntegrityMigration()
            .BuildOperations()
            .Where(operation =>
                operation is SqlOperation ||
                operation is DropIndexOperation { Table: "PickLists" } ||
                operation is CreateIndexOperation { Table: "PickLists" })
            .ToList();
        Assert.IsType<DropIndexOperation>(operations[0]);
        Assert.IsType<SqlOperation>(operations[1]);
        Assert.IsType<CreateIndexOperation>(operations[2]);

        var generator = context.GetService<IMigrationsSqlGenerator>();
        foreach (var command in generator.Generate(operations, context.Model))
        {
            await context.Database.ExecuteSqlRawAsync(command.CommandText);
        }

        context.ChangeTracker.Clear();
        var migrated = await context.PickLists
            .OrderBy(list => list.CreatedAt)
            .ToListAsync();
        Assert.Equal(DocumentStatus.Draft, migrated[0].Status);
        Assert.Equal(DocumentStatus.Cancelled, migrated[1].Status);

        context.PickLists.Add(new PickList
        {
            PickListNo = "PK-LEGACY-003",
            SalesOrderId = order.Id,
            Status = DocumentStatus.Draft
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    private sealed class InspectableFinalIntegrityMigration
        : AddPhase9FinalIntegrityAndNotificationIndexes
    {
        public IReadOnlyList<MigrationOperation> BuildOperations()
        {
            var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.Sqlite");
            base.Up(builder);
            return builder.Operations;
        }
    }
}
