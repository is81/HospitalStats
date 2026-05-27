using System.ComponentModel.DataAnnotations;

namespace HospitalStats.Api.Models;

public class QueryField
{
    public int Id { get; set; }

    public int QueryConfigId { get; set; }
    public QueryConfig? QueryConfig { get; set; }

    public int MetaColumnId { get; set; }
    public MetaColumn? MetaColumn { get; set; }

    [MaxLength(200)]
    public string? Alias { get; set; }

    public int SortOrder { get; set; }

    [MaxLength(20)]
    public string? AggregateFunc { get; set; }
}
