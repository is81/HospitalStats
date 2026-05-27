using System.ComponentModel.DataAnnotations;

namespace HospitalStats.Api.Models;

public class Role
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<RoleMenu> RoleMenus { get; set; } = new();
    public List<UserRole> UserRoles { get; set; } = new();
}

public class RoleMenu
{
    public int Id { get; set; }

    public int RoleId { get; set; }
    public Role? Role { get; set; }

    public int MenuId { get; set; }
    public Menu? Menu { get; set; }
}

public class UserRole
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public int RoleId { get; set; }
    public Role? Role { get; set; }
}
