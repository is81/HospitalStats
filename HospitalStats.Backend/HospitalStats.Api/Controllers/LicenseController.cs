using HospitalStats.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HospitalStats.Api.Controllers;

[ApiController]
[Route("api/license")]
public class LicenseController : ControllerBase
{
    private readonly LicenseService _service;

    public LicenseController(LicenseService service)
    {
        _service = service;
    }

    /// <summary>Get machine code (public, used on login page).</summary>
    [AllowAnonymous]
    [HttpGet("machine-code")]
    public IActionResult GetMachineCode()
    {
        return Ok(new { machineCode = LicenseService.GetMachineCode() });
    }

    /// <summary>Check activation status.</summary>
    [AllowAnonymous]
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var msg = await _service.GetStatusAsync();
        var activated = await _service.IsActivatedAsync();
        return Ok(new { activated, message = msg, machineCode = LicenseService.GetMachineCode() });
    }

    /// <summary>Activate with code.</summary>
    [AllowAnonymous]
    [HttpPost("activate")]
    public async Task<IActionResult> Activate([FromBody] ActivateRequest req)
    {
        if (string.IsNullOrEmpty(req.ActivationCode))
            return BadRequest(new { message = "请输入激活码" });

        var ok = await _service.ActivateAsync(req.ActivationCode.Trim());
        if (!ok)
            return BadRequest(new { message = "激活码无效" });

        return Ok(new { message = "激活成功，请刷新页面" });
    }

    /// <summary>Clear license (admin only, for re-activation).</summary>
    [Authorize(Roles = "admin")]
    [HttpPost("reset")]
    public async Task<IActionResult> Reset()
    {
        await _service.ResetAsync();
        return Ok(new { message = "已清除激活状态", machineCode = LicenseService.GetMachineCode() });
    }
}

public class ActivateRequest
{
    public string ActivationCode { get; set; } = "";
}
