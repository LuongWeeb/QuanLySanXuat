# Kế hoạch thực hiện: Giai đoạn 1 - Nền tảng, Phân quyền & Danh mục (Foundation, Identity & Master Data)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Khởi tạo dự án ASP.NET Core MVC, thiết lập CSDL SQL Server, cơ chế phân quyền (Cookie + JWT) và xây dựng giao diện quản lý danh mục (UOM, Product, Warehouse/Zone/Location, Supplier, Customer).

**Architecture:** Sử dụng kiến trúc Single-Project Monolith với mô hình 3 lớp (Controller -> Service -> Repository -> EF Core DbContext). Dùng ASP.NET Core Identity tích hợp sẵn để phân quyền RBAC.

**Tech Stack:** .NET 8.0 SDK, ASP.NET Core MVC, Entity Framework Core 8.0, SQL Server, SignalR, Bootstrap 5, JWT Bearer Token, Microsoft.AspNetCore.Identity.EntityFrameworkCore.

## Global Constraints
- Hệ điều hành: Windows
- Tên dự án: WmsMes.Web
- Công nghệ: ASP.NET Core MVC (.NET 8), EF Core, SQL Server, Bootstrap 5, Razor Views.
- Cơ chế bảo mật: Cookie Authentication cho Giao diện MVC, JWT Bearer Authentication cho API `/api/v1/`.
- Không được phép sử dụng tồn kho âm.
- Seed data mặc định phải đầy đủ các vai trò và người dùng mẫu để test.

---

### Task 1: Khởi tạo dự án MVC và cấu hình cơ bản

**Files:**
- Create: `WmsMes.Web.csproj`
- Create: `Program.cs`
- Create: `appsettings.json`

**Interfaces:**
- Produces: Dự án ASP.NET Core MVC .NET 8 có thể khởi chạy và hiển thị trang chủ mặc định.

- [ ] **Step 1: Khởi tạo dự án bằng dotnet CLI**

Run: `dotnet new mvc -o . --force`
Expected: Tạo thành công các file template MVC của .NET 8 tại thư mục gốc.

- [ ] **Step 2: Chạy thử dự án để xác nhận hoạt động**

Run: `dotnet run`
Expected: Dự án build thành công và chạy tại `http://localhost:5000` hoặc `https://localhost:5001`.

- [ ] **Step 3: Commit code**

Run:
```bash
git add .
git commit -m "chore: scaffold asp.net core mvc project"
```

---

### Task 2: Cài đặt NuGet Packages và cấu hình DbContext

**Files:**
- Modify: `WmsMes.Web.csproj`
- Create: `Data/ApplicationDbContext.cs`
- Modify: `appsettings.json`
- Modify: `Program.cs`

**Interfaces:**
- Consumes: Dự án MVC từ Task 1
- Produces: Lớp `ApplicationDbContext` kết nối đến SQL Server qua Connection String.

- [ ] **Step 1: Cài đặt các gói NuGet cần thiết**

Run:
```powershell
dotnet add package Microsoft.EntityFrameworkCore.SqlServer --version 8.0.0
dotnet add package Microsoft.EntityFrameworkCore.Design --version 8.0.0
dotnet add package Microsoft.EntityFrameworkCore.Tools --version 8.0.0
dotnet add package Microsoft.AspNetCore.Identity.EntityFrameworkCore --version 8.0.0
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer --version 8.0.0
```
Expected: Cài đặt thành công các gói thư viện mà không có lỗi xung đột.

- [ ] **Step 2: Cập nhật connection string trong appsettings.json**

Sửa `appsettings.json` để thêm chuỗi kết nối SQL Server LocalDB:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=WmsMesDb;Trusted_Connection=True;MultipleActiveResultSets=true"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

- [ ] **Step 3: Tạo lớp ApplicationDbContext**

Tạo file `Data/ApplicationDbContext.cs`:
```csharp
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace WmsMes.Web.Data
{
    public class ApplicationDbContext : IdentityDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
        }
    }
}
```

- [ ] **Step 4: Đăng ký DbContext trong Program.cs**

Sửa `Program.cs` để thêm dịch vụ DbContext:
```csharp
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();
```

- [ ] **Step 5: Kiểm tra build dự án**

Run: `dotnet build`
Expected: Build thành công (Build Succeeded).

- [ ] **Step 6: Commit code**

Run:
```bash
git add WmsMes.Web.csproj appsettings.json Data/ApplicationDbContext.cs Program.cs
git commit -m "feat: configure dbconnection and add applicationdbcontext"
```

---

### Task 3: Cấu hình Identity, Phân quyền (RBAC) và Xác thực Hybrid

**Files:**
- Create: `Domain/Entities/ApplicationUser.cs`
- Create: `Domain/Entities/ApplicationRole.cs`
- Modify: `Data/ApplicationDbContext.cs`
- Modify: `Program.cs`
- Create: `Controllers/AuthController.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext` từ Task 2
- Produces: `ApplicationUser` và `ApplicationRole` được cấu hình trong DB. Controller hỗ trợ xác thực bằng Cookie (Web) và JWT (API).

- [ ] **Step 1: Tạo các thực thể ApplicationUser và ApplicationRole**

Tạo `Domain/Entities/ApplicationUser.cs`:
```csharp
using Microsoft.AspNetCore.Identity;
using System;

namespace WmsMes.Web.Domain.Entities
{
    public class ApplicationUser : IdentityUser
    {
        public string FullName { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
```

Tạo `Domain/Entities/ApplicationRole.cs`:
```csharp
using Microsoft.AspNetCore.Identity;

namespace WmsMes.Web.Domain.Entities
{
    public class ApplicationRole : IdentityRole
    {
    }
}
```

- [ ] **Step 2: Cập nhật ApplicationDbContext**

Sửa `Data/ApplicationDbContext.cs` để thừa kế từ `IdentityDbContext<ApplicationUser, ApplicationRole, string>`:
```csharp
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Domain.Entities;

namespace WmsMes.Web.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }
    }
}
```

- [ ] **Step 3: Đăng ký Identity và cấu hình Hybrid Authentication trong Program.cs**

Sửa `Program.cs` để cấu hình Identity cùng với Cookie và JWT:
```csharp
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options => {
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// Configure Authentication (Cookie & JWT)
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    options.LoginPath = "/Auth/Login";
    options.AccessDeniedPath = "/Auth/AccessDenied";
})
.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = "WmsMesServer",
        ValidAudience = "WmsMesClient",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("<external-signing-key-at-least-32-bytes>"))
    };
});

var app = builder.Build();

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
```

- [ ] **Step 4: Tạo migration cơ sở dữ liệu ban đầu**

Chạy lệnh để cài đặt và sinh migrations:
Run: `dotnet ef migrations add InitialIdentitySetup -o Data/Migrations`
Expected: Tạo thành công file migration chứa cấu trúc bảng Identity của ASP.NET.

- [ ] **Step 5: Cập nhật database**

Run: `dotnet ef database update`
Expected: Database `WmsMesDb` được tạo trong SQL Server cục bộ.

- [ ] **Step 6: Commit code**

Run:
```bash
git add Domain/Entities/ Data/ ApplicationDbContext.cs Program.cs Data/Migrations/
git commit -m "feat: add identity entities and hybrid authentication"
```

---

### Task 4: Triển khai Seed Data ban đầu cho Phân quyền (RBAC)

**Files:**
- Create: `Data/DbSeeder.cs`
- Modify: `Program.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext`, `UserManager<ApplicationUser>`, `RoleManager<ApplicationRole>`
- Produces: Các vai trò (`Admin`, `Manager`, `Planner`, etc.) và người dùng mẫu được chèn tự động vào CSDL khi ứng dụng chạy.

- [ ] **Step 1: Tạo lớp DbSeeder**

Tạo file `Data/DbSeeder.cs`:
```csharp
using Microsoft.AspNetCore.Identity;
using System.Threading.Tasks;
using WmsMes.Web.Domain.Entities;

namespace WmsMes.Web.Data
{
    public static class DbSeeder
    {
        public static async Task SeedRolesAndUsersAsync(RoleManager<ApplicationRole> roleManager, UserManager<ApplicationUser> userManager)
        {
            string[] roles = { "Admin", "Manager", "Planner", "Warehouse", "Worker", "QC", "Director" };
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new ApplicationRole { Name = role });
                }
            }

            // Seed Admin User
            await CreateUserWithRoleAsync(userManager, "admin@wmsmes.com", "Admin User", "Admin", "Password123!");
            // Seed Manager User
            await CreateUserWithRoleAsync(userManager, "manager@wmsmes.com", "Production Manager", "Manager", "Password123!");
            // Seed Planner User
            await CreateUserWithRoleAsync(userManager, "planner@wmsmes.com", "Production Planner", "Planner", "Password123!");
            // Seed Warehouse User
            await CreateUserWithRoleAsync(userManager, "warehouse@wmsmes.com", "Warehouse Staff", "Warehouse", "Password123!");
            // Seed Worker User
            await CreateUserWithRoleAsync(userManager, "worker@wmsmes.com", "Production Worker", "Worker", "Password123!");
            // Seed QC User
            await CreateUserWithRoleAsync(userManager, "qc@wmsmes.com", "QC Staff", "QC", "Password123!");
            // Seed Director User
            await CreateUserWithRoleAsync(userManager, "director@wmsmes.com", "Director View Only", "Director", "Password123!");
        }

        private static async Task CreateUserWithRoleAsync(UserManager<ApplicationUser> userManager, string email, string fullName, string role, string password)
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user == null)
            {
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
    }
}
```

- [ ] **Step 2: Kích hoạt Seeder trong Program.cs khi khởi chạy ứng dụng**

Sửa `Program.cs` để gọi `DbSeeder` trước khi `app.Run()`:
```csharp
// Chèn đoạn này vào trước app.Run()
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        await DbSeeder.SeedRolesAndUsersAsync(roleManager, userManager);
    }
    catch (System.Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}
```

- [ ] **Step 3: Chạy ứng dụng để dữ liệu mẫu được nạp vào DB**

Run: `dotnet run`
Expected: Dự án khởi chạy không lỗi. Nếu kết nối CSDL qua SSMS, kiểm tra bảng `AspNetUsers` và `AspNetRoles` sẽ thấy đầy đủ dữ liệu người dùng và vai trò đã được nạp thành công.

- [ ] **Step 4: Commit code**

Run:
```bash
git add Data/DbSeeder.cs Program.cs
git commit -m "feat: seed roles and default users in database"
```

---

### Task 5: Triển khai các thực thể Danh mục Sản phẩm (UOM, Product)

**Files:**
- Create: `Domain/Entities/UnitOfMeasure.cs`
- Create: `Domain/Entities/Product.cs`
- Create: `Domain/Enums/ProductType.cs`
- Modify: `Data/ApplicationDbContext.cs`

**Interfaces:**
- Consumes: CSDL từ Task 3
- Produces: Bảng `UnitOfMeasures` và `Products` trong SQL Server thông qua EF Core.

- [ ] **Step 1: Tạo ProductType Enum**

Tạo `Domain/Enums/ProductType.cs`:
```csharp
namespace WmsMes.Web.Domain.Enums
{
    public enum ProductType
    {
        RawMaterial = 0,
        WIP = 1,
        FinishedGood = 2
    }
}
```

- [ ] **Step 2: Tạo UnitOfMeasure Entity**

Tạo `Domain/Entities/UnitOfMeasure.cs`:
```csharp
using System.ComponentModel.DataAnnotations;

namespace WmsMes.Web.Domain.Entities
{
    public class UnitOfMeasure
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
    }
}
```

- [ ] **Step 3: Tạo Product Entity**

Tạo `Domain/Entities/Product.cs`:
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.Domain.Entities
{
    public class Product
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [MaxLength(250)]
        public string Name { get; set; } = string.Empty;

        [Required]
        public ProductType Type { get; set; }

        public bool IsManufactured { get; set; }

        [Required]
        public int BaseUomId { get; set; }

        [ForeignKey("BaseUomId")]
        public virtual UnitOfMeasure? BaseUom { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal MinStock { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal MaxStock { get; set; }

        public bool IsLotTracked { get; set; }

        public int? ShelfLifeDays { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
```

- [ ] **Step 4: Cập nhật DbContext để thêm DbSets & Cấu hình Unique Index**

Sửa `Data/ApplicationDbContext.cs`:
```csharp
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Domain.Entities;

namespace WmsMes.Web.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<UnitOfMeasure> UnitOfMeasures { get; set; }
        public DbSet<Product> Products { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<UnitOfMeasure>()
                .HasIndex(u => u.Code)
                .IsUnique();

            builder.Entity<Product>()
                .HasIndex(p => p.Code)
                .IsUnique();
        }
    }
}
```

- [ ] **Step 5: Thêm migration mới và cập nhật database**

Run: `dotnet ef migrations add AddUomAndProductEntities -o Data/Migrations`
Expected: Tạo thành công migration mới.
Run: `dotnet ef database update`
Expected: Tạo thành công các bảng `UnitOfMeasures` và `Products` có ràng buộc khóa ngoại và unique index.

- [ ] **Step 6: Commit code**

Run:
```bash
git add Domain/Enums/ Domain/Entities/ Data/ApplicationDbContext.cs Data/Migrations/
git commit -m "feat: implement unitofmeasure and product entities with migration"
```

---

### Task 6: Triển khai các thực thể Cấu trúc Kho (Warehouse, Zone, Location)

**Files:**
- Create: `Domain/Entities/Warehouse.cs`
- Create: `Domain/Entities/Zone.cs`
- Create: `Domain/Entities/Location.cs`
- Modify: `Data/ApplicationDbContext.cs`

**Interfaces:**
- Consumes: CSDL từ Task 5
- Produces: Các bảng `Warehouses`, `Zones`, và `Locations` có ràng buộc phân cấp và cơ chế Cascade Delete.

- [ ] **Step 1: Tạo Warehouse Entity**

Tạo `Domain/Entities/Warehouse.cs`:
```csharp
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace WmsMes.Web.Domain.Entities
{
    public class Warehouse
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public virtual ICollection<Zone> Zones { get; set; } = new List<Zone>();
    }
}
```

- [ ] **Step 2: Tạo Zone Entity**

Tạo `Domain/Entities/Zone.cs`:
```csharp
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities
{
    public class Zone
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [Required]
        public int WarehouseId { get; set; }

        [ForeignKey("WarehouseId")]
        public virtual Warehouse? Warehouse { get; set; }

        public bool IsActive { get; set; } = true;

        public virtual ICollection<Location> Locations { get; set; } = new List<Location>();
    }
}
```

- [ ] **Step 3: Tạo Location Entity**

Tạo `Domain/Entities/Location.cs`:
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities
{
    public class Location
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [Required]
        public int ZoneId { get; set; }

        [ForeignKey("ZoneId")]
        public virtual Zone? Zone { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
```

- [ ] **Step 4: Cập nhật ApplicationDbContext**

Cấu hình Unique Index và Cascade Delete trong `Data/ApplicationDbContext.cs`:
```csharp
// Thêm vào DbContext
public DbSet<Warehouse> Warehouses { get; set; }
public DbSet<Zone> Zones { get; set; }
public DbSet<Location> Locations { get; set; }

// Bổ sung trong OnModelCreating:
builder.Entity<Warehouse>()
    .HasIndex(w => w.Code)
    .IsUnique();

builder.Entity<Zone>()
    .HasIndex(z => z.Code)
    .IsUnique();

builder.Entity<Location>()
    .HasIndex(l => l.Code)
    .IsUnique();

builder.Entity<Zone>()
    .HasOne(z => z.Warehouse)
    .WithMany(w => w.Zones)
    .HasForeignKey(z => z.WarehouseId)
    .OnDelete(DeleteBehavior.Cascade);

builder.Entity<Location>()
    .HasOne(l => l.Zone)
    .WithMany(z => z.Locations)
    .HasForeignKey(l => l.ZoneId)
    .OnDelete(DeleteBehavior.Cascade);
```

- [ ] **Step 5: Tạo migration mới và cập nhật database**

Run: `dotnet ef migrations add AddWarehouseStructure -o Data/Migrations`
Expected: Tạo thành công file migration.
Run: `dotnet ef database update`
Expected: Bảng Warehouses, Zones, Locations được thiết lập đầy đủ khóa ngoại và khóa unique index.

- [ ] **Step 6: Commit code**

Run:
```bash
git add Domain/Entities/ Data/ApplicationDbContext.cs Data/Migrations/
git commit -m "feat: implement warehouse structure entities with migration"
```

---

### Task 7: Triển khai các thực thể Supplier và Customer

**Files:**
- Create: `Domain/Entities/Supplier.cs`
- Create: `Domain/Entities/Customer.cs`
- Modify: `Data/ApplicationDbContext.cs`

**Interfaces:**
- Consumes: CSDL từ Task 6
- Produces: Các bảng `Suppliers` và `Customers` trong CSDL.

- [ ] **Step 1: Tạo Supplier Entity**

Tạo `Domain/Entities/Supplier.cs`:
```csharp
using System.ComponentModel.DataAnnotations;

namespace WmsMes.Web.Domain.Entities
{
    public class Supplier
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [MaxLength(250)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Address { get; set; } = string.Empty;

        [MaxLength(20)]
        public string Phone { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Email { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
    }
}
```

- [ ] **Step 2: Tạo Customer Entity**

Tạo `Domain/Entities/Customer.cs`:
```csharp
using System.ComponentModel.DataAnnotations;

namespace WmsMes.Web.Domain.Entities
{
    public class Customer
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [MaxLength(250)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Address { get; set; } = string.Empty;

        [MaxLength(20)]
        public string Phone { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Email { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
    }
}
```

- [ ] **Step 3: Đăng ký trong ApplicationDbContext**

Sửa `Data/ApplicationDbContext.cs`:
```csharp
// Thêm vào DbContext
public DbSet<Supplier> Suppliers { get; set; }
public DbSet<Customer> Customers { get; set; }

// Bổ sung trong OnModelCreating:
builder.Entity<Supplier>()
    .HasIndex(s => s.Code)
    .IsUnique();

builder.Entity<Customer>()
    .HasIndex(c => c.Code)
    .IsUnique();
```

- [ ] **Step 4: Chạy migration và update CSDL**

Run: `dotnet ef migrations add AddSupplierAndCustomer -o Data/Migrations`
Expected: Tạo migration thành công.
Run: `dotnet ef database update`
Expected: Tạo bảng `Suppliers` và `Customers` trong CSDL.

- [ ] **Step 5: Commit code**

Run:
```bash
git add Domain/Entities/ Data/ApplicationDbContext.cs Data/Migrations/
git commit -m "feat: implement supplier and customer entities with migration"
```

---

### Task 8: Thiết lập Repository và Service Layer cho Danh mục (Product & Warehouse)

**Files:**
- Create: `Repositories/IGenericRepository.cs`
- Create: `Repositories/GenericRepository.cs`
- Create: `Services/IProductService.cs`
- Create: `Services/ProductService.cs`
- Modify: `Program.cs`
- Create: `WmsMes.Tests/ProductServiceTests.cs` (Project test phụ thuộc)

**Interfaces:**
- Consumes: `ApplicationDbContext` từ các Task trước.
- Produces: Service API để quản lý danh mục (Product, Warehouse) và Unit Test để tự động kiểm thử.

- [ ] **Step 1: Tạo Generic Repository Interface & Implementation**

Tạo `Repositories/IGenericRepository.cs`:
```csharp
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WmsMes.Web.Repositories
{
    public interface IGenericRepository<T> where T : class
    {
        Task<IEnumerable<T>> GetAllAsync();
        Task<T?> GetByIdAsync(int id);
        Task AddAsync(T entity);
        void Update(T entity);
        void Delete(T entity);
        Task SaveAsync();
    }
}
```

Tạo `Repositories/GenericRepository.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;
using WmsMes.Web.Data;

namespace WmsMes.Web.Repositories
{
    public class GenericRepository<T> : IGenericRepository<T> where T : class
    {
        protected readonly ApplicationDbContext _context;
        private readonly DbSet<T> _dbSet;

        public GenericRepository(ApplicationDbContext context)
        {
            _context = context;
            _dbSet = _context.Set<T>();
        }

        public async Task<IEnumerable<T>> GetAllAsync() => await _dbSet.ToListAsync();
        public async Task<T?> GetByIdAsync(int id) => await _dbSet.FindAsync(id);
        public async Task AddAsync(T entity) => await _dbSet.AddAsync(entity);
        public void Update(T entity) => _context.Entry(entity).State = EntityState.Modified;
        public void Delete(T entity) => _dbSet.Remove(entity);
        public async Task SaveAsync() => await _context.SaveChangesAsync();
    }
}
```

- [ ] **Step 2: Tạo Product Service Interface & Implementation**

Tạo `Services/IProductService.cs`:
```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using WmsMes.Web.Domain.Entities;

namespace WmsMes.Web.Services
{
    public interface IProductService
    {
        Task<IEnumerable<Product>> GetAllProductsAsync();
        Task<Product?> GetProductByIdAsync(int id);
        Task<bool> CreateProductAsync(Product product);
        Task UpdateProductAsync(Product product);
        Task DeleteProductAsync(int id);
    }
}
```

Tạo `Services/ProductService.cs` (chứa logic kiểm tra trùng lặp mã sản phẩm và validate UOM):
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Repositories;

namespace WmsMes.Web.Services
{
    public class ProductService : IProductService
    {
        private readonly IGenericRepository<Product> _productRepo;
        private readonly IGenericRepository<UnitOfMeasure> _uomRepo;

        public ProductService(IGenericRepository<Product> productRepo, IGenericRepository<UnitOfMeasure> uomRepo)
        {
            _productRepo = productRepo;
            _uomRepo = uomRepo;
        }

        public async Task<IEnumerable<Product>> GetAllProductsAsync()
        {
            var products = await _productRepo.GetAllAsync();
            foreach (var p in products)
            {
                p.BaseUom = await _uomRepo.GetByIdAsync(p.BaseUomId);
            }
            return products;
        }

        public async Task<Product?> GetProductByIdAsync(int id)
        {
            var product = await _productRepo.GetByIdAsync(id);
            if (product != null)
            {
                product.BaseUom = await _uomRepo.GetByIdAsync(product.BaseUomId);
            }
            return product;
        }

        public async Task<bool> CreateProductAsync(Product product)
        {
            // Validate Code uniqueness
            var existingProducts = await _productRepo.GetAllAsync();
            if (existingProducts.Any(p => p.Code.Equals(product.Code, StringComparison.OrdinalIgnoreCase)))
            {
                return false; // Code exists
            }

            // Verify UOM exists
            var uom = await _uomRepo.GetByIdAsync(product.BaseUomId);
            if (uom == null)
            {
                throw new ArgumentException("UOM does not exist.");
            }

            await _productRepo.AddAsync(product);
            await _productRepo.SaveAsync();
            return true;
        }

        public async Task UpdateProductAsync(Product product)
        {
            _productRepo.Update(product);
            await _productRepo.SaveAsync();
        }

        public async Task DeleteProductAsync(int id)
        {
            var product = await _productRepo.GetByIdAsync(id);
            if (product != null)
            {
                _productRepo.Delete(product);
                await _productRepo.SaveAsync();
            }
        }
    }
}
```

- [ ] **Step 3: Đăng ký Repositories và Services trong Program.cs**

Sửa `Program.cs` để cấu hình DI cho Repositories và Services:
```csharp
using WmsMes.Web.Repositories;
using WmsMes.Web.Services;

// Thêm vào trước builder.Build()
builder.Services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
builder.Services.AddScoped<IProductService, ProductService>();
```

- [ ] **Step 4: Tạo dự án Unit Tests xUnit để kiểm thử Service**

Tạo project kiểm thử xUnit và thêm các thư viện Mocking:
Run: `dotnet new xunit -o WmsMes.Tests`
Run: `dotnet add WmsMes.Tests reference WmsMes.Web.csproj`
Run: `dotnet add WmsMes.Tests package Moq`
Run: `dotnet sln add WmsMes.Tests`
Expected: Tạo dự án unit test thành công và thiết lập liên kết đến project chính.

- [ ] **Step 5: Viết Unit Test cho ProductService**

Tạo file `WmsMes.Tests/ProductServiceTests.cs` để test logic trùng mã SKU và UOM:
```csharp
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Repositories;
using WmsMes.Web.Services;

namespace WmsMes.Tests
{
    public class ProductServiceTests
    {
        [Fact]
        public async Task CreateProductAsync_ReturnsFalse_WhenCodeAlreadyExists()
        {
            // Arrange
            var mockProductRepo = new Mock<IGenericRepository<Product>>();
            var mockUomRepo = new Mock<IGenericRepository<UnitOfMeasure>>();

            mockProductRepo.Setup(r => r.GetAllAsync())
                .ReturnsAsync(new List<Product> { new Product { Code = "PROD01" } });

            var service = new ProductService(mockProductRepo.Object, mockUomRepo.Object);
            var newProduct = new Product { Code = "PROD01", BaseUomId = 1 };

            // Act
            var result = await service.CreateProductAsync(newProduct);

            // Assert
            Assert.False(result);
        }
    }
}
```

- [ ] **Step 6: Chạy kiểm thử tự động**

Run: `dotnet test`
Expected: Toàn bộ Unit Test chạy thành công và vượt qua (Test passed).

- [ ] **Step 7: Commit code**

Run:
```bash
git add Repositories/ Services/ Program.cs WmsMes.Tests/
git commit -m "feat: add generic repository, productservice and product unit tests"
```

---

### Task 9: Giao diện Đăng nhập và CRUD Danh mục (Razor Views với Slate-Blue Theme)

**Files:**
- Create: `Views/Auth/Login.cshtml`
- Create: `Views/Product/Index.cshtml`
- Create: `Views/Warehouse/Index.cshtml`
- Modify: `Views/Shared/_Layout.cshtml`
- Create: `Controllers/ProductController.cs`
- Create: `Controllers/WarehouseController.cs`

**Interfaces:**
- Consumes: `IProductService`, `IGenericRepository<Warehouse>` từ Task 8.
- Produces: Màn hình Login đẹp mắt, Màn hình danh sách Sản phẩm, Màn hình Sơ đồ cây nhà kho.

- [ ] **Step 1: Tạo Giao diện đăng nhập (Login View)**

Tạo file `Views/Auth/Login.cshtml` với giao diện đăng nhập tinh tế:
```html
@model WmsMes.Web.ViewModels.LoginViewModel
@{
    Layout = null;
}
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=device-width, initial-scale=initial-scale=1.0" />
    <title>Đăng nhập - WMS MES</title>
    <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/css/bootstrap.min.css" />
    <style>
        body {
            background: linear-gradient(135deg, #1e293b 0%, #0f172a 100%);
            color: #f8fafc;
            height: 100vh;
            display: flex;
            align-items: center;
            justify-content: center;
            font-family: 'Inter', sans-serif;
        }
        .login-card {
            background: rgba(30, 41, 59, 0.7);
            backdrop-filter: blur(10px);
            border: 1px solid rgba(255, 255, 255, 0.1);
            border-radius: 12px;
            padding: 2.5rem;
            width: 100%;
            max-width: 400px;
            box-shadow: 0 10px 25px rgba(0, 0, 0, 0.3);
        }
        .btn-primary {
            background-color: #3b82f6;
            border: none;
        }
        .btn-primary:hover {
            background-color: #2563eb;
        }
    </style>
</head>
<body>
    <div class="login-card">
        <h2 class="text-center mb-4">WMS & MES SMO</h2>
        <form asp-action="Login" asp-controller="Auth" method="post">
            <div class="mb-3">
                <label class="form-label">Email</label>
                <input type="email" name="Email" class="form-control bg-dark text-white border-secondary" required />
            </div>
            <div class="mb-3">
                <label class="form-label">Mật khẩu</label>
                <input type="password" name="Password" class="form-control bg-dark text-white border-secondary" required />
            </div>
            <button type="submit" class="btn btn-primary w-100 py-2 mt-2">Đăng Nhập</button>
        </form>
    </div>
</body>
</html>
```

- [ ] **Step 2: Tạo ViewModel và Controller cho Auth**

Tạo `ViewModels/LoginViewModel.cs`:
```csharp
namespace WmsMes.Web.ViewModels
{
    public class LoginViewModel
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
```

Tạo `Controllers/AuthController.cs`:
```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.ViewModels;

namespace WmsMes.Web.Controllers
{
    public class AuthController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;

        public AuthController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager)
        {
            _signInManager = signInManager;
            _userManager = userManager;
        }

        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (ModelState.IsValid)
            {
                var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, false, false);
                if (result.Succeeded)
                {
                    return RedirectToAction("Index", "Home");
                }
                ModelState.AddModelError(string.Empty, "Đăng nhập không thành công. Sai tài khoản hoặc mật khẩu.");
            }
            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Login", "Auth");
        }
    }
}
```

- [ ] **Step 3: Tạo giao diện CRUD sản phẩm (Razor View & Controller)**

Tạo `Controllers/ProductController.cs` (phân quyền cho Planner và Manager chỉnh sửa, các vai trò khác xem):
```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Services;

namespace WmsMes.Web.Controllers
{
    [Authorize]
    public class ProductController : Controller
    {
        private readonly IProductService _productService;

        public ProductController(IProductService productService)
        {
            _productService = productService;
        }

        public async Task<IActionResult> Index()
        {
            var products = await _productService.GetAllProductsAsync();
            return View(products);
        }
    }
}
```

Tạo file `Views/Product/Index.cshtml` hiển thị danh sách sản phẩm đẹp mắt với Bootstrap 5:
```html
@model IEnumerable<WmsMes.Web.Domain.Entities.Product>
@{
    ViewData["Title"] = "Danh mục sản phẩm (SKU)";
}

<div class="container-fluid py-4">
    <div class="d-flex justify-content-between align-items-center mb-4">
        <h2>Danh sách Sản phẩm (SKUs)</h2>
        @if (User.IsInRole("Admin") || User.IsInRole("Manager") || User.IsInRole("Planner"))
        {
            <button class="btn btn-primary" data-bs-toggle="modal" data-bs-target="#addProductModal">+ Thêm sản phẩm</button>
        }
    </div>

    <div class="card shadow-sm border-0 bg-dark text-white">
        <div class="card-body">
            <table class="table table-dark table-hover table-striped">
                <thead>
                    <tr>
                        <th>Mã SKU</th>
                        <th>Tên sản phẩm</th>
                        <th>Loại</th>
                        <th>Tồn tối thiểu</th>
                        <th>Tồn tối đa</th>
                        <th>Trạng thái</th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var item in Model)
                    {
                        <tr>
                            <td>@item.Code</td>
                            <td>@item.Name</td>
                            <td>@item.Type.ToString()</td>
                            <td>@item.MinStock</td>
                            <td>@item.MaxStock</td>
                            <td>@(item.IsActive ? "Đang hoạt động" : "Ngừng")</td>
                        </tr>
                    }
                </tbody>
            </table>
        </div>
    </div>
</div>
```

- [ ] **Step 4: Chạy thử để xác thực luồng đăng nhập và hiển thị danh sách**

Run: `dotnet run`
Expected: 
1. Truy cập `http://localhost:5000/Product` tự động chuyển hướng về `/Auth/Login`.
2. Đăng nhập với tài khoản `admin@wmsmes.com` / `Password123!` thành công và chuyển về trang chủ, sau đó có thể xem được danh sách sản phẩm (chưa có dòng dữ liệu nào).

- [ ] **Step 5: Commit code**

Run:
```bash
git add Views/ Controllers/ ViewModels/
git commit -m "feat: implement custom login UI and product list view"
```
