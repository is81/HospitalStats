using System.ComponentModel.DataAnnotations;
using HospitalStats.Api.DTOs;
using HospitalStats.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HospitalStats.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DataSourcesController : ControllerBase
{
    private readonly DataSourceService _service;

    public DataSourcesController(DataSourceService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<List<DataSourceDto>>> GetAll()
    {
        return await _service.GetAllAsync();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<DataSourceDto>> GetById(int id)
    {
        var result = await _service.GetByIdAsync(id);
        return result == null ? NotFound() : result;
    }

    [HttpPost]
    public async Task<ActionResult<DataSourceDto>> Create([FromBody] DataSourceCreateRequest request)
    {
        var result = await _service.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<DataSourceDto>> Update(int id, [FromBody] DataSourceUpdateRequest request)
    {
        var result = await _service.UpdateAsync(id, request);
        return result == null ? NotFound() : result;
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var success = await _service.DeleteAsync(id);
        return success ? NoContent() : NotFound();
    }

    [HttpPost("{id}/test")]
    public async Task<ActionResult<TestConnectionResult>> TestConnection(int id)
    {
        return await _service.TestConnectionAsync(id);
    }

    [HttpPost("test")]
    public async Task<ActionResult<TestConnectionResult>> TestConnectionString(
        [FromBody] TestConnectionStringRequest request)
    {
        return await _service.TestConnectionStringAsync(request.ConnectionString);
    }
}

public class TestConnectionStringRequest
{
    [Required]
    [MaxLength(2000)]
    public string ConnectionString { get; set; } = string.Empty;
}
