using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.Services;
using WmsMes.Web.ViewModels;

namespace WmsMes.Tests;

public class QcControllerTests
{
    private static DbContextOptions<ApplicationDbContext> Options() => new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase($"QC_{Guid.NewGuid()}").Options;

    [Fact]
    public async Task Index_ReturnsOnlyLotsWithOnHoldStockAndLoadsProduct()
    {
        await using var db = new ApplicationDbContext(Options());
        var pending = Lot("PENDING"); var available = Lot("AVAILABLE");
        db.StockBalances.AddRange(Balance(pending, 4), Balance(available, 0)); await db.SaveChangesAsync();
        var view = Assert.IsType<ViewResult>(await Controller(db).Index());
        var lots = Assert.IsAssignableFrom<IEnumerable<Lot>>(view.Model).ToList();
        Assert.Equal("PENDING", Assert.Single(lots).LotNo); Assert.NotNull(lots[0].Product);
    }

    [Fact]
    public async Task Inspect_LoadsEligibleLotAndLatestActiveChecklistItems()
    {
        await using var db = new ApplicationDbContext(Options()); var lot = Lot("L1", true);
        db.StockBalances.Add(Balance(lot, 2));
        db.QCChecklists.AddRange(Checklist(lot.Product!, "Old", false, "Old"), Checklist(lot.Product!, "Active", true, "Moisture")); await db.SaveChangesAsync();
        var model = Assert.IsType<QcInspectionInputModel>(Assert.IsType<ViewResult>(await Controller(db).Inspect(lot.Id)).Model);
        Assert.Equal(lot.Id, model.LotId); Assert.Equal("Active", model.ChecklistName); Assert.Equal("Moisture", Assert.Single(model.Measurements).ParameterName);
    }

    [Fact]
    public async Task Inspect_ReturnsNotFoundForMissingAndIneligibleLots()
    {
        await using var db = new ApplicationDbContext(Options()); var lot = Lot("L1", true); db.Lots.Add(lot); await db.SaveChangesAsync();
        Assert.IsType<NotFoundResult>(await Controller(db).Inspect(404));
        Assert.IsType<NotFoundResult>(await Controller(db).Inspect(lot.Id));
    }

    [Fact]
    public async Task Submit_ValidInputBuildsServerOwnedInspectionAndUsesAuthenticatedUser()
    {
        await using var db = new ApplicationDbContext(Options()); var lot = Lot("L1", true); db.StockBalances.Add(Balance(lot, 2)); var list = Checklist(lot.Product!, "Active", true, "Moisture"); db.QCChecklists.Add(list); await db.SaveChangesAsync();
        QCInspection? captured = null; var service = new Mock<IQcService>(); service.Setup(x => x.SubmitQCInspectionAsync(It.IsAny<QCInspection>(), "qc-1")).Callback<QCInspection,string>((x,_) => captured=x).ReturnsAsync(true);
        var input = new QcInspectionInputModel { LotId=lot.Id, ChecklistId=list.Id, Result=QCResult.PASS, Note=" ok ", Measurements=[new() { ChecklistItemId=list.Items.Single().Id, Value="12.5" }] };
        var controller = Controller(db, service.Object, "qc-1"); var redirect = Assert.IsType<RedirectToActionResult>(await controller.Inspect(input));
        Assert.Equal(nameof(QcController.Index), redirect.ActionName); service.VerifyAll(); Assert.NotNull(captured); Assert.Equal(lot.WorkOrderId, captured.WorkOrderId); Assert.Equal("Moisture", Assert.Single(captured.Lines).ParameterName); Assert.Equal("12.5", captured.Lines.Single().ValueInspected); Assert.Equal("ok", captured.Note); Assert.Equal(0, captured.Id);
    }

    [Fact]
    public async Task Submit_InwardLotBuildsInspectionLinkedToGoodsReceipt()
    {
        await using var db = new ApplicationDbContext(Options());
        var lot = Lot("RAW");
        var balance = Balance(lot, 2);
        db.StockBalances.Add(balance);
        var checklist = Checklist(lot.Product!, "Inward", true, "Moisture");
        db.QCChecklists.Add(checklist);
        var receipt = new GoodsReceipt
        {
            ReceiptNo = "GR-001",
            Status = DocumentStatus.Completed,
            Lines =
            {
                new GoodsReceiptLine
                {
                    Product = lot.Product,
                    LotNo = lot.LotNo,
                    Qty = 2,
                    Location = balance.Location
                }
            }
        };
        db.GoodsReceipts.Add(receipt);
        await db.SaveChangesAsync();
        QCInspection? captured = null;
        var service = new Mock<IQcService>();
        service.Setup(item => item.SubmitQCInspectionAsync(
                It.IsAny<QCInspection>(), "qc"))
            .Callback<QCInspection, string>((item, _) => captured = item)
            .ReturnsAsync(true);
        var input = new QcInspectionInputModel
        {
            LotId = lot.Id,
            ChecklistId = checklist.Id,
            Result = QCResult.PASS,
            Measurements =
            {
                new QcMeasurementInputModel
                {
                    ChecklistItemId = checklist.Items.Single().Id,
                    Value = "12"
                }
            }
        };

        await Controller(db, service.Object, "qc").Inspect(input);

        Assert.NotNull(captured);
        Assert.Equal(QCInspectionType.InwardQC, captured.Type);
        Assert.Equal(receipt.Id, captured.GoodsReceiptId);
        Assert.Null(captured.WorkOrderId);
    }

    [Theory]
    [InlineData(2, "12")]
    [InlineData(0, "")]
    public async Task Submit_RejectsInvalidResultOrRequiredMeasurement(int result, string value)
    {
        await using var db = new ApplicationDbContext(Options()); var lot=Lot("L",true); db.StockBalances.Add(Balance(lot,1)); var list=Checklist(lot.Product!,"A",true,"M"); db.QCChecklists.Add(list); await db.SaveChangesAsync();
        var service=new Mock<IQcService>(); var controller=Controller(db,service.Object);
        var view=Assert.IsType<ViewResult>(await controller.Inspect(new QcInspectionInputModel { LotId=lot.Id, ChecklistId=list.Id, Result=(QCResult)result, Measurements=[new(){ChecklistItemId=list.Items.Single().Id,Value=value}] }));
        Assert.False(controller.ModelState.IsValid); Assert.IsType<QcInspectionInputModel>(view.Model); service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Submit_RejectsForgedChecklistItem()
    {
        await using var db=new ApplicationDbContext(Options()); var lot=Lot("L",true); db.StockBalances.Add(Balance(lot,1)); var list=Checklist(lot.Product!,"A",true,"M"); db.QCChecklists.Add(list); await db.SaveChangesAsync();
        var service=new Mock<IQcService>(); var controller=Controller(db,service.Object);
        await controller.Inspect(new QcInspectionInputModel {LotId=lot.Id,ChecklistId=list.Id,Result=QCResult.PASS,Measurements=[new(){ChecklistItemId=999,Value="12"}]});
        Assert.False(controller.ModelState.IsValid); service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Submit_RejectsNonNumericValueForMeasuredChecklistItem()
    {
        await using var db=new ApplicationDbContext(Options()); var lot=Lot("L",true); db.StockBalances.Add(Balance(lot,1)); var list=Checklist(lot.Product!,"A",true,"M"); db.QCChecklists.Add(list); await db.SaveChangesAsync();
        var service=new Mock<IQcService>(); var controller=Controller(db,service.Object);
        await controller.Inspect(new QcInspectionInputModel {LotId=lot.Id,ChecklistId=list.Id,Result=QCResult.PASS,Measurements=[new(){ChecklistItemId=list.Items.Single().Id,Value="not-a-number"}]});
        Assert.False(controller.ModelState.IsValid); service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Submit_OmitsBlankOptionalChecklistItemFromServiceLines()
    {
        await using var db=new ApplicationDbContext(Options()); var lot=Lot("L",true); db.StockBalances.Add(Balance(lot,1)); var list=Checklist(lot.Product!,"A",true,"Required"); list.Items.Add(new QCChecklistItem{ParameterName="Optional",IsRequired=false}); db.QCChecklists.Add(list); await db.SaveChangesAsync();
        QCInspection? captured=null; var service=new Mock<IQcService>(); service.Setup(x=>x.SubmitQCInspectionAsync(It.IsAny<QCInspection>(),"qc")).Callback<QCInspection,string>((x,_)=>captured=x).ReturnsAsync(true);
        await Controller(db,service.Object,"qc").Inspect(new QcInspectionInputModel{LotId=lot.Id,ChecklistId=list.Id,Result=QCResult.PASS,Measurements=[new(){ChecklistItemId=list.Items.First().Id,Value="12"},new(){ChecklistItemId=list.Items.Last().Id,Value=" "}]});
        Assert.Equal("Required",Assert.Single(captured!.Lines).ParameterName);
    }

    [Fact]
    public async Task Submit_WithoutAuthenticatedUserRejectsAndNeverCallsService()
    {
        await using var db=new ApplicationDbContext(Options()); var lot=Lot("L",true); db.StockBalances.Add(Balance(lot,1)); var list=Checklist(lot.Product!,"A",true,"M"); db.QCChecklists.Add(list); await db.SaveChangesAsync();
        var service=new Mock<IQcService>(); var controller=Controller(db,service.Object);
        var result=Assert.IsType<RedirectToActionResult>(await controller.Inspect(new QcInspectionInputModel{LotId=lot.Id,ChecklistId=list.Id,Result=QCResult.PASS,Measurements=[new(){ChecklistItemId=list.Items.Single().Id,Value="12"}]}));
        Assert.Equal(nameof(QcController.Inspect),result.ActionName); service.VerifyNoOtherCalls(); Assert.Contains("danh tính",controller.TempData["StatusMessage"]!.ToString(),StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InspectView_RendersAllModelStateErrors()
    {
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","..","Views","Qc","Inspect.cshtml"));
        Assert.Contains("asp-validation-summary=\"All\"",File.ReadAllText(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Submit_FailureOrExceptionReturnsPrgWithSafeMessage(bool throws)
    {
        await using var db=new ApplicationDbContext(Options()); var lot=Lot("L",true); db.StockBalances.Add(Balance(lot,1)); var list=Checklist(lot.Product!,"A",true,"M"); db.QCChecklists.Add(list); await db.SaveChangesAsync();
        var service=new Mock<IQcService>(); var setup=service.Setup(x=>x.SubmitQCInspectionAsync(It.IsAny<QCInspection>(),It.IsAny<string>())); if(throws) setup.ThrowsAsync(new Exception("secret")); else setup.ReturnsAsync(false);
        var controller=Controller(db,service.Object); var result=Assert.IsType<RedirectToActionResult>(await controller.Inspect(new QcInspectionInputModel {LotId=lot.Id,ChecklistId=list.Id,Result=QCResult.PASS,Measurements=[new(){ChecklistItemId=list.Items.Single().Id,Value="12"}]}));
        Assert.Equal(nameof(QcController.Inspect),result.ActionName); Assert.DoesNotContain("secret",controller.TempData["StatusMessage"]!.ToString());
    }

    [Fact]
    public void ControllerAndPostHaveRequiredSecurityAndDedicatedInputModel()
    {
        Assert.Equal("Admin,QC,Manager", Assert.Single(typeof(QcController).GetCustomAttributes<AuthorizeAttribute>()).Roles);
        var method=typeof(QcController).GetMethod(nameof(QcController.Inspect),new[]{typeof(QcInspectionInputModel)})!;
        Assert.Single(method.GetCustomAttributes<HttpPostAttribute>()); Assert.Single(method.GetCustomAttributes<ValidateAntiForgeryTokenAttribute>());
        Assert.Equal(typeof(QcInspectionInputModel),method.GetParameters().Single().ParameterType);
        Assert.DoesNotContain(typeof(QcInspectionInputModel).GetProperties(),x=>x.Name is "InspectorId" or "WorkOrderId" or "InspectionTime" or "Lines");
    }

    private static QcController Controller(ApplicationDbContext db, IQcService? service=null, string? user=null) { var c=new QcController(db,service??Mock.Of<IQcService>(),Mock.Of<ILogger<QcController>>()){ControllerContext=new ControllerContext{HttpContext=new DefaultHttpContext()}}; c.TempData=new TempDataDictionary(c.HttpContext,Mock.Of<ITempDataProvider>()); if(user!=null)c.HttpContext.User=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,user)],"test")); return c; }
    private static Lot Lot(string no,bool work=false){var p=new Product{Code=$"P-{no}",Name=no,IsActive=true,IsManufactured=true}; return new Lot{LotNo=no,Product=p,Qty=10,WorkOrder=work?new WorkOrder{Code=$"WO-{no}",Product=p,Qty=10,DueDate=DateTime.Today,BomVersion="B",RoutingVersion="R"}:null};}
    private static StockBalance Balance(Lot lot,decimal hold)=>new(){Lot=lot,Product=lot.Product,Location=new Location{Code=$"L-{lot.LotNo}",Name="Loc",Zone=new Zone{Code=$"Z-{lot.LotNo}",Name="Zone"}},QtyOnHold=hold};
    private static QCChecklist Checklist(Product p,string name,bool active,string parameter){var c=new QCChecklist{Product=p,Name=name,IsActive=active};c.Items.Add(new QCChecklistItem{ParameterName=parameter,MinVal=1,MaxVal=20,Unit="%",IsRequired=true});return c;}
}
