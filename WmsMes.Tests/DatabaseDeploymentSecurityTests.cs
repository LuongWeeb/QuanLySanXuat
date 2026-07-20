using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WmsMes.Web.Data;

namespace WmsMes.Tests;

public class DatabaseDeploymentSecurityTests
{
    [Fact]
    public void FileBackedPassword_RoundTripsApostropheAndSemicolonThroughSqlConnectionStringBuilder()
    {
        const string password = "Arbitrary'p;assphrase!";
        var passwordFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(passwordFile, password);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Server"] = "sql-host",
                    ["Database:Name"] = "WmsMesDb",
                    ["Database:User"] = "wmsmes_app",
                    ["Database:PasswordFile"] = passwordFile
                })
                .Build();

            var connectionString = DatabaseConnectionStringFactory.Resolve(configuration);
            var parsed = new SqlConnectionStringBuilder(connectionString);

            Assert.Equal("sql-host", parsed.DataSource);
            Assert.Equal("WmsMesDb", parsed.InitialCatalog);
            Assert.Equal("wmsmes_app", parsed.UserID);
            Assert.Equal(password, parsed.Password);
        }
        finally
        {
            File.Delete(passwordFile);
        }
    }

    [Fact]
    public void LoginProvisioning_BindsArbitraryPasswordAsSqlParameter()
    {
        const string password = "Provisioner's;p@ssword!";
        using var connection = new SqlConnection();
        using var command = SqlServerDatabaseProvisioner.CreateLoginCommand(
            connection,
            "wmsmes_app",
            password,
            "WmsMesDb");

        Assert.Equal(password, command.Parameters["@Password"].Value);
        Assert.Equal("wmsmes_app", command.Parameters["@LoginName"].Value);
        Assert.Equal("WmsMesDb", command.Parameters["@DefaultDatabase"].Value);
        Assert.DoesNotContain(password, command.CommandText, StringComparison.Ordinal);
    }

    [Fact]
    public void Initialization_IsDisabledByDefaultAndRequiresExplicitOptIn()
    {
        var defaultConfiguration = new ConfigurationBuilder().Build();
        var enabledConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseInitialization:Enabled"] = "true"
            })
            .Build();

        Assert.False(DatabaseInitializationPolicy.ShouldInitialize([], defaultConfiguration));
        Assert.True(DatabaseInitializationPolicy.ShouldInitialize(
            ["--initialize-database"],
            defaultConfiguration));
        Assert.True(DatabaseInitializationPolicy.ShouldInitialize([], enabledConfiguration));
    }
}
