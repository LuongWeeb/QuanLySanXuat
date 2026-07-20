using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using WmsMes.Web.Authentication;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;

namespace WmsMes.Tests;

public class JwtAuthenticationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("too-short")]
    public void JwtOptionsValidator_RejectsMissingOrWeakSigningKeys(string signingKey)
    {
        var result = new JwtOptionsValidator().Validate(null, new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            SigningKey = signingKey
        });

        Assert.True(result.Failed);
        Assert.Contains("at least 32 UTF-8 bytes", result.FailureMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("too-short")]
    public void NormalRuntimeStartup_FailsClearlyForMissingOrWeakSigningKey(string signingKey)
    {
        using var factory = new InvalidJwtWebApplicationFactory(signingKey);

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Jwt:SigningKey", exception.ToString());
        Assert.Contains("at least 32 UTF-8 bytes", exception.ToString());
    }

    [Fact]
    public async Task ApiLogin_IssuesTokenAcceptedByProtectedEndpoint()
    {
        await using var factory = new JwtWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = JwtWebApplicationFactory.UserEmail,
            password = JwtWebApplicationFactory.UserPassword
        });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var loginBody = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = loginBody.RootElement.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var protectedResponse = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.OK, protectedResponse.StatusCode);
        using var protectedBody = JsonDocument.Parse(await protectedResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            JwtWebApplicationFactory.UserEmail,
            protectedBody.RootElement.GetProperty("email").GetString());
        Assert.Contains(
            "Admin",
            protectedBody.RootElement.GetProperty("roles").EnumerateArray().Select(role => role.GetString()));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void SigningKeyFile_NormalizesOneTerminalLineEndingBeforeOptionsBinding(string lineEnding)
    {
        const string signingKey = " test-only JWT key's;intentional spaces remain ";
        var signingKeyFile = Path.GetTempFileName();
        File.WriteAllText(signingKeyFile, signingKey + lineEnding);
        try
        {
            using var factory = new JwtSecretFileWebApplicationFactory(signingKeyFile);
            using var client = factory.CreateClient();

            var options = factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;

            Assert.Equal(signingKey, options.SigningKey);
        }
        finally
        {
            File.Delete(signingKeyFile);
        }
    }
}

public sealed class JwtSecretFileWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _signingKeyFile;

    public JwtSecretFileWebApplicationFactory(string signingKeyFile)
    {
        _signingKeyFile = signingKeyFile;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("DatabaseInitialization:Enabled", "false");
        builder.UseSetting("Jwt:SigningKeyFile", _signingKeyFile);
    }
}

public sealed class InvalidJwtWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _signingKey;

    public InvalidJwtWebApplicationFactory(string signingKey)
    {
        _signingKey = signingKey;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("DatabaseInitialization:Enabled", "false");
        builder.UseSetting("Jwt:SigningKey", _signingKey);
    }
}

public sealed class JwtWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string UserEmail = "jwt-test@example.invalid";
    public const string UserPassword = "JwtTest123!";
    private const string TestSigningKey = "test-only-signing-key-with-at-least-32-bytes";
    private readonly string _databaseName = $"jwt-tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("DatabaseInitialization:Enabled", "false");
        builder.UseSetting("Jwt:SigningKey", TestSigningKey);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        roleManager.CreateAsync(new ApplicationRole { Name = "Admin" }).GetAwaiter().GetResult();
        var user = new ApplicationUser
        {
            UserName = UserEmail,
            Email = UserEmail,
            EmailConfirmed = true,
            IsActive = true,
            FullName = "JWT Integration Test"
        };
        var createResult = userManager.CreateAsync(user, UserPassword).GetAwaiter().GetResult();
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(string.Join(", ", createResult.Errors.Select(error => error.Description)));
        }

        userManager.AddToRoleAsync(user, "Admin").GetAwaiter().GetResult();
        return host;
    }
}
