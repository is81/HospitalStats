using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HospitalStats.Api.Data;
using HospitalStats.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace HospitalStats.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public AuthController(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u =>
            u.Username == request.Username && u.IsEnabled);

        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Unauthorized(new { message = "用户名或密码错误" });

        // load user roles with their menu permissions
        var userRoles = await _db.UserRoles
            .Where(ur => ur.UserId == user.Id)
            .Include(ur => ur.Role)
            .ToListAsync();

        var roleNames = userRoles.Select(ur => ur.Role!.Name).ToList();
        var menuIds = await _db.RoleMenus
            .Where(rm => userRoles.Select(ur => ur.RoleId).Contains(rm.RoleId))
            .Select(rm => rm.MenuId)
            .Distinct()
            .ToListAsync();

        var token = GenerateJwt(user, roleNames);
        return new LoginResponse
        {
            Token = token,
            DisplayName = user.DisplayName ?? user.Username,
            Roles = roleNames,
            MenuIds = menuIds,
            DeptName = user.DeptName
        };
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<ActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var user = await _db.Users.FindAsync(userId);
        if (user == null)
            return NotFound(new { message = "用户不存在" });

        if (!BCrypt.Net.BCrypt.Verify(request.OldPassword, user.PasswordHash))
            return BadRequest(new { message = "原密码错误" });

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
            return BadRequest(new { message = "新密码至少6位" });

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        await _db.SaveChangesAsync();

        return Ok(new { message = "密码修改成功" });
    }

    private string GenerateJwt(Models.User user, List<string> roles)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_config["Jwt:Key"] ?? "HospitalStats@JwtSecretKey2026!"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.GivenName, user.DisplayName ?? user.Username)
        };

        if (!string.IsNullOrEmpty(user.DeptName))
            claims.Add(new Claim("dept_name", user.DeptName));

        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"] ?? "HospitalStats",
            audience: _config["Jwt:Audience"] ?? "HospitalStats",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
