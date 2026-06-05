using HospitalStats.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HospitalStats.Api.Controllers;

[ApiController]
[Route("api/admin/settings")]
[Authorize(Roles = "admin")]
public class SettingsController : ControllerBase
{
    private readonly SystemSettingsService _service;

    public SettingsController(SystemSettingsService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<Dictionary<string, string>>> GetAll()
    {
        return Ok(await _service.GetAllAsync());
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] Dictionary<string, string> settings)
    {
        await _service.SetBatchAsync(settings);
        return NoContent();
    }
}
