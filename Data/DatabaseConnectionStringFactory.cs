using Microsoft.Data.SqlClient;

namespace WmsMes.Web.Data;

public static class SecretFile
{
    public static string ReadRequired(string path, string settingName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException($"{settingName} must point to a readable secret file.");
        }

        try
        {
            var value = File.ReadAllText(path);
            if (value.EndsWith("\r\n", StringComparison.Ordinal))
            {
                value = value[..^2];
            }
            else if (value.EndsWith('\n'))
            {
                value = value[..^1];
            }

            if (value.Length == 0)
            {
                throw new InvalidOperationException(
                    $"The secret file configured by {settingName} must not be empty after normalization.");
            }

            return value;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Unable to read the secret file configured by {settingName}.",
                exception);
        }
    }
}

public static class DatabaseConnectionStringFactory
{
    public static string Resolve(IConfiguration configuration)
    {
        var passwordFile = configuration["Database:PasswordFile"];
        if (string.IsNullOrWhiteSpace(passwordFile))
        {
            return configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "Configure ConnectionStrings:DefaultConnection or the file-backed Database settings.");
        }

        return Create(
            Required(configuration, "Database:Server"),
            Required(configuration, "Database:Name"),
            Required(configuration, "Database:User"),
            SecretFile.ReadRequired(passwordFile, "Database:PasswordFile"),
            configuration.GetValue("Database:TrustServerCertificate", true));
    }

    public static string Create(
        string server,
        string database,
        string user,
        string password,
        bool trustServerCertificate = true)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = database,
            UserID = user,
            Password = password,
            TrustServerCertificate = trustServerCertificate,
            MultipleActiveResultSets = true
        };
        return builder.ConnectionString;
    }

    private static string Required(IConfiguration configuration, string key) =>
        string.IsNullOrWhiteSpace(configuration[key])
            ? throw new InvalidOperationException($"{key} is required when Database:PasswordFile is configured.")
            : configuration[key]!;
}
