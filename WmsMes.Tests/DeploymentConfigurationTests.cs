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
        Assert.Contains("${MSSQL_SA_PASSWORD:?", compose);
        Assert.Contains("${MSSQL_MIGRATION_PASSWORD:?", compose);
        Assert.Contains("${MSSQL_APP_PASSWORD:?", compose);
        Assert.Contains("${JWT_SECRET_KEY:?", compose);
        Assert.DoesNotContain("1433:1433", compose);
        Assert.Contains("ASPNETCORE_ENVIRONMENT: Production", compose);
    }

    [Fact]
    public void Compose_SeparatesProvisioningMigrationAndLeastPrivilegedWebCredentials()
    {
        var compose = Read("docker-compose.yml");

        Assert.Contains("wmsmes-provision:", compose);
        Assert.Contains("wmsmes-migrate:", compose);
        Assert.Contains("User Id=wmsmes_migrator", compose);
        Assert.Contains("User Id=wmsmes_app", compose);
        Assert.DoesNotContain("User Id=sa", compose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--initialize-database", compose);
        Assert.Contains("condition: service_completed_successfully", compose);
        Assert.DoesNotContain("-v MigrationPassword", compose);

        var provisioningPath = Path.Combine(RepositoryRoot, "docker", "sql", "provision.sql");
        Assert.True(File.Exists(provisioningPath), "A first-start SQL provisioning script is required.");
        var provisioning = File.ReadAllText(provisioningPath);
        Assert.Contains("ALTER ROLE [db_owner] ADD MEMBER [wmsmes_migrator]", provisioning);
        Assert.Contains("ALTER ROLE [db_datareader] ADD MEMBER [wmsmes_app]", provisioning);
        Assert.Contains("ALTER ROLE [db_datawriter] ADD MEMBER [wmsmes_app]", provisioning);
        Assert.DoesNotContain("ALTER ROLE [db_owner] ADD MEMBER [wmsmes_app]", provisioning);
        Assert.DoesNotContain(":setvar MigrationPassword", provisioning, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(":setvar ApplicationPassword", provisioning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExampleEnvironmentDocumentsBlankRequiredSecretsAndQuestPdfEligibility()
    {
        var path = Path.Combine(RepositoryRoot, ".env.example");
        Assert.True(File.Exists(path), ".env.example must document the required external values.");
        var example = File.ReadAllText(path);

        foreach (var variable in new[]
                 {
                     "MSSQL_SA_PASSWORD",
                     "MSSQL_MIGRATION_PASSWORD",
                     "MSSQL_APP_PASSWORD",
                     "JWT_SECRET_KEY"
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
        var compose = Read("docker-compose.yml");

        Assert.Contains("--initialize-database", program);
        Assert.Contains("DatabaseInitialization:Enabled", program);
        Assert.Contains("DatabaseInitialization__Enabled: \"false\"", compose);
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, relativePath));
}
