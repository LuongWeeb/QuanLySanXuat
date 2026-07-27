using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.Domain.Entities;

public class Product
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [MaxLength(250)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public ProductType Type { get; set; }

    public bool IsManufactured { get; set; }

    [Required]
    public int BaseUomId { get; set; }

    [ForeignKey(nameof(BaseUomId))]
    public virtual UnitOfMeasure? BaseUom { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal MinStock { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal MaxStock { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal StandardCost { get; set; } = 0m;

    public bool IsLotTracked { get; set; }

    public int? ShelfLifeDays { get; set; }

    public bool IsActive { get; set; } = true;
}
