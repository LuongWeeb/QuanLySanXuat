using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;

namespace WmsMes.Web.Controllers;

[Authorize]
public class InventoryController : Controller
{
    private readonly ApplicationDbContext _context;

    public InventoryController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var balances = await _context.StockBalances
            .Include(sb => sb.Product)
            .Include(sb => sb.Lot)
            .Include(sb => sb.Location)
            .ThenInclude(location => location!.Zone)
            .OrderBy(sb => sb.Product!.Code)
            .ThenBy(sb => sb.Lot!.ExpiryDate)
            .ThenBy(sb => sb.Location!.Code)
            .AsNoTracking()
            .ToListAsync();

        return View(balances);
    }
}
