using WmsMes.Web.Data;

namespace WmsMes.Tests;

public class StartupDatabaseInitializerTests
{
    [Fact]
    public async Task MigrateWithRetryAsync_RetriesTransientFailureBeforeSucceeding()
    {
        var attempts = 0;

        await StartupDatabaseInitializer.MigrateWithRetryAsync(
            _ =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new InvalidOperationException("SQL Server is starting.");
                }

                return Task.CompletedTask;
            },
            maxAttempts: 3,
            delay: TimeSpan.Zero);

        Assert.Equal(3, attempts);
    }
}
