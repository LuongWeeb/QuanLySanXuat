using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.DTOs;

namespace WmsMes.Web.Services;

public class TraceabilityService : ITraceabilityService
{
    private readonly ApplicationDbContext _context;

    public TraceabilityService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<LotNodeDto?> GetBackwardTraceAsync(string lotNo)
    {
        var lot = await _context.Lots
            .AsNoTracking()
            .Include(l => l.Product)
            .FirstOrDefaultAsync(l => l.LotNo == lotNo);

        if (lot is null)
        {
            return null;
        }

        var node = CreateNode(lot, lot.Qty);
        await BuildBackwardTreeAsync(lot.Id, node, new HashSet<int> { lot.Id });
        return node;
    }

    public async Task<LotNodeDto?> GetForwardTraceAsync(string lotNo)
    {
        var lot = await _context.Lots
            .AsNoTracking()
            .Include(l => l.Product)
            .FirstOrDefaultAsync(l => l.LotNo == lotNo);

        if (lot is null)
        {
            return null;
        }

        var node = CreateNode(lot, lot.Qty);
        await BuildForwardTreeAsync(lot.Id, node, new HashSet<int> { lot.Id });
        return node;
    }

    private async Task BuildBackwardTreeAsync(int outputLotId, LotNodeDto parentNode, HashSet<int> visited)
    {
        var relations = await _context.LotGenealogies
            .AsNoTracking()
            .Include(g => g.InputLot)
            .ThenInclude(l => l!.Product)
            .Where(g => g.OutputLotId == outputLotId)
            .ToListAsync();

        foreach (var relation in relations)
        {
            if (relation.InputLot is null || !visited.Add(relation.InputLotId))
            {
                continue;
            }

            var childNode = CreateNode(relation.InputLot, relation.QtyConsumed);
            parentNode.Children.Add(childNode);
            await BuildBackwardTreeAsync(relation.InputLotId, childNode, visited);
        }
    }

    private async Task BuildForwardTreeAsync(int inputLotId, LotNodeDto parentNode, HashSet<int> visited)
    {
        var relations = await _context.LotGenealogies
            .AsNoTracking()
            .Include(g => g.OutputLot)
            .ThenInclude(l => l!.Product)
            .Where(g => g.InputLotId == inputLotId)
            .ToListAsync();

        foreach (var relation in relations)
        {
            if (relation.OutputLot is null || !visited.Add(relation.OutputLotId))
            {
                continue;
            }

            var childNode = CreateNode(relation.OutputLot, relation.OutputLot.Qty);
            parentNode.Children.Add(childNode);
            await BuildForwardTreeAsync(relation.OutputLotId, childNode, visited);
        }
    }

    private static LotNodeDto CreateNode(Domain.Entities.Lot lot, decimal qty)
    {
        return new LotNodeDto
        {
            LotNo = lot.LotNo,
            ProductCode = lot.Product?.Code ?? string.Empty,
            ProductName = lot.Product?.Name ?? string.Empty,
            Qty = qty,
            ExpiryDate = lot.ExpiryDate?.ToString("yyyy-MM-dd") ?? "N/A"
        };
    }
}
