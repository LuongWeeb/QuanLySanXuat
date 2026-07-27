using System.Text.RegularExpressions;

namespace WmsMes.Tests;

public class ProductCostingAdminViewTests
{
    [Fact]
    public void Index_CreateProductModal_ProvidesRequiredStandardCostInput()
    {
        var view = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "Views", "Product", "Index.cshtml"));

        Assert.Contains("Giá vốn tiêu chuẩn (dự phòng)", view);
        Assert.Matches(
            new Regex(
                "<input(?=[^>]*\\bid=\\\"StandardCost\\\")(?=[^>]*\\bname=\\\"StandardCost\\\")(?=[^>]*\\btype=\\\"number\\\")(?=[^>]*\\bmin=\\\"0\\\")(?=[^>]*\\bstep=\\\"0\\.01\\\")(?=[^>]*\\brequired)[^>]*>",
                RegexOptions.IgnoreCase),
            view);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WmsMes.sln")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
