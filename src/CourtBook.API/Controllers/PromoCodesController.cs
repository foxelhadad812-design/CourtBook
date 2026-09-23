using System.Security.Claims;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CourtBook.API.Controllers;

[ApiController]
[Route("api/promocodes")]
public class PromoCodesController : ControllerBase
{
    private readonly IPromoCodeService _promoCodeService;

    public PromoCodesController(IPromoCodeService promoCodeService)
    {
        _promoCodeService = promoCodeService;
    }

    [HttpPost("validate")]
    [AllowAnonymous]
    public async Task<IActionResult> Validate([FromBody] ValidatePromoCodeRequest request)
    {
        Guid? userId = null;
        var subClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(subClaim) && Guid.TryParse(subClaim, out var parsedId))
        {
            userId = parsedId;
        }

        var result = await _promoCodeService.ValidatePromoCodeAsync(request, userId);
        return Ok(result);
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAll()
    {
        var promos = await _promoCodeService.GetAllPromoCodesAsync();
        return Ok(promos);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] CreatePromoCodeRequest request)
    {
        try
        {
            var promo = await _promoCodeService.CreatePromoCodeAsync(request);
            return CreatedAtAction(nameof(GetAll), new { id = promo.Id }, promo);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        var success = await _promoCodeService.DeactivatePromoCodeAsync(id);
        return success ? NoContent() : NotFound(new { error = "Promo code not found." });
    }
}
