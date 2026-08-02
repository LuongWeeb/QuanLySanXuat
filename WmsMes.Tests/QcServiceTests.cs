using Microsoft.EntityFrameworkCore;
using Moq;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.Services;

namespace WmsMes.Tests;

public class QcServiceTests
{
    [Theory]
    [InlineData("15", true, QCResult.PASS)]
    [InlineData("25", false, QCResult.REJECT)]
    public async Task EvaluateMinMaxRange_AutoSetsIsOK(
        string inspectedValue,
        bool expectedIsOk,
        QCResult expectedResult)
    {
        await using var context = CreateContext();
        await SeedAsync(context);
        var inspection = CreateInspection(inspectedValue);

        Assert.True(await new QcService(context)
            .SubmitQCInspectionAsync(inspection, "qc-user"));

        var saved = await context.QCInspections
            .Include(item => item.Lines)
            .SingleAsync();
        Assert.Equal(expectedIsOk, saved.Lines.Single().IsOK);
        Assert.Equal(expectedResult, saved.Result);
    }

    [Fact]
    public async Task SubmitInspection_Pass_ReleasesQtyOnHoldToAvailable()
    {
        await using var context = CreateContext();
        await SeedAsync(context, quantityOnHold: 50m);

        Assert.True(await new QcService(context)
            .SubmitQCInspectionAsync(CreateInspection("15"), "qc-user"));

        var balance = await context.StockBalances.SingleAsync(item =>
            item.LotId == 1 && item.LocationId == 1);
        Assert.Equal(0m, balance.QtyOnHold);
        Assert.Equal(50m, balance.QtyAvailable);
        var release = await context.StockTransactions.SingleAsync();
        Assert.Equal(50m, release.Qty);
        Assert.Equal(50m, release.QtyAfter);
        Assert.Equal("QC-PASS-1", release.ReferenceNo);
    }

    [Fact]
    public async Task SubmitInspection_Reject_MovesHeldStockToQuarantine()
    {
        await using var context = CreateContext();
        await SeedAsync(context, quantityOnHold: 50m);

        Assert.True(await new QcService(context)
            .SubmitQCInspectionAsync(CreateInspection("25"), "qc-user"));

        var balances = await context.StockBalances
            .Where(item => item.LotId == 1)
            .ToListAsync();
        Assert.Equal(0m, balances.Single(item => item.LocationId == 1).QtyOnHold);
        Assert.Equal(50m, balances.Single(item => item.LocationId == 2).QtyOnHold);
    }

    [Fact]
    public async Task SubmitInspection_Reject_PublishesDangerNotificationAfterPersistence()
    {
        await using var context = CreateContext();
        await SeedAsync(context, quantityOnHold: 50m);
        var notifications = new Mock<INotificationService>(MockBehavior.Strict);
        notifications.Setup(service => service.SendNotificationAsync(
                "QC REJECT",
                It.Is<string>(message => message.Contains("LOT-001", StringComparison.Ordinal)),
                "Danger",
                "/Dashboard"))
            .Returns(() =>
            {
                Assert.Single(context.QCInspections);
                Assert.Equal(50m, context.StockBalances.Single(item => item.LocationId == 2).QtyOnHold);
                return Task.CompletedTask;
            });
        var service = new QcService(
            context,
            qualityHub: null,
            inventoryHub: null,
            logger: null,
            notificationService: notifications.Object);

        Assert.True(await service.SubmitQCInspectionAsync(CreateInspection("25"), "qc-user"));

        notifications.VerifyAll();
    }

    [Fact]
    public async Task SubmitInspection_WhenSourceDoesNotMatchType_ReturnsFalse()
    {
        await using var context = CreateContext();
        await SeedAsync(context);
        var inspection = CreateInspection("15");
        inspection.Type = QCInspectionType.FinalFGQC;

        var notifications = new Mock<INotificationService>(MockBehavior.Strict);
        Assert.False(await new QcService(
                context,
                qualityHub: null,
                inventoryHub: null,
                logger: null,
                notificationService: notifications.Object)
            .SubmitQCInspectionAsync(inspection, "qc-user"));
        Assert.Empty(await context.QCInspections.ToListAsync());
        notifications.VerifyNoOtherCalls();
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static QCInspection CreateInspection(string value)
    {
        return new QCInspection
        {
            LotId = 1,
            Type = QCInspectionType.InwardQC,
            GoodsReceiptId = 1,
            Lines =
            {
                new QCInspectionLine
                {
                    ParameterName = " Độ ẩm ",
                    ValueInspected = value
                }
            }
        };
    }

    private static async Task SeedAsync(
        ApplicationDbContext context,
        decimal quantityOnHold = 10m)
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
            BaseUomId = 1
        });
        context.Warehouses.Add(new Warehouse
        {
            Id = 1,
            Code = "WH",
            Name = "Kho"
        });
        context.Zones.AddRange(
            new Zone { Id = 1, WarehouseId = 1, Code = "HOLD", Name = "Chờ QC" },
            new Zone { Id = 2, WarehouseId = 1, Code = "QUAR", Name = "Cách ly" });
        context.Locations.AddRange(
            new Location { Id = 1, ZoneId = 1, Code = "IN-QC", Name = "Chờ QC" },
            new Location
            {
                Id = 2,
                ZoneId = 2,
                Code = QcService.QuarantineLocationCode,
                Name = "Cách ly QC"
            });
        context.GoodsReceipts.Add(new GoodsReceipt
        {
            Id = 1,
            ReceiptNo = "GR-001",
            ReceiptDate = DateTime.UtcNow,
            Status = DocumentStatus.Completed
        });
        context.Lots.Add(new Lot
        {
            Id = 1,
            ProductId = 1,
            LotNo = "LOT-001",
            Qty = quantityOnHold,
            UnitPrice = 20m
        });
        context.StockBalances.Add(new StockBalance
        {
            ProductId = 1,
            LotId = 1,
            LocationId = 1,
            QtyOnHold = quantityOnHold
        });
        context.QCChecklists.Add(new QCChecklist
        {
            ProductId = 1,
            Name = "Kiểm tra đầu vào",
            Items =
            {
                new QCChecklistItem
                {
                    ParameterName = "Độ ẩm",
                    MinVal = 10m,
                    MaxVal = 20m,
                    Unit = "%"
                }
            }
        });
        await context.SaveChangesAsync();
    }
}
