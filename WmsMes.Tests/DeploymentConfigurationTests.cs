using System.Text.RegularExpressions;

namespace WmsMes.Tests;

public class DeploymentConfigurationTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    [Fact]
    public void Compose_RequiresExternalSecretsKeepsSqlInternalAndRunsWebInProduction()
    {
        var compose = Read("docker-compose.yml");

        Assert.DoesNotContain("YourSecurePassword", compose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mssql_sa_password:", compose);
        Assert.Contains("mssql_migration_password:", compose);
        Assert.Contains("mssql_app_password:", compose);
        Assert.Contains("jwt_signing_key:", compose);
        Assert.Contains("/run/secrets/mssql_app_password", compose);
        Assert.Contains("/run/secrets/jwt_signing_key", compose);
        Assert.DoesNotContain("Password=${", compose, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1433:1433", compose);
        Assert.Contains("ASPNETCORE_ENVIRONMENT: Production", compose);
    }

    [Fact]
    public void Compose_SeparatesProvisioningMigrationAndLeastPrivilegedWebCredentials()
    {
        var compose = Read("docker-compose.yml");

        Assert.Contains("wmsmes-provision:", compose);
        Assert.Contains("wmsmes-migrate:", compose);
        Assert.Contains("Database__User: wmsmes_migrator", compose);
        Assert.Contains("Database__User: wmsmes_app", compose);
        Assert.Contains("--initialize-database", compose);
        Assert.Contains("condition: service_completed_successfully", compose);
        Assert.Contains("--provision-database", compose);
        Assert.DoesNotContain("-v MigrationPassword", compose);
        Assert.DoesNotContain("provision.sql", compose, StringComparison.OrdinalIgnoreCase);

        var provisioner = Read(Path.Combine("Data", "SqlServerDatabaseProvisioner.cs"));
        Assert.Contains("ALTER ROLE [db_owner] ADD MEMBER [wmsmes_migrator]", provisioner);
        Assert.Contains("ALTER ROLE [db_datareader] ADD MEMBER [wmsmes_app]", provisioner);
        Assert.Contains("ALTER ROLE [db_datawriter] ADD MEMBER [wmsmes_app]", provisioner);
        Assert.DoesNotContain("ALTER ROLE [db_owner] ADD MEMBER [wmsmes_app]", provisioner);
        Assert.Contains("@Password", provisioner);
        Assert.Contains("IF SUSER_ID(@LoginName) IS NULL", provisioner);
        Assert.Contains("sp_password", provisioner);
        Assert.Contains("IF USER_ID(N'wmsmes_app') IS NULL", provisioner);

        var sqlEntrypoint = Read(Path.Combine("docker", "sql", "entrypoint.sh"));
        Assert.Contains("/run/secrets/mssql_sa_password", sqlEntrypoint);
        Assert.DoesNotContain("$1", sqlEntrypoint, StringComparison.Ordinal);
    }

    [Fact]
    public void ExampleEnvironmentDocumentsBlankRequiredSecretsAndQuestPdfEligibility()
    {
        var path = Path.Combine(RepositoryRoot, ".env.example");
        Assert.True(File.Exists(path), ".env.example must document the required external values.");
        var example = File.ReadAllText(path);

        foreach (var variable in new[]
                 {
                     "MSSQL_SA_PASSWORD_FILE",
                     "MSSQL_MIGRATION_PASSWORD_FILE",
                     "MSSQL_APP_PASSWORD_FILE",
                     "JWT_SIGNING_KEY_FILE"
                 })
        {
            Assert.Matches(new Regex($@"(?m)^{variable}=\s*$"), example);
        }

        Assert.Contains("QuestPDF Community", example, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://www.questpdf.com/license/", example);
        var gitignore = Read(".gitignore");
        Assert.Matches(new Regex(@"(?m)^\.env$"), gitignore);
        Assert.DoesNotMatch(new Regex(@"(?m)^\.env\.example$"), gitignore);
        var dockerignore = Read(".dockerignore");
        Assert.Matches(new Regex(@"(?m)^\.env$"), dockerignore);
    }

    [Fact]
    public void DockerImageRunsAsDotNetEightNonRootAppUser()
    {
        var dockerfile = Read("Dockerfile");
        var finalStage = dockerfile.IndexOf("FROM base AS final", StringComparison.Ordinal);
        var user = dockerfile.IndexOf("USER $APP_UID", StringComparison.Ordinal);

        Assert.True(finalStage >= 0);
        Assert.True(user > finalStage, "The final runtime stage must switch to the .NET 8 non-root app user.");
    }

    [Fact]
    public void QuestPdfLicenseIsConfiguredOnceAtProcessStartupNotPerReportScope()
    {
        var program = Read("Program.cs");
        var reportService = Read(Path.Combine("Services", "ReportExportService.cs"));

        Assert.Single(Regex.Matches(program, @"QuestPDF\.Settings\.License").Cast<Match>());
        Assert.DoesNotContain("QuestPDF.Settings.License", reportService);
    }

    [Fact]
    public void ProgramSupportsOneShotMigrationAndDisablingInitializationForWebRuntime()
    {
        var program = Read("Program.cs");
        var policy = Read(Path.Combine("Data", "DatabaseInitializationPolicy.cs"));
        var compose = Read("docker-compose.yml");

        Assert.Contains("--initialize-database", policy);
        Assert.Contains("DatabaseInitialization:Enabled", policy);
        Assert.Contains("DatabaseInitializationPolicy.ShouldInitialize", program);
        Assert.Contains("DatabaseInitialization__Enabled: \"false\"", compose);
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, relativePath));
}
