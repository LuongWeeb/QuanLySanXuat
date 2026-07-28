using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.ViewModels;

namespace WmsMes.Tests;

public class QcChecklistControllerTests
{
    [Fact]
    public async Task Create_PersistsChecklistWithMeasurementItems()
    {
        await using var context = CreateContext();
        await SeedProductAsync(context);
        var controller = new QcChecklistController(context);
        var input = new QcChecklistInputModel
        {
            ProductId = 1,
            Name = "  Kiểm tra đầu vào  ",
            Items =
            {
                new QcChecklistItemInputModel
                {
                    ParameterName = "  Độ ẩm  ",
                    MinVal = 10m,
                    MaxVal = 20m,
                    Unit = " % ",
                    IsRequired = true
                }
            }
        };

        var result = await controller.Create(input);

        Assert.IsType<RedirectToActionResult>(result);
        var checklist = await context.QCChecklists
            .Include(item => item.Items)
            .SingleAsync();
        Assert.Equal("Kiểm tra đầu vào", checklist.Name);
        var criterion = Assert.Single(checklist.Items);
        Assert.Equal("Độ ẩm", criterion.ParameterName);
        Assert.Equal("%", criterion.Unit);
    }

    [Fact]
    public async Task Create_WhenMinExceedsMax_ReturnsValidationError()
    {
        await using var context = CreateContext();
        await SeedProductAsync(context);
        var controller = new QcChecklistController(context);
        var input = new QcChecklistInputModel
        {
            ProductId = 1,
            Name = "Kiểm tra",
            Items =
            {
                new QcChecklistItemInputModel
                {
                    ParameterName = "Độ ẩm",
                    MinVal = 20m,
                    MaxVal = 10m
                }
            }
        };

        var result = await controller.Create(input);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Empty(await context.QCChecklists.ToListAsync());
    }

    [Fact]
    public async Task Create_WhenParameterNamesAreDuplicated_ReturnsValidationError()
    {
        await using var context = CreateContext();
        await SeedProductAsync(context);
        var controller = new QcChecklistController(context);
        var input = new QcChecklistInputModel
        {
            ProductId = 1,
            Name = "Kiểm tra",
            Items =
            {
                new QcChecklistItemInputModel { ParameterName = "Độ ẩm" },
                new QcChecklistItemInputModel { ParameterName = " độ ẨM " }
            }
        };

        Assert.IsType<ViewResult>(await controller.Create(input));

        Assert.False(controller.ModelState.IsValid);
        Assert.Empty(await context.QCChecklists.ToListAsync());
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task SeedProductAsync(ApplicationDbContext context)
    {
        context.UnitOfMeasures.Add(new UnitOfMeasure
        {
            Id = 1,
            Code = "KG",
            Name = "Kilogram"
        });
        context.Products.Add(new Product
        {
            Id = 1,
            Code = "RM-01",
            Name = "Nguyên liệu",
            BaseUomId = 1,
            IsActive = true
        });
        await context.SaveChangesAsync();
    }
}
