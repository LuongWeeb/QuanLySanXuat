using System.ComponentModel.DataAnnotations;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.Domain.Entities;

public class GoodsIssue
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string IssueNo { get; set; } = string.Empty;

    [Required]
    public DateTime IssueDate { get; set; } = DateTime.UtcNow;

    [Required]
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

    public virtual ICollection<GoodsIssueLine> Lines { get; set; } = new List<GoodsIssueLine>();
}
