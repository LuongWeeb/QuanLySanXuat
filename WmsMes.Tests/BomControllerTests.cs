using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.ViewModels;

namespace WmsMes.Tests;

public class BomControllerTests
{
    private static DbContextOptions<ApplicationDbContext> Options(string name) =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

    [Fact]
    public void Controller_RequiresProductionManagementRoles()
    {
        var authorize = Assert.Single(typeof(BomController).GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal("Admin,Planner,Manager", authorize.Roles);
    }

    [Fact]
    public async Task Index_ReturnsBomsInPredictableProductAndEffectiveDateOrder()
    {
        await using var context = new ApplicationDbContext(Options($"BOM_Index_{Guid.NewGuid()}"));
        var productA = Product("A-FG", ProductType.FinishedGood);
        var productB = Product("B-FG", ProductType.FinishedGood);
        context.BOMs.AddRange(
            Bom(productB, "V1", new DateTime(2026, 7, 1)),
            Bom(productA, "V1", new DateTime(2026, 6, 1)),
            Bom(productA, "V2", new DateTime(2026, 7, 1)));
        await context.SaveChangesAsync();

        var result = await new BomController(context).Index();

        var model = Assert.IsAssignableFrom<IEnumerable<BOM>>(Assert.IsType<ViewResult>(result).Model).ToList();
        Assert.Equal(new[] { "A-FG:V2", "A-FG:V1", "B-FG:V1" }, model.Select(x => $"{x.Product!.Code}:{x.Version}"));
    }

    [Fact]
    public async Task Details_WhenMissing_ReturnsNotFound()
    {
        await using var context = new ApplicationDbContext(Options($"BOM_Missing_{Guid.NewGuid()}"));

        var result = await new BomController(context).Details(404);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Details_ReturnsBomWithParentAndComponents()
    {
        await using var context = new ApplicationDbContext(Options($"BOM_Details_{Guid.NewGuid()}"));
        var parent = Product("FG", ProductType.FinishedGood);
        var component = Product("RM", ProductType.RawMaterial, manufactured: false);
        var bom = Bom(parent, "V1", new DateTime(2026, 7, 1));
        bom.Items.Add(new BOMItem { ComponentProduct = component, QtyPer = 2.5m, ScrapPercent = 1m });
        context.BOMs.Add(bom);
        await context.SaveChangesAsync();

        var result = await new BomController(context).Details(bom.Id);

        var model = Assert.IsType<BOM>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("FG", model.Product!.Code);
        Assert.Equal("RM", Assert.Single(model.Items).ComponentProduct!.Code);
    }

    [Fact]
    public async Task CreateGet_LoadsEligibleParentsAndActiveComponentsWithOneBlankItem()
    {
        await using var context = new ApplicationDbContext(Options($"BOM_CreateGet_{Guid.NewGuid()}"));
        context.Products.AddRange(
            Product("FG", ProductType.FinishedGood),
            Product("WIP", ProductType.WIP),
            Product("NOT-MADE", ProductType.FinishedGood, manufactured: false),
            Product("RM", ProductType.RawMaterial, manufactured: false),
            Product("INACTIVE", ProductType.RawMaterial, manufactured: false, active: false));
        await context.SaveChangesAsync();

        var result = Assert.IsType<ViewResult>(await new BomController(context).Create());

        var model = Assert.IsType<BomCreateInputModel>(result.Model);
        Assert.Single(model.Items);
        var parents = Assert.IsAssignableFrom<IEnumerable<Product>>(result.ViewData["ParentProducts"]);
        Assert.Equal(new[] { "FG", "WIP" }, parents.Select(x => x.Code));
        var components = Assert.IsAssignableFrom<IEnumerable<Product>>(result.ViewData["ComponentProducts"]);
        Assert.Equal(new[] { "FG", "NOT-MADE", "RM", "WIP" }, components.Select(x => x.Code));
    }

    [Fact]
    public void CreateInputModels_ExposeOnlyIntendedBindableFields()
    {
        Assert.Equal(
            new[] { "EffectiveDate", "Items", "ProductId", "Version" },
            typeof(BomCreateInputModel).GetProperties().Select(x => x.Name).OrderBy(x => x));
        Assert.Equal(
            new[] { "ComponentProductId", "QtyPer", "ScrapPercent" },
            typeof(BomItemInputModel).GetProperties().Select(x => x.Name).OrderBy(x => x));

        var invalid = new BomCreateInputModel();
        var validationResults = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(
            invalid,
            new ValidationContext(invalid),
            validationResults,
            validateAllProperties: true));
        Assert.Contains(validationResults, x => x.MemberNames.Contains(nameof(BomCreateInputModel.ProductId)));
        Assert.Contains(validationResults, x => x.MemberNames.Contains(nameof(BomCreateInputModel.Version)));
        Assert.Contains(validationResults, x => x.MemberNames.Contains(nameof(BomCreateInputModel.EffectiveDate)));
    }

    [Fact]
    public void CreatePost_UsesNarrowModelAndRequiresPostWithAntiforgery()
    {
        var method = typeof(BomController).GetMethod(
            nameof(BomController.Create),
            [typeof(BomCreateInputModel)]);

        Assert.NotNull(method);
        Assert.Single(method!.GetCustomAttributes<HttpPostAttribute>());
        Assert.Single(method.GetCustomAttributes<ValidateAntiForgeryTokenAttribute>());
        Assert.Equal(typeof(BomCreateInputModel), Assert.Single(method.GetParameters()).ParameterType);
    }

    [Fact]
    public async Task CreatePost_WithInvalidFields_UsesIndexedKeysAndRefreshesChoices()
    {
        await using var context = new ApplicationDbContext(Options($"BOM_Invalid_{Guid.NewGuid()}"));
        var ineligibleParent = Product("PURCHASED", ProductType.FinishedGood, manufactured: false);
        var inactiveComponent = Product("INACTIVE", ProductType.RawMaterial, manufactured: false, active: false);
        context.Products.AddRange(ineligibleParent, inactiveComponent);
        await context.SaveChangesAsync();
        var controller = Controller(context);
        var input = new BomCreateInputModel
        {
            ProductId = ineligibleParent.Id,
            Version = " ",
            EffectiveDate = null,
            Items =
            [
                new BomItemInputModel
                {
                    ComponentProductId = inactiveComponent.Id,
                    QtyPer = 0,
                    ScrapPercent = 101
                }
            ]
        };

        var result = Assert.IsType<ViewResult>(await controller.Create(input));

        Assert.Same(input, result.Model);
        Assert.True(controller.ModelState.ContainsKey(nameof(BomCreateInputModel.ProductId)));
        Assert.True(controller.ModelState.ContainsKey(nameof(BomCreateInputModel.Version)));
        Assert.True(controller.ModelState.ContainsKey(nameof(BomCreateInputModel.EffectiveDate)));
        Assert.True(controller.ModelState.ContainsKey("Items[0].ComponentProductId"));
        Assert.True(controller.ModelState.ContainsKey("Items[0].QtyPer"));
        Assert.True(controller.ModelState.ContainsKey("Items[0].ScrapPercent"));
        Assert.NotNull(result.ViewData["ParentProducts"]);
        Assert.NotNull(result.ViewData["ComponentProducts"]);
        Assert.Empty(context.BOMs);
    }

    [Fact]
    public async Task CreatePost_WithoutItems_ReturnsOneBlankItemAndDoesNotPersist()
    {
        await using var context = new ApplicationDbContext(Options($"BOM_NoItems_{Guid.NewGuid()}"));
        var parent = Product("FG", ProductType.FinishedGood);
        context.Products.Add(parent);
        await context.SaveChangesAsync();
        var controller = Controller(context);
        var input = new BomCreateInputModel
        {
            ProductId = parent.Id,
            Version = "V1",
            EffectiveDate = new DateTime(2026, 7, 24),
            Items = []
        };

        var result = Assert.IsType<ViewResult>(await controller.Create(input));

        Assert.True(controller.ModelState.ContainsKey(nameof(BomCreateInputModel.Items)));
        Assert.Single(Assert.IsType<BomCreateInputModel>(result.Model).Items);
        Assert.Empty(context.BOMs);
    }

    [Fact]
    public async Task CreatePost_RejectsParentAsComponentWithIndexedError()
    {
        await using var context = new ApplicationDbContext(
            Options($"BOM_SelfReference_{Guid.NewGuid()}"));
        var parent = Product("FG", ProductType.FinishedGood);
        context.Products.Add(parent);
        await context.SaveChangesAsync();
        var controller = Controller(context);

        var result = await controller.Create(new BomCreateInputModel
        {
            ProductId = parent.Id,
            Version = "V1",
            EffectiveDate = new DateTime(2026, 7, 24),
            Items =
            [
                new BomItemInputModel
                {
                    ComponentProductId = parent.Id,
                    QtyPer = 1,
                    ScrapPercent = 0
                }
            ]
        });

        Assert.IsType<ViewResult>(result);
        var error = Assert.Single(
            controller.ModelState["Items[0].ComponentProductId"]!.Errors);
        Assert.Contains("chính nó", error.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(context.BOMs);
    }

    [Fact]
    public async Task CreatePost_RejectsDuplicateProductVersion()
    {
        await using var context = new ApplicationDbContext(
            Options($"BOM_DuplicateVersion_{Guid.NewGuid()}"));
        var parent = Product("FG", ProductType.FinishedGood);
        var component = Product(
            "RM",
            ProductType.RawMaterial,
            manufactured: false);
        context.BOMs.Add(Bom(parent, "V1", new DateTime(2026, 7, 1)));
        context.Products.Add(component);
        await context.SaveChangesAsync();
        var controller = Controller(context);

        var result = await controller.Create(new BomCreateInputModel
        {
            ProductId = parent.Id,
            Version = " V1 ",
            EffectiveDate = new DateTime(2026, 8, 1),
            Items =
            [
                new BomItemInputModel
                {
                    ComponentProductId = component.Id,
                    QtyPer = 1,
                    ScrapPercent = 0
                }
            ]
        });

        Assert.IsType<ViewResult>(result);
        var error = Assert.Single(
            controller.ModelState[nameof(BomCreateInputModel.Version)]!.Errors);
        Assert.Contains("đã tồn tại", error.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Single(context.BOMs);
    }

    [Fact]
    public async Task CreatePost_WithValidInput_PersistsInactiveBomAndAllItemsInSqlite()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var parent = Product("FG", ProductType.FinishedGood);
        var componentA = Product("RM-A", ProductType.RawMaterial, manufactured: false);
        var componentB = Product("RM-B", ProductType.RawMaterial, manufactured: false);
        var uom = new UnitOfMeasure { Code = "EA", Name = "Each" };
        parent.BaseUom = componentA.BaseUom = componentB.BaseUom = uom;
        context.Products.AddRange(parent, componentA, componentB);
        await context.SaveChangesAsync();
        var controller = Controller(context);

        var result = await controller.Create(new BomCreateInputModel
        {
            ProductId = parent.Id,
            Version = " V2 ",
            EffectiveDate = new DateTime(2026, 8, 1),
            Items =
            [
                new BomItemInputModel { ComponentProductId = componentA.Id, QtyPer = 2.5m, ScrapPercent = 1.25m },
                new BomItemInputModel { ComponentProductId = componentB.Id, QtyPer = 1m, ScrapPercent = 0m }
            ]
        });

        Assert.Equal(nameof(BomController.Index), Assert.IsType<RedirectToActionResult>(result).ActionName);
        context.ChangeTracker.Clear();
        var saved = await context.BOMs.Include(x => x.Items).SingleAsync();
        Assert.Equal("V2", saved.Version);
        Assert.False(saved.IsActive);
        Assert.Equal(new[] { 1m, 2.5m }, saved.Items.Select(x => x.QtyPer).OrderBy(x => x));
        Assert.Contains("chưa kích hoạt", controller.TempData["StatusMessage"]!.ToString());
    }

    [Fact]
    public async Task CreatePost_WhenItemInsertFails_RollsBackRelationalBomInsert()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        int parentId;
        int componentId;
        await using (var setup = new ApplicationDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            var uom = new UnitOfMeasure { Code = "EA", Name = "Each" };
            var parent = Product("FG", ProductType.FinishedGood);
            var component = Product("RM", ProductType.RawMaterial, manufactured: false);
            parent.BaseUom = component.BaseUom = uom;
            setup.Products.AddRange(parent, component);
            await setup.SaveChangesAsync();
            parentId = parent.Id;
            componentId = component.Id;
            await setup.Database.ExecuteSqlRawAsync(
                """CREATE TRIGGER reject_bom_item BEFORE INSERT ON BOMItems BEGIN SELECT RAISE(ABORT, 'reject'); END;""");
        }

        await using (var failing = new ApplicationDbContext(options))
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => Controller(failing).Create(new BomCreateInputModel
            {
                ProductId = parentId,
                Version = "V1",
                EffectiveDate = new DateTime(2026, 8, 1),
                Items = [new BomItemInputModel { ComponentProductId = componentId, QtyPer = 1, ScrapPercent = 0 }]
            }));
        }

        await using var verification = new ApplicationDbContext(options);
        Assert.Empty(await verification.BOMs.ToListAsync());
        Assert.Empty(await verification.BOMItems.ToListAsync());
    }

    [Fact]
    public void ToggleActive_RequiresPostWithAntiforgery()
    {
        var method = typeof(BomController).GetMethod(nameof(BomController.ToggleActive));

        Assert.NotNull(method);
        Assert.Single(method!.GetCustomAttributes<HttpPostAttribute>());
        Assert.Single(method.GetCustomAttributes<ValidateAntiForgeryTokenAttribute>());
    }

    [Fact]
    public void ToggleActive_StartsSerializableTransactionBeforeReadingTargetState()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Controllers",
            "BomController.cs"));
        var actionStart = source.IndexOf(
            "public async Task<IActionResult> ToggleActive(int id)",
            StringComparison.Ordinal);
        var transactionStart = source.IndexOf(
            "BeginTransactionAsync(IsolationLevel.Serializable)",
            actionStart,
            StringComparison.Ordinal);
        var targetRead = source.IndexOf(
            ".SingleOrDefaultAsync(x => x.Id == id)",
            actionStart,
            StringComparison.Ordinal);

        Assert.True(actionStart >= 0 && transactionStart > actionStart);
        Assert.True(targetRead > transactionStart, "Target activation state must be read inside the serializable transaction.");
    }

    [Fact]
    public async Task ToggleActive_WhenMissing_ReturnsNotFound()
    {
        await using var context = new ApplicationDbContext(Options($"BOM_ToggleMissing_{Guid.NewGuid()}"));

        var result = await Controller(context).ToggleActive(404);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task ToggleActive_WhenCurrentlyActive_DeactivatesIt()
    {
        await using var context = new ApplicationDbContext(Options($"BOM_Deactivate_{Guid.NewGuid()}"));
        var bom = Bom(Product("FG", ProductType.FinishedGood), "V1", new DateTime(2026, 7, 1));
        bom.IsActive = true;
        context.BOMs.Add(bom);
        await context.SaveChangesAsync();
        var controller = Controller(context);

        var result = await controller.ToggleActive(bom.Id);

        Assert.Equal(nameof(BomController.Index), Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.False((await context.BOMs.FindAsync(bom.Id))!.IsActive);
        Assert.Contains("ngừng kích hoạt", controller.TempData["StatusMessage"]!.ToString());
    }

    [Fact]
    public async Task ToggleActive_ActivatesTargetDeactivatesSameProductOnly_InSqlite()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var uom = new UnitOfMeasure { Code = "EA", Name = "Each" };
        var productA = Product("FG-A", ProductType.FinishedGood);
        var productB = Product("FG-B", ProductType.FinishedGood);
        productA.BaseUom = productB.BaseUom = uom;
        var target = Bom(productA, "V2", new DateTime(2026, 8, 1));
        var sameProductSibling = Bom(productA, "V1", new DateTime(2026, 7, 1));
        var differentProduct = Bom(productB, "V1", new DateTime(2026, 7, 1));
        sameProductSibling.IsActive = true;
        differentProduct.IsActive = true;
        context.BOMs.AddRange(target, sameProductSibling, differentProduct);
        await context.SaveChangesAsync();
        var controller = Controller(context);

        await controller.ToggleActive(target.Id);

        context.ChangeTracker.Clear();
        Assert.True((await context.BOMs.FindAsync(target.Id))!.IsActive);
        Assert.False((await context.BOMs.FindAsync(sameProductSibling.Id))!.IsActive);
        Assert.True((await context.BOMs.FindAsync(differentProduct.Id))!.IsActive);
        Assert.Contains("Đã kích hoạt", controller.TempData["StatusMessage"]!.ToString());
    }

    [Fact]
    public async Task ToggleActive_WhenCandidateClosesDependencyCycle_RejectsWithoutStateChange()
    {
        await using var context = new ApplicationDbContext(
            Options($"BOM_Cycle_{Guid.NewGuid()}"));
        var productA = Product("A", ProductType.FinishedGood);
        var productB = Product("B", ProductType.WIP);
        var productC = Product("C", ProductType.WIP);
        var candidate = Bom(productA, "A1", new DateTime(2026, 8, 1));
        candidate.Items.Add(new BOMItem
        {
            ComponentProduct = productB,
            QtyPer = 1
        });
        var activeB = Bom(productB, "B1", new DateTime(2026, 7, 1));
        activeB.IsActive = true;
        activeB.Items.Add(new BOMItem
        {
            ComponentProduct = productC,
            QtyPer = 1
        });
        var activeC = Bom(productC, "C1", new DateTime(2026, 7, 1));
        activeC.IsActive = true;
        activeC.Items.Add(new BOMItem
        {
            ComponentProduct = productA,
            QtyPer = 1
        });
        context.BOMs.AddRange(candidate, activeB, activeC);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var controller = Controller(context);

        var result = await controller.ToggleActive(candidate.Id);

        Assert.Equal(
            nameof(BomController.Index),
            Assert.IsType<RedirectToActionResult>(result).ActionName);
        context.ChangeTracker.Clear();
        Assert.False((await context.BOMs.FindAsync(candidate.Id))!.IsActive);
        Assert.True((await context.BOMs.FindAsync(activeB.Id))!.IsActive);
        Assert.True((await context.BOMs.FindAsync(activeC.Id))!.IsActive);
        Assert.Contains(
            "chu trình",
            controller.TempData["StatusMessage"]!.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ToggleActive_WhenSiblingUpdateFails_RollsBackRelationalActivation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        int targetId;
        int siblingId;
        await using (var setup = new ApplicationDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            var product = Product("FG", ProductType.FinishedGood);
            product.BaseUom = new UnitOfMeasure { Code = "EA", Name = "Each" };
            var target = Bom(product, "V2", new DateTime(2026, 8, 1));
            var sibling = Bom(product, "BLOCK", new DateTime(2026, 7, 1));
            sibling.IsActive = true;
            setup.BOMs.AddRange(target, sibling);
            await setup.SaveChangesAsync();
            targetId = target.Id;
            siblingId = sibling.Id;
            await setup.Database.ExecuteSqlRawAsync(
                """CREATE TRIGGER reject_target_activation BEFORE UPDATE ON BOMs WHEN OLD.Version = 'V2' AND NEW.IsActive = 1 BEGIN SELECT RAISE(ABORT, 'reject'); END;""");
        }

        await using (var failing = new ApplicationDbContext(options))
        {
            var controller = Controller(failing);

            var result = await controller.ToggleActive(targetId);

            Assert.Equal(
                nameof(BomController.Index),
                Assert.IsType<RedirectToActionResult>(result).ActionName);
            Assert.Contains(
                "xung đột",
                controller.TempData["StatusMessage"]!.ToString(),
                StringComparison.OrdinalIgnoreCase);
        }

        await using var verification = new ApplicationDbContext(options);
        Assert.False((await verification.BOMs.FindAsync(targetId))!.IsActive);
        Assert.True((await verification.BOMs.FindAsync(siblingId))!.IsActive);
    }

    private static BomController Controller(ApplicationDbContext context)
    {
        var controller = new BomController(context)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.TempData = new TempDataDictionary(controller.HttpContext, Mock.Of<ITempDataProvider>());
        return controller;
    }

    private static Product Product(
        string code,
        ProductType type,
        bool manufactured = true,
        bool active = true) =>
        new()
        {
            Code = code,
            Name = code,
            Type = type,
            IsManufactured = manufactured,
            IsActive = active
        };

    private static BOM Bom(Product product, string version, DateTime effectiveDate) =>
        new()
        {
            Product = product,
            Version = version,
            EffectiveDate = effectiveDate,
            IsActive = false
        };

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WmsMes.sln")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
