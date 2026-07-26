using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.DTOs;

namespace WmsMes.Web.Services;

public class MrpService : IMrpService
{
    private readonly ApplicationDbContext _context;

    public MrpService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<MrpResultDto>> CalculateRequirementsAsync(int productId, decimal qty)
    {
        var bom = await _context.BOMs
            .Include(b => b.Items)
            .ThenInclude(i => i.ComponentProduct)
            .FirstOrDefaultAsync(b => b.ProductId == productId && b.IsActive);

        if (bom == null)
        {
            return Enumerable.Empty<MrpResultDto>();
        }

        var results = new List<MrpResultDto>();
        foreach (var componentGroup in bom.Items
            .Where(item => item.ComponentProduct is not null)
            .GroupBy(item => item.ComponentProductId)
            .OrderBy(group => group.Key))
        {
            var component = componentGroup.First().ComponentProduct!;
            var grossDemand = componentGroup.Sum(item =>
                qty * item.QtyPer * (1 + item.ScrapPercent / 100));
            var stockAvailable = await _context.StockBalances
                .Where(sb => sb.ProductId == componentGroup.Key)
                .SumAsync(sb => sb.QtyAvailable);

            results.Add(new MrpResultDto
            {
                ComponentProductId = componentGroup.Key,
                ComponentCode = component.Code,
                ComponentName = component.Name,
                GrossDemand = grossDemand,
                StockAvailable = stockAvailable,
                NetDemand = Math.Max(0, grossDemand - stockAvailable)
            });
        }

        return results;
    }
}
