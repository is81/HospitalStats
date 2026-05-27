using System.ComponentModel.DataAnnotations;

namespace HospitalStats.Api.Models;

public class MetaColumn
{
    public int Id { get; set; }

    public int MetaTableId { get; set; }
    public MetaTable? MetaTable { get; set; }

    [Required, MaxLength(128)]
    public string ColumnName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? DataType { get; set; }

    public int? DataLength { get; set; }
    public int? DataPrecision { get; set; }
    public int? DataScale { get; set; }
    public bool Nullable { get; set; }

    [MaxLength(200)]
    public string? Alias { get; set; }

    [MaxLength(500)]
    public string? Comment { get; set; }

    public bool IsQueryField { get; set; }
    public bool IsFilterField { get; set; }
    public bool IsDisplayField { get; set; }

    public int? SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
