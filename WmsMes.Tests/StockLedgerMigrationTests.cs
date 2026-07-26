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
