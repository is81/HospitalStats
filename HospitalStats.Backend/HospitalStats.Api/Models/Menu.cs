using System.ComponentModel.DataAnnotations;

namespace HospitalStats.Api.Models;

public class Menu
{
    public int Id { get; set; }

    public int? ParentId { get; set; }
    public Menu? Parent { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Icon { get; set; }

    public int SortOrder { get; set; }

    public int? QueryConfigId { get; set; }
    public QueryConfig? QueryConfig { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Menu> Children { get; set; } = new();
}
