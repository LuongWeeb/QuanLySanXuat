using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;

namespace WmsMes.Web.Services;

public class CostingService : ICostingService
{
    private readonly ApplicationDbContext _context;

    public CostingService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<decimal> CalculateProductionCostAsync(int workOrderId)
    {
        var outputLot = await _context.Lots
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.WorkOrderId == workOrderId);
        if (outputLot is null || outputLot.Qty <= 0)
        {
            return 0m;
        }

        var materialCost = await _context.LotGenealogies
            .AsNoTracking()
            .Include(g => g.InputLot)
            .Where(g => g.OutputLotId == outputLot.Id)
            .SumAsync(g => g.QtyConsumed * (g.InputLot == null ? 0m : g.InputLot.UnitPrice));

        return Math.Round(materialCost / outputLot.Qty, 2, MidpointRounding.AwayFromZero);
    }
}
