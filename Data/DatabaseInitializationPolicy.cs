namespace WmsMes.Web.Data;

public static class DatabaseInitializationPolicy
{
    private const string InitializeArgument = "--initialize-database";

    public static bool IsOneShot(IReadOnlyCollection<string> arguments) =>
        arguments.Any(argument =>
            string.Equals(argument, InitializeArgument, StringComparison.OrdinalIgnoreCase));

    public static bool ShouldInitialize(
        IReadOnlyCollection<string> arguments,
        IConfiguration configuration) =>
        IsOneShot(arguments) ||
        configuration.GetValue<bool>("DatabaseInitialization:Enabled");
}
