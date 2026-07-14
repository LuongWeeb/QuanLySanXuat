using Microsoft.AspNetCore.Identity;
using WmsMes.Web.Domain.Entities;

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
