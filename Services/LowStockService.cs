using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.ViewModels;

namespace WmsMes.Web.Services;

public sealed class LowStockService : ILowStockService
{
    private readonly ApplicationDbContext _context;

    public LowStockService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<LowStockItemViewModel>> GetLowStockItemsAsync(
        CancellationToken cancellationToken = default)
    {
        return await LowStockQuery.Create(_context)
            .ToListAsync(cancellationToken);
    }
}
