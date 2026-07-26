using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Tests;

public class InventoryCancellationRuntimeTests :
    IClassFixture<InventoryCancellationWebApplicationFactory>
{
    private readonly InventoryCancellationWebApplicationFactory _factory;

    public InventoryCancellationRuntimeTests(
        InventoryCancellationWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/Inventory/Receipts")]
    [InlineData("/Inventory/Issues")]
    [InlineData("/Inventory/Transactions")]
    public async Task InventoryPages_RejectUnauthenticatedAndForbiddenUsers(string route)
    {
        using var anonymous = _factory.CreateInventoryClient();
        using var forbidden = _factory.CreateInventoryClient("Viewer");

        var anonymousResponse = await anonymous.GetAsync(route);
        var forbiddenResponse = await forbidden.GetAsync(route);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
    }

    [Theory]
    [InlineData("/Inventory/CancelReceipt/101")]
    [InlineData("/Inventory/CancelIssue/201")]
    public async Task CancellationPosts_RejectUnauthenticatedAndForbiddenUsers(string route)
    {
        using var anonymous = _factory.CreateInventoryClient();
        using var forbidden = _factory.CreateInventoryClient("Viewer");

        var anonymousResponse = await anonymous.PostAsync(
            route,
            new FormUrlEncodedContent([]));
        var forbiddenResponse = await forbidden.PostAsync(
            route,
            new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
    }

    [Fact]
    public async Task WarehouseUser_CanFollowDocumentLedgerLinks()
    {
        using var client = _factory.CreateInventoryClient("Warehouse");

        var receipts = await client.GetAsync("/Inventory/Receipts");
        var issues = await client.GetAsync("/Inventory/Issues");
        var ledger = await client.GetAsync("/Inventory/Transactions");
        var receiptHtml = await receipts.Content.ReadAsStringAsync();
        var issueHtml = await issues.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, receipts.StatusCode);
        Assert.Equal(HttpStatusCode.OK, issues.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ledger.StatusCode);
        Assert.Contains("href=\"/Inventory/Transactions\"", receiptHtml);
        Assert.Contains("href=\"/Inventory/Transactions\"", issueHtml);
    }

    [Fact]
    public async Task WarehouseUser_CanRoundTripFromOlderLedgerPageBackToNewest()
    {
        using var client = _factory.CreateInventoryClient("Warehouse");

        var newestResponse = await client.GetAsync("/Inventory/Transactions");
        var newestHtml = await newestResponse.Content.ReadAsStringAsync();
        var olderMatch = Regex.Match(
            newestHtml,
            """href="([^"]+)"[^>]*>Cũ hơn</a>""",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        Assert.Equal(HttpStatusCode.OK, newestResponse.StatusCode);
        Assert.True(olderMatch.Success);
        var olderUrl = WebUtility.HtmlDecode(olderMatch.Groups[1].Value);
        var olderResponse = await client.GetAsync(olderUrl);
        var olderHtml = await olderResponse.Content.ReadAsStringAsync();
        var newestMatch = Regex.Match(
            olderHtml,
            """href="([^"]+)"[^>]*>Mới nhất</a>""",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        Assert.Equal(HttpStatusCode.OK, olderResponse.StatusCode);
        Assert.Contains("REF-001", olderHtml);
        Assert.True(newestMatch.Success);
        var newestUrl = WebUtility.HtmlDecode(newestMatch.Groups[1].Value);
        var roundTripResponse = await client.GetAsync(newestUrl);
        var roundTripHtml = await roundTripResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, roundTripResponse.StatusCode);
        Assert.Contains("REF-051", roundTripHtml);
        Assert.Contains(">Cũ hơn</a>", roundTripHtml);
    }

    [Fact]
    public async Task EmptyCursorLedgerPage_KeepsNewestEscapeLinkVisible()
    {
        using var client = _factory.CreateInventoryClient("Warehouse");
        var beforeDate = Uri.EscapeDataString(
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToString("O"));

        var response = await client.GetAsync(
            $"/Inventory/Transactions?beforeDate={beforeDate}&beforeId=1");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Chưa có giao dịch kho", html);
        Assert.Contains(">Mới nhất</a>", html);
    }

    [Theory]
    [InlineData("/Inventory/Transactions?beforeId=1")]
    [InlineData("/Inventory/Transactions?beforeDate=2026-07-26T00%3A00%3A00.0000000Z")]
    [InlineData("/Inventory/Transactions?beforeDate=not-a-date&beforeId=1")]
    [InlineData("/Inventory/Transactions?beforeDate=")]
    [InlineData("/Inventory/Transactions?beforeDate=&beforeId=")]
    [InlineData("/Inventory/Transactions?beforeDate=%20&beforeId=%20")]
    [InlineData("/Inventory/Transactions?beforeDate=&beforeId=1")]
    [InlineData("/Inventory/Transactions?beforeDate=2026-07-26T00%3A00%3A00.0000000Z&beforeId=")]
    public async Task LedgerCursor_RejectsPartialOrMalformedPairs(string route)
    {
        using var client = _factory.CreateInventoryClient("Warehouse");

        var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/Inventory/CancelReceipt/101")]
    [InlineData("/Inventory/CancelIssue/201")]
    public async Task CancellationPosts_RejectMissingAntiforgeryToken(string route)
    {
        using var client = _factory.CreateInventoryClient("Warehouse");

        var response = await client.PostAsync(route, new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/Inventory/Receipts", "CancelReceipt", 101, 102)]
    [InlineData("/Inventory/Issues", "CancelIssue", 201, 202)]
    public async Task DocumentLists_RenderOneSecureFormPerCompletedMultilineDocument(
        string route,
        string action,
        int completedId,
        int cancelledId)
    {
        using var client = _factory.CreateInventoryClient("Warehouse");

        var response = await client.GetAsync(route);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var completedForm = Assert.Single(Regex.Matches(
                html,
                $"""<form(?=[^>]*action="[^"]*{action}/{completedId}")[\s\S]*?</form>""",
                RegexOptions.IgnoreCase)
            .Cast<Match>());
        Assert.Single(Regex.Matches(
                completedForm.Value,
                "name=\"__RequestVerificationToken\"",
                RegexOptions.IgnoreCase)
            .Cast<Match>());
        Assert.DoesNotContain($"/{action}/{cancelledId}", html);
        Assert.Contains("""<span class="badge bg-danger">Đã hủy</span>""", html);

        var body = Assert.Single(Regex.Matches(
                html,
                """<tbody>([\s\S]*?)</tbody>""",
                RegexOptions.IgnoreCase)
            .Cast<Match>()).Groups[1].Value;
        var rows = Regex.Matches(body, """<tr\b[\s\S]*?</tr>""", RegexOptions.IgnoreCase)
            .Cast<Match>()
            .ToList();
        Assert.Equal(4, rows.Count);
        Assert.Equal(
            new[] { 7, 7, 9, 9 },
            rows.Select(row => Regex.Matches(row.Value, """<td\b""", RegexOptions.IgnoreCase).Count)
                .Order());
        Assert.Equal(4, Regex.Matches(body, "rowspan=\"2\"", RegexOptions.IgnoreCase).Count);
    }
}

public sealed class InventoryCancellationWebApplicationFactory :
    WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"inventory-cancellation-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("DatabaseInitialization:Enabled", "false");
        builder.UseSetting(
            "Jwt:SigningKey",
            "test-only-inventory-cancellation-key-32-bytes");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme =
                        InventoryTestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme =
                        InventoryTestAuthenticationHandler.SchemeName;
                    options.DefaultForbidScheme =
                        InventoryTestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, InventoryTestAuthenticationHandler>(
                    InventoryTestAuthenticationHandler.SchemeName,
                    _ => { });
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Seed(context);
        return host;
    }

    public HttpClient CreateInventoryClient(string? role = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        if (role is not null)
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    InventoryTestAuthenticationHandler.SchemeName);
            client.DefaultRequestHeaders.Add(
                InventoryTestAuthenticationHandler.RolesHeader,
                role);
        }
        return client;
    }

    private static void Seed(ApplicationDbContext context)
    {
        var supplier = new Supplier { Code = "SUP", Name = "Supplier" };
        var customer = new Customer { Code = "CUS", Name = "Customer" };
        var product = new Product { Code = "P", Name = "Product" };
        var secondProduct = new Product { Code = "P2", Name = "Product 2" };
        var location = new Location
        {
            Code = "LOC",
            Name = "Location",
            Zone = new Zone { Code = "ZONE", Name = "Zone" }
        };
        var secondLocation = new Location
        {
            Code = "LOC2",
            Name = "Location 2",
            Zone = new Zone { Code = "ZONE2", Name = "Zone 2" }
        };
        var lot = new Lot { LotNo = "LOT", Product = product };
        var secondLot = new Lot { LotNo = "LOT2", Product = secondProduct };

        context.GoodsReceipts.AddRange(
            new GoodsReceipt
            {
                Id = 101,
                ReceiptNo = "GR-COMPLETED",
                ReceiptDate = new DateTime(2026, 7, 25),
                Supplier = supplier,
                Status = DocumentStatus.Completed,
                Lines =
                {
                    new GoodsReceiptLine
                    {
                        Product = product,
                        Location = location,
                        LotNo = "GR-LOT-1",
                        Qty = 2
                    },
                    new GoodsReceiptLine
                    {
                        Product = secondProduct,
                        Location = secondLocation,
                        LotNo = "GR-LOT-2",
                        Qty = 3
                    }
                }
            },
            new GoodsReceipt
            {
                Id = 102,
                ReceiptNo = "GR-CANCELLED",
                ReceiptDate = new DateTime(2026, 7, 24),
                Supplier = supplier,
                Status = DocumentStatus.Cancelled,
                Lines =
                {
                    new GoodsReceiptLine
                    {
                        Product = product,
                        Location = location,
                        LotNo = "GR-LOT-3",
                        Qty = 1
                    },
                    new GoodsReceiptLine
                    {
                        Product = secondProduct,
                        Location = secondLocation,
                        LotNo = "GR-LOT-4",
                        Qty = 1
                    }
                }
            });
        context.GoodsIssues.AddRange(
            new GoodsIssue
            {
                Id = 201,
                IssueNo = "GI-COMPLETED",
                IssueDate = new DateTime(2026, 7, 25),
                Customer = customer,
                Status = DocumentStatus.Completed,
                Lines =
                {
                    new GoodsIssueLine
                    {
                        Product = product,
                        Lot = lot,
                        Location = location,
                        Qty = 2
                    },
                    new GoodsIssueLine
                    {
                        Product = secondProduct,
                        Lot = secondLot,
                        Location = secondLocation,
                        Qty = 3
                    }
                }
            },
            new GoodsIssue
            {
                Id = 202,
                IssueNo = "GI-CANCELLED",
                IssueDate = new DateTime(2026, 7, 24),
                Customer = customer,
                Status = DocumentStatus.Cancelled,
                Lines =
                {
                    new GoodsIssueLine
                    {
                        Product = product,
                        Lot = lot,
                        Location = location,
                        Qty = 1
                    },
                    new GoodsIssueLine
                    {
                        Product = secondProduct,
                        Lot = secondLot,
                        Location = secondLocation,
                        Qty = 1
                    }
                }
            });
        var ledgerTime = new DateTime(2026, 7, 26, 1, 0, 0, DateTimeKind.Utc);
        for (var index = 1; index <= 51; index++)
        {
            context.StockTransactions.Add(new StockTransaction
            {
                Id = 300 + index,
                Type = TransactionType.Receipt,
                Product = product,
                Lot = lot,
                Location = location,
                Qty = 1m,
                QtyAfter = index,
                ValuationRate = 2m,
                TransactionDate = ledgerTime.AddMinutes(index),
                UserId = "ledger-user",
                ReferenceNo = $"REF-{index:000}"
            });
        }
        context.SaveChanges();
    }
}

public sealed class InventoryTestAuthenticationHandler :
    AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "InventoryTest";
    public const string RolesHeader = "X-Test-Roles";

    public InventoryTestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authorization) ||
            authorization != SchemeName)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "inventory-test-user")
        };
        if (Request.Headers.TryGetValue(RolesHeader, out var roles))
        {
            claims.AddRange(roles
                .SelectMany(value => value?.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    ?? [])
                .Select(role => new Claim(ClaimTypes.Role, role.Trim())));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(principal, SchemeName)));
    }
}
