using CourtBook.API.Extensions;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CourtBook.API.Controllers;

[ApiController]
[Route("api/admin/payouts")]
[Authorize(Roles = "Admin")]
public class AdminPayoutsController : ControllerBase
{
    private readonly IPayoutService _payoutService;

    public AdminPayoutsController(IPayoutService payoutService)
    {
        _payoutService = payoutService;
    }

    [HttpGet]
    public async Task<IActionResult> GetPayouts(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] Guid? ownerId = null)
    {
        var result = await _payoutService.GetAdminPayoutsAsync(page, pageSize, status, ownerId);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetPayoutById(Guid id)
    {
        var payout = await _payoutService.GetAdminPayoutByIdAsync(id);
        return payout is null ? NotFound(new { error = "Payout request not found." }) : Ok(payout);
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> ApprovePayout(Guid id)
    {
        var adminId = User.GetUserId();
        if (adminId == Guid.Empty) return Unauthorized();

        try
        {
            var payout = await _payoutService.ApprovePayoutAsync(id, adminId);
            return Ok(payout);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> RejectPayout(Guid id, [FromBody] RejectPayoutRequest request)
    {
        var adminId = User.GetUserId();
        if (adminId == Guid.Empty) return Unauthorized();

        try
        {
            var payout = await _payoutService.RejectPayoutAsync(id, adminId, request.Reason);
            return Ok(payout);
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

    [HttpPost("{id:guid}/mark-paid")]
    public async Task<IActionResult> MarkPaid(Guid id, [FromBody] MarkPayoutPaidRequest request)
    {
        var adminId = User.GetUserId();
        if (adminId == Guid.Empty) return Unauthorized();

        try
        {
            var payout = await _payoutService.MarkPayoutPaidAsync(id, adminId, request);
            return Ok(payout);
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

    [HttpGet("summary")]
    public async Task<IActionResult> GetPayoutSummary()
    {
        var summary = await _payoutService.GetAdminPayoutSummaryAsync();
        return Ok(summary);
    }
}
