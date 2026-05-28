using System.ComponentModel.DataAnnotations;

namespace HospitalStats.Api.DTOs;

public class LoginRequest
{
    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public List<int> MenuIds { get; set; } = new();
    public string? DeptName { get; set; }
}

public class ChangePasswordRequest
{
    [Required]
    public string OldPassword { get; set; } = string.Empty;

    [Required]
    [MinLength(6, ErrorMessage = "新密码至少6位")]
    public string NewPassword { get; set; } = string.Empty;
}
