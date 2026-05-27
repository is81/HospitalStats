using System.ComponentModel.DataAnnotations;

namespace HospitalStats.Api.DTOs;

public class DataSourceDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DbType { get; set; } = string.Empty;
    public string? Schema { get; set; }
    public string? CharSetOverride { get; set; }
    public string? CharSetInfo { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DataSourceCreateRequest
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string DbType { get; set; } = "Oracle";

    [Required, MaxLength(500)]
    public string ConnectionString { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Schema { get; set; }

    [MaxLength(50)]
    public string? CharSetOverride { get; set; }
}

public class DataSourceUpdateRequest
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string DbType { get; set; } = "Oracle";

    [Required, MaxLength(500)]
    public string ConnectionString { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Schema { get; set; }

    [MaxLength(50)]
    public string? CharSetOverride { get; set; }

    public bool IsEnabled { get; set; } = true;
}

public class TestConnectionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? TableCount { get; set; }
    public string? DbVersion { get; set; }
    public string? CharSet { get; set; }
}
