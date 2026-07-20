using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WmsMes.Web.Services;

namespace WmsMes.Tests;

public class ExportRouteTests : IClassFixture<ExportWebApplicationFactory>
{
    private readonly ExportWebApplicationFactory _factory;

    public ExportRouteTests(ExportWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/Inventory/ExportExcel", null)]
    [InlineData("/Inventory/ExportExcel?warehouseId=17", 17)]
    public async Task InventoryExportExcel_ConventionalRouteSupportsOptionalWarehouse(string route, int? warehouseId)
    {
        _factory.Reports.WarehouseIds.Clear();
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new[] { warehouseId }, _factory.Reports.WarehouseIds);
    }

    [Fact]
    public async Task WorkOrderExportPdf_ConventionalControllerRouteReturnsPdf()
    {
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync("/WorkOrder/ExportPdf/23");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(23, _factory.Reports.LastWorkOrderId);
    }

    [Fact]
    public async Task WorkOrderExportPdf_MissingWorkOrderReturnsNotFound()
    {
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync("/WorkOrder/ExportPdf/404");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

public sealed class ExportWebApplicationFactory : WebApplicationFactory<Program>
{
    public StubReportExportService Reports { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("DatabaseInitialization:Enabled", "false");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IReportExportService>();
            services.AddSingleton<IReportExportService>(Reports);
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { });
        });
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName);
        return client;
    }
}

public sealed class StubReportExportService : IReportExportService
{
    public List<int?> WarehouseIds { get; } = [];
    public int? LastWorkOrderId { get; private set; }

    public Task<byte[]> ExportStockBalanceToExcelAsync(int? warehouseId = null)
    {
        WarehouseIds.Add(warehouseId);
        return Task.FromResult(new byte[] { 1, 2, 3 });
    }

    public Task<byte[]> ExportWorkOrderToPdfAsync(int workOrderId)
    {
        LastWorkOrderId = workOrderId;
        return workOrderId == 404
            ? Task.FromException<byte[]>(new KeyNotFoundException("missing"))
            : Task.FromResult(new byte[] { 4, 5, 6 });
    }
}

public sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ExportTest";

    public TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, "export-test-user"),
            new(ClaimTypes.Role, "Admin"),
            new(ClaimTypes.Role, "Planner"),
            new(ClaimTypes.Role, "Manager"),
            new(ClaimTypes.Role, "Warehouse")
        ];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
