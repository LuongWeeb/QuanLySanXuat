using System.ComponentModel.DataAnnotations;

namespace WmsMes.Web.Domain.Entities;

public class AppNotification
{
    public int Id { get; set; }

    [Required]
    [MaxLength(150)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string Message { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string Severity { get; set; } = "Info";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsRead { get; set; }

    [MaxLength(450)]
    public string? UserId { get; set; }

    [MaxLength(500)]
    public string? ReferenceUrl { get; set; }
}
