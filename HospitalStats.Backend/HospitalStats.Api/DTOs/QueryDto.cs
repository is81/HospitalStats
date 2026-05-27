namespace HospitalStats.Api.DTOs;

// ===== Menu =====

public class MenuDto
{
    public int Id { get; set; }
    public int? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public int SortOrder { get; set; }
    public int? QueryConfigId { get; set; }
    public string? QueryConfigName { get; set; }
    public bool IsEnabled { get; set; }
    public List<MenuDto> Children { get; set; } = new();
}

public class MenuSaveRequest
{
    public int? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public int SortOrder { get; set; }
    public int? QueryConfigId { get; set; }
    public bool IsEnabled { get; set; } = true;
}

// ===== QueryConfig =====

public class QueryConfigDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int MainTableId { get; set; }
    public string? MainTableName { get; set; }
    public string DisplayType { get; set; } = "table";
    public string? AggregateType { get; set; }
    public string? AggregateColumn { get; set; }
    public string? GroupByColumn { get; set; }
    public string? SortColumn { get; set; }
    public string? SortDirection { get; set; }
    public int? PageSize { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? RawSql { get; set; }
    public List<QueryFieldDto> Fields { get; set; } = new();
    public List<QueryFilterDto> Filters { get; set; } = new();
    public List<QueryJoinDto> Joins { get; set; } = new();
}

public class QueryConfigSaveRequest
{
    public string Name { get; set; } = string.Empty;
    public int MainTableId { get; set; }
    public string DisplayType { get; set; } = "table";
    public string? AggregateType { get; set; }
    public string? AggregateColumn { get; set; }
    public string? GroupByColumn { get; set; }
    public string? SortColumn { get; set; }
    public string? SortDirection { get; set; }
    public int? PageSize { get; set; } = 50;
    public bool IsEnabled { get; set; } = true;
    public string? RawSql { get; set; }
    public List<QueryFieldSaveRequest> Fields { get; set; } = new();
    public List<QueryFilterSaveRequest> Filters { get; set; } = new();
    public List<QueryJoinSaveRequest> Joins { get; set; } = new();
}

public class QueryFieldDto
{
    public int Id { get; set; }
    public int MetaColumnId { get; set; }
    public string? ColumnName { get; set; }
    public string? ColumnAlias { get; set; }
    public string? TableName { get; set; }
    public string? Alias { get; set; }
    public int SortOrder { get; set; }
    public string? AggregateFunc { get; set; }
}

public class QueryFieldSaveRequest
{
    public int MetaColumnId { get; set; }
    public string? Alias { get; set; }
    public int SortOrder { get; set; }
    public string? AggregateFunc { get; set; }
}

public class QueryFilterDto
{
    public int Id { get; set; }
    public int MetaColumnId { get; set; }
    public string? ColumnName { get; set; }
    public string? ColumnAlias { get; set; }
    public string Operator { get; set; } = "EQ";
    public string? DefaultValue { get; set; }
    public bool IsRequired { get; set; }
    public string ControlType { get; set; } = "input";
    public string? Label { get; set; }
    public int SortOrder { get; set; }
    public bool IsContextFilter { get; set; }
    public string? ContextKey { get; set; }
}

public class QueryFilterSaveRequest
{
    public int MetaColumnId { get; set; }
    public string Operator { get; set; } = "EQ";
    public string? DefaultValue { get; set; }
    public bool IsRequired { get; set; }
    public string ControlType { get; set; } = "input";
    public string? Label { get; set; }
    public int SortOrder { get; set; }
    public bool IsContextFilter { get; set; }
    public string? ContextKey { get; set; }
}

public class QueryJoinDto
{
    public int Id { get; set; }
    public int JoinTableId { get; set; }
    public string? JoinTableName { get; set; }
    public string JoinType { get; set; } = "LEFT";
    public int LeftMetaColumnId { get; set; }
    public string? LeftColumnName { get; set; }
    public int RightMetaColumnId { get; set; }
    public string? RightColumnName { get; set; }
    public int SortOrder { get; set; }
}

public class QueryJoinSaveRequest
{
    public int JoinTableId { get; set; }
    public string JoinType { get; set; } = "LEFT";
    public int LeftMetaColumnId { get; set; }
    public int RightMetaColumnId { get; set; }
    public int SortOrder { get; set; }
}

// ===== SQL Import =====

public class SqlParseRequest
{
    public string Sql { get; set; } = "";
}

public class SqlParseResponse
{
    public int? MainTableId { get; set; }
    public string? MainTableName { get; set; }
    public List<SqlColumnMatch> Columns { get; set; } = new();
    public List<SqlFilterMatch> Filters { get; set; } = new();
    public List<SqlJoinMatch> Joins { get; set; } = new();
    public string? SortColumn { get; set; }
    public string? SortDirection { get; set; }
    public string? GroupByColumn { get; set; }
    public List<string> UnmatchedColumns { get; set; } = new();
    public string? RawSql { get; set; }
}

public class SqlJoinMatch
{
    public int? JoinTableId { get; set; }
    public string? JoinTableName { get; set; }
    public string JoinType { get; set; } = "LEFT";
    public int? LeftMetaColumnId { get; set; }
    public string? LeftColumnName { get; set; }
    public int? RightMetaColumnId { get; set; }
    public string? RightColumnName { get; set; }
    public bool Matched { get; set; }
}

public class SqlColumnMatch
{
    public int? MetaColumnId { get; set; }
    public string? Alias { get; set; }
    public string? AggregateFunc { get; set; }
    public string? Expression { get; set; }
    public bool Matched { get; set; }
}

public class SqlFilterMatch
{
    public int? MetaColumnId { get; set; }
    public string? Operator { get; set; } = "EQ";
    public string? DefaultValue { get; set; }
    public string? Label { get; set; }
    public bool Matched { get; set; }
}
