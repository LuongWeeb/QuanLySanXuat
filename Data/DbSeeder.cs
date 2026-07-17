using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
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

    public static async Task SeedUnitOfMeasuresAsync(ApplicationDbContext context)
    {
        await AddMissingAsync(context, context.UnitOfMeasures, u => u.Code,
            new UnitOfMeasure { Code = "KG", Name = "Kilogram" },
            new UnitOfMeasure { Code = "PCS", Name = "Cái/Chiếc" },
            new UnitOfMeasure { Code = "LITER", Name = "Lít" },
            new UnitOfMeasure { Code = "BAG", Name = "Bao" });
    }

    public static async Task SeedWarehouseStructureAsync(ApplicationDbContext context)
    {
        var warehouse = await context.Warehouses.SingleOrDefaultAsync(w => w.Code == "WH01");
        if (warehouse is null)
        {
            warehouse = new Warehouse { Code = "WH01", Name = "Kho chính Nhà máy" };
            context.Warehouses.Add(warehouse);
            await context.SaveChangesAsync();
        }

        async Task<Zone> GetOrCreateZoneAsync(string code, string name)
        {
            var zone = await context.Zones.SingleOrDefaultAsync(z => z.Code == code);
            if (zone is not null) return zone;
            zone = new Zone { WarehouseId = warehouse.Id, Code = code, Name = name };
            context.Zones.Add(zone);
            await context.SaveChangesAsync();
            return zone;
        }

        var rawZone = await GetOrCreateZoneAsync("Z-RAW", "Khu nguyên vật liệu");
        var wipZone = await GetOrCreateZoneAsync("Z-WIP", "Khu bán thành phẩm");
        var fgZone = await GetOrCreateZoneAsync("Z-FG", "Khu thành phẩm");
        await AddMissingAsync(context, context.Locations, l => l.Code,
            new Location { ZoneId = rawZone.Id, Code = "LOC-RAW-01", Name = "Kệ nguyên liệu 01" },
            new Location { ZoneId = rawZone.Id, Code = "LOC-RAW-02", Name = "Kệ nguyên liệu 02" },
            new Location { ZoneId = wipZone.Id, Code = "LOC-WIP-01", Name = "Khu vực BTP 01" },
            new Location { ZoneId = fgZone.Id, Code = "LOC-FG-01", Name = "Kệ thành phẩm 01" });
    }

    public static async Task SeedComprehensiveSampleDataAsync(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager)
    {
        await SeedMasterDataAsync(context);
        await SeedInventoryDataAsync(context);
        await SeedWorkOrdersAsync(context);
    }

    public static async Task SeedMasterDataAsync(ApplicationDbContext context)
    {
        await AddMissingAsync(context, context.Suppliers, s => s.Code,
            new Supplier { Code = "SUPP-HN-01", Name = "Công ty Phụ tùng Xe đạp Hữu Nghị", Address = "Hai Bà Trưng, Hà Nội", Phone = "0243123456", Email = "contact@huunghiparts.com" },
            new Supplier { Code = "SUPP-NP-01", Name = "Tổng kho Hạt nhựa miền Bắc", Address = "KCN Đình Vũ, Hải Phòng", Phone = "02253888999", Email = "sales@northernplastics.com" });
        await AddMissingAsync(context, context.Customers, c => c.Code,
            new Customer { Code = "CUST-DECA-01", Name = "Chuỗi siêu thị thể thao Decathlon Việt Nam", Address = "Aeon Mall Long Biên, Hà Nội", Phone = "18009000", Email = "support@decathlon.vn" },
            new Customer { Code = "CUST-HN-01", Name = "Đại lý bán lẻ xe đạp thể thao Hà Nội", Address = "Tây Sơn, Đống Đa, Hà Nội", Phone = "0912345678", Email = "hanoibike@gmail.com" });

        var pcs = await context.UnitOfMeasures.SingleOrDefaultAsync(u => u.Code == "PCS");
        var kg = await context.UnitOfMeasures.SingleOrDefaultAsync(u => u.Code == "KG");
        if (pcs is null || kg is null) return;

        await AddMissingAsync(context, context.Products, p => p.Code,
            new Product { Code = "RM-FRAME-01", Name = "Khung xe hợp kim nhôm", Type = ProductType.RawMaterial, BaseUomId = pcs.Id, MinStock = 10, MaxStock = 500, IsLotTracked = true },
            new Product { Code = "RM-WHEEL-01", Name = "Cặp bánh xe 26 inch", Type = ProductType.RawMaterial, BaseUomId = pcs.Id, MinStock = 20, MaxStock = 1000, IsLotTracked = true },
            new Product { Code = "RM-CHAIN-01", Name = "Bộ xích líp Shimano", Type = ProductType.RawMaterial, BaseUomId = pcs.Id, MinStock = 10, MaxStock = 500, IsLotTracked = true },
            new Product { Code = "RM-SADDLE-01", Name = "Yên xe thể thao", Type = ProductType.RawMaterial, BaseUomId = pcs.Id, MinStock = 10, MaxStock = 500 },
            new Product { Code = "RM-ABS-01", Name = "Hạt nhựa ABS cao cấp", Type = ProductType.RawMaterial, BaseUomId = kg.Id, MinStock = 100, MaxStock = 5000, IsLotTracked = true },
            new Product { Code = "RM-STRAP-01", Name = "Dây quai mũ bảo hiểm", Type = ProductType.RawMaterial, BaseUomId = pcs.Id, MinStock = 50, MaxStock = 2000 },
            new Product { Code = "PROD-BIKE-01", Name = "Xe đạp địa hình thể thao MTB-26", Type = ProductType.FinishedGood, IsManufactured = true, BaseUomId = pcs.Id, MinStock = 5, MaxStock = 100, IsLotTracked = true },
            new Product { Code = "PROD-HELM-01", Name = "Mũ bảo hiểm thể thao ProtectPro", Type = ProductType.FinishedGood, IsManufactured = true, BaseUomId = pcs.Id, MinStock = 10, MaxStock = 500, IsLotTracked = true, ShelfLifeDays = 1095 });
        await AddMissingAsync(context, context.WorkCenters, w => w.Code,
            new WorkCenter { Code = "WC-ASM-01", Name = "Xưởng lắp ráp khung xe cơ khí" },
            new WorkCenter { Code = "WC-FIN-01", Name = "Trạm hoàn thiện, cân chỉnh & đóng gói" },
            new WorkCenter { Code = "WC-MOLD-01", Name = "Xưởng ép nhựa vỏ mũ bảo hiểm" });

        var products = await context.Products.Where(p => p.Code.StartsWith("RM-") || p.Code.StartsWith("PROD-")).ToDictionaryAsync(p => p.Code);
        var centers = await context.WorkCenters.Where(w => w.Code.StartsWith("WC-")).ToDictionaryAsync(w => w.Code);
        await SeedBomAsync(context, products["PROD-BIKE-01"], (products["RM-FRAME-01"], 1m, 0m), (products["RM-WHEEL-01"], 1m, 0m), (products["RM-CHAIN-01"], 1m, 0m), (products["RM-SADDLE-01"], 1m, 0m));
        await SeedBomAsync(context, products["PROD-HELM-01"], (products["RM-ABS-01"], .5m, 2m), (products["RM-STRAP-01"], 1m, 0m));
        await SeedRoutingAsync(context, products["PROD-BIKE-01"], "Quy trình lắp ráp xe đạp địa hình", (10, "Lắp ráp khung và bánh xe", centers["WC-ASM-01"], 30m, false), (20, "Lắp xích, yên xe và cân chỉnh", centers["WC-FIN-01"], 15m, true));
        await SeedRoutingAsync(context, products["PROD-HELM-01"], "Quy trình chế tạo mũ bảo hiểm ProtectPro", (10, "Ép nhựa vỏ mũ bảo hiểm", centers["WC-MOLD-01"], 10m, false), (20, "Lắp quai đeo và dán mút xốp", centers["WC-FIN-01"], 5m, true));
    }

    public static async Task SeedInventoryDataAsync(ApplicationDbContext context)
    {
        var products = await context.Products.ToDictionaryAsync(p => p.Code);
        if (!products.TryGetValue("PROD-BIKE-01", out var bike) || !products.TryGetValue("PROD-HELM-01", out var helmet)) return;
        await SeedChecklistAsync(context, bike, "QC Lắp ráp xe đạp hoàn thiện", ("Kiểm tra độ bám phanh lực bóp", 15m, 30m, "N"), ("Kiểm tra độ chắc chắn khung sườn", null, null, "Ok/Ng"));
        await SeedChecklistAsync(context, helmet, "QC Mũ bảo hiểm hoàn thiện ProtectPro", ("Kiểm tra độ chịu lực va đập vỏ mũ", 200m, 300m, "J"), ("Kiểm tra độ chắc chắn quai đeo", null, null, "Ok/Ng"));

        var locations = await context.Locations.ToDictionaryAsync(l => l.Code);
        var suppliers = await context.Suppliers.ToDictionaryAsync(s => s.Code);
        await SeedReceiptAsync(context, "GR-20260715-01", suppliers["SUPP-HN-01"], locations["LOC-RAW-01"],
            (products["RM-FRAME-01"], "L-FRAME-001", 100m), (products["RM-WHEEL-01"], "L-WHEEL-001", 200m), (products["RM-CHAIN-01"], "L-CHAIN-001", 150m), (products["RM-SADDLE-01"], "L-SAD-001", 150m));
        await SeedReceiptAsync(context, "GR-20260715-02", suppliers["SUPP-NP-01"], locations["LOC-RAW-02"],
            (products["RM-ABS-01"], "L-ABS-001", 500m), (products["RM-STRAP-01"], "L-STRAP-001", 300m));
    }

    public static async Task SeedWorkOrdersAsync(ApplicationDbContext context)
    {
        var products = await context.Products.ToDictionaryAsync(p => p.Code);
        var centers = await context.WorkCenters.ToDictionaryAsync(w => w.Code);
        var locations = await context.Locations.ToDictionaryAsync(l => l.Code);
        var userId = (await context.Users.FirstOrDefaultAsync())?.Id ?? "system";

        if (!await context.WorkOrders.AnyAsync(w => w.Code == "WO-20260717-01"))
        {
            var wo = NewWorkOrder("WO-20260717-01", products["PROD-BIKE-01"], 10, WorkOrderStatus.Completed, -1);
            context.WorkOrders.Add(wo); await context.SaveChangesAsync();
            context.WorkOrderSteps.AddRange(NewStep(wo, 10, "Lắp ráp khung và bánh xe", centers["WC-ASM-01"], WorkOrderStepStatus.Completed, 10), NewStep(wo, 20, "Lắp xích, yên xe và cân chỉnh", centers["WC-FIN-01"], WorkOrderStepStatus.Completed, 10));
            var outputLot = new Lot { LotNo = "PROD-BIKE-01-20260717-01", ProductId = wo.ProductId, Qty = 10, WorkOrderId = wo.Id, ManufactureDate = DateTime.UtcNow.AddDays(-1) };
            context.Lots.Add(outputLot); await context.SaveChangesAsync();
            context.StockBalances.Add(new StockBalance { ProductId = wo.ProductId, LotId = outputLot.Id, LocationId = locations["LOC-FG-01"].Id, QtyAvailable = 10 });
            context.StockTransactions.Add(NewTransaction(TransactionType.Receipt, wo.ProductId, outputLot.Id, locations["LOC-FG-01"].Id, 10, wo.Code, userId));
            await context.SaveChangesAsync();
            foreach (var code in new[] { "RM-FRAME-01", "RM-WHEEL-01", "RM-CHAIN-01", "RM-SADDLE-01" })
                await ConsumeAsync(context, products[code], 10, locations["LOC-RAW-01"], wo.Code, outputLot.Id, userId);
            await SeedInspectionAsync(context, wo, outputLot, userId);
        }

        if (!await context.WorkOrders.AnyAsync(w => w.Code == "WO-20260717-02"))
        {
            var wo = NewWorkOrder("WO-20260717-02", products["PROD-HELM-01"], 50, WorkOrderStatus.InProgress, 1);
            context.WorkOrders.Add(wo); await context.SaveChangesAsync();
            context.WorkOrderSteps.AddRange(NewStep(wo, 10, "Ép nhựa vỏ mũ bảo hiểm", centers["WC-MOLD-01"], WorkOrderStepStatus.Completed, 50), NewStep(wo, 20, "Lắp quai đeo và dán mút xốp", centers["WC-FIN-01"], WorkOrderStepStatus.Pending, 0));
            await context.SaveChangesAsync();
            await ReserveAsync(context, wo, products["RM-ABS-01"], 25, locations["LOC-RAW-02"]);
            await ReserveAsync(context, wo, products["RM-STRAP-01"], 50, locations["LOC-RAW-02"]);
        }

        if (!await context.WorkOrders.AnyAsync(w => w.Code == "WO-20260717-03"))
        {
            var wo = NewWorkOrder("WO-20260717-03", products["PROD-BIKE-01"], 5, WorkOrderStatus.Draft, 3);
            context.WorkOrders.Add(wo); await context.SaveChangesAsync();
            context.WorkOrderSteps.AddRange(NewStep(wo, 10, "Lắp ráp khung và bánh xe", centers["WC-ASM-01"], WorkOrderStepStatus.Pending, 0), NewStep(wo, 20, "Lắp xích, yên xe và cân chỉnh", centers["WC-FIN-01"], WorkOrderStepStatus.Pending, 0));
            await context.SaveChangesAsync();
        }
    }

    private static async Task AddMissingAsync<TEntity>(ApplicationDbContext context, DbSet<TEntity> set, Func<TEntity, string> keySelector, params TEntity[] items) where TEntity : class
    {
        var existing = (await set.AsNoTracking().ToListAsync()).Select(keySelector).ToHashSet(StringComparer.OrdinalIgnoreCase);
        set.AddRange(items.Where(item => !existing.Contains(keySelector(item))));
        await context.SaveChangesAsync();
    }

    private static async Task SeedBomAsync(ApplicationDbContext context, Product product, params (Product component, decimal qty, decimal scrap)[] items)
    {
        if (await context.BOMs.AnyAsync(b => b.ProductId == product.Id && b.Version == "V1.0")) return;
        var bom = new BOM { ProductId = product.Id, Version = "V1.0", EffectiveDate = DateTime.UtcNow.AddDays(-10) };
        context.BOMs.Add(bom); await context.SaveChangesAsync();
        context.BOMItems.AddRange(items.Select(i => new BOMItem { BomId = bom.Id, ComponentProductId = i.component.Id, QtyPer = i.qty, ScrapPercent = i.scrap }));
        await context.SaveChangesAsync();
    }

    private static async Task SeedRoutingAsync(ApplicationDbContext context, Product product, string name, params (int number, string name, WorkCenter center, decimal minutes, bool qc)[] steps)
    {
        if (await context.Routings.AnyAsync(r => r.ProductId == product.Id && r.Version == "V1.0")) return;
        var routing = new Routing { ProductId = product.Id, Name = name, Version = "V1.0" };
        context.Routings.Add(routing); await context.SaveChangesAsync();
        context.RoutingSteps.AddRange(steps.Select(s => new RoutingStep { RoutingId = routing.Id, StepNumber = s.number, StepName = s.name, WorkCenterId = s.center.Id, StandardTimeMinutes = s.minutes, RequireQC = s.qc }));
        await context.SaveChangesAsync();
    }

    private static async Task SeedChecklistAsync(ApplicationDbContext context, Product product, string name, params (string parameter, decimal? min, decimal? max, string unit)[] items)
    {
        if (await context.QCChecklists.AnyAsync(c => c.ProductId == product.Id && c.StepNumber == 20)) return;
        var checklist = new QCChecklist { ProductId = product.Id, StepNumber = 20, Name = name };
        context.QCChecklists.Add(checklist); await context.SaveChangesAsync();
        context.QCChecklistItems.AddRange(items.Select(i => new QCChecklistItem { QCChecklistId = checklist.Id, ParameterName = i.parameter, MinVal = i.min, MaxVal = i.max, Unit = i.unit }));
        await context.SaveChangesAsync();
    }

    private static async Task SeedReceiptAsync(ApplicationDbContext context, string receiptNo, Supplier supplier, Location location, params (Product product, string lotNo, decimal qty)[] lines)
    {
        if (await context.GoodsReceipts.AnyAsync(r => r.ReceiptNo == receiptNo)) return;
        var receipt = new GoodsReceipt { ReceiptNo = receiptNo, SupplierId = supplier.Id, ReceiptDate = DateTime.UtcNow.AddDays(-2), Status = DocumentStatus.Completed };
        context.GoodsReceipts.Add(receipt); await context.SaveChangesAsync();
        context.GoodsReceiptLines.AddRange(lines.Select(l => new GoodsReceiptLine { GoodsReceiptId = receipt.Id, ProductId = l.product.Id, LotNo = l.lotNo, Qty = l.qty, LocationId = location.Id }));
        await context.SaveChangesAsync();
        foreach (var line in lines) await CreateInventoryAsync(context, line.product, line.lotNo, line.qty, location, receiptNo);
    }

    private static async Task CreateInventoryAsync(ApplicationDbContext context, Product product, string lotNo, decimal qty, Location location, string referenceNo)
    {
        var lot = await context.Lots.SingleOrDefaultAsync(l => l.LotNo == lotNo);
        if (lot is null)
        {
            lot = new Lot { LotNo = lotNo, ProductId = product.Id, Qty = qty, ManufactureDate = DateTime.UtcNow.AddDays(-3) };
            context.Lots.Add(lot); await context.SaveChangesAsync();
        }
        if (!await context.StockBalances.AnyAsync(b => b.ProductId == product.Id && b.LotId == lot.Id && b.LocationId == location.Id))
            context.StockBalances.Add(new StockBalance { ProductId = product.Id, LotId = lot.Id, LocationId = location.Id, QtyAvailable = qty });
        if (!await context.StockTransactions.AnyAsync(t => t.ReferenceNo == referenceNo && t.ProductId == product.Id && t.LotId == lot.Id))
            context.StockTransactions.Add(NewTransaction(TransactionType.Receipt, product.Id, lot.Id, location.Id, qty, referenceNo, (await context.Users.FirstOrDefaultAsync())?.Id ?? "system"));
        await context.SaveChangesAsync();
    }

    private static WorkOrder NewWorkOrder(string code, Product product, decimal qty, WorkOrderStatus status, int dueInDays) => new()
    {
        Code = code, ProductId = product.Id, Qty = qty, DueDate = DateTime.UtcNow.AddDays(dueInDays), Status = status, BomVersion = "V1.0", RoutingVersion = "V1.0"
    };

    private static WorkOrderStep NewStep(WorkOrder order, int number, string name, WorkCenter center, WorkOrderStepStatus status, decimal qtyOk) => new()
    {
        WorkOrderId = order.Id, StepNumber = number, StepName = name, WorkCenterId = center.Id, Status = status, QtyOK = qtyOk,
        StartTime = status == WorkOrderStepStatus.Pending ? null : DateTime.UtcNow.AddHours(-4),
        EndTime = status == WorkOrderStepStatus.Completed ? DateTime.UtcNow.AddHours(-1) : null
    };

    private static StockTransaction NewTransaction(TransactionType type, int productId, int lotId, int locationId, decimal qty, string referenceNo, string userId) => new()
    {
        Type = type, ProductId = productId, LotId = lotId, LocationId = locationId, Qty = qty, ReferenceNo = referenceNo, UserId = userId, TransactionDate = DateTime.UtcNow
    };

    private static async Task ConsumeAsync(ApplicationDbContext context, Product product, decimal qty, Location location, string referenceNo, int outputLotId, string userId)
    {
        var lot = await context.Lots.SingleAsync(l => l.ProductId == product.Id);
        var balance = await context.StockBalances.SingleAsync(b => b.ProductId == product.Id && b.LotId == lot.Id && b.LocationId == location.Id);
        balance.QtyAvailable -= qty;
        context.StockTransactions.Add(NewTransaction(TransactionType.Backflush, product.Id, lot.Id, location.Id, -qty, referenceNo, userId));
        context.LotGenealogies.Add(new LotGenealogy { OutputLotId = outputLotId, InputLotId = lot.Id, QtyConsumed = qty });
        await context.SaveChangesAsync();
    }

    private static async Task ReserveAsync(ApplicationDbContext context, WorkOrder order, Product product, decimal qty, Location location)
    {
        var lot = await context.Lots.SingleAsync(l => l.ProductId == product.Id);
        var balance = await context.StockBalances.SingleAsync(b => b.ProductId == product.Id && b.LotId == lot.Id && b.LocationId == location.Id);
        balance.QtyAvailable -= qty;
        balance.QtyReserved += qty;
        context.MaterialReservations.Add(new MaterialReservation { WorkOrderId = order.Id, ProductId = product.Id, LotId = lot.Id, LocationId = location.Id, QtyReserved = qty });
        await context.SaveChangesAsync();
    }

    private static async Task SeedInspectionAsync(ApplicationDbContext context, WorkOrder order, Lot lot, string userId)
    {
        var inspection = new QCInspection { WorkOrderId = order.Id, LotId = lot.Id, InspectorId = userId, InspectionTime = DateTime.UtcNow.AddDays(-1), Result = QCResult.PASS, Note = "Dữ liệu kiểm tra mẫu đạt." };
        context.QCInspections.Add(inspection); await context.SaveChangesAsync();
        context.QCInspectionLines.AddRange(
            new QCInspectionLine { QCInspectionId = inspection.Id, ParameterName = "Kiểm tra độ bám phanh lực bóp", ValueInspected = "22.50", IsOK = true },
            new QCInspectionLine { QCInspectionId = inspection.Id, ParameterName = "Kiểm tra độ chắc chắn khung sườn", ValueInspected = "1.00", IsOK = true });
        await context.SaveChangesAsync();
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
