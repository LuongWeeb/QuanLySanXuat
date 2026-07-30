using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using WmsMes.Web.Data.Migrations;

namespace WmsMes.Tests;

public class WorkOrderLotDateMigrationTests
{
    [Fact]
    public void Up_ConvertsOnlyWorkOrderLotUtcTimestampsToSaigonCalendarDates()
    {
        var migration = new NormalizeHistoricalWorkOrderLotDates();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");

        typeof(NormalizeHistoricalWorkOrderLotDates)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, new object[] { builder });

        var sql = Assert.Single(builder.Operations.OfType<SqlOperation>());
        Assert.Contains("UPDATE [Lots]", sql.Sql, StringComparison.Ordinal);
        Assert.Contains(
            "[ManufactureDate] AT TIME ZONE 'UTC' AT TIME ZONE 'SE Asia Standard Time'",
            sql.Sql,
            StringComparison.Ordinal);
        Assert.Contains("CONVERT(date", sql.Sql, StringComparison.Ordinal);
        Assert.Contains("[WorkOrderId] IS NOT NULL", sql.Sql, StringComparison.Ordinal);
        Assert.Contains("[ManufactureDate] IS NOT NULL", sql.Sql, StringComparison.Ordinal);
        Assert.Contains("<>", sql.Sql, StringComparison.Ordinal);
        Assert.False(sql.SuppressTransaction);
    }

    [Fact]
    public void Migration_FollowsOeeIndexUpgradeAndDocumentsIrreversibleDown()
    {
        var migrationId = typeof(NormalizeHistoricalWorkOrderLotDates)
            .GetCustomAttribute<MigrationAttribute>()!
            .Id;
        var previousMigrationId = typeof(AddOeeReportingIndex)
            .GetCustomAttribute<MigrationAttribute>()!
            .Id;
        var source = File.ReadAllText(Assert.Single(Directory.GetFiles(
            Path.Combine(FindRepositoryRoot(), "Data", "Migrations"),
            "*_NormalizeHistoricalWorkOrderLotDates.cs",
            SearchOption.TopDirectoryOnly)));

        Assert.True(
            string.CompareOrdinal(migrationId, previousMigrationId) > 0,
            $"{migrationId} must sort after {previousMigrationId}.");
        Assert.Contains("irreversible", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Down is intentionally a no-op", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WmsMes.sln")))
            directory = directory.Parent;

        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
