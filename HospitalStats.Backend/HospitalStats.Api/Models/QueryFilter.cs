using System.ComponentModel.DataAnnotations;

namespace HospitalStats.Api.Models;

public class QueryFilter
{
    public int Id { get; set; }

    public int QueryConfigId { get; set; }
    public QueryConfig? QueryConfig { get; set; }

    public int MetaColumnId { get; set; }
    public MetaColumn? MetaColumn { get; set; }

    [Required, MaxLength(20)]
    public string Operator { get; set; } = "EQ";

    [MaxLength(500)]
    public string? DefaultValue { get; set; }

    public bool IsRequired { get; set; }

    [MaxLength(30)]
    public string ControlType { get; set; } = "input";

    [MaxLength(200)]
    public string? Label { get; set; }

    public bool IsContextFilter { get; set; }

    [MaxLength(50)]
    public string? ContextKey { get; set; }

    public int SortOrder { get; set; }
}
