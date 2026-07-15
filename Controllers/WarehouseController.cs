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

        return View(warehouses);
    }
}
