using System.ComponentModel.DataAnnotations;

namespace HospitalStats.Api.DTOs;

public class BizDomainDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public int TableCount { get; set; }
}

public class BizDomainCreateRequest
{
    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Description { get; set; }
    public int SortOrder { get; set; }
}

public class MetaTableDto
{
    public int Id { get; set; }
    public int DataSourceId { get; set; }
    public int? BizDomainId { get; set; }
    public string? BizDomainName { get; set; }
    public string TableName { get; set; } = string.Empty;
    public string? SchemaName { get; set; }
    public string? Alias { get; set; }
    public string? Description { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsView { get; set; }
    public int ColumnCount { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class MetaTableUpdateRequest
{
    [MaxLength(200)]
    public string? Alias { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }
    public int? BizDomainId { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public class MetaColumnDto
{
    public int Id { get; set; }
    public int MetaTableId { get; set; }
    public string ColumnName { get; set; } = string.Empty;
    public string? DataType { get; set; }
    public int? DataLength { get; set; }
    public int? DataPrecision { get; set; }
    public int? DataScale { get; set; }
    public bool Nullable { get; set; }
    public string? Alias { get; set; }
    public string? Comment { get; set; }
    public bool IsQueryField { get; set; }
    public bool IsFilterField { get; set; }
    public bool IsDisplayField { get; set; }
    public int? SortOrder { get; set; }
}

public class MetaColumnUpdateRequest
{
    [MaxLength(200)]
    public string? Alias { get; set; }
    public bool IsQueryField { get; set; }
    public bool IsFilterField { get; set; }
    public bool IsDisplayField { get; set; }
}
