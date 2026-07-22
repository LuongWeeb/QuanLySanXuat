using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;

namespace WmsMes.Web.Controllers;

[Authorize]
public class WarehouseController : Controller
{
    private readonly ApplicationDbContext _context;

    public WarehouseController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var warehouses = await _context.Warehouses
            .Include(warehouse => warehouse.Zones)
            .ThenInclude(zone => zone.Locations)
            .OrderBy(warehouse => warehouse.Code)
            .AsNoTracking()
            .ToListAsync();

        var locationIds = warehouses
            .SelectMany(warehouse => warehouse.Zones)
            .SelectMany(zone => zone.Locations)
            .Select(location => location.Id)
            .ToList();

        ViewData["StockBalances"] = await _context.StockBalances
            .Where(balance => locationIds.Contains(balance.LocationId))
            .Include(balance => balance.Location)
            .Include(balance => balance.Product)
            .Include(balance => balance.Lot)
            .AsNoTracking()
            .OrderBy(balance => balance.Location!.Code)
            .ThenBy(balance => balance.Product!.Code)
            .ThenBy(balance => balance.Lot!.LotNo)
            .ToListAsync();

        return View(warehouses);
    }
}
