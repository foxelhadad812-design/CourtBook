using System.Security.Claims;
using CourtBook.API.Extensions;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CourtBook.API.Controllers;

[ApiController]
[Route("api/owner")]
[Authorize(Roles = "Owner")]
public class OwnerPayoutsController : ControllerBase
{
    private readonly IPayoutService _payoutService;

    public OwnerPayoutsController(IPayoutService payoutService)
    {
        _payoutService = payoutService;
    }

    [HttpGet("balance")]
    public async Task<IActionResult> GetBalance()
    {
        var ownerId = User.GetUserId();
        if (ownerId == Guid.Empty) return Unauthorized();

        var balance = await _payoutService.GetOwnerBalanceAsync(ownerId);
        return Ok(balance);
    }

    [HttpGet("payout-methods")]
    public async Task<IActionResult> GetPayoutMethods()
    {
        var ownerId = User.GetUserId();
        if (ownerId == Guid.Empty) return Unauthorized();

        var methods = await _payoutService.GetPayoutMethodsAsync(ownerId);
        return Ok(methods);
    }

    [HttpPost("payout-methods")]
    public async Task<IActionResult> CreatePayoutMethod([FromBody] CreatePayoutMethodRequest request)
    {
        var ownerId = User.GetUserId();
        if (ownerId == Guid.Empty) return Unauthorized();

        try
        {
            var created = await _payoutService.CreatePayoutMethodAsync(ownerId, request);
            return CreatedAtAction(nameof(GetPayoutMethods), new { id = created.Id }, created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("payout-methods/{id:guid}")]
    public async Task<IActionResult> DeletePayoutMethod(Guid id)
    {
        var ownerId = User.GetUserId();
        if (ownerId == Guid.Empty) return Unauthorized();

        var success = await _payoutService.DeletePayoutMethodAsync(ownerId, id);
        return success ? NoContent() : NotFound(new { error = "Payout method not found." });
    }

    [HttpPost("payout-methods/{id:guid}/default")]
    public async Task<IActionResult> SetDefaultPayoutMethod(Guid id)
    {
        var ownerId = User.GetUserId();
        if (ownerId == Guid.Empty) return Unauthorized();

        var success = await _payoutService.SetDefaultPayoutMethodAsync(ownerId, id);
        return success ? Ok(new { success = true }) : NotFound(new { error = "Payout method not found." });
    }

    [HttpPost("payouts/request")]
    public async Task<IActionResult> RequestPayout([FromBody] CreatePayoutRequest request)
    {
        var ownerId = User.GetUserId();
        if (ownerId == Guid.Empty) return Unauthorized();

        // Check for idempotency header if body token is absent
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) && Request.Headers.TryGetValue("X-Idempotency-Key", out var headerKey))
        {
            request.IdempotencyKey = headerKey.ToString();
        }

        try
        {
            var payout = await _payoutService.RequestPayoutAsync(ownerId, request);
            return CreatedAtAction(nameof(GetPayoutById), new { id = payout.Id }, payout);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("payouts")]
    public async Task<IActionResult> GetPayouts(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null)
    {
        var ownerId = User.GetUserId();
        if (ownerId == Guid.Empty) return Unauthorized();

        var result = await _payoutService.GetOwnerPayoutsAsync(ownerId, page, pageSize, status);
        return Ok(result);
    }

    [HttpGet("payouts/{id:guid}")]
    public async Task<IActionResult> GetPayoutById(Guid id)
    {
        var ownerId = User.GetUserId();
        if (ownerId == Guid.Empty) return Unauthorized();

        var payout = await _payoutService.GetOwnerPayoutByIdAsync(ownerId, id);
        return payout is null ? NotFound(new { error = "Payout request not found." }) : Ok(payout);
    }

    [HttpPost("payouts/{id:guid}/cancel")]
    public async Task<IActionResult> CancelPayout(Guid id)
    {
        var ownerId = User.GetUserId();
        if (ownerId == Guid.Empty) return Unauthorized();

        try
        {
            var success = await _payoutService.CancelPayoutRequestAsync(ownerId, id);
            return success ? Ok(new { success = true }) : NotFound(new { error = "Payout request not found." });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }
}
