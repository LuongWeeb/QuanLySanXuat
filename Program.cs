using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuestPDF.Infrastructure;
using WmsMes.Web.Authentication;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Hubs;
using WmsMes.Web.Repositories;
using WmsMes.Web.Services;

var builder = WebApplication.CreateBuilder(args);
var initializeDatabaseOnly = DatabaseInitializationPolicy.IsOneShot(args);
var provisionDatabaseOnly = args.Any(argument =>
    string.Equals(argument, "--provision-database", StringComparison.OrdinalIgnoreCase));

if (provisionDatabaseOnly)
{
    await SqlServerDatabaseProvisioner.ProvisionFromConfigurationAsync(builder.Configuration);
    return;
}

var jwtSigningKeyFile = builder.Configuration["Jwt:SigningKeyFile"];
if (!string.IsNullOrWhiteSpace(jwtSigningKeyFile))
{
    builder.Configuration["Jwt:SigningKey"] = SecretFile.ReadRequired(
        jwtSigningKeyFile,
        "Jwt:SigningKeyFile");
}

QuestPDF.Settings.License = LicenseType.Community;

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton(_ => BusinessTimeZoneResolver.Resolve(
    builder.Configuration.GetValue<string>("BusinessTimeZone") ?? "Asia/Ho_Chi_Minh"));

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(DatabaseConnectionStringFactory.Resolve(builder.Configuration)));

builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 6;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Auth/Login";
    options.AccessDeniedPath = "/Auth/AccessDenied";
});

builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
builder.Services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearerOptions>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();

builder.Services.AddAuthentication()
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, _ => { });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "WMS - MES System API",
        Version = "v1",
        Description = "Hệ thống Quản lý Kho & Điều hành Sản xuất WMS-MES API Documentation"
    });

    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Nhập JWT Token theo định dạng: Bearer {token}"
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<ICycleCountService, CycleCountService>();
builder.Services.AddScoped<IMrpService, MrpService>();
builder.Services.AddScoped<IProductionPlanService, ProductionPlanService>();
builder.Services.AddScoped<IPurchaseRequestService, PurchaseRequestService>();
builder.Services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
builder.Services.AddScoped<ISalesOrderService, SalesOrderService>();
builder.Services.AddScoped<IWorkOrderService, WorkOrderService>();
builder.Services.AddScoped<ICostingService, CostingService>();
builder.Services.AddScoped<IQcService, QcService>();
builder.Services.AddScoped<ITraceabilityService, TraceabilityService>();
builder.Services.AddScoped<IReportExportService, ReportExportService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "WMS MES API v1");
    });
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHub<InventoryHub>("/inventoryHub");
app.MapHub<ProductionHub>("/productionHub");
app.MapHub<QualityHub>("/qualityHub");

if (DatabaseInitializationPolicy.ShouldInitialize(args, builder.Configuration))
{
    await StartupDatabaseInitializer.InitializeAsync(app.Services, app.Logger);
}

if (initializeDatabaseOnly)
{
    return;
}

app.Run();

public partial class Program;
