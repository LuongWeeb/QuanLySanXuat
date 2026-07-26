using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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
    public void Model_EnforcesUniqueComponentPerBom()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"BomItemIntegrity_{Guid.NewGuid()}")
            .Options;
        using var context = new ApplicationDbContext(options);
        var itemType = context.Model.FindEntityType(typeof(BOMItem));
        Assert.NotNull(itemType);

        var componentIndex = Assert.Single(itemType!.GetIndexes().Where(index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(BOMItem.BomId), nameof(BOMItem.ComponentProductId)])));

        Assert.True(componentIndex.IsUnique);
        Assert.Equal(
            "UX_BOMItems_BomId_ComponentProductId",
            componentIndex.GetDatabaseName());
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
    public async Task Sqlite_RejectsDuplicateComponentRowsInSameBom()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var parent = Product("FG");
        var component = Product("RM");
        component.Type = ProductType.RawMaterial;
        component.IsManufactured = false;
        context.Products.AddRange(parent, component);
        await context.SaveChangesAsync();
        context.BOMs.Add(new BOM
        {
            ProductId = parent.Id,
            Version = "V1",
            EffectiveDate = new DateTime(2026, 8, 1),
            Items =
            [
                new BOMItem { ComponentProductId = component.Id, QtyPer = 1 },
                new BOMItem { ComponentProductId = component.Id, QtyPer = 2 }
            ]
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
    }

    [Fact]
    public void EnforceUniqueBomComponentsMigration_IsScopedAndCycleCountFree()
    {
        var root = FindRepositoryRoot();
        var migrationPaths = Directory.GetFiles(
            Path.Combine(root, "Data", "Migrations"),
            "*_EnforceUniqueBomComponents.cs",
            SearchOption.TopDirectoryOnly);
        var migrationPath = Assert.Single(migrationPaths);
        var migration = File.ReadAllText(migrationPath);

        Assert.Contains("UX_BOMItems_BomId_ComponentProductId", migration);
        Assert.Contains(
            "ActiveProvider == \"Microsoft.EntityFrameworkCore.SqlServer\"",
            migration);
        Assert.Contains("SUM([QtyPer] * [ScrapPercent])", migration);
        Assert.Contains("NULLIF(SUM([QtyPer]), 0)", migration);
        Assert.Contains("ROW_NUMBER() OVER", migration);
        var sqliteBranchStart = migration.IndexOf(
            "else if (ActiveProvider == \"Microsoft.EntityFrameworkCore.Sqlite\")",
            StringComparison.Ordinal);
        Assert.True(sqliteBranchStart > 0);
        Assert.DoesNotContain(
            "AS REAL",
            migration[..sqliteBranchStart],
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CycleCount", migration, StringComparison.OrdinalIgnoreCase);

        var designer = File.ReadAllText(Path.ChangeExtension(migrationPath, ".Designer.cs"));
        Assert.Contains("UX_BOMItems_BomId_ComponentProductId", designer);
        Assert.DoesNotContain("CycleCount", designer, StringComparison.OrdinalIgnoreCase);

        var snapshot = File.ReadAllText(Path.Combine(
            root,
            "Data",
            "Migrations",
            "ApplicationDbContextModelSnapshot.cs"));
        Assert.Contains("UX_BOMItems_BomId_ComponentProductId", snapshot);
    }

    [Fact]
    public async Task EnforceUniqueBomComponentsMigration_ReconcilesLegacyDuplicatesAndPreservesDemand()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new ApplicationDbContext(options);
        var migrator = context.Database.GetService<IMigrator>();
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE "__EFMigrationsHistory" (
                "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                "ProductVersion" TEXT NOT NULL
            );
            CREATE TABLE "BOMItems" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_BOMItems" PRIMARY KEY AUTOINCREMENT,
                "BomId" INTEGER NOT NULL,
                "ComponentProductId" INTEGER NOT NULL,
                "QtyPer" decimal(18,2) NOT NULL,
                "ScrapPercent" decimal(18,2) NOT NULL
            );
            CREATE INDEX "IX_BOMItems_BomId" ON "BOMItems" ("BomId");
            INSERT INTO "BOMItems"
                ("BomId", "ComponentProductId", "QtyPer", "ScrapPercent")
            VALUES
                (10, 20, 5, 10),
                (10, 20, 3, 20);
            """);
        const string migrationName = "20260724112000_EnforceUniqueBomComponents";
        foreach (var appliedMigration in context.Database.GetMigrations()
            .Where(migration => migration != migrationName))
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES ({0}, '8.0.0');
                """,
                appliedMigration);
        }
        var originalItems = await context.BOMItems
            .AsNoTracking()
            .OrderBy(item => item.Id)
            .ToListAsync();
        var keptId = originalItems[0].Id;
        var grossDemandBefore = originalItems.Sum(
            item => item.QtyPer * (1 + item.ScrapPercent / 100));
        context.ChangeTracker.Clear();

        await migrator.MigrateAsync();

        var reconciled = Assert.Single(await context.BOMItems
            .AsNoTracking()
            .ToListAsync());
        Assert.Equal(keptId, reconciled.Id);
        Assert.Equal(8m, reconciled.QtyPer);
        Assert.Equal(13.75m, reconciled.ScrapPercent);
        Assert.Equal(
            grossDemandBefore,
            reconciled.QtyPer * (1 + reconciled.ScrapPercent / 100));

        context.BOMItems.Add(new BOMItem
        {
            BomId = reconciled.BomId,
            ComponentProductId = reconciled.ComponentProductId,
            QtyPer = 1,
            ScrapPercent = 0
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
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
