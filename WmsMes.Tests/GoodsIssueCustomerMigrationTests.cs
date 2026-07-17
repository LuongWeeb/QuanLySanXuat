using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using WmsMes.Web.Data.Migrations;

namespace WmsMes.Tests;

public class GoodsIssueCustomerMigrationTests
{
    [Fact]
    public void Up_BackfillsLegacyIssuesWithCollisionSafeReservedCustomerBeforeRequiredForeignKey()
    {
        var migration = new AddGoodsIssueCustomer();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(AddGoodsIssueCustomer)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, new object[] { builder });

        var add = Assert.Single(builder.Operations.OfType<AddColumnOperation>());
        Assert.True(add.IsNullable);
        Assert.Null(add.DefaultValue);

        var guardSql = string.Join("\n", builder.Operations.OfType<SqlOperation>().Select(operation => operation.Sql));
        Assert.Contains("LEGACY-UNASSIGNED", guardSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NOT EXISTS", guardSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UPDATE [GoodsIssues]", guardSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE [CustomerId] IS NULL", guardSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("THROW", guardSql, StringComparison.OrdinalIgnoreCase);

        var alter = Assert.Single(builder.Operations.OfType<AlterColumnOperation>());
        Assert.False(alter.IsNullable);
        Assert.Null(alter.DefaultValue);
    }
}
