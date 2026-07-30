using System.ComponentModel.DataAnnotations;
using System.Reflection;
using WmsMes.Web.Domain.Entities;

namespace WmsMes.Tests;

public class OperationsEnhancementsEntityTests
{
    [Theory]
    [InlineData(typeof(GoodsReceiptLine), nameof(GoodsReceiptLine.VarianceReason))]
    [InlineData(typeof(GoodsIssueLine), nameof(GoodsIssueLine.VarianceReason))]
    [InlineData(typeof(CycleCountItem), nameof(CycleCountItem.ReasonNote))]
    public void ReasonNoteProperties_AreNullableStringsLimitedTo250Characters(Type entityType, string propertyName)
    {
        var property = entityType.GetProperty(propertyName);

        Assert.NotNull(property);
        Assert.Equal(typeof(string), property.PropertyType);
        Assert.Equal(NullabilityState.Nullable, new NullabilityInfoContext().Create(property).WriteState);
        Assert.Equal(250, property.GetCustomAttribute<MaxLengthAttribute>()?.Length);
    }
}
