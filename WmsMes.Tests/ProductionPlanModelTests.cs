using System.Reflection;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using Xunit;

namespace WmsMes.Tests;

public class ProductionPlanModelTests
{
    [Theory]
    [InlineData("WmsMes.Web.Domain.Entities.ProductionPlan")]
    [InlineData("WmsMes.Web.Domain.Entities.ProductionPlanItem")]
    public void ProductionPlanningEntity_IsPartOfTheDomainModel(string typeName)
    {
        var entityType = typeof(ApplicationDbContext).Assembly.GetType(typeName);

        Assert.NotNull(entityType);
    }

    [Theory]
    [InlineData("ProductionPlans")]
    [InlineData("ProductionPlanItems")]
    public void ApplicationDbContext_ExposesProductionPlanningSet(string propertyName)
    {
        var property = typeof(ApplicationDbContext).GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(property);
        Assert.True(property!.PropertyType.IsGenericType);
        Assert.Equal(typeof(DbSet<>), property.PropertyType.GetGenericTypeDefinition());
    }
}
