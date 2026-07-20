using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Domain.Entities;

namespace WmsMes.Web.Data;

public static class StartupDatabaseInitializer
{
    private const int MigrationAttempts = 12;
    private static readonly TimeSpan MigrationRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly HashSet<int> TransientSqlErrorNumbers =
    [
        -2, 2, 20, 53, 64, 233, 258,
        10053, 10054, 10060, 10928, 10929, 11001,
        17197, 18401, 40197, 40501, 40613, 49918, 49919, 49920
    ];

    public static async Task InitializeAsync(
        IServiceProvider serviceProvider,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<ApplicationDbContext>();

        await MigrateThenSeedAsync(
            dbContext.Database.MigrateAsync,
            async _ =>
            {
                var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();
                var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
                var environment = services.GetRequiredService<IHostEnvironment>();
                var configuration = services.GetRequiredService<IConfiguration>();

                await DbSeeder.SeedRolesAsync(roleManager);
                if (ShouldSeedDemoUsers(environment, configuration))
                {
                    await DbSeeder.SeedDemoUsersAsync(userManager);
                }
                await DbSeeder.SeedQcInfrastructureAsync(dbContext);
                await DbSeeder.SeedUnitOfMeasuresAsync(dbContext);
                await DbSeeder.SeedWarehouseStructureAsync(dbContext);
                await DbSeeder.SeedComprehensiveSampleDataAsync(dbContext, userManager);
            },
            MigrationAttempts,
            MigrationRetryDelay,
            logger,
            cancellationToken);
    }

    public static bool ShouldSeedDemoUsers(
        IHostEnvironment environment,
        IConfiguration configuration) =>
        environment.IsDevelopment() &&
        configuration.GetValue<bool>("DatabaseInitialization:SeedDemoUsers");

    public static async Task MigrateThenSeedAsync(
        Func<CancellationToken, Task> migrateAsync,
        Func<CancellationToken, Task> seedAsync,
        int maxAttempts,
        TimeSpan delay,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seedAsync);

        await MigrateWithRetryAsync(
            migrateAsync,
            maxAttempts,
            delay,
            logger,
            cancellationToken);
        await seedAsync(cancellationToken);
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
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsTransientSqlFailure(ex))
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

    private static bool IsTransientSqlFailure(Exception exception)
    {
        if (exception is SqlException sqlException)
        {
            return sqlException.Errors
                .Cast<SqlError>()
                .Any(error => TransientSqlErrorNumbers.Contains(error.Number));
        }

        return exception.InnerException is not null && IsTransientSqlFailure(exception.InnerException);
    }
}
