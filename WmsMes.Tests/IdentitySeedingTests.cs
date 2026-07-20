using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;

namespace WmsMes.Tests;

public class IdentitySeedingTests
{
    [Fact]
    public async Task ProductionIdentitySeed_CreatesRolesWithoutKnownDefaultUsers()
    {
        await using var services = CreateIdentityServices();
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        await DbSeeder.SeedRolesAsync(roleManager);

        Assert.Equal(7, await roleManager.Roles.CountAsync());
        Assert.Empty(await userManager.Users.ToListAsync());
        Assert.Null(await userManager.FindByEmailAsync("admin@wmsmes.com"));
    }

    [Fact]
    public async Task ExplicitDevelopmentDemoSeed_CreatesIntentionalDemoUsers()
    {
        await using var services = CreateIdentityServices();
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        await DbSeeder.SeedRolesAsync(roleManager);
        await DbSeeder.SeedDemoUsersAsync(userManager);

        var admin = await userManager.FindByEmailAsync("admin@wmsmes.com");
        Assert.NotNull(admin);
        Assert.True(await userManager.CheckPasswordAsync(admin, "Password123!"));
        Assert.True(await userManager.IsInRoleAsync(admin, "Admin"));
    }

    [Theory]
    [InlineData("Production", true, false)]
    [InlineData("Development", false, false)]
    [InlineData("Development", true, true)]
    public void DemoUserSeeding_RequiresDevelopmentAndExplicitOptIn(
        string environmentName,
        bool configured,
        bool expected)
    {
        var environment = new TestWebHostEnvironment { EnvironmentName = environmentName };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseInitialization:SeedDemoUsers"] = configured.ToString()
            })
            .Build();

        Assert.Equal(expected, StartupDatabaseInitializer.ShouldSeedDemoUsers(environment, configuration));
    }

    private static ServiceProvider CreateIdentityServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequiredLength = 6;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = false;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();
        return services.BuildServiceProvider();
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "WmsMes.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = string.Empty;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
