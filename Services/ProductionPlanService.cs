using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.DTOs;

namespace WmsMes.Web.Services;

public class ProductionPlanService : IProductionPlanService
{
    private readonly ApplicationDbContext _context;
    private readonly INotificationService? _notificationService;
    private readonly ILogger<ProductionPlanService> _logger;

    public ProductionPlanService(ApplicationDbContext context)
        : this(context, null, null)
    {
    }

    public ProductionPlanService(
        ApplicationDbContext context,
        INotificationService? notificationService = null,
        ILogger<ProductionPlanService>? logger = null)
    {
        _context = context;
        _notificationService = notificationService;
        _logger = logger ?? NullLogger<ProductionPlanService>.Instance;
    }

    public Task<ProductionPlan?> GetByIdAsync(int id)
    {
        return _context.ProductionPlans
            .Include(plan => plan.Items)
                .ThenInclude(item => item.Product)
            .Include(plan => plan.Items)
                .ThenInclude(item => item.WorkOrder)
            .FirstOrDefaultAsync(plan => plan.Id == id);
    }

    public async Task<bool> CreatePlanAsync(ProductionPlan plan)
    {
        _context.ProductionPlans.Add(plan);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<IEnumerable<MrpResultDto>> CalculatePlanRequirementsAsync(int planId)
    {
        var plan = await _context.ProductionPlans
            .Include(candidate => candidate.Items)
            .FirstOrDefaultAsync(candidate => candidate.Id == planId);
        if (plan is null)
        {
            return [];
        }

        var componentDemands = new Dictionary<int, decimal>();
        foreach (var item in plan.Items)
        {
            var bom = await _context.BOMs
                .Include(candidate => candidate.Items)
                .FirstOrDefaultAsync(candidate =>
                    candidate.ProductId == item.ProductId &&
                    candidate.IsActive);
            if (bom is null)
            {
                continue;
            }

            foreach (var bomItem in bom.Items)
            {
                var grossDemand = item.PlannedQty *
                                  bomItem.QtyPer *
                                  (1m + (bomItem.ScrapPercent / 100m));
                componentDemands[bomItem.ComponentProductId] =
                    componentDemands.GetValueOrDefault(bomItem.ComponentProductId) + grossDemand;
            }
        }

        var results = new List<MrpResultDto>();
        foreach (var demand in componentDemands.OrderBy(pair => pair.Key))
        {
            var product = await _context.Products.FindAsync(demand.Key);
            if (product is null)
            {
                continue;
            }

            var stockAvailable = await _context.StockBalances
                .Where(balance => balance.ProductId == demand.Key)
                .SumAsync(balance => balance.QtyAvailable);
            var grossDemand = Math.Round(demand.Value, 2, MidpointRounding.AwayFromZero);

            results.Add(new MrpResultDto
            {
                ComponentProductId = demand.Key,
                ComponentCode = product.Code,
                ComponentName = product.Name,
                GrossDemand = grossDemand,
                StockAvailable = stockAvailable,
                NetDemand = Math.Max(
                    0m,
                    Math.Round(demand.Value - stockAvailable, 2, MidpointRounding.AwayFromZero))
            });
        }

        return results;
    }

    public async Task<bool> GenerateWorkOrdersAsync(int planId, string userId)
    {
        var plan = await _context.ProductionPlans
            .Include(candidate => candidate.Items)
                .ThenInclude(item => item.Product)
            .FirstOrDefaultAsync(candidate => candidate.Id == planId);
        if (plan is null || plan.Status != DocumentStatus.Draft)
        {
            return false;
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            foreach (var item in plan.Items)
            {
                if (item.WorkOrderId.HasValue)
                {
                    continue;
                }

                var bom = await _context.BOMs.FirstOrDefaultAsync(candidate =>
                    candidate.ProductId == item.ProductId &&
                    candidate.IsActive);
                var routing = await _context.Routings.FirstOrDefaultAsync(candidate =>
                    candidate.ProductId == item.ProductId &&
                    candidate.IsActive);
                if (bom is null || routing is null)
                {
                    throw new InvalidOperationException(
                        $"Sản phẩm {item.Product?.Code} chưa có BOM hoặc Routing hoạt động để tạo Lệnh sản xuất.");
                }

                var workOrder = new WorkOrder
                {
                    Code = $"WO-{plan.PlanNo}-{item.Product?.Code}",
                    ProductId = item.ProductId,
                    Qty = item.PlannedQty,
                    DueDate = plan.PlanDate.AddDays(7),
                    Status = WorkOrderStatus.Draft,
                    BomVersion = bom.Version,
                    RoutingVersion = routing.Version
                };

                _context.WorkOrders.Add(workOrder);
                await _context.SaveChangesAsync();
                item.WorkOrderId = workOrder.Id;
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return true;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<bool> CompletePlanAsync(int planId)
    {
        ProductionPlan? plan;
        if (_context.Database.IsRelational())
        {
            plan = await _context.ProductionPlans
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == planId);
            if (plan is null)
            {
                return false;
            }

            var transitioned = await _context.ProductionPlans
                .Where(candidate =>
                    candidate.Id == planId &&
                    candidate.Status == DocumentStatus.Draft)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(candidate => candidate.Status, DocumentStatus.Completed));
            if (transitioned != 1)
            {
                return false;
            }

            plan.Status = DocumentStatus.Completed;
            var trackedPlan = _context.ChangeTracker.Entries<ProductionPlan>()
                .FirstOrDefault(entry => entry.Entity.Id == planId);
            if (trackedPlan is not null)
            {
                trackedPlan.Entity.Status = DocumentStatus.Completed;
                trackedPlan.State = EntityState.Unchanged;
            }
        }
        else
        {
            plan = await _context.ProductionPlans.FindAsync(planId);
            if (plan is null || plan.Status != DocumentStatus.Draft)
            {
                return false;
            }

            plan.Status = DocumentStatus.Completed;
            await _context.SaveChangesAsync();
        }

        await NotifyCompletionSafelyAsync(plan);
        return true;
    }

    private async Task NotifyCompletionSafelyAsync(ProductionPlan plan)
    {
        if (_notificationService is null)
        {
            return;
        }

        try
        {
            await _notificationService.SendNotificationAsync(
                "Kế hoạch sản xuất hoàn thành",
                $"Kế hoạch sản xuất {plan.PlanNo} đã hoàn thành.",
                "Info",
                "/Dashboard");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Production plan {ProductionPlanId} completed but business notification persistence failed.",
                plan.Id);
        }
    }
}
