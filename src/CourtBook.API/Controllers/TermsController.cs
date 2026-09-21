using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CourtBook.API.Controllers;

[ApiController]
[Route("api/terms")]
[AllowAnonymous]
public class TermsController : ControllerBase
{
    private readonly ITermsService _termsService;

    public TermsController(ITermsService termsService)
    {
        _termsService = termsService;
    }

    /// <summary>
    /// Fetches the currently active legal terms document for the given role type (Player or FacilityOwner).
    /// </summary>
    [HttpGet("{type}")]
    [ProducesResponseType(typeof(TermsDocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetActiveTerms(TermsType type)
    {
        var terms = await _termsService.GetActiveTermsAsync(type);
        return terms is null ? NotFound("Terms document not found.") : Ok(terms);
    }
}
