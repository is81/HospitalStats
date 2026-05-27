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
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? DeptName { get; set; }
    public List<int>? RoleIds { get; set; }
}

public class UserUpdateRequest
{
    public string? DisplayName { get; set; }
    public string? Password { get; set; }
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
    public string Name { get; set; } = string.Empty;
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
    public object? Data { get; set; }
}

public class DashboardCardSaveRequest
{
    public string Title { get; set; } = string.Empty;
    public int? QueryConfigId { get; set; }
    public string DisplayType { get; set; } = "number";
    public string? Icon { get; set; }
    public string? Color { get; set; }
    public string? Unit { get; set; }
    public int SortOrder { get; set; }
    public int Width { get; set; } = 6;
    public bool IsEnabled { get; set; } = true;
}

public class DashboardCardOrderRequest
{
    public List<int> CardIds { get; set; } = new();
}
