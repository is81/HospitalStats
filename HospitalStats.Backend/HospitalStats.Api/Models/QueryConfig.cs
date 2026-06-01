using System.ComponentModel.DataAnnotations;

namespace HospitalStats.Api.Models;

public class QueryConfig
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public int MainTableId { get; set; }
    public MetaTable? MainTable { get; set; }

    [MaxLength(50)]
    public string DisplayType { get; set; } = "table";

    [MaxLength(50)]
    public string? AggregateType { get; set; }

    [MaxLength(128)]
    public string? AggregateColumn { get; set; }

    [MaxLength(128)]
    public string? GroupByColumn { get; set; }

    [MaxLength(128)]
    public string? SortColumn { get; set; }

    [MaxLength(10)]
    public string? SortDirection { get; set; } = "ASC";

    public int? PageSize { get; set; } = 50;

    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public string? RawSql { get; set; }
    public string? OriginalSql { get; set; }

    public List<QueryField> Fields { get; set; } = new();
    public List<QueryFilter> Filters { get; set; } = new();
    public List<QueryJoin> Joins { get; set; } = new();
}
