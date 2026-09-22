using CourtBook.API.Extensions;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CourtBook.API.Controllers;

[ApiController]
[Route("api/admin/settlements")]
[Authorize(Roles = "Admin")]
public class AdminSettlementsController : ControllerBase
{
    private readonly ISettlementService _settlementService;

    public AdminSettlementsController(ISettlementService settlementService)
    {
        _settlementService = settlementService;
    }

    [HttpGet]
    public async Task<IActionResult> GetSettlementBatches(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _settlementService.GetSettlementBatchesAsync(page, pageSize);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetSettlementBatchById(Guid id)
    {
        var batch = await _settlementService.GetSettlementBatchByIdAsync(id);
        return batch is null ? NotFound(new { error = "Settlement batch not found." }) : Ok(batch);
    }

    [HttpPost("run")]
    public async Task<IActionResult> RunSettlement([FromBody] RunSettlementRequest? request)
    {
        var adminId = User.GetUserId();
        var buffer = request?.BufferHours ?? 24;

        try
        {
            var batch = await _settlementService.ExecuteSettlementBatchAsync(buffer, adminId);
            return Ok(batch);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"Settlement execution error: {ex.Message}" });
        }
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSettlementSummary()
    {
        var summary = await _settlementService.GetSettlementSummaryAsync();
        return Ok(summary);
    }
}
