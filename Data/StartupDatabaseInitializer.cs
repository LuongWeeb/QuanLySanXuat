using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Domain.Entities;

namespace WmsMes.Web.Data;

public static class StartupDatabaseInitializer
{
    private const int MigrationAttempts = 12;
    private static readonly TimeSpan MigrationRetryDelay = TimeSpan.FromSeconds(5);

    public static async Task InitializeAsync(
        IServiceProvider serviceProvider,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<ApplicationDbContext>();

        await MigrateWithRetryAsync(
            dbContext.Database.MigrateAsync,
            MigrationAttempts,
            MigrationRetryDelay,
            logger,
            cancellationToken);

        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        await DbSeeder.SeedRolesAndUsersAsync(roleManager, userManager);
        await DbSeeder.SeedQcInfrastructureAsync(dbContext);
        await DbSeeder.SeedUnitOfMeasuresAsync(dbContext);
        await DbSeeder.SeedWarehouseStructureAsync(dbContext);
        await DbSeeder.SeedComprehensiveSampleDataAsync(dbContext, userManager);
    }

    public static async Task MigrateWithRetryAsync(
        Func<CancellationToken, Task> migrateAsync,
        int maxAttempts,
        TimeSpan delay,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(migrateAsync);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await migrateAsync(cancellationToken);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                logger?.LogWarning(
                    ex,
                    "Database migration attempt {Attempt} of {MaxAttempts} failed. Retrying in {Delay}.",
                    attempt,
                    maxAttempts,
                    delay);

                await Task.Delay(delay, cancellationToken);
            }
        }
    }
}
