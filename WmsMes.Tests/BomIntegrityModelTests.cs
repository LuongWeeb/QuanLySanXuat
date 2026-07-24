using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Tests;

public class BomIntegrityModelTests
{
    [Fact]
    public void Model_EnforcesUniqueVersionAndOneFilteredActiveBomPerProduct()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"BomIntegrity_{Guid.NewGuid()}")
            .Options;
        using var context = new ApplicationDbContext(options);
        var bomType = context.Model.FindEntityType(typeof(BOM));
        Assert.NotNull(bomType);

        var versionIndex = Assert.Single(bomType!.GetIndexes().Where(index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(BOM.ProductId), nameof(BOM.Version)])));
        Assert.True(versionIndex.IsUnique);

        var activeIndex = Assert.Single(bomType.GetIndexes().Where(index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(BOM.ProductId)])));
        Assert.Equal("[IsActive] = 1", activeIndex.GetFilter());
    }

    [Fact]
    public async Task Sqlite_RejectsDuplicateProductVersionFromAlternateWriter()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var product = Product("FG");
        context.BOMs.Add(Bom(product, "V1", isActive: false));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        context.BOMs.Add(new BOM
        {
            ProductId = product.Id,
            Version = "V1",
            EffectiveDate = new DateTime(2026, 8, 1),
            IsActive = false
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Sqlite_RejectsSecondActiveBomFromAlternateWriter()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var product = Product("FG");
        context.BOMs.Add(Bom(product, "V1", isActive: true));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        context.BOMs.Add(new BOM
        {
            ProductId = product.Id,
            Version = "V2",
            EffectiveDate = new DateTime(2026, 8, 1),
            IsActive = true
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public void EnforceBomIntegrityMigration_ContainsOnlyBomIndexesAndNoCycleCountSchema()
    {
        var root = FindRepositoryRoot();
        var migrationPaths = Directory.GetFiles(
            Path.Combine(root, "Data", "Migrations"),
            "*_EnforceBomIntegrity.cs",
            SearchOption.TopDirectoryOnly);
        var migrationPath = Assert.Single(migrationPaths);
        var migration = File.ReadAllText(migrationPath);
        Assert.Contains("UX_BOMs_ProductId_Version", migration);
        Assert.Contains("UX_BOMs_OneActivePerProduct", migration);
        Assert.Contains("filter: \"[IsActive] = 1\"", migration);
        Assert.DoesNotContain("CycleCount", migration, StringComparison.OrdinalIgnoreCase);

        var designer = File.ReadAllText(
            Path.ChangeExtension(migrationPath, ".Designer.cs"));
        Assert.Contains("UX_BOMs_ProductId_Version", designer);
        Assert.Contains("UX_BOMs_OneActivePerProduct", designer);
        Assert.DoesNotContain(
            "CycleCount",
            designer,
            StringComparison.OrdinalIgnoreCase);

        var snapshot = File.ReadAllText(Path.Combine(
            root,
            "Data",
            "Migrations",
            "ApplicationDbContextModelSnapshot.cs"));
        Assert.Contains("UX_BOMs_ProductId_Version", snapshot);
        Assert.Contains("UX_BOMs_OneActivePerProduct", snapshot);
        Assert.DoesNotContain(
            "CycleCount",
            snapshot,
            StringComparison.OrdinalIgnoreCase);
    }

    private static Product Product(string code) =>
        new()
        {
            Code = code,
            Name = code,
            Type = ProductType.FinishedGood,
            IsManufactured = true,
            IsActive = true,
            BaseUom = new UnitOfMeasure
            {
                Code = $"EA-{code}",
                Name = "Each"
            }
        };

    private static BOM Bom(Product product, string version, bool isActive) =>
        new()
        {
            Product = product,
            Version = version,
            EffectiveDate = new DateTime(2026, 7, 1),
            IsActive = isActive
        };

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "WmsMes.sln")))
        {
            directory = directory.Parent;
        }

        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
