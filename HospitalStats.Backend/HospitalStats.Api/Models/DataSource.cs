using System.ComponentModel.DataAnnotations;

namespace HospitalStats.Api.Models;

public class DataSource
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string DbType { get; set; } = "Oracle";

    [Required, MaxLength(500)]
    public string ConnectionString { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Schema { get; set; }

    [MaxLength(50)]
    public string? CharSetOverride { get; set; }

    [MaxLength(1000)]
    public string? CharSetInfo { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
