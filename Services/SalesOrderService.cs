using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.Services;

public class SalesOrderService : ISalesOrderService
{
    private readonly ApplicationDbContext _context;

    public SalesOrderService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<SalesOrder>> GetAllAsync()
    {
        return await _context.SalesOrders
            .AsNoTracking()
            .Include(order => order.Customer)
            .Include(order => order.Items)
            .OrderByDescending(order => order.OrderDate)
            .ThenByDescending(order => order.Id)
            .ToListAsync();
    }

    public Task<SalesOrder?> GetByIdAsync(int id)
    {
        return _context.SalesOrders
            .AsNoTracking()
            .Include(order => order.Customer)
            .Include(order => order.Items)
                .ThenInclude(item => item.Product)
            .FirstOrDefaultAsync(order => order.Id == id);
    }

    public async Task<SalesOrder?> CreateAsync(SalesOrder order, string userId)
    {
        _ = userId;
        if (order.Items.Count == 0 ||
            order.Items.Any(item => item.Qty <= 0) ||
            !await _context.Customers.AnyAsync(customer =>
                customer.Id == order.CustomerId && customer.IsActive))
        {
            return null;
        }
        var productIds = order.Items.Select(item => item.ProductId).ToList();
        if (productIds.Distinct().Count() != productIds.Count ||
            await _context.Products.CountAsync(product =>
                productIds.Contains(product.Id) && product.IsActive) != productIds.Count)
        {
            return null;
        }

        var today = DateTime.UtcNow;
        var prefix = $"SO-{today:yyyyMMdd}-";
        var existingNumbers = await _context.SalesOrders
            .Where(candidate => candidate.OrderNo.StartsWith(prefix))
            .Select(candidate => candidate.OrderNo)
            .ToListAsync();
        var sequence = existingNumbers
            .Select(number => int.TryParse(number[prefix.Length..], out var value) ? value : 0)
            .DefaultIfEmpty()
            .Max() + 1;
        order.Id = 0;
        order.OrderNo = $"{prefix}{sequence:000}";
        order.OrderDate = today;
        order.Status = DocumentStatus.Draft;
        foreach (var item in order.Items)
        {
            item.Id = 0;
            item.DeliveredQty = 0m;
        }

        _context.SalesOrders.Add(order);
        await _context.SaveChangesAsync();
        return order;
    }
}
