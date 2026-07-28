using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.Services;

public class PurchaseRequestService : IPurchaseRequestService
{
    private readonly ApplicationDbContext _context;
    private readonly IProductionPlanService _planService;

    public PurchaseRequestService(
        ApplicationDbContext context,
        IProductionPlanService planService)
    {
        _context = context;
        _planService = planService;
    }

    public async Task<IReadOnlyList<PurchaseRequest>> GetAllAsync()
    {
        return await _context.PurchaseRequests
            .AsNoTracking()
            .Include(request => request.ProductionPlan)
            .Include(request => request.Items)
                .ThenInclude(item => item.Product)
            .OrderByDescending(request => request.RequestDate)
            .ThenByDescending(request => request.Id)
            .ToListAsync();
    }

    public Task<PurchaseRequest?> GetByIdAsync(int id)
    {
        return _context.PurchaseRequests
            .AsNoTracking()
            .Include(request => request.ProductionPlan)
            .Include(request => request.Items)
                .ThenInclude(item => item.Product)
            .FirstOrDefaultAsync(request => request.Id == id);
    }

    public async Task<PurchaseRequest?> GenerateFromMrpAsync(
        int productionPlanId,
        string userId)
    {
        _ = userId;
        var plan = await _context.ProductionPlans.FindAsync(productionPlanId);
        if (plan is null)
        {
            return null;
        }

        var neededItems = (await _planService
                .CalculatePlanRequirementsAsync(productionPlanId))
            .Where(result => result.NetDemand > 0)
            .ToList();
        if (neededItems.Count == 0)
        {
            return null;
        }

        var requestNo = $"PR-{plan.PlanNo}";
        var existing = await _context.PurchaseRequests
            .Include(request => request.Items)
            .FirstOrDefaultAsync(request => request.RequestNo == requestNo);
        if (existing is not null)
        {
            return existing;
        }

        var request = new PurchaseRequest
        {
            RequestNo = requestNo,
            RequestDate = DateTime.UtcNow,
            RequiredDate = DateTime.UtcNow.AddDays(5),
            Status = DocumentStatus.Draft,
            ProductionPlanId = productionPlanId,
            Items = neededItems.Select(result => new PurchaseRequestItem
            {
                ProductId = result.ComponentProductId,
                Qty = result.NetDemand
            }).ToList()
        };

        _context.PurchaseRequests.Add(request);
        await _context.SaveChangesAsync();
        return request;
    }
}
