using System.ComponentModel.DataAnnotations;

namespace HospitalStats.Api.Models;

public class DashboardCard
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Title { get; set; } = string.Empty;

    public int? QueryConfigId { get; set; }
    public QueryConfig? QueryConfig { get; set; }

    [MaxLength(20)]
    public string DisplayType { get; set; } = "number";

    [MaxLength(50)]
    public string? Icon { get; set; }

    [MaxLength(20)]
    public string? Color { get; set; }

    [MaxLength(50)]
    public string? Unit { get; set; }

    public int SortOrder { get; set; }

    public int Width { get; set; } = 6;

    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
