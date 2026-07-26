using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;

namespace WmsMes.Tests;

public class StockLedgerMigrationTests
{
    [Fact]
    public void AddCycleCountSchemaMigration_ContainsOnlyCycleCountSchema()
    {
        var migration = ReadMigration("AddCycleCountSchema");

        Assert.Contains("name: \"CycleCountOrders\"", migration);
        Assert.Contains("name: \"CycleCountItems\"", migration);
        Assert.Contains("IX_CycleCountItems_CycleCountOrderId", migration);
        Assert.Contains("IX_CycleCountOrders_WarehouseId", migration);
        Assert.DoesNotContain("QtyAfter", migration);
        Assert.DoesNotContain("ValuationRate", migration);
        Assert.DoesNotContain("IsCancelled", migration);
        Assert.DoesNotContain("StockTransactions", migration);
    }

    [Fact]
    public void AddStockLedgerFieldsMigration_ContainsOnlyLedgerColumnsWithIndependentRollback()
    {
        var migration = ReadMigration("AddStockLedgerFields");

        Assert.Contains("name: \"IsCancelled\"", migration);
        Assert.Contains("type: \"bit\"", migration);
        Assert.Contains("defaultValue: false", migration);
        Assert.Contains("name: \"QtyAfter\"", migration);
        Assert.Contains("name: \"ValuationRate\"", migration);
        Assert.Contains("type: \"decimal(18,2)\"", migration);
        Assert.Contains("defaultValue: 0m", migration);
        Assert.DoesNotContain("CreateTable(", migration);
        Assert.DoesNotContain("CycleCount", migration);
        Assert.DoesNotContain("DropTable(", migration);
        Assert.Contains("DropColumn(", migration);
    }

    [Fact]
    public void StockTransactionModel_HasDescendingLedgerPagingIndex()
    {
        using var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=LedgerIndexShape")
                .Options);

        var entity = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(StockTransaction));
        var index = Assert.Single(entity!.GetIndexes().Where(candidate =>
            candidate.Properties.Select(property => property.Name)
                .SequenceEqual(new[]
                {
                    nameof(StockTransaction.TransactionDate),
                    nameof(StockTransaction.Id)
                })));

        Assert.Equal("IX_StockTransactions_TransactionDate_Id", index.GetDatabaseName());
        Assert.Empty(index.IsDescending!);
    }

    [Fact]
    public void AddStockLedgerPagingIndexMigration_ContainsOnlyDescendingCompositeIndex()
    {
        var migration = ReadMigration("AddStockLedgerPagingIndex");

        Assert.Contains("CreateIndex(", migration);
        Assert.Contains("name: \"IX_StockTransactions_TransactionDate_Id\"", migration);
        Assert.Contains("columns: new[] { \"TransactionDate\", \"Id\" }", migration);
        Assert.Contains("descending: new bool[0]", migration);
        Assert.Contains("DropIndex(", migration);
        Assert.DoesNotContain("AddColumn(", migration);
        Assert.DoesNotContain("DropColumn(", migration);
        Assert.DoesNotContain("CreateTable(", migration);
        Assert.DoesNotContain("DropTable(", migration);
    }

    private static string ReadMigration(string migrationName)
    {
        var root = FindRepositoryRoot();
        var migrationPaths = Directory.GetFiles(
            Path.Combine(root, "Data", "Migrations"),
            $"*_{migrationName}.cs",
            SearchOption.TopDirectoryOnly);

        return File.ReadAllText(Assert.Single(migrationPaths));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WmsMes.sln")))
            directory = directory.Parent;

        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
