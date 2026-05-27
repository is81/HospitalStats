using System.ComponentModel.DataAnnotations;

namespace HospitalStats.Api.Models;

public class QueryJoin
{
    public int Id { get; set; }

    public int QueryConfigId { get; set; }
    public QueryConfig? QueryConfig { get; set; }

    public int JoinTableId { get; set; }
    public MetaTable? JoinTable { get; set; }

    [Required, MaxLength(10)]
    public string JoinType { get; set; } = "LEFT";

    public int LeftMetaColumnId { get; set; }
    public MetaColumn? LeftMetaColumn { get; set; }

    public int RightMetaColumnId { get; set; }
    public MetaColumn? RightMetaColumn { get; set; }

    public int SortOrder { get; set; }
}
