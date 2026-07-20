# Kế hoạch thực hiện: Seeding dữ liệu mẫu toàn diện cho hệ thống

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Triển khai thêm các phương thức seeding dữ liệu mẫu toàn diện (Sản phẩm, Định mức BOM, Quy trình, Lệnh sản xuất, Tồn kho ban đầu, QC) vào `DbSeeder` để tự động hóa việc khởi tạo môi trường thử nghiệm cho hệ thống.

**Architecture:** Bổ sung các phương thức seeding tĩnh trong `DbSeeder.cs`. Kiểm tra sự tồn tại của dữ liệu (Idempotent check) bằng EF Core trước khi thêm mới để đảm bảo an toàn khi chạy lại nhiều lần. Tích hợp và gọi từ `Program.cs` sau các bước khởi tạo nền tảng.

**Tech Stack:** ASP.NET Core MVC, Entity Framework Core, SQL Server.

## Global Constraints
- Hệ điều hành: Windows
- Tên dự án: WmsMes.Web
- Công nghệ: ASP.NET Core (.NET 8), EF Core, SQL Server.
- Seeding an toàn khi chạy lại nhiều lần (kiểm tra tồn tại bằng Code, LotNo, ReceiptNo, v.v.).

---

### Task 1: Seeding Dữ liệu danh mục cốt lõi (Master Data)

**Files:**
- Modify: `Data/DbSeeder.cs`

**Interfaces:**
- Consumes: `ApplicationDbContext`
- Produces: Các danh mục cơ bản (Product, Supplier, Customer, WorkCenter, BOM, Routing) được điền vào DB.

- [ ] **Step 1: Viết phương thức `SeedMasterDataAsync` trong `DbSeeder.cs`**

Sửa `Data/DbSeeder.cs` bổ sung phương thức `SeedMasterDataAsync` để seed Khách hàng, Nhà cung cấp, Sản phẩm, Work Center, BOM, Routing:

```csharp
    public static async Task SeedMasterDataAsync(ApplicationDbContext context)
    {
        // 1. Suppliers
        if (!await context.Suppliers.AnyAsync(s => s.Code == "SUPP-HN-01"))
        {
            context.Suppliers.AddRange(
                new Supplier { Code = "SUPP-HN-01", Name = "Công ty Phụ tùng Xe đạp Hữu Nghị", Address = "Hai Bà Trưng, Hà Nội", Phone = "0243123456", Email = "contact@huunghiparts.com" },
                new Supplier { Code = "SUPP-NP-01", Name = "Tổng kho Hạt nhựa miền Bắc", Address = "KCN Đình Vũ, Hải Phòng", Phone = "02253888999", Email = "sales@northernplastics.com" }
            );
            await context.SaveChangesAsync();
        }

        // 2. Customers
        if (!await context.Customers.AnyAsync(c => c.Code == "CUST-DECA-01"))
        {
            context.Customers.AddRange(
                new Customer { Code = "CUST-DECA-01", Name = "Chuỗi siêu thị thể thao Decathlon Việt Nam", Address = "Aeon Mall Long Biên, Hà Nội", Phone = "18009000", Email = "support@decathlon.vn" },
                new Customer { Code = "CUST-HN-01", Name = "Đại lý bán lẻ xe đạp thể thao Hà Nội", Address = "Tây Sơn, Đống Đa, Hà Nội", Phone = "0912345678", Email = "hanoibike@gmail.com" }
            );
            await context.SaveChangesAsync();
        }

        // 3. Products
        var pcsUom = await context.UnitOfMeasures.FirstOrDefaultAsync(u => u.Code == "PCS");
        var kgUom = await context.UnitOfMeasures.FirstOrDefaultAsync(u => u.Code == "KG");

        if (pcsUom != null && kgUom != null && !await context.Products.AnyAsync(p => p.Code == "RM-FRAME-01"))
        {
            context.Products.AddRange(
                new Product { Code = "RM-FRAME-01", Name = "Khung xe hợp kim nhôm", Type = ProductType.RawMaterial, IsManufactured = false, BaseUomId = pcsUom.Id, MinStock = 10, MaxStock = 500, IsLotTracked = true },
                new Product { Code = "RM-WHEEL-01", Name = "Cặp bánh xe 26 inch", Type = ProductType.RawMaterial, IsManufactured = false, BaseUomId = pcsUom.Id, MinStock = 20, MaxStock = 1000, IsLotTracked = true },
                new Product { Code = "RM-CHAIN-01", Name = "Bộ xích líp Shimano", Type = ProductType.RawMaterial, IsManufactured = false, BaseUomId = pcsUom.Id, MinStock = 10, MaxStock = 500, IsLotTracked = true },
                new Product { Code = "RM-SADDLE-01", Name = "Yên xe thể thao", Type = ProductType.RawMaterial, IsManufactured = false, BaseUomId = pcsUom.Id, MinStock = 10, MaxStock = 500, IsLotTracked = false },
                new Product { Code = "RM-ABS-01", Name = "Hạt nhựa ABS cao cấp", Type = ProductType.RawMaterial, IsManufactured = false, BaseUomId = kgUom.Id, MinStock = 100, MaxStock = 5000, IsLotTracked = true },
                new Product { Code = "RM-STRAP-01", Name = "Dây quai mũ bảo hiểm", Type = ProductType.RawMaterial, IsManufactured = false, BaseUomId = pcsUom.Id, MinStock = 50, MaxStock = 2000, IsLotTracked = false },
                new Product { Code = "PROD-BIKE-01", Name = "Xe đạp địa hình thể thao MTB-26", Type = ProductType.FinishedGood, IsManufactured = true, BaseUomId = pcsUom.Id, MinStock = 5, MaxStock = 100, IsLotTracked = true },
                new Product { Code = "PROD-HELM-01", Name = "Mũ bảo hiểm thể thao ProtectPro", Type = ProductType.FinishedGood, IsManufactured = true, BaseUomId = pcsUom.Id, MinStock = 10, MaxStock = 500, IsLotTracked = true, ShelfLifeDays = 1095 }
            );
            await context.SaveChangesAsync();
        }

        // 4. WorkCenters
        if (!await context.WorkCenters.AnyAsync(w => w.Code == "WC-ASM-01"))
        {
            context.WorkCenters.AddRange(
                new WorkCenter { Code = "WC-ASM-01", Name = "Xưởng lắp ráp khung xe cơ khí", IsActive = true },
                new WorkCenter { Code = "WC-FIN-01", Name = "Trạm hoàn thiện, cân chỉnh & đóng gói", IsActive = true },
                new WorkCenter { Code = "WC-MOLD-01", Name = "Xưởng ép nhựa vỏ mũ bảo hiểm", IsActive = true }
            );
            await context.SaveChangesAsync();
        }

        // 5. BOMs & BOMItems
        var bike = await context.Products.FirstOrDefaultAsync(p => p.Code == "PROD-BIKE-01");
        var frame = await context.Products.FirstOrDefaultAsync(p => p.Code == "RM-FRAME-01");
        var wheel = await context.Products.FirstOrDefaultAsync(p => p.Code == "RM-WHEEL-01");
        var chain = await context.Products.FirstOrDefaultAsync(p => p.Code == "RM-CHAIN-01");
        var saddle = await context.Products.FirstOrDefaultAsync(p => p.Code == "RM-SADDLE-01");

        if (bike != null && frame != null && wheel != null && chain != null && saddle != null && !await context.BOMs.AnyAsync(b => b.ProductId == bike.Id))
        {
            var bikeBom = new BOM { ProductId = bike.Id, Version = "V1.0", EffectiveDate = DateTime.UtcNow.AddDays(-10), IsActive = true };
            context.BOMs.Add(bikeBom);
            await context.SaveChangesAsync();

            context.BOMItems.AddRange(
                new BOMItem { BomId = bikeBom.Id, ComponentProductId = frame.Id, QtyPer = 1, ScrapPercent = 0 },
                new BOMItem { BomId = bikeBom.Id, ComponentProductId = wheel.Id, QtyPer = 1, ScrapPercent = 0 },
                new BOMItem { BomId = bikeBom.Id, ComponentProductId = chain.Id, QtyPer = 1, ScrapPercent = 0 },
                new BOMItem { BomId = bikeBom.Id, ComponentProductId = saddle.Id, QtyPer = 1, ScrapPercent = 0 }
            );
            await context.SaveChangesAsync();
        }

        var helm = await context.Products.FirstOrDefaultAsync(p => p.Code == "PROD-HELM-01");
        var abs = await context.Products.FirstOrDefaultAsync(p => p.Code == "RM-ABS-01");
        var strap = await context.Products.FirstOrDefaultAsync(p => p.Code == "RM-STRAP-01");

        if (helm != null && abs != null && strap != null && !await context.BOMs.AnyAsync(b => b.ProductId == helm.Id))
        {
            var helmBom = new BOM { ProductId = helm.Id, Version = "V1.0", EffectiveDate = DateTime.UtcNow.AddDays(-10), IsActive = true };
            context.BOMs.Add(helmBom);
            await context.SaveChangesAsync();

            context.BOMItems.AddRange(
                new BOMItem { BomId = helmBom.Id, ComponentProductId = abs.Id, QtyPer = 0.5m, ScrapPercent = 2.0m },
                new BOMItem { BomId = helmBom.Id, ComponentProductId = strap.Id, QtyPer = 1, ScrapPercent = 0 }
            );
            await context.SaveChangesAsync();
        }

        // 6. Routings & RoutingSteps
        var wcAsm = await context.WorkCenters.FirstOrDefaultAsync(w => w.Code == "WC-ASM-01");
        var wcFin = await context.WorkCenters.FirstOrDefaultAsync(w => w.Code == "WC-FIN-01");
        var wcMold = await context.WorkCenters.FirstOrDefaultAsync(w => w.Code == "WC-MOLD-01");

        if (bike != null && wcAsm != null && wcFin != null && !await context.Routings.AnyAsync(r => r.ProductId == bike.Id))
        {
            var bikeRouting = new Routing { ProductId = bike.Id, Name = "Quy trình lắp ráp xe đạp địa hình", Version = "V1.0", IsActive = true };
            context.Routings.Add(bikeRouting);
            await context.SaveChangesAsync();

            context.RoutingSteps.AddRange(
                new RoutingStep { RoutingId = bikeRouting.Id, StepNumber = 10, StepName = "Lắp ráp khung và bánh xe", WorkCenterId = wcAsm.Id, StandardTimeMinutes = 30, RequireQC = false },
                new RoutingStep { RoutingId = bikeRouting.Id, StepNumber = 20, StepName = "Lắp xích, yên xe và cân chỉnh", WorkCenterId = wcFin.Id, StandardTimeMinutes = 15, RequireQC = true }
            );
            await context.SaveChangesAsync();
        }

        if (helm != null && wcMold != null && wcFin != null && !await context.Routings.AnyAsync(r => r.ProductId == helm.Id))
        {
            var helmRouting = new Routing { ProductId = helm.Id, Name = "Quy trình chế tạo mũ bảo hiểm ProtectPro", Version = "V1.0", IsActive = true };
            context.Routings.Add(helmRouting);
            await context.SaveChangesAsync();

            context.RoutingSteps.AddRange(
                new RoutingStep { RoutingId = helmRouting.Id, StepNumber = 10, StepName = "Ép nhựa vỏ mũ bảo hiểm", WorkCenterId = wcMold.Id, StandardTimeMinutes = 10, RequireQC = false },
                new RoutingStep { RoutingId = helmRouting.Id, StepNumber = 20, StepName = "Lắp quai đeo và dán mút xốp", WorkCenterId = wcFin.Id, StandardTimeMinutes = 5, RequireQC = true }
            );
            await context.SaveChangesAsync();
        }
    }
```

- [ ] **Step 2: Commit code**

Run:
```bash
git add Data/DbSeeder.cs
git commit -m "feat: implement SeedMasterDataAsync in DbSeeder"
```

---

### Task 2: Seeding QC Checklist & Nhập kho ban đầu (Inventory Data)

**Files:**
- Modify: `Data/DbSeeder.cs`

**Interfaces:**
- Consumes: Master Data từ Task 1.
- Produces: QC Checklists, GoodsReceipts, Lots, StockBalances, và StockTransactions.

- [ ] **Step 1: Viết phương thức `SeedInventoryDataAsync` trong `DbSeeder.cs`**

Sửa `Data/DbSeeder.cs` bổ sung phương thức `SeedInventoryDataAsync`:

```csharp
    public static async Task SeedInventoryDataAsync(ApplicationDbContext context)
    {
        // 1. QC Checklists
        var bike = await context.Products.FirstOrDefaultAsync(p => p.Code == "PROD-BIKE-01");
        var helm = await context.Products.FirstOrDefaultAsync(p => p.Code == "PROD-HELM-01");

        if (bike != null && !await context.QCChecklists.AnyAsync(c => c.ProductId == bike.Id))
        {
            var checklist = new QCChecklist { ProductId = bike.Id, StepNumber = 20, Name = "QC Lắp ráp xe đạp hoàn thiện", IsActive = true };
            context.QCChecklists.Add(checklist);
            await context.SaveChangesAsync();

            context.QCChecklistItems.AddRange(
                new QCChecklistItem { QCChecklistId = checklist.Id, ParameterName = "Kiểm tra độ bám phanh lực bóp", MinVal = 15, MaxVal = 30, Unit = "N", IsRequired = true },
                new QCChecklistItem { QCChecklistId = checklist.Id, ParameterName = "Kiểm tra độ chắc chắn khung sườn", MinVal = null, MaxVal = null, Unit = "Ok/Ng", IsRequired = true }
            );
            await context.SaveChangesAsync();
        }

        if (helm != null && !await context.QCChecklists.AnyAsync(c => c.ProductId == helm.Id))
        {
            var checklist = new QCChecklist { ProductId = helm.Id, StepNumber = 20, Name = "QC Mũ bảo hiểm hoàn thiện ProtectPro", IsActive = true };
            context.QCChecklists.Add(checklist);
            await context.SaveChangesAsync();

            context.QCChecklistItems.AddRange(
                new QCChecklistItem { QCChecklistId = checklist.Id, ParameterName = "Kiểm tra độ chịu lực va đập vỏ mũ", MinVal = 200, MaxVal = 300, Unit = "J", IsRequired = true },
                new QCChecklistItem { QCChecklistId = checklist.Id, ParameterName = "Kiểm tra độ chắc chắn quai đeo", MinVal = null, MaxVal = null, Unit = "Ok/Ng", IsRequired = true }
            );
            await context.SaveChangesAsync();
        }

        // 2. Initial Goods Receipts (GR-20260715-01 and GR-20260715-02)
        var suppHn = await context.Suppliers.FirstOrDefaultAsync(s => s.Code == "SUPP-HN-01");
        var suppNp = await context.Suppliers.FirstOrDefaultAsync(s => s.Code == "SUPP-NP-01");
        var locRaw01 = await context.Locations.FirstOrDefaultAsync(l => l.Code == "LOC-RAW-01");
        var locRaw02 = await context.Locations.FirstOrDefaultAsync(l => l.Code == "LOC-RAW-02");

        var frame = await context.Products.FirstOrDefaultAsync(p => p.Code == "RM-FRAME-01");
        var wheel = await context.Products.FirstOrDefaultAsync(p => p.Code == "RM-WHEEL-01");
        var chain = await context.Products.FirstOrDefaultAsync(p => p.Code == "RM-CHAIN-01");
        var saddle = await context.Products.FirstOrDefaultAsync(p => p.Code == "RM-SADDLE-01");
        var abs = await context.Products.FirstOrDefaultAsync(p => p.Code == "RM-ABS-01");
        var strap = await context.Products.FirstOrDefaultAsync(p => p.Code == "RM-STRAP-01");

        // Receipt 1
        if (suppHn != null && locRaw01 != null && frame != null && wheel != null && chain != null && saddle != null 
            && !await context.GoodsReceipts.AnyAsync(r => r.ReceiptNo == "GR-20260715-01"))
        {
            var gr1 = new GoodsReceipt { ReceiptNo = "GR-20260715-01", SupplierId = suppHn.Id, ReceiptDate = DateTime.UtcNow.AddDays(-2), Status = DocumentStatus.Completed };
            context.GoodsReceipts.Add(gr1);
            await context.SaveChangesAsync();

            context.GoodsReceiptLines.AddRange(
                new GoodsReceiptLine { GoodsReceiptId = gr1.Id, ProductId = frame.Id, LotNo = "L-FRAME-001", Qty = 100, LocationId = locRaw01.Id },
                new GoodsReceiptLine { GoodsReceiptId = gr1.Id, ProductId = wheel.Id, LotNo = "L-WHEEL-001", Qty = 200, LocationId = locRaw01.Id },
                new GoodsReceiptLine { GoodsReceiptId = gr1.Id, ProductId = chain.Id, LotNo = "L-CHAIN-001", Qty = 150, LocationId = locRaw01.Id },
                new GoodsReceiptLine { GoodsReceiptId = gr1.Id, ProductId = saddle.Id, LotNo = "L-SAD-001", Qty = 150, LocationId = locRaw01.Id }
            );
            await context.SaveChangesAsync();

            // Seed Lots and Stock Balances / Transactions
            await CreateInventoryAndTransaction(context, frame.Id, "L-FRAME-001", 100, locRaw01.Id, "GR-20260715-01");
            await CreateInventoryAndTransaction(context, wheel.Id, "L-WHEEL-001", 200, locRaw01.Id, "GR-20260715-01");
            await CreateInventoryAndTransaction(context, chain.Id, "L-CHAIN-001", 150, locRaw01.Id, "GR-20260715-01");
            await CreateInventoryAndTransaction(context, saddle.Id, "L-SAD-001", 150, locRaw01.Id, "GR-20260715-01");
        }

        // Receipt 2
        if (suppNp != null && locRaw02 != null && abs != null && strap != null 
            && !await context.GoodsReceipts.AnyAsync(r => r.ReceiptNo == "GR-20260715-02"))
        {
            var gr2 = new GoodsReceipt { ReceiptNo = "GR-20260715-02", SupplierId = suppNp.Id, ReceiptDate = DateTime.UtcNow.AddDays(-2), Status = DocumentStatus.Completed };
            context.GoodsReceipts.Add(gr2);
            await context.SaveChangesAsync();

            context.GoodsReceiptLines.AddRange(
                new GoodsReceiptLine { GoodsReceiptId = gr2.Id, ProductId = abs.Id, LotNo = "L-ABS-001", Qty = 500, LocationId = locRaw02.Id },
                new GoodsReceiptLine { GoodsReceiptId = gr2.Id, ProductId = strap.Id, LotNo = "L-STRAP-001", Qty = 300, LocationId = locRaw02.Id }
            );
            await context.SaveChangesAsync();

            await CreateInventoryAndTransaction(context, abs.Id, "L-ABS-001", 500, locRaw02.Id, "GR-20260715-02");
            await CreateInventoryAndTransaction(context, strap.Id, "L-STRAP-001", 300, locRaw02.Id, "GR-20260715-02");
        }
    }

    private static async Task CreateInventoryAndTransaction(
        ApplicationDbContext context,
        int productId,
        string lotNo,
        decimal qty,
        int locationId,
        string referenceNo)
    {
        var lot = await context.Lots.FirstOrDefaultAsync(l => l.LotNo == lotNo && l.ProductId == productId);
        if (lot == null)
        {
            lot = new Lot { LotNo = lotNo, ProductId = productId, Qty = qty, ManufactureDate = DateTime.UtcNow.AddDays(-3) };
            context.Lots.Add(lot);
            await context.SaveChangesAsync();
        }

        var balance = await context.StockBalances.FirstOrDefaultAsync(sb => sb.ProductId == productId && sb.LotId == lot.Id && sb.LocationId == locationId);
        if (balance == null)
        {
            balance = new StockBalance { ProductId = productId, LotId = lot.Id, LocationId = locationId, QtyAvailable = qty, QtyReserved = 0, QtyOnHold = 0 };
            context.StockBalances.Add(balance);
        }
        else
        {
            balance.QtyAvailable = qty;
        }
        await context.SaveChangesAsync();

        var adminUser = await context.Users.FirstOrDefaultAsync();
        string userId = adminUser?.Id ?? "system";

        if (!await context.StockTransactions.AnyAsync(t => t.ReferenceNo == referenceNo && t.ProductId == productId && t.LotId == lot.Id))
        {
            context.StockTransactions.Add(new StockTransaction
            {
                Type = TransactionType.Receipt,
                ProductId = productId,
                LotId = lot.Id,
                LocationId = locationId,
                Qty = qty,
                TransactionDate = DateTime.UtcNow.AddDays(-2),
                UserId = userId,
                ReferenceNo = referenceNo
            });
            await context.SaveChangesAsync();
        }
    }
```

- [ ] **Step 2: Commit code**

Run:
```bash
git add Data/DbSeeder.cs
git commit -m "feat: implement SeedInventoryDataAsync in DbSeeder"
```

---

### Task 3: Seeding Lệnh sản xuất mẫu & QC Inspections & Phả hệ lô

**Files:**
- Modify: `Data/DbSeeder.cs`

**Interfaces:**
- Consumes: Dữ liệu từ Task 2.
- Produces: WorkOrders, WorkOrderSteps, MaterialReservations, QCInspections, LotGenealogies.

- [ ] **Step 1: Viết phương thức `SeedWorkOrdersAsync` trong `DbSeeder.cs`**

Sửa `Data/DbSeeder.cs` để bổ sung phương thức `SeedWorkOrdersAsync`:

```csharp
    public static async Task SeedWorkOrdersAsync(ApplicationDbContext context)
    {
        var bike = await context.Products.FirstOrDefaultAsync(p => p.Code == "PROD-BIKE-01");
        var helm = await context.Products.FirstOrDefaultAsync(p => p.Code == "PROD-HELM-01");
        var wcAsm = await context.WorkCenters.FirstOrDefaultAsync(w => w.Code == "WC-ASM-01");
        var wcFin = await context.WorkCenters.FirstOrDefaultAsync(w => w.Code == "WC-FIN-01");
        var wcMold = await context.WorkCenters.FirstOrDefaultAsync(w => w.Code == "WC-MOLD-01");
        var locRaw01 = await context.Locations.FirstOrDefaultAsync(l => l.Code == "LOC-RAW-01");
        var locRaw02 = await context.Locations.FirstOrDefaultAsync(l => l.Code == "LOC-RAW-02");
        var locFg01 = await context.Locations.FirstOrDefaultAsync(l => l.Code == "LOC-FG-01");

        var adminUser = await context.Users.FirstOrDefaultAsync();
        string userId = adminUser?.Id ?? "system";

        // ==========================================
        // 1. WO-20260717-01: Completed Bike Production
        // ==========================================
        if (bike != null && wcAsm != null && wcFin != null && locFg01 != null && !await context.WorkOrders.AnyAsync(w => w.Code == "WO-20260717-01"))
        {
            var wo1 = new WorkOrder
            {
                Code = "WO-20260717-01",
                ProductId = bike.Id,
                TargetQty = 10,
                Status = WorkOrderStatus.Completed,
                CreatedDate = DateTime.UtcNow.AddDays(-1),
                StartDate = DateTime.UtcNow.AddDays(-1).AddHours(2),
                EndDate = DateTime.UtcNow.AddDays(-1).AddHours(4)
            };
            context.WorkOrders.Add(wo1);
            await context.SaveChangesAsync();

            context.WorkOrderSteps.AddRange(
                new WorkOrderStep { WorkOrderId = wo1.Id, StepNumber = 10, StepName = "Lắp ráp khung và bánh xe", WorkCenterId = wcAsm.Id, StandardTimeMinutes = 300, ActualTimeMinutes = 300, Status = WorkOrderStepStatus.Completed, StartDate = DateTime.UtcNow.AddDays(-1).AddHours(2), EndDate = DateTime.UtcNow.AddDays(-1).AddHours(3) },
                new WorkOrderStep { WorkOrderId = wo1.Id, StepNumber = 20, StepName = "Lắp xích, yên xe và cân chỉnh", WorkCenterId = wcFin.Id, StandardTimeMinutes = 150, ActualTimeMinutes = 150, Status = WorkOrderStepStatus.Completed, StartDate = DateTime.UtcNow.AddDays(-1).AddHours(3), EndDate = DateTime.UtcNow.AddDays(-1).AddHours(4) }
            );
            await context.SaveChangesAsync();

            // Create finished goods Lot & Balance & Transaction
            var lotBike = new Lot { LotNo = "PROD-BIKE-01-20260717-01", ProductId = bike.Id, Qty = 10, WorkOrderId = wo1.Id, ManufactureDate = DateTime.UtcNow.AddDays(-1) };
            context.Lots.Add(lotBike);
            await context.SaveChangesAsync();

            context.StockBalances.Add(new StockBalance { ProductId = bike.Id, LotId = lotBike.Id, LocationId = locFg01.Id, QtyAvailable = 10, QtyReserved = 0, QtyOnHold = 0 });
            context.StockTransactions.Add(new StockTransaction { Type = TransactionType.Receipt, ProductId = bike.Id, LotId = lotBike.Id, LocationId = locFg01.Id, Qty = 10, TransactionDate = DateTime.UtcNow.AddDays(-1), UserId = userId, ReferenceNo = "WO-20260717-01" });
            await context.SaveChangesAsync();

            // Spend materials (Backflushing consumption logic)
            var frame = await context.Products.FirstOrDefaultAsync(p => p.Code == "RM-FRAME-01");
            var wheel = await context.Products.FirstOrDefaultAsync(p => p.Code == "RM-WHEEL-01");
            var chain = await context.Products.FirstOrDefaultAsync(p => p.Code == "RM-CHAIN-01");
            var saddle = await context.Products.FirstOrDefaultAsync(p => p.Code == "RM-SADDLE-01");

            if (frame != null && wheel != null && chain != null && saddle != null && locRaw01 != null)
            {
                await ConsumeMaterialForWO(context, frame.Id, "L-FRAME-001", 10, locRaw01.Id, "WO-20260717-01", lotBike.Id, userId);
                await ConsumeMaterialForWO(context, wheel.Id, "L-WHEEL-001", 10, locRaw01.Id, "WO-20260717-01", lotBike.Id, userId);
                await ConsumeMaterialForWO(context, chain.Id, "L-CHAIN-001", 10, locRaw01.Id, "WO-20260717-01", lotBike.Id, userId);
                await ConsumeMaterialForWO(context, saddle.Id, "L-SAD-001", 10, locRaw01.Id, "WO-20260717-01", lotBike.Id, userId);
            }

            // QC Inspection
            var qcCheck = await context.QCChecklists.Include(c => c.Items).FirstOrDefaultAsync(c => c.ProductId == bike.Id && c.StepNumber == 20);
            if (qcCheck != null)
            {
                var inspection = new QCInspection
                {
                    WorkOrderId = wo1.Id,
                    LotId = lotBike.Id,
                    StepNumber = 20,
                    InspectorId = userId,
                    InspectionDate = DateTime.UtcNow.AddDays(-1).AddHours(4),
                    Result = QCResult.Passed,
                    Notes = "Seeded inspection pass data."
                };
                context.QCInspections.Add(inspection);
                await context.SaveChangesAsync();

                foreach (var item in qcCheck.Items)
                {
                    context.QCInspectionLines.Add(new QCInspectionLine
                    {
                        QCInspectionId = inspection.Id,
                        ParameterName = item.ParameterName,
                        ActualVal = item.ParameterName.Contains("bám phanh") ? 22.5m : 1.0m,
                        Result = QCResult.Passed
                    });
                }
                await context.SaveChangesAsync();
            }
        }

        // ==========================================
        // 2. WO-20260717-02: InProgress Helmet Production with Reservation
        // ==========================================
        if (helm != null && wcMold != null && wcFin != null && !await context.WorkOrders.AnyAsync(w => w.Code == "WO-20260717-02"))
        {
            var wo2 = new WorkOrder
            {
                Code = "WO-20260717-02",
                ProductId = helm.Id,
                TargetQty = 50,
                Status = WorkOrderStatus.InProgress,
                CreatedDate = DateTime.UtcNow.AddHours(-5),
                StartDate = DateTime.UtcNow.AddHours(-4)
            };
            context.WorkOrders.Add(wo2);
            await context.SaveChangesAsync();

            context.WorkOrderSteps.AddRange(
                new WorkOrderStep { WorkOrderId = wo2.Id, StepNumber = 10, StepName = "Ép nhựa vỏ mũ bảo hiểm", WorkCenterId = wcMold.Id, StandardTimeMinutes = 500, ActualTimeMinutes = 500, Status = WorkOrderStepStatus.Completed, StartDate = DateTime.UtcNow.AddHours(-4), EndDate = DateTime.UtcNow.AddHours(-1) },
                new WorkOrderStep { WorkOrderId = wo2.Id, StepNumber = 20, StepName = "Lắp quai đeo và dán mút xốp", WorkCenterId = wcFin.Id, StandardTimeMinutes = 250, Status = WorkOrderStepStatus.InProgress, StartDate = DateTime.UtcNow.AddHours(-1) }
            );
            await context.SaveChangesAsync();

            // Material Reservations (ABS & Strap)
            var abs = await context.Products.FirstOrDefaultAsync(p => p.Code == "RM-ABS-01");
            var strap = await context.Products.FirstOrDefaultAsync(p => p.Code == "RM-STRAP-01");

            if (abs != null && strap != null && locRaw02 != null)
            {
                await ReserveMaterialForWO(context, wo2.Id, abs.Id, "L-ABS-001", 25, locRaw02.Id);
                await ReserveMaterialForWO(context, wo2.Id, strap.Id, "L-STRAP-001", 50, locRaw02.Id);
            }
        }

        // ==========================================
        // 3. WO-20260717-03: Draft Bike Production
        // ==========================================
        if (bike != null && wcAsm != null && wcFin != null && !await context.WorkOrders.AnyAsync(w => w.Code == "WO-20260717-03"))
        {
            var wo3 = new WorkOrder
            {
                Code = "WO-20260717-03",
                ProductId = bike.Id,
                TargetQty = 5,
                Status = WorkOrderStatus.Draft,
                CreatedDate = DateTime.UtcNow
            };
            context.WorkOrders.Add(wo3);
            await context.SaveChangesAsync();

            context.WorkOrderSteps.AddRange(
                new WorkOrderStep { WorkOrderId = wo3.Id, StepNumber = 10, StepName = "Lắp ráp khung và bánh xe", WorkCenterId = wcAsm.Id, StandardTimeMinutes = 150, Status = WorkOrderStepStatus.Pending },
                new WorkOrderStep { WorkOrderId = wo3.Id, StepNumber = 20, StepName = "Lắp xích, yên xe và cân chỉnh", WorkCenterId = wcFin.Id, StandardTimeMinutes = 75, Status = WorkOrderStepStatus.Pending }
            );
            await context.SaveChangesAsync();
        }
    }

    private static async Task ConsumeMaterialForWO(
        ApplicationDbContext context,
        int productId,
        string lotNo,
        decimal qty,
        int locationId,
        string referenceNo,
        int outputLotId,
        string userId)
    {
        var inputLot = await context.Lots.FirstOrDefaultAsync(l => l.LotNo == lotNo && l.ProductId == productId);
        if (inputLot == null) return;

        var balance = await context.StockBalances.FirstOrDefaultAsync(sb => sb.ProductId == productId && sb.LotId == inputLot.Id && sb.LocationId == locationId);
        if (balance != null)
        {
            balance.QtyAvailable = Math.Max(0, balance.QtyAvailable - qty);
            await context.SaveChangesAsync();
        }

        context.StockTransactions.Add(new StockTransaction
        {
            Type = TransactionType.Backflush,
            ProductId = productId,
            LotId = inputLot.Id,
            LocationId = locationId,
            Qty = -qty,
            TransactionDate = DateTime.UtcNow.AddDays(-1),
            UserId = userId,
            ReferenceNo = referenceNo
        });
        await context.SaveChangesAsync();

        // Lot Genealogy mapping
        context.LotGenealogies.Add(new LotGenealogy
        {
            OutputLotId = outputLotId,
            InputLotId = inputLot.Id,
            QtyUsed = qty
        });
        await context.SaveChangesAsync();
    }

    private static async Task ReserveMaterialForWO(
        ApplicationDbContext context,
        int workOrderId,
        int productId,
        string lotNo,
        decimal qty,
        int locationId)
    {
        var lot = await context.Lots.FirstOrDefaultAsync(l => l.LotNo == lotNo && l.ProductId == productId);
        if (lot == null) return;

        var balance = await context.StockBalances.FirstOrDefaultAsync(sb => sb.ProductId == productId && sb.LotId == lot.Id && sb.LocationId == locationId);
        if (balance != null)
        {
            balance.QtyAvailable = Math.Max(0, balance.QtyAvailable - qty);
            balance.QtyReserved += qty;
            await context.SaveChangesAsync();
        }

        context.MaterialReservations.Add(new MaterialReservation
        {
            WorkOrderId = workOrderId,
            ProductId = productId,
            LotId = lot.Id,
            LocationId = locationId,
            Qty = qty,
            Status = "Reserved"
        });
        await context.SaveChangesAsync();
    }
```

- [ ] **Step 2: Tạo phương thức tổng `SeedComprehensiveSampleDataAsync`**

Sửa `Data/DbSeeder.cs` để thêm phương thức chính kết nối tất cả:

```csharp
    public static async Task SeedComprehensiveSampleDataAsync(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        // Chạy lần lượt các bước seeding danh mục và nghiệp vụ
        await SeedMasterDataAsync(context);
        await SeedInventoryDataAsync(context);
        await SeedWorkOrdersAsync(context);
    }
```

- [ ] **Step 3: Commit code**

Run:
```bash
git add Data/DbSeeder.cs
git commit -m "feat: implement SeedWorkOrdersAsync and SeedComprehensiveSampleDataAsync in DbSeeder"
```

---

### Task 4: Tích hợp vào Startup & Xác minh hoạt động (Integration)

**Files:**
- Modify: `Program.cs`

**Interfaces:**
- Consumes: `DbSeeder.SeedComprehensiveSampleDataAsync`
- Produces: Hệ thống tự động nạp dữ liệu khi khởi động, không phát sinh lỗi.

- [ ] **Step 1: Cập nhật gọi Seeding trong `Program.cs`**

Sửa `Program.cs` để gọi `DbSeeder.SeedComprehensiveSampleDataAsync`:

Tìm đoạn:
```csharp
        await DbSeeder.SeedRolesAndUsersAsync(roleManager, userManager);
        await DbSeeder.SeedQcInfrastructureAsync(dbContext);
        await DbSeeder.SeedUnitOfMeasuresAsync(dbContext);
        await DbSeeder.SeedWarehouseStructureAsync(dbContext);
```
Sửa thành:
```csharp
        await DbSeeder.SeedRolesAndUsersAsync(roleManager, userManager);
        await DbSeeder.SeedQcInfrastructureAsync(dbContext);
        await DbSeeder.SeedUnitOfMeasuresAsync(dbContext);
        await DbSeeder.SeedWarehouseStructureAsync(dbContext);
        
        // Nạp dữ liệu mẫu toàn diện WMS/MES
        await DbSeeder.SeedComprehensiveSampleDataAsync(dbContext, userManager);
```

- [ ] **Step 2: Build và chạy ứng dụng để xác minh**

Run: `dotnet build`
Expected: Build thành công (0 Errors).

Run: `dotnet run`
Expected: Server chạy ổn định, không có ngoại lệ (Exception) log ra từ DbSeeder.

- [ ] **Step 3: Commit và hoàn thành**

Run:
```bash
git add Program.cs
git commit -m "feat: integrate SeedComprehensiveSampleDataAsync in Program.cs"
```
