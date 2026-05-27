using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HospitalStats.Api.Models;

public class MetaTable
{
    public int Id { get; set; }

    public int DataSourceId { get; set; }
    public DataSource? DataSource { get; set; }

    public int? BizDomainId { get; set; }
    public BizDomain? BizDomain { get; set; }

    [Required, MaxLength(128)]
    public string TableName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? SchemaName { get; set; }

    [MaxLength(200)]
    public string? Alias { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public bool IsEnabled { get; set; } = true;
    public bool IsView { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<MetaColumn> Columns { get; set; } = new();
}
