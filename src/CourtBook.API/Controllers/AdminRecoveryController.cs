using CourtBook.API.Extensions;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CourtBook.API.Controllers;

[ApiController]
[Route("api/admin/financial/recovery-obligations")]
[Authorize(Roles = "Admin")]
public class AdminRecoveryController : ControllerBase
{
    private readonly IRecoveryService _recoveryService;

    public AdminRecoveryController(IRecoveryService recoveryService)
    {
        _recoveryService = recoveryService;
    }

    [HttpGet]
    public async Task<IActionResult> GetRecoveryObligations(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] Guid? ownerId = null)
    {
        var result = await _recoveryService.GetRecoveryObligationsAsync(page, pageSize, status, ownerId);
        return Ok(result);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetRecoverySummary()
    {
        var summary = await _recoveryService.GetRecoverySummaryAsync();
        return Ok(summary);
    }

    [HttpPost("{id:guid}/settle-manually")]
    public async Task<IActionResult> SettleManually(Guid id, [FromBody] ManualSettleRecoveryRequest request)
    {
        var adminId = User.GetUserId();
        if (adminId == Guid.Empty) return Unauthorized();

        try
        {
            var result = await _recoveryService.SettleObligationManuallyAsync(id, adminId, request);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/write-off")]
    public async Task<IActionResult> WriteOff(Guid id, [FromBody] WriteOffRecoveryRequest request)
    {
        var adminId = User.GetUserId();
        if (adminId == Guid.Empty) return Unauthorized();

        try
        {
            var result = await _recoveryService.WriteOffObligationAsync(id, adminId, request.Reason);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }
}
