using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.Services;

public class PurchaseOrderService : IPurchaseOrderService
{
    private readonly ApplicationDbContext _context;

    public PurchaseOrderService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<PurchaseOrder>> GetAllAsync()
    {
        return await _context.PurchaseOrders
            .AsNoTracking()
            .Include(order => order.Supplier)
            .Include(order => order.Items)
            .OrderByDescending(order => order.OrderDate)
            .ThenByDescending(order => order.Id)
            .ToListAsync();
    }

    public Task<PurchaseOrder?> GetByIdAsync(int id)
    {
        return _context.PurchaseOrders
            .AsNoTracking()
            .Include(order => order.Supplier)
            .Include(order => order.PurchaseRequest)
            .Include(order => order.Items)
                .ThenInclude(item => item.Product)
            .FirstOrDefaultAsync(order => order.Id == id);
    }

    public async Task<PurchaseOrder?> CreateOrderFromRequestAsync(
        int requestId,
        int supplierId,
        string userId)
    {
        _ = userId;
        var existingOrder = await _context.PurchaseOrders
            .Include(order => order.Items)
            .FirstOrDefaultAsync(order => order.PurchaseRequestId == requestId);
        if (existingOrder is not null)
        {
            return existingOrder;
        }

        var request = await _context.PurchaseRequests
            .Include(candidate => candidate.Items)
                .ThenInclude(item => item.Product)
            .FirstOrDefaultAsync(candidate => candidate.Id == requestId);
        if (request is null ||
            request.Status != DocumentStatus.Draft ||
            !await _context.Suppliers.AnyAsync(supplier =>
                supplier.Id == supplierId && supplier.IsActive))
        {
            return null;
        }

        var today = DateTime.UtcNow;
        var prefix = $"PO-{today:yyyyMMdd}-";
        var existingNumbers = await _context.PurchaseOrders
            .Where(order => order.OrderNo.StartsWith(prefix))
            .Select(order => order.OrderNo)
            .ToListAsync();
        var sequence = existingNumbers
            .Select(number => int.TryParse(number[prefix.Length..], out var value) ? value : 0)
            .DefaultIfEmpty()
            .Max() + 1;
        var order = new PurchaseOrder
        {
            OrderNo = $"{prefix}{sequence:000}",
            SupplierId = supplierId,
            OrderDate = today,
            ExpectedDeliveryDate = request.RequiredDate,
            Status = DocumentStatus.Draft,
            PurchaseRequestId = request.Id,
            Items = request.Items.Select(item => new PurchaseOrderItem
            {
                ProductId = item.ProductId,
                Qty = item.Qty,
                UnitPrice = item.Product?.StandardCost ?? 0m
            }).ToList()
        };

        request.Status = DocumentStatus.Completed;
        _context.PurchaseOrders.Add(order);
        await _context.SaveChangesAsync();
        return order;
    }
}
