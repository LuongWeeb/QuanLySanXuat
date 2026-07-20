using System.Data;
using Microsoft.Data.SqlClient;

namespace WmsMes.Web.Data;

public static class SqlServerDatabaseProvisioner
{
    private const string MigratorLogin = "wmsmes_migrator";
    private const string ApplicationLogin = "wmsmes_app";

    public static async Task ProvisionFromConfigurationAsync(
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var server = Required(configuration, "DatabaseProvisioning:Server");
        var database = Required(configuration, "DatabaseProvisioning:Database");
        var saUser = configuration["DatabaseProvisioning:SaUser"] ?? "sa";
        var saPassword = ReadSecret(configuration, "DatabaseProvisioning:SaPasswordFile");
        var migratorPassword = ReadSecret(configuration, "DatabaseProvisioning:MigratorPasswordFile");
        var applicationPassword = ReadSecret(configuration, "DatabaseProvisioning:ApplicationPasswordFile");

        await using (var masterConnection = new SqlConnection(
                         DatabaseConnectionStringFactory.Create(server, "master", saUser, saPassword)))
        {
            await masterConnection.OpenAsync(cancellationToken);
            await using var createDatabase = CreateDatabaseCommand(masterConnection, database);
            await createDatabase.ExecuteNonQueryAsync(cancellationToken);

            await using var createMigrator = CreateLoginCommand(
                masterConnection,
                MigratorLogin,
                migratorPassword,
                database);
            await createMigrator.ExecuteNonQueryAsync(cancellationToken);

            await using var createApplication = CreateLoginCommand(
                masterConnection,
                ApplicationLogin,
                applicationPassword,
                database);
            await createApplication.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var databaseConnection = new SqlConnection(
            DatabaseConnectionStringFactory.Create(server, database, saUser, saPassword));
        await databaseConnection.OpenAsync(cancellationToken);
        await using var grantRoles = databaseConnection.CreateCommand();
        grantRoles.CommandText = """
            IF USER_ID(N'wmsmes_migrator') IS NULL
                CREATE USER [wmsmes_migrator] FOR LOGIN [wmsmes_migrator];
            ELSE
                ALTER USER [wmsmes_migrator] WITH LOGIN = [wmsmes_migrator];

            IF IS_ROLEMEMBER(N'db_owner', N'wmsmes_migrator') <> 1
                ALTER ROLE [db_owner] ADD MEMBER [wmsmes_migrator];

            IF USER_ID(N'wmsmes_app') IS NULL
                CREATE USER [wmsmes_app] FOR LOGIN [wmsmes_app];
            ELSE
                ALTER USER [wmsmes_app] WITH LOGIN = [wmsmes_app];

            IF IS_ROLEMEMBER(N'db_owner', N'wmsmes_app') = 1
                ALTER ROLE [db_owner] DROP MEMBER [wmsmes_app];
            IF IS_ROLEMEMBER(N'db_datareader', N'wmsmes_app') <> 1
                ALTER ROLE [db_datareader] ADD MEMBER [wmsmes_app];
            IF IS_ROLEMEMBER(N'db_datawriter', N'wmsmes_app') <> 1
                ALTER ROLE [db_datawriter] ADD MEMBER [wmsmes_app];
            """;
        await grantRoles.ExecuteNonQueryAsync(cancellationToken);
    }

    public static SqlCommand CreateLoginCommand(
        SqlConnection connection,
        string loginName,
        string password,
        string defaultDatabase)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            IF SUSER_ID(@LoginName) IS NULL
                EXEC master.dbo.sp_addlogin
                    @loginame = @LoginName,
                    @passwd = @Password,
                    @defdb = @DefaultDatabase;
            ELSE
                EXEC master.dbo.sp_password
                    @old = NULL,
                    @new = @Password,
                    @loginame = @LoginName;
            """;
        command.Parameters.Add("@LoginName", SqlDbType.NVarChar, 128).Value = loginName;
        command.Parameters.Add("@Password", SqlDbType.NVarChar, 128).Value = password;
        command.Parameters.Add("@DefaultDatabase", SqlDbType.NVarChar, 128).Value = defaultDatabase;
        return command;
    }

    private static SqlCommand CreateDatabaseCommand(SqlConnection connection, string database)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            IF DB_ID(@DatabaseName) IS NULL
            BEGIN
                DECLARE @CreateDatabaseSql nvarchar(max) =
                    N'CREATE DATABASE ' + QUOTENAME(@DatabaseName);
                EXEC sys.sp_executesql @CreateDatabaseSql;
            END
            """;
        command.Parameters.Add("@DatabaseName", SqlDbType.NVarChar, 128).Value = database;
        return command;
    }

    private static string ReadSecret(IConfiguration configuration, string key) =>
        SecretFile.ReadRequired(Required(configuration, key), key);

    private static string Required(IConfiguration configuration, string key) =>
        string.IsNullOrWhiteSpace(configuration[key])
            ? throw new InvalidOperationException($"{key} is required for database provisioning.")
            : configuration[key]!;
}
