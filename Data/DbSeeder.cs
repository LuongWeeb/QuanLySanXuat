using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Services;

namespace WmsMes.Web.Data;

public static class DbSeeder
{
    public static async Task SeedRolesAndUsersAsync(
        RoleManager<ApplicationRole> roleManager,
        UserManager<ApplicationUser> userManager)
    {
        string[] roles = { "Admin", "Manager", "Planner", "Warehouse", "Worker", "QC", "Director" };

        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new ApplicationRole { Name = role });
            }
        }

        await CreateUserWithRoleAsync(userManager, "admin@wmsmes.com", "Admin User", "Admin", "Password123!");
        await CreateUserWithRoleAsync(userManager, "manager@wmsmes.com", "Production Manager", "Manager", "Password123!");
        await CreateUserWithRoleAsync(userManager, "planner@wmsmes.com", "Production Planner", "Planner", "Password123!");
        await CreateUserWithRoleAsync(userManager, "warehouse@wmsmes.com", "Warehouse Staff", "Warehouse", "Password123!");
        await CreateUserWithRoleAsync(userManager, "worker@wmsmes.com", "Production Worker", "Worker", "Password123!");
        await CreateUserWithRoleAsync(userManager, "qc@wmsmes.com", "QC Staff", "QC", "Password123!");
        await CreateUserWithRoleAsync(userManager, "director@wmsmes.com", "Director View Only", "Director", "Password123!");
    }

    public static async Task SeedQcInfrastructureAsync(ApplicationDbContext context)
    {
        var warehouse = await context.Warehouses.FirstOrDefaultAsync(w => w.Code == "QC");
        if (warehouse is null)
        {
            warehouse = new Warehouse
            {
                Code = "QC",
                Name = "Quality Control Warehouse"
            };
            context.Warehouses.Add(warehouse);
            await context.SaveChangesAsync();
        }

        var zone = await context.Zones.FirstOrDefaultAsync(z => z.Code == "QUAR" && z.WarehouseId == warehouse.Id);
        if (zone is null)
        {
            zone = new Zone
            {
                WarehouseId = warehouse.Id,
                Code = "QUAR",
                Name = "Quarantine Zone"
            };
            context.Zones.Add(zone);
            await context.SaveChangesAsync();
        }

        var location = await context.Locations.FirstOrDefaultAsync(l => l.Code == QcService.QuarantineLocationCode);
        if (location is null)
        {
            context.Locations.Add(new Location
            {
                ZoneId = zone.Id,
                Code = QcService.QuarantineLocationCode,
                Name = "QC Quarantine"
            });
            await context.SaveChangesAsync();
        }
    }

    private static async Task CreateUserWithRoleAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        string fullName,
        string role,
        string password)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is not null)
        {
            return;
        }

        user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = fullName,
            EmailConfirmed = true,
            IsActive = true
        };

        var result = await userManager.CreateAsync(user, password);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(user, role);
        }
    }
}
