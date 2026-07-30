using System.ComponentModel.DataAnnotations;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.ViewModels;

public sealed class InventoryIndexViewModel
{
    public IReadOnlyList<WmsMes.Web.Domain.Entities.StockBalance> Balances { get; init; } =
        Array.Empty<WmsMes.Web.Domain.Entities.StockBalance>();

    public IReadOnlyList<LowStockItemViewModel> LowStockItems { get; init; } =
        Array.Empty<LowStockItemViewModel>();
}

public class CreateReceiptViewModel
{
    public int? PurchaseOrderId { get; set; }
    public int SupplierId { get; set; }
    public List<ReceiptLineInput> Lines { get; set; } = new();
}

public class ReceiptLineInput
{
    public int ProductId { get; set; }
    public string LotNo { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public int LocationId { get; set; }

    [MaxLength(250)]
    public string? VarianceReason { get; set; }
}

public class CreateIssueViewModel
{
    public int? SalesOrderId { get; set; }
    public int CustomerId { get; set; }
    public List<IssueLineInput> Lines { get; set; } = new();
}

public class IssueLineInput
{
    public int ProductId { get; set; }
    public int LotId { get; set; }
    public decimal Qty { get; set; }
    public int LocationId { get; set; }

    [MaxLength(250)]
    public string? VarianceReason { get; set; }
}

public sealed class StockTransactionPageViewModel
{
    public IReadOnlyList<StockTransactionListItemViewModel> Items { get; init; } =
        Array.Empty<StockTransactionListItemViewModel>();

    public bool HasNextPage { get; init; }

    public bool IsFirstPage { get; init; }

    public DateTime? NextBeforeDate { get; init; }

    public int? NextBeforeId { get; init; }
}

public sealed class StockTransactionListItemViewModel
{
    public int Id { get; init; }

    public TransactionType Type { get; init; }

    public string ProductCode { get; init; } = string.Empty;

    public string ProductName { get; init; } = string.Empty;

    public string LotNo { get; init; } = string.Empty;

    public string LocationCode { get; init; } = string.Empty;

    public decimal Qty { get; init; }

    public decimal QtyAfter { get; init; }

    public decimal ValuationRate { get; init; }

    public bool IsCancelled { get; init; }

    public DateTime TransactionDate { get; init; }

    public string ReferenceNo { get; init; } = string.Empty;
}
