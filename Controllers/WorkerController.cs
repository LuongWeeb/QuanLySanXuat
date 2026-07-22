using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.Services;

namespace WmsMes.Web.Controllers;

[Authorize(Roles = "Admin,Worker")]
public class WorkerController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IWorkOrderService _workOrderService;

    public WorkerController(ApplicationDbContext context, IWorkOrderService workOrderService)
    {
        _context = context;
        _workOrderService = workOrderService;
    }

    public async Task<IActionResult> Index()
    {
        var steps = await _context.WorkOrderSteps
            .Include(s => s.WorkOrder)
            .ThenInclude(w => w!.Product)
            .Include(s => s.WorkCenter)
            .Where(s => s.Status != WorkOrderStepStatus.Completed &&
                        s.WorkOrder != null &&
                        (s.WorkOrder.Status == WorkOrderStatus.Approved || s.WorkOrder.Status == WorkOrderStatus.InProgress))
            .OrderBy(s => s.WorkOrder!.DueDate)
            .ThenBy(s => s.WorkOrder!.Code)
            .ThenBy(s => s.StepNumber)
            .AsNoTracking()
            .ToListAsync();

        return View(steps);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(int id)
    {
        await _workOrderService.StartStepAsync(id);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(int id, decimal qtyOk)
    {
        await _workOrderService.CompleteStepAsync(id, qtyOk, 0, 0);
        return RedirectToAction(nameof(Index));
    }
}
