namespace WmsMes.Web.ViewModels;

public class CreateReceiptViewModel
{
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
}

public class CreateIssueViewModel
{
    public int CustomerId { get; set; }
    public List<IssueLineInput> Lines { get; set; } = new();
}

public class IssueLineInput
{
    public int ProductId { get; set; }
    public int LotId { get; set; }
    public decimal Qty { get; set; }
    public int LocationId { get; set; }
}
