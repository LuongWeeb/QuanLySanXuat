using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using WmsMes.Web.Data;

namespace WmsMes.Tests;

public class SupplyChainSchemaTests
{
    [Fact]
    public void Phase9_entities_are_mapped_with_required_columns_relationships_and_unique_document_numbers()
    {
        using var context = CreateContext();
        var model = context.Model;

        var pickList = AssertEntity(model, "PickList", "PickLists");
        AssertColumn(pickList, "PickListNo", "nvarchar(50)", nullable: false, maxLength: 50);
        AssertColumn(pickList, "SalesOrderId", "int", nullable: false);
        AssertColumn(pickList, "CreatedAt", "datetime2", nullable: false);
        AssertColumn(pickList, "Status", "int", nullable: false);
        AssertUniqueIndex(pickList, "PickListNo");
        AssertForeignKey(pickList, "SalesOrderId", "SalesOrder", DeleteBehavior.Restrict);

        var pickListItem = AssertEntity(model, "PickListItem", "PickListItems");
        AssertColumn(pickListItem, "QtyToPick", "decimal(18,2)", nullable: false);
        AssertColumn(pickListItem, "PickedQty", "decimal(18,2)", nullable: false);
        AssertForeignKey(pickListItem, "PickListId", "PickList", DeleteBehavior.Cascade);
        AssertForeignKey(pickListItem, "ProductId", "Product", DeleteBehavior.Restrict);
        AssertForeignKey(pickListItem, "LocationId", "Location", DeleteBehavior.Restrict);
        AssertForeignKey(pickListItem, "LotId", "Lot", DeleteBehavior.Restrict);

        var packingSlip = AssertEntity(model, "PackingSlip", "PackingSlips");
        AssertColumn(packingSlip, "PackingNo", "nvarchar(50)", nullable: false, maxLength: 50);
        AssertColumn(packingSlip, "GrossWeight", "decimal(18,2)", nullable: false);
        AssertUniqueIndex(packingSlip, "PackingNo");
        AssertForeignKey(packingSlip, "SalesOrderId", "SalesOrder", DeleteBehavior.Restrict);

        var notification = AssertEntity(model, "AppNotification", "AppNotifications");
        AssertColumn(notification, "Title", "nvarchar(150)", nullable: false, maxLength: 150);
        AssertColumn(notification, "Message", "nvarchar(500)", nullable: false, maxLength: 500);
        AssertColumn(notification, "Severity", "nvarchar(20)", nullable: false, maxLength: 20);
        AssertColumn(notification, "UserId", "nvarchar(450)", nullable: true, maxLength: 450);
        AssertColumn(notification, "ReferenceUrl", "nvarchar(500)", nullable: true, maxLength: 500);
    }

    [Fact]
    public void Phase9_entities_provide_the_specified_initial_values()
    {
        using var context = CreateContext();

        AssertPropertyValue(context, "PickList", "Status", "Draft");
        AssertPropertyValue(context, "PickListItem", "PickedQty", 0m);
        AssertPropertyValue(context, "PickListItem", "SequenceOrder", 1);
        AssertPropertyValue(context, "PackingSlip", "PackageNo", 1);
        AssertPropertyValue(context, "PackingSlip", "GrossWeight", 0m);
        AssertPropertyValue(context, "PackingSlip", "Status", "Draft");
        AssertPropertyValue(context, "AppNotification", "Severity", "Info");
        AssertPropertyValue(context, "AppNotification", "IsRead", false);
    }

    private static ApplicationDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=SupplyChainSchemaTests")
            .Options);

    private static IEntityType AssertEntity(IModel model, string entityName, string tableName)
    {
        var entity = model.FindEntityType($"WmsMes.Web.Domain.Entities.{entityName}");

        Assert.NotNull(entity);
        Assert.Equal(tableName, entity!.GetTableName());
        return entity;
    }

    private static void AssertColumn(
        IEntityType entity,
        string propertyName,
        string columnType,
        bool nullable,
        int? maxLength = null)
    {
        var property = entity.FindProperty(propertyName);

        Assert.NotNull(property);
        Assert.Equal(columnType, property!.GetColumnType());
        Assert.Equal(nullable, property.IsNullable);
        Assert.Equal(maxLength, property.GetMaxLength());
    }

    private static void AssertUniqueIndex(IEntityType entity, string propertyName)
    {
        var index = Assert.Single(entity.GetIndexes().Where(candidate =>
            candidate.Properties.Select(property => property.Name).SequenceEqual(new[] { propertyName })));

        Assert.True(index.IsUnique);
    }

    private static void AssertForeignKey(
        IEntityType entity,
        string propertyName,
        string principalEntityName,
        DeleteBehavior deleteBehavior)
    {
        var foreignKey = Assert.Single(entity.GetForeignKeys().Where(candidate =>
            candidate.Properties.Select(property => property.Name).SequenceEqual(new[] { propertyName })));

        Assert.Equal(
            $"WmsMes.Web.Domain.Entities.{principalEntityName}",
            foreignKey.PrincipalEntityType.Name);
        Assert.Equal(deleteBehavior, foreignKey.DeleteBehavior);
    }

    private static void AssertPropertyValue(
        ApplicationDbContext context,
        string entityName,
        string propertyName,
        object expected)
    {
        var type = AssertEntity(context.Model, entityName, $"{entityName}s").ClrType;
        var instance = Activator.CreateInstance(type);
        var property = type.GetProperty(propertyName);

        Assert.NotNull(property);
        var actual = property!.GetValue(instance);
        Assert.Equal(expected, actual is Enum ? actual.ToString() : actual);
    }
}
