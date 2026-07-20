using System.Reflection;
using Microsoft.Data.SqlClient;
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
                    throw CreateSqlException(40613);
                }

                return Task.CompletedTask;
            },
            maxAttempts: 3,
            delay: TimeSpan.Zero);

        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task MigrateWithRetryAsync_RethrowsFinalTransientFailureAfterBoundedAttempts()
    {
        var attempts = 0;
        var expected = CreateSqlException(40501);

        var actual = await Assert.ThrowsAsync<SqlException>(() =>
            StartupDatabaseInitializer.MigrateWithRetryAsync(
                _ =>
                {
                    attempts++;
                    throw expected;
                },
                maxAttempts: 3,
                delay: TimeSpan.Zero));

        Assert.Same(expected, actual);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task MigrateWithRetryAsync_RethrowsCancellationImmediately()
    {
        var attempts = 0;

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            StartupDatabaseInitializer.MigrateWithRetryAsync(
                _ =>
                {
                    attempts++;
                    throw new OperationCanceledException("stop now");
                },
                maxAttempts: 5,
                delay: TimeSpan.Zero));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task MigrateWithRetryAsync_RethrowsDeterministicSqlFailureImmediately()
    {
        var attempts = 0;
        var expected = CreateSqlException(2627);

        var actual = await Assert.ThrowsAsync<SqlException>(() =>
            StartupDatabaseInitializer.MigrateWithRetryAsync(
                _ =>
                {
                    attempts++;
                    throw expected;
                },
                maxAttempts: 5,
                delay: TimeSpan.Zero));

        Assert.Same(expected, actual);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task MigrateThenSeedAsync_DoesNotSeedAfterMigrationFailure()
    {
        var seeded = false;

        await Assert.ThrowsAsync<SqlException>(() =>
            StartupDatabaseInitializer.MigrateThenSeedAsync(
                _ => Task.FromException(CreateSqlException(2627)),
                _ =>
                {
                    seeded = true;
                    return Task.CompletedTask;
                },
                maxAttempts: 3,
                delay: TimeSpan.Zero));

        Assert.False(seeded);
    }

    private static SqlException CreateSqlException(int number)
    {
        var error = (SqlError)typeof(SqlError)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(constructor => constructor.GetParameters().Length == 9)
            .Invoke(
            [
                number,
                (byte)0,
                (byte)0,
                "test-server",
                $"SQL failure {number}",
                "test-procedure",
                0,
                (uint)0,
                null!
            ]);
        var errors = (SqlErrorCollection)Activator.CreateInstance(
            typeof(SqlErrorCollection),
            nonPublic: true)!;
        typeof(SqlErrorCollection)
            .GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(errors, [error]);

        return (SqlException)typeof(SqlException)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(constructor => constructor.GetParameters().Length == 4)
            .Invoke(["SQL migration failure", errors, null!, Guid.NewGuid()]);
    }
}
