using System.Runtime.CompilerServices;
using QuestPDF.Infrastructure;

namespace WmsMes.Tests;

internal static class TestAssemblyConfiguration
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }
}
