using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.ViewModels;

namespace WmsMes.Web.Controllers;

[Authorize(Roles = "Admin,Planner,Manager")]
public class WorkCenterController : Controller
{
    private readonly ApplicationDbContext _context;

    public WorkCenterController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var workCenters = await _context.WorkCenters.AsNoTracking()
            .OrderBy(x => x.Code)
            .ToListAsync();
        return View(workCenters);
    }

    [HttpGet]
    public IActionResult Create() => View(new WorkCenterCreateInputModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(WorkCenterCreateInputModel input)
    {
        var code = input.Code.Trim();
        if (await _context.WorkCenters.AnyAsync(x => x.Code == code))
            ModelState.AddModelError(nameof(input.Code), "Mã trạm đã tồn tại.");

        if (!ModelState.IsValid)
            return View(input);

        var workCenter = new WorkCenter
        {
            Code = code,
            Name = input.Name.Trim(),
            HourlyLaborRate = RoundCurrency(input.HourlyLaborRate),
            HourlyMachineRate = RoundCurrency(input.HourlyMachineRate),
            IsActive = true
        };
        _context.WorkCenters.Add(workCenter);
        await _context.SaveChangesAsync();

        TempData["StatusMessage"] = $"Đã thêm trạm sản xuất {workCenter.Code}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var workCenter = await _context.WorkCenters.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id);
        if (workCenter is null)
            return NotFound();

        ViewData["WorkCenterName"] = $"{workCenter.Code} - {workCenter.Name}";
        return View(new WorkCenterRateInputModel
        {
            Id = workCenter.Id,
            HourlyLaborRate = workCenter.HourlyLaborRate,
            HourlyMachineRate = workCenter.HourlyMachineRate
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(WorkCenterRateInputModel input)
    {
        var workCenter = await _context.WorkCenters.SingleOrDefaultAsync(x => x.Id == input.Id);
        if (workCenter is null)
            return NotFound();

        if (!ModelState.IsValid)
        {
            ViewData["WorkCenterName"] = $"{workCenter.Code} - {workCenter.Name}";
            return View(input);
        }

        workCenter.HourlyLaborRate = RoundCurrency(input.HourlyLaborRate);
        workCenter.HourlyMachineRate = RoundCurrency(input.HourlyMachineRate);
        await _context.SaveChangesAsync();

        TempData["StatusMessage"] = $"Đã cập nhật chi phí trạm {workCenter.Code}.";
        return RedirectToAction(nameof(Index));
    }

    private static decimal RoundCurrency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
