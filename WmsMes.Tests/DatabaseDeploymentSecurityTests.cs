using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WmsMes.Web.Data;

namespace WmsMes.Tests;

public class DatabaseDeploymentSecurityTests
{
    [Theory]
    [InlineData("DatabaseProvisioning:SaPasswordFile", "\n")]
    [InlineData("DatabaseProvisioning:SaPasswordFile", "\r\n")]
    [InlineData("DatabaseProvisioning:MigratorPasswordFile", "\n")]
    [InlineData("DatabaseProvisioning:MigratorPasswordFile", "\r\n")]
    [InlineData("DatabaseProvisioning:ApplicationPasswordFile", "\n")]
    [InlineData("DatabaseProvisioning:ApplicationPasswordFile", "\r\n")]
    [InlineData("Jwt:SigningKeyFile", "\n")]
    [InlineData("Jwt:SigningKeyFile", "\r\n")]
    public void EverySecretType_RemovesOneTerminalLineEndingAndPreservesOtherCharacters(
        string settingName,
        string lineEnding)
    {
        const string secret = " leading secret's;spaces stay ";
        var secretFile = WriteTemporarySecret(secret + lineEnding);
        try
        {
            Assert.Equal(secret, SecretFile.ReadRequired(secretFile, settingName));
        }
        finally
        {
            File.Delete(secretFile);
        }
    }

    [Theory]
    [InlineData("secret\n\n", "secret\n")]
    [InlineData("secret\r\n\r\n", "secret\r\n")]
    [InlineData("secret\r\n\n", "secret\r\n")]
    [InlineData("secret\r", "secret\r")]
    public void SecretFile_RemovesExactlyOneTerminatorAndPreservesAdditionalCharacters(
        string stored,
        string expected)
    {
        var secretFile = WriteTemporarySecret(stored);
        try
        {
            Assert.Equal(expected, SecretFile.ReadRequired(secretFile, "Test:SecretFile"));
        }
        finally
        {
            File.Delete(secretFile);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void SecretFile_RejectsValuesThatAreEmptyAfterNormalization(string stored)
    {
        var secretFile = WriteTemporarySecret(stored);
        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                SecretFile.ReadRequired(secretFile, "Test:SecretFile"));

            Assert.Contains("Test:SecretFile", exception.Message);
            Assert.Contains("must not be empty", exception.Message);
        }
        finally
        {
            File.Delete(secretFile);
        }
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void FileBackedPassword_NormalizesLineEndingAndRoundTripsSpecialCharacters(
        string lineEnding)
    {
        const string password = " Arbitrary'p;assphrase! ";
        var passwordFile = WriteTemporarySecret(password + lineEnding);
        try
        {
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

    private static string WriteTemporarySecret(string value)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, value);
        return path;
    }
}
