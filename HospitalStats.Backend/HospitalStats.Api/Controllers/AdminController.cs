using Dapper;
using HospitalStats.Api.Data;
using HospitalStats.Api.DTOs;
using HospitalStats.Api.Models;
using HospitalStats.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Oracle.ManagedDataAccess.Client;

namespace HospitalStats.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "admin")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly DataSourceService _dsService;

    public AdminController(AppDbContext db, DataSourceService dsService)
    {
        _db = db;
        _dsService = dsService;
    }

    // ===== Users =====

    [HttpGet("users")]
    public async Task<ActionResult<List<UserDto>>> GetUsers()
    {
        var users = await _db.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .OrderBy(u => u.Username)
            .ToListAsync();

        return users.Select(u => new UserDto
        {
            Id = u.Id,
            Username = u.Username,
            DisplayName = u.DisplayName,
            DeptName = u.DeptName,
            IsEnabled = u.IsEnabled,
            Roles = u.UserRoles.Select(ur => ur.Role?.Name ?? "").Where(r => r != "").ToList(),
            RoleIds = u.UserRoles.Select(ur => ur.RoleId).ToList(),
            CreatedAt = u.CreatedAt
        }).ToList();
    }

    [HttpPost("users")]
    public async Task<ActionResult<UserDto>> CreateUser([FromBody] UserCreateRequest req)
    {
        if (await _db.Users.AnyAsync(u => u.Username == req.Username))
            return BadRequest(new { message = "用户名已存在" });

        var user = new User
        {
            Username = req.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            DisplayName = req.DisplayName,
            DeptName = req.DeptName
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        if (req.RoleIds?.Count > 0)
        {
            foreach (var roleId in req.RoleIds)
            {
                _db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId });
            }
            await _db.SaveChangesAsync();
        }

        return CreatedAtAction(nameof(GetUsers), new UserDto
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            IsEnabled = true
        });
    }

    [HttpPut("users/{id}")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UserUpdateRequest req)
    {
        var user = await _db.Users.Include(u => u.UserRoles).FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return NotFound();

        if (req.DisplayName != null) user.DisplayName = req.DisplayName;
        if (req.DeptName != null) user.DeptName = req.DeptName;
        if (!string.IsNullOrEmpty(req.Password))
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password);
        user.IsEnabled = req.IsEnabled;

        if (req.RoleIds != null)
        {
            var existing = await _db.UserRoles.Where(ur => ur.UserId == id).ToListAsync();
            _db.UserRoles.RemoveRange(existing);
            foreach (var roleId in req.RoleIds)
            {
                _db.UserRoles.Add(new UserRole { UserId = id, RoleId = roleId });
            }
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound();
        if (user.Username == "admin")
            return BadRequest(new { message = "不能删除内置管理员" });
        _db.Users.Remove(user);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("dept-options")]
    public async Task<ActionResult<List<string>>> GetDeptOptions()
    {
        var metaTable = await _db.MetaTables.FirstOrDefaultAsync(t => t.TableName == "DEPT_DICT");
        if (metaTable == null) return new List<string>();

        var ds = await _db.DataSources.FindAsync(metaTable.DataSourceId);
        if (ds == null) return new List<string>();

        var connStr = _dsService.Decrypt(ds.ConnectionString);
        using var conn = new OracleConnection(connStr);
        await conn.OpenAsync();

        var schema = metaTable.SchemaName ?? "HOSPITAL";
        var sql = $"SELECT \"DEPT_NAME\" FROM (SELECT DISTINCT \"DEPT_NAME\", \"SERIAL_NO\" FROM \"{schema}\".\"DEPT_DICT\") ORDER BY \"SERIAL_NO\"";
        var values = await conn.QueryAsync<string>(sql);
        return values.Where(v => v != null).ToList();
    }

    // ===== Roles =====

    [HttpGet("roles")]
    public async Task<ActionResult<List<RoleDto>>> GetRoles()
    {
        var roles = await _db.Roles
            .Include(r => r.RoleMenus)
            .OrderBy(r => r.Name)
            .ToListAsync();

        return roles.Select(r => new RoleDto
        {
            Id = r.Id,
            Name = r.Name,
            Description = r.Description,
            MenuIds = r.RoleMenus.Select(rm => rm.MenuId).ToList(),
            CreatedAt = r.CreatedAt
        }).ToList();
    }

    [HttpPost("roles")]
    public async Task<ActionResult<RoleDto>> CreateRole([FromBody] RoleSaveRequest req)
    {
        var role = new Role { Name = req.Name, Description = req.Description };
        _db.Roles.Add(role);
        await _db.SaveChangesAsync();

        UpdateRoleMenus(role.Id, req.MenuIds);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetRoles), new RoleDto
        {
            Id = role.Id,
            Name = role.Name,
            Description = role.Description,
            MenuIds = req.MenuIds
        });
    }

    [HttpPut("roles/{id}")]
    public async Task<IActionResult> UpdateRole(int id, [FromBody] RoleSaveRequest req)
    {
        var role = await _db.Roles.FindAsync(id);
        if (role == null) return NotFound();

        if (role.Name == "admin" && req.Name != "admin")
            return BadRequest(new { message = "不能重命名内置管理员角色" });

        role.Name = req.Name;
        role.Description = req.Description;

        var existingMenus = await _db.RoleMenus.Where(rm => rm.RoleId == id).ToListAsync();
        _db.RoleMenus.RemoveRange(existingMenus);

        UpdateRoleMenus(id, req.MenuIds);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("roles/{id}")]
    public async Task<IActionResult> DeleteRole(int id)
    {
        var role = await _db.Roles.FindAsync(id);
        if (role == null) return NotFound();
        if (role.Name == "admin")
            return BadRequest(new { message = "不能删除内置管理员角色" });
        _db.Roles.Remove(role);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("roles/{id}/menus")]
    public async Task<ActionResult<List<int>>> GetRoleMenuIds(int id)
    {
        return await _db.RoleMenus
            .Where(rm => rm.RoleId == id)
            .Select(rm => rm.MenuId)
            .ToListAsync();
    }

    private void UpdateRoleMenus(int roleId, List<int> menuIds)
    {
        // validate that menu IDs are valid and include child menu IDs
        foreach (var mid in menuIds)
        {
            _db.RoleMenus.Add(new RoleMenu { RoleId = roleId, MenuId = mid });
        }
    }
}
