using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using System.Text.RegularExpressions;
using WmsMes.Web.Domain.Entities;

namespace WmsMes.Tests;

public class CostingEntityMigrationTests
{
    [Theory]
    [InlineData(typeof(Product), "StandardCost")]
    [InlineData(typeof(WorkCenter), "HourlyLaborRate")]
    [InlineData(typeof(WorkCenter), "HourlyMachineRate")]
    [InlineData(typeof(BOM), "TotalMaterialCost")]
    [InlineData(typeof(BOM), "TotalOperationCost")]
    [InlineData(typeof(BOM), "TotalStandardCost")]
    public void Costing_properties_are_decimal_currency_fields_with_zero_defaults(
        Type entityType,
        string propertyName)
    {
        var property = entityType.GetProperty(propertyName);

        Assert.NotNull(property);
        Assert.Equal(typeof(decimal), property!.PropertyType);
        Assert.Equal(0m, property.GetValue(Activator.CreateInstance(entityType)));
        Assert.Equal(
            "decimal(18,2)",
            property.GetCustomAttribute<ColumnAttribute>()?.TypeName);
    }

    [Theory]
    [InlineData("TargetMaterialCost")]
    [InlineData("TargetLaborCost")]
    [InlineData("TargetMachineCost")]
    [InlineData("ActualMaterialCost")]
    [InlineData("ActualLaborCost")]
    [InlineData("ActualMachineCost")]
    public void Work_order_cost_snapshots_are_nullable_decimal_currency_fields(
        string propertyName)
    {
        var property = typeof(WorkOrder).GetProperty(propertyName);

        Assert.NotNull(property);
        Assert.Equal(typeof(decimal?), property!.PropertyType);
        Assert.Null(property.GetValue(new WorkOrder()));
        Assert.Equal(
            "decimal(18,2)",
            property.GetCustomAttribute<ColumnAttribute>()?.TypeName);
    }

    [Fact]
    public void AddCostingFields_migration_contains_exactly_six_decimal_cost_columns()
    {
        var migrationPath = Assert.Single(Directory.GetFiles(
            Path.Combine(FindRepositoryRoot(), "Data", "Migrations"),
            "*_AddCostingFields.cs",
            SearchOption.TopDirectoryOnly));
        var migration = File.ReadAllText(migrationPath);

        Assert.Equal(6, Regex.Matches(migration, "migrationBuilder\\.AddColumn<decimal>\\(").Count);
        Assert.Contains("name: \"StandardCost\"", migration);
        Assert.Contains("name: \"HourlyLaborRate\"", migration);
        Assert.Contains("name: \"HourlyMachineRate\"", migration);
        Assert.Contains("name: \"TotalMaterialCost\"", migration);
        Assert.Contains("name: \"TotalOperationCost\"", migration);
        Assert.Contains("name: \"TotalStandardCost\"", migration);
        Assert.Contains("type: \"decimal(18,2)\"", migration);
        Assert.Contains("defaultValue: 0m", migration);
        Assert.DoesNotContain("CreateTable(", migration);
        Assert.DoesNotContain("DropTable(", migration);
    }

    [Fact]
    public void AddWorkOrderCostSnapshots_migration_contains_six_nullable_currency_columns()
    {
        var migrationPath = Assert.Single(Directory.GetFiles(
            Path.Combine(FindRepositoryRoot(), "Data", "Migrations"),
            "*_AddWorkOrderCostSnapshots.cs",
            SearchOption.TopDirectoryOnly));
        var migration = File.ReadAllText(migrationPath);

        Assert.Equal(
            6,
            Regex.Matches(migration, "migrationBuilder\\.AddColumn<decimal>\\(").Count);
        Assert.Equal(6, Regex.Matches(migration, "nullable: true").Count);
        Assert.Contains("name: \"TargetMaterialCost\"", migration);
        Assert.Contains("name: \"TargetLaborCost\"", migration);
        Assert.Contains("name: \"TargetMachineCost\"", migration);
        Assert.Contains("name: \"ActualMaterialCost\"", migration);
        Assert.Contains("name: \"ActualLaborCost\"", migration);
        Assert.Contains("name: \"ActualMachineCost\"", migration);
        Assert.Contains("type: \"decimal(18,2)\"", migration);
        Assert.DoesNotContain("defaultValue:", migration);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WmsMes.sln")))
            directory = directory.Parent;

        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
