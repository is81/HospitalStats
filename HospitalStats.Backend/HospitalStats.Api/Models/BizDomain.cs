using System.ComponentModel.DataAnnotations;

namespace HospitalStats.Api.Models;

public class BizDomain
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Description { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<MetaTable> Tables { get; set; } = new();
}
