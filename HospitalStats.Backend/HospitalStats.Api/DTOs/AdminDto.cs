using System.ComponentModel.DataAnnotations;

namespace HospitalStats.Api.DTOs;

public class UserDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? DeptName { get; set; }
    public bool IsEnabled { get; set; }
    public List<string> Roles { get; set; } = new();
    public List<int> RoleIds { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public class UserCreateRequest
{
    [Required]
    [MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [MinLength(6, ErrorMessage = "密码至少6位")]
    public string Password { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? DisplayName { get; set; }

    [MaxLength(50)]
    public string? DeptName { get; set; }
    public List<int>? RoleIds { get; set; }
}

public class UserUpdateRequest
{
    [MaxLength(100)]
    public string? DisplayName { get; set; }
    public string? Password { get; set; }

    [MaxLength(50)]
    public string? DeptName { get; set; }
    public bool IsEnabled { get; set; } = true;
    public List<int>? RoleIds { get; set; }
}

public class RoleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<int> MenuIds { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public class RoleSaveRequest
{
    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Description { get; set; }
    public List<int> MenuIds { get; set; } = new();
}

public class DashboardCardDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? QueryConfigId { get; set; }
    public string? QueryConfigName { get; set; }
    public string DisplayType { get; set; } = "number";
    public string? Icon { get; set; }
    public string? Color { get; set; }
    public string? Unit { get; set; }
    public int SortOrder { get; set; }
    public int Width { get; set; } = 6;
    public bool IsEnabled { get; set; }
    public string? CompareMode { get; set; }
    public int? DecimalPlaces { get; set; }
    public object? Data { get; set; }
}

public class DashboardCardSaveRequest
{
    [Required]
    [MaxLength(100)]
    public string Title { get; set; } = string.Empty;

    public int? QueryConfigId { get; set; }

    [MaxLength(20)]
    public string DisplayType { get; set; } = "number";

    [MaxLength(50)]
    public string? Icon { get; set; }

    [MaxLength(20)]
    public string? Color { get; set; }

    [MaxLength(50)]
    public string? Unit { get; set; }
    public int SortOrder { get; set; }
    public int Width { get; set; } = 6;
    public bool IsEnabled { get; set; } = true;

    [MaxLength(10)]
    public string? CompareMode { get; set; }

    public int? DecimalPlaces { get; set; }
}

public class DashboardCardOrderRequest
{
    public List<int> CardIds { get; set; } = new();
}
